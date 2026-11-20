# Contrato del núcleo de stock

**Versión de diseño**: 1 · **Proponente**: B · **Consumidores**: A y B.
Base: [modelo](../data-model.md), [plan global](../../project-plan.md),
[constitución](../../constitution.md) I, II, IV y V.
Es un contrato interno, no un endpoint genérico para sobrescribir saldos.

## Operaciones admitidas

Receive, Reserve, ReleaseReservation, Ship, ApplyCountAdjustment y CorrectMovement.
Los handlers de documentos coordinan el comando completo. El núcleo recibe las
asignaciones artículo/ubicación/lote, cantidades, documento origen, motivo, OperationId,
actor verificado e instante del hecho. El usuario no elige el instante de registro.

El resultado identifica documento, movimientos y operación. Solo existe éxito definitivo
tras commit de todos los efectos. Los comandos de stock no hacen llamadas HTTP internas
a la misma API ni inician otra transacción o DbContext.

## Protocolo obligatorio de cada comando

1. Autenticar, autorizar y validar la forma de la petición, incluidas unidades y precisión.
2. Ejecutar mediante la estrategia de resiliencia una transacción nueva por intento;
   DbContext limpio, misma conexión y transacción para documento, stock y alertas.
3. Adquirir applock exclusivo `operation:{uuid}` con propietario Transaction.
   Si existe resultado, validar actor/permisos y huella: devolverlo si coincide;
   rechazar si el contenido o recurso difiere.
4. Adquirir los bloqueos de documentos involucrados, por tipo e ID ordenados.
   Una corrección declara de antemano todos los documentos relacionados que modifica.
5. Determinar el conjunto de ubicaciones y adquirir sus bloqueos por ID ordenado.
6. Determinar artículos afectados y adquirir sus bloqueos por ID ordenado. El bloqueo
   `article:{uuid}` coordina todas sus ubicaciones/lotes y la evaluación global de mínimos.
7. Leer después de los bloqueos: estado actual del documento, maestros, congelación,
   saldos, reservas y pendientes. Si aparecen recursos anteriores al orden ya adquirido,
   rollback y replanificación del intento, sin improvisar un nuevo orden.
8. Validar todas las invariantes, preparar nuevas filas si faltan y aplicar cambios.
   Persistir documento, diario, reservas, saldos, alertas internas y OperationResult juntos.
9. Commit. Un fallo revierte todos los cambios y libera los bloqueos. Un worker externo
   solo recibe intenciones persistidas, nunca trabajo en memoria necesario para la corrección.

Los nombres de recursos se normalizan, tienen menos de 255 caracteres y usan la misma
base y DbPrincipal. El comparador de IDs es único para todos los comandos. Timeout de
bloqueo inicial: 5 s; retorno negativo obliga a rollback explícito, incluso deadlock de applock.
Máximo tres intentos completos ante fallos transitorios; después se devuelve error recuperable.
La configuración de EF debe ejecutar la transacción completa dentro de su estrategia,
no combinar reintentos de comandos SQL individuales con decisiones ya calculadas.

Concreción de formato para consumidores: UUID D en minúsculas; ordenar recursos de cada
fase por su representación canónica con comparación ordinal. Los maestros Family/Supplier
usan `document:family:{uuid}` y `document:supplier:{uuid}` dentro de la fase documental.
Todos sus escritores y los que los asignan o validan estado comparten esos recursos;
no adquirir familia/proveedor después de bloquear un artículo.

## Alcance y disciplina de escritura

- La referencia de artículo de H0 es `Domain.MasterData.Article`, tabla `Articles`,
  compartida por A y B. Lot, StockBalance y StockEntry deben declarar claves foráneas
  a ese artículo con borrado restringido desde su primera migración. Las configuraciones
  se mantienen en archivos `IEntityTypeConfiguration` dentro del mismo contexto.
- Los comandos obtienen el actor mediante `ICurrentActor.GetRequiredActorIdAsync` y
  comprueban sus permisos antes de ejecutar. La implementación Identity rechaza ausencia
  de sesión, identificador inválido o usuario inexistente. No se recibe ActorId del cliente;
  la interfaz no sustituye la autorización del endpoint ni los permisos de la operación.
- Todas las rutas de escritura, importaciones y trabajos en segundo plano siguen el
  protocolo. Las restricciones SQL son defensa adicional; applock es cooperativo.
- Desactivar un artículo o cambiar su mínimo adquiere el mismo bloqueo de artículo.
  Cambiar el estado de una ubicación adquiere el de ubicación. Nunca se adquiere
  ubicación después de artículo. Editar varios maestros sigue orden de IDs consistente.
- Retirar una Receiving adquiere además `locations:receiving` en la fase de ubicaciones,
  después de todos los `location:{id}` necesarios (orden ordinal). Serializa el recuento
  de zonas activas y evita retirar las dos últimas simultáneamente. StockCore no lo toma.
- Editar unidad base, seguimiento por lote o admisión de fracciones y crear referencias
  documentales al artículo se coordinan con el mismo bloqueo de artículo. Tras adquirirlo,
  la edición comprueba marca de primer uso false y ausencia de documentos, reservas o
  movimientos asociados. Crear la primera referencia fija IsDefinitionLocked=true dentro
  de la misma transacción, incluidos borradores. La marca no se revierte al retirar líneas;
  crear una referencia revalida la definición vigente después del bloqueo y respeta el orden global.
- Los lectores de uso cubren las FK consumidoras de las features integradas; una dependencia
  presente sin lector autoritativo impide habilitar la mutación, no equivale a «sin uso».
  Antes de admitir cambios de definición también se acredita el enlace de primera referencia
  de cada escritor presente. Si falta, DependencyUnavailable; una nueva feature completa
  ambas conexiones y sus pruebas antes de habilitar sus escritores. La ausencia acreditada
  de consumidores permite el corte H1 sin esperar todas las historias futuras.
  Política de propiedad e interfaces particulares en la
  [integración HU-08](../../../../specs/002-gestionar-datos-maestros/contracts/integration.md).
- Las asignaciones de reserva y salida son explícitas; no se decide disponibilidad
  a partir de filas que se leyeron antes del bloqueo. Una reserva sobre un bucket vacío
  se rechaza, aunque su ubicación o lote todavía no tenga fila de saldo.
- Un lock por artículo protege también la agregación para alertas y la creación de un
  nuevo lote. El lock de ubicación impide iniciar un conteo mientras cambia su stock.
- Un comando de modificación del pedido de cliente declara primero las reservas que va
  a liberar y sus ubicaciones, conservando la jerarquía documento → ubicación → artículo.

## Implementación H0 (B, 2026-11-18)

`Inventario.Api.Features.Stock.Core.StockCore` es un paso de dominio que se ejecuta **dentro**
de `InventoryOperationExecutor` de A; no abre transacción, no guarda resultados ni toma el
bloqueo de operación. El consumidor (HU-01/03/04/05):

1. Valida y normaliza con `StockCore.Prepare(operación, contexto, asignaciones)` o
   `PrepareShipment(...)`. Obtiene un `StockCommand` cuyo `NormalizedBody` (cantidades con
   tres decimales, lote en mayúsculas, tipo de documento canónico) va en la `OperationRequest`.
2. Llama al ejecutor con la ruta y el `RequiredPermission` de su operación. El ejecutor
   autentica, autoriza, resuelve el replay con el único `OperationResult` y confirma todo.
3. Dentro de `change`, invoca `StockCore.ApplyAsync(context, command)` y devuelve
   `StockCore.Response(outcome)`.

`StockCommandContext` acepta CounterpartyReference o CounterpartyNotApplicableReason
(máximo 500 caracteres para el motivo). Los valores se normalizan con Trim; un hecho
nuevo sin ambos se rechaza 422 desde ApplyAsync, después del replay del ejecutor y antes
de cualquier efecto. Esto permite recuperar una intención histórica confirmada aunque
su petición anterior careciera de ese dato. El campo nuevo solo se incorpora a NormalizedBody
cuando tiene valor: las huellas de peticiones antiguas sin él no cambian. Con otro motivo,
la misma clave produce IdempotencyConflict. El diario y el lector conservan el motivo;
los hechos históricos sin evidencia continúan siendo distinguibles y no se reescriben.

### Ejemplo de integración: bloqueos del consumidor en la recepción (HU-01)

Si el consumidor tiene que leer y validar su propio documento antes de aplicar el stock,
declara **todos** sus bloqueos dentro de `change` y antes de `ApplyAsync`, una llamada por
fase y en el orden del protocolo: documentos → ubicaciones → artículos. La recepción
bloquea la compra y la recepción en la fase documental (sección «Recepción y compra»):

```csharp
// Dentro de executor.ExecuteAsync(request, async (context, token) => { ... })
var allocations = /* las mismas StockAllocation con las que se preparó el command */;
await context.Locks.DocumentsAsync(
    [new DocumentLock("purchase-order", purchaseOrderId), new DocumentLock("receipt", receiptId)], token);
await context.Locks.LocationsAsync(allocations.Select(line => line.LocationId), token);
await context.Locks.ArticlesAsync(allocations.Select(line => line.ArticleId), token);

// Revalidar la compra bajo los bloqueos: pendiente, estado, destinos activos…
// (dos recepciones parciales de 6 no pueden consumir un pendiente de 10).

var outcome = await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, token);
return StockCore.Response(outcome);
```

Reglas, tal como las aplica `InventoryLocks`:

- Cada llamada ordena sus recursos por comparación ordinal: `document:purchase-order:{id}`
  precede a `document:receipt:{id}`, y ubicaciones y artículos se ordenan por UUID en
  minúsculas. Lo más simple es pedir los dos documentos en la misma llamada; en llamadas
  separadas, `purchase-order` debe ir antes que `receipt`, porque el orden inverso se rechaza.
- Las fases son operación (0, la toma el ejecutor) → documentos (1) → ubicaciones (2) →
  artículos (3). Pedir una fase anterior a la última adquirida lanza
  `InvalidOperationException` («Los bloqueos requieren replanificar…»).
- Un recurso ya retenido no se vuelve a adquirir ni comprueba el orden. Por eso
  `ApplyAsync`, que pide `document:receipt:{SourceId}`, las ubicaciones y los artículos de
  sus líneas (y `ArticleReferenceTracker`, que vuelve a pedir los artículos), los encuentra
  ya retenidos y los reutiliza sin adquirirlos de nuevo.
- Para que eso funcione, el consumidor bloquea **al menos** el documento de origen del
  comando (`SourceType`/`SourceId`, aquí `receipt`/`receiptId`) y todas las ubicaciones y
  artículos de sus asignaciones. Si falta alguno, `ApplyAsync` intentaría adquirirlo
  después de la fase de artículos y fallaría por orden.
- Así, la revalidación del consumidor y la aplicación del núcleo ven el mismo estado: nada
  puede cambiar la compra, la recepción, las ubicaciones o los artículos entre ambas.

`ApplyAsync` sigue los pasos 4–8 con `context.Locks` (`InventoryLocks`): documento
`document:{tipo}:{id}` → ubicaciones → artículos. Después lee el estado, marca la primera
referencia del artículo con `ArticleReferenceTracker` (IsDefinitionLocked, mismo bloqueo y
transacción) y escribe diario, lotes y saldos. `StockMovement.OperationId` tiene FK
restrictiva al resultado común, como `MasterDataAudit`; por eso no se llama a
`SaveChanges` dentro de `change` antes de que el ejecutor añada el resultado.

Los rechazos son `InventoryProblemException` (patrón de A) con los códigos del
[contrato HTTP](api.md): ValidationFailed, InsufficientStock, ReservedStockConflict,
ArticleInactive y DocumentAlreadyConfirmed; IdempotencyConflict, Forbidden y LockTimeout
los aporta el ejecutor. Cada hecho documental es único por (SourceDocumentType,
SourceDocumentId, Kind); una reserva o liberación usa su propio identificador como
documento. El tipo de documento es canónico (`^[a-z][a-z0-9-]{0,31}$`), el mismo que exige
`InventoryLocks`. En Ship, el consumidor indica `OwnReservedToConsume` = min(Q, Rpropia)
a partir de sus reservas (HU-03); el núcleo comprueba que no excede el reservado del bucket.

`StockArticleUsageContributor` es a la vez `IArticleUsageContributor` e
`IArticleFirstReferenceContributor` para Lot, StockBalance y StockEntry. Con saldo, lote o
entrada ⇒ HasHistory. Si `H0_StockCore` aún no está aplicada (ventana HU-08 Expand), no hay
tablas y devuelve sin uso. Pendiente para las historias: el lector `IArticleMovementReader`
del historial de artículo, CustomerOrder/Reservation (HU-03/HU-05), congelación de conteos
y ajustes (HU-04), alertas (HU-06) y correcciones.

## Cálculo de salida

Para cada bucket se usa Q = cantidad a servir, Rpropia = reserva remanente del pedido,
F = físico, R = reservado total, D = F − R.

- Consumir = min(Q, Rpropia).
- SinReserva = Q − Consumir; debe cumplirse SinReserva ≤ D y Q ≤ F.
- NuevoF = F − Q; NuevoR = R − Consumir.

Ejemplo: F=10, R=7 (propia 4, ajena 3), D=3. Servir Q=6 es válido: consume 4 de
la reserva propia y 2 disponibles; quedan F=4, R=3, D=1. Servir Q=8 se rechaza.
Además se respeta el pendiente de la línea del pedido. Repetir la confirmación no cambia nada.
Si las reservas remanentes del propio pedido en otros buckets excedieran su nuevo pendiente,
se rechaza con ReservedStockConflict: liberar/reasignar explícitamente antes de servir desde
un origen alternativo. No se dejan reservas sin pedido pendiente ni se liberan sin auditoría.

## Recepción y compra

La confirmación bloquea la compra y la recepción antes de ubicaciones/artículos. Revalida
pendientes bajo ese bloqueo: dos parciales de 6 no pueden consumir un pendiente de 10.
AcceptedQuantity se distribuye exactamente en destinos válidos. En un commit incrementa
el acumulado de la compra, deriva su estado, crea movimientos y saldos y evalúa alertas.
No cambia el estado de compra si el correo aún no fue aceptado y el pedido sigue Draft.

## Conteo sin transacción humana abierta

Inicio: bloquear documento y ubicaciones; rechazar solapamiento; guardar congelación y
teórico, y terminar la transacción. Incluir todos los lotes actuales y permitir registrar
hallazgos físicos nuevos durante el conteo. Todos los escritores comprueban la congelación.

Mientras se cuenta: no recepciones, salidas ni nuevas reservas en esas ubicaciones.
Se permite liberar compromisos para resolver físico contado inferior a reservado,
con motivo y auditoría. No se levanta automáticamente la congelación por timeout.

Confirmación: bloquear el mismo alcance y todos los artículos ajustados; exigir permiso
de ajuste, motivo y aprobación persistida. Revalidar reservado y físico teórico; calcular
diferencia, añadir movimientos y liberar el alcance en un commit. Si falta resolver una
reserva, conservar el conteo y el alcance congelado, mostrando el conflicto.
Cancelar explícitamente conserva el conteo histórico, no modifica saldos y libera alcance.

## Correcciones y contingencia

Una corrección agrega movimiento compensatorio enlazado, nunca modifica el diario original.
Si afecta recibido o expedido, sus acumulados y estado se corrigen en el mismo commit.
No se revierte stock ya consumido o comprometido sin resolver antes los documentos afectados.

Ante caída se usa comprobante físico con identificador único, responsable, fecha y detalle
incluidas ubicaciones/lotes. Se suspenden operaciones ordinarias sobre su alcance pendiente
de conciliación. Tras recuperar el servicio, un usuario autorizado incorpora el comprobante
por los mismos comandos y verifica saldos antes de reabrir ese alcance.

## Consulta y recuperación de resultado incierto

Las consultas no leen cambios sin commit ni usan NOLOCK. Para una pantalla se obtiene un
snapshot consistente. Si se pierde la respuesta tras confirmar, la UI muestra «resultado
pendiente de comprobar» y consulta OperationId. Si aún no hay resultado, no interpreta
ausencia como fallo definitivo: repite con la misma clave o espera al comando en curso.
El lock de operación serializa intentos iguales; la unicidad documental evita duplicados
con una clave distinta. Ningún mensaje de éxito antecede al commit.
