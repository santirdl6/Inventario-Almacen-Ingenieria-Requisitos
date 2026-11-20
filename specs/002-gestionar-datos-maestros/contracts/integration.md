# Integración de HU-08 con H0 y las features consumidoras

**Fecha de diseño**: 2026-11-12. **Implementación común/familias/Article H1**: 2026-11-17,
verificada localmente; revisión A+B pendiente antes de integrar.
**Fuentes autoritativas**: [plan global](../../../.specify/memory/project-plan.md),
[modelo común](../../../.specify/memory/project/data-model.md),
[protocolo de stock](../../../.specify/memory/project/contracts/stock-operations.md).
Las operaciones de stock conservan sus estados, efectos y orden global.

## Responsabilidad y precedencias

| Entrega | Responsable | Consumo por HU-08 |
|---------|-------------|------------------|
| Article H0, ICurrentActor y sesión | A; ya presentes | Ampliar la misma entidad; reutilizar actor y permisos. |
| Ejecutor transaccional, applocks y OperationResult común | A adelanta la base por autorización del usuario el 2026-11-16; B la revisa y consume | Un único componente en Infrastructure/Persistence/Operations; no implementar otra alternativa en MasterData o Stock. Revisión conjunta pendiente antes de integrar. |
| Family, Supplier, auditoría, ampliación Article y StockPolicy inicial | A, HU-08 | Modelos, configuraciones, handlers, HTTP y UI verticales. |
| Lectores de uso e historial | Cada feature aporta sus consultas; A compone el endpoint | B: stock/reservas/salidas/conteos. A: compras/recepciones y efectos de mínimos. |
| Sesión/layout frontend compartidos | B, base común | Reutilizar si están integrados; acordar con B el mínimo necesario si A debe adelantarlo para HU-08. |
| Evaluación de alertas tras cambio de estado | A, HU-06 | Invocar dentro de la transacción cuando HU-06 esté integrada. |

Se puede iniciar modelos, catálogos y pruebas H1 sin todas las historias. No se habilitan
mutaciones sin ejecutor común real, ni se acepta integración de referencias con un lector
que siempre responde «sin uso». La parte integrada no se declara terminada por usar dobles.

## Servicio transaccional común

Contrato semántico del ejecutor, cuya implementación pertenece a la infraestructura común:

- Recibe intención estable, método/ruta canónicos, payload normalizado y permiso requerido.
- Crea scope/InventoryDbContext limpio por intento. ICurrentActor se resuelve dentro de
  ese scope con la petición autenticada; todos los participantes usan ese mismo contexto.
- Autoriza e identifica al actor; adquiere `operation:{uuid}`; recupera replay o conflicto.
- En una operación nueva, bloquea documentos, ubicaciones y artículos en el orden común,
  relee condiciones, evalúa If-Match y ejecuta el handler. Tiene como máximo tres intentos
  completos y timeout de bloqueo de 5 segundos, conforme a la base global.
- Almacena resultado HTTP reproducible y evidencia junto con los cambios; confirma una
  sola vez. No hace llamadas HTTP internas ni devuelve éxito antes del commit.

HU-08 necesita preservar ETag/Location y el permiso original en OperationResult; las
extensiones se describen en el modelo global para que B no genere un esquema incompatible.
El cliente usa el UUID de Idempotency-Key como OperationId antes de conocer el resultado.

## Recursos de bloqueo de maestros

Family y Supplier se tratan en la fase de documentos con los recursos comunes
`document:family:{uuid}` y `document:supplier:{uuid}`; UUID canónico D en minúsculas.
Los bloqueos documentales se ordenan por recurso canónico con comparación ordinal; artículos
y ubicaciones por su UUID canónico con el mismo comparador. No crear otra convención en B.

- Crear/reasignar Article: operación → familia destino → artículo. Releer familia y artículo;
  solo exigir familia activa si es una nueva asignación.
- Editar/desactivar/reactivar Family: operación → familia. No bloquear todos sus artículos
  ni desactivarlos en cascada; una nueva asignación usa ese mismo recurso de familia.
- Supplier: operación → proveedor; futuros escritores de compras que deban validar estado
  lo coordinan dentro de su conjunto ordenado de documentos, antes de artículos.
- Borrar/cambiar definición/estado de Article: operación → artículo y los recursos previos
  necesarios. Una referencia documental nueva mantiene su documento antes del artículo.

Una condición descubierta que necesite otro recurso de una fase previa obliga a descartar
el intento y replanificarlo. No adquirir familia/proveedor después de bloquear artículos.

## Primera referencia y lectores de uso

La operación que incorpora una referencia llama al comportamiento compartido de Article
para fijar IsDefinitionLocked=true, antes del commit y bajo su bloqueo. Es idempotente;
el estado nunca vuelve a false. Se aplica también al agregar/reemplazar líneas de borrador,
reservas y hallazgos de conteo, sin esperar a confirmar un movimiento físico.

**Gate anterior al endpoint de edición de definición**: comprobar, para cada consumidor
presente, su lector de uso y el enlace de primera referencia probado con su escritor real.
La existencia de un lector por sí sola no acredita que el escritor active la marca.
Si falta alguno, rechazar el cambio de unidad/lotes/fracciones con DependencyUnavailable;
un PUT sin cambios de definición puede seguir las demás reglas aplicables. Cuando no hay
consumidores, acreditar esa ausencia en el modelo/registro y permitir el corte H1.
Las nuevas features deben aportar lector, enlace y pruebas antes de habilitar sus escritores,
sin exigir que todas las historias futuras estén entregadas para completar ese gate de H1.

El lector scoped de uso devuelve dos hechos autoritativos: existencia de referencias que
impiden borrar y existencia de historial; la marca de definición se consulta en Article.
Los consumidores registran sus consultas reales en el mismo contexto/transacción. Un
control de cobertura comprueba las FK consumidoras del modelo integrado: cada una debe
estar cubierta por un lector o declarada propiedad interna —StockPolicy—, nunca omitida
silenciosamente. Un consumidor presente sin lector bloquea el arranque/habilitación de
mutaciones con DependencyUnavailable; un error SQL se propaga, no se transforma en false.

El registro vacío solo es válido en el modelo H1 que no contiene consumidores documentales
ni de stock; no es una implementación permisiva intercambiable. Las FK restrictivas siguen
siendo una segunda defensa. Las pruebas de carreras utilizan consumidores reales integrados.

HU-08 no crea Lot ni StockBalance al dar de alta. Si una implementación de B crea dimensiones
vacías antes del primer uso, su integración debe permitir la limpieza segura de esas dimensiones
sin uso en el borrado legítimo, dentro del mismo contexto y bloqueo; no cambiar FR-011 para
que una fila técnica sin documentos ni historial impida permanentemente borrar el artículo.

## Historial y alertas

El proveedor de historial lee StockMovement/StockEntry comunes, incluidas sus instantáneas;
no filtra por Article.IsActive. La ausencia de filas devuelve una página vacía únicamente
si el proveedor y sus tablas están realmente integrados. Si no están, el endpoint devuelve
503 DependencyUnavailable y la interfaz explica la dependencia, sin simular historial vacío.

La política inicial cero no requiere crear alertas en H1. Al integrar HU-06, desactivar
resuelve el episodio y reactivar reevalúa la política, bajo el bloqueo/transacción comunes.
Si esa feature está presente pero falta su evaluador, el cambio de estado no puede confirmar
parcialmente. No crear otro cálculo de disponibilidad en HU-08.

**Gate H1 verificado, 2026-11-17**: el modelo actual solo tiene StockPolicy como propiedad
de Article, sin FK consumidoras documentales ni de stock. ArticleUsageReader valida esa
cobertura; ArticleDefinitionReadiness exige además igualdad con las declaraciones de enlaces
de primer uso. Una prueba de metadatos comprueba el rechazo de un consumidor con lector pero
sin enlace. ArticleReferenceTracker.MarkAsync recibe OperationContext, adquiere los bloqueos
de artículo y activa la marca en el mismo contexto sin commit autónomo. Los escritores futuros
deben invocarlo después de sus bloqueos previos y revalidar definición/estado antes de guardar.
La marca histórica preparada por SQL en pruebas **no acredita** crear/retirar referencias
reales ni sus carreras; esas pruebas permanecen en T035/T075/T076. Detalle en [US2](../validation/us2.md).

## Migraciones y revisión

Cadena actual: InitialInfrastructure → H0Identity → H0ArticleReference → H0OperationProtocol
→ HU08AuditInfrastructure → HU08Families → HU08MasterDataExpand → HU08RequireArticleFamily
→ HU08Suppliers.
B incorpora H0_StockCore sobre esta base,
sin recrear resultados/auditoría. Los cambios restantes HU-08 parten del snapshot integrado y del
[modelo y su transición](../data-model.md). Un solo propietario genera cada migración.
No fijar timestamps futuros ni reescribir migraciones compartidas. Si B ya aporta una
tabla común, A la referencia y no vuelve a crearla.

Rutas implementadas de la base: Infrastructure/Persistence/Operations contiene OperationResult,
InventoryOperationExecutor, InventoryLocks, OperationResponse, RequestFingerprint y el escritor
de auditoría; el callback recibe OperationContext con Database/Locks/ActorId/OperationId y
Services del mismo scope por intento. No resolver participantes con DbContext desde el scope
HTTP exterior. Los DTO se normalizan antes de la huella; sus propiedades se ordenan de forma
ordinal por el hasher común. Evidencia y nombres completos de migración en
[foundation](../validation/foundation.md).

La preparación de familia la ejecuta PrepareHu08FamilyMapHandler con sesión Identity y
MasterData.Manage, ICurrentActor y el mismo ejecutor, en la ventana de mantenimiento del
[contrato HTTP](api.md#mantenimiento-autenticado-de-la-transición-h0). Recibe solo el mapa,
nunca un actor manual. El proceso API conserva su conexión sin DDL; la expansión y el cierre
los aplica la herramienta de migración externa. Un DTO nullable de mantenimiento evita
materializar Article contra un esquema aún transitorio y conserva una sola transacción.

A+B revisan: marca de primer uso, recursos de bloqueo, cobertura de referencias,
OperationResult/cabeceras/permisos, rol SQL y transición de familia. El mínimo de H0 y la
aceptación final de maestros conservan sus alcances; las reglas completas de Ship/Reserve/
conteos permanecen en las historias responsables.


## Continuación local, 2026-11-18

Proveedores, borrado H1 de artículos y recuperación de operaciones están implementados.
La cadena añade `20261117104422_HU08Suppliers`; modelo/snapshot sin cambios pendientes.
Se ejecutaron 76 pruebas backend SQL y 36 unitarias frontend; evidencia en
[US3](../validation/us3.md), [US4](../validation/us4.md) y [US5](../validation/us5.md).
DELETE de Article conserva auditoría/resultado; no existe DELETE de Family/Supplier.

El contrato IArticleMovementReader está disponible para el único diario compartido; no se
registra un lector vacío. Sin núcleo de stock, movements devuelve 503 y la UI lo explica.
IArticleStateEffects recibe el mismo OperationContext para el futuro evaluador HU-06;
StockAlert presente sin evaluador rechaza el cambio. No se han integrado documentos,
reservas, movimientos ni alertas, y permanecen pendientes sus pruebas reales y la revisión B.
El estado consolidado de validación se registra en [aceptación](../validation/acceptance.md).

## Historial con el núcleo H0 de stock

`StockCoreRegistration` aporta `StockArticleMovementReader` al endpoint
`GET /api/v1/articles/{id}/movements`. El lector comparte el contexto y la transacción
snapshot de la consulta de artículos. Usa `StockEntry` y `StockMovement` de
`H0_StockCore`, con una fila por entrada del diario y el ID de la cabecera como
`movementId`. Una salida mixta conserva sus dos entradas. El filtro temporal
`[from,to)` opera sobre `RecordedAt`, que también ordena la página en sentido
descendente, con el ID de entrada ascendente como desempate.

La unidad procede de `BaseUnitSnapshot`. La ubicación se presenta como origen en
salidas, reservas y liberaciones, y como destino en recepciones. Las entradas de
ajuste usan origen o destino según su efecto. El lote técnico de artículos sin
seguimiento se presenta como ausencia de lote. Se expone la contraparte si el
diario la registró. El esquema H0 solo guarda la referencia de contraparte. El
motivo de no aplicabilidad de la contraparte queda pendiente en el modelo común.
El lector conserva la respuesta 503 si falta la migración, y
devuelve una página vacía cuando el diario existe y el filtro no encuentra entradas.

## Convergencia implementada, 2026-11-19

El árbol evaluado termina en `20261118194940_H0_StockCore`: no incorpora todavía la
migración ni endpoints HU-02, aunque existan ensayos previos de combinación con esa entrega.
`IArticleMovementReader.FiltersAsync` y `ArticleMovementQueries.FiltersAsync` aportan las
opciones legibles de T098 mediante consultas al mismo diario y snapshot; el contrato HTTP
particular define sus permisos y paginación. No cambia el modelo ni la cadena de migraciones.

T099 acredita ambos órdenes de primera referencia StockCore/edición de definición con
espera SQL observada, y T104 conserva diario/saldos en navegador. El auxiliar de preparación
está en `scripts/prepare-hu08-history.cs` y `Fixtures/Hu08HistoryE2EData.cs`, fuera de la API:
solo admite InventoryHu08E2E, sus cuentas controladas y el artículo específico del recorrido.
Las cantidades se generan con StockCore/InventoryOperationExecutor, nunca por DML de saldos.

T097 permanece bloqueada por la reserva del snapshot para B ratificada por el usuario.
La UI distingue contraparte no registrada, pero el diario aún no conserva su motivo de
no aplicabilidad. Los lectores/enlaces documentales de T100–T103 y el evaluador de T106
esperan a los consumidores reales. [Evidencia y bloqueos](../validation/convergence.md).

## Cierre técnico y recepción de nuevas integraciones, 2026-11-19

Este estado sustituye los bloqueos de la continuación anterior: el usuario solicita
terminar T097 y cerrar HU-08. La cadena termina ahora en
`20261119171332_HU08MovementCounterparty`, sobre H0_StockCore. CounterpartyNotApplicableReason
se conserva y proyecta desde el diario. Las filas históricas sin información permanecen
intactas y distinguibles; las nuevas requieren referencia o motivo. El replay histórico
se resuelve antes de exigir nueva evidencia. Las pruebas de migración/SQL lo acreditan.

Los módulos consumidores reciben las tareas en
[integration-tasks.md](../../001-reparto-specs-inventario/integration-tasks.md), con IDs
INT-HU01/03/04/05/06/07. Cada nuevo escritor integra y prueba su lector/marca/transacción
en su propio módulo antes de habilitarse; no reabre HU-08. La revisión de los cambios
compartidos se conserva en COORD-01, y las condiciones operativas, en H5. No se presenta
su traslado como revisión o prueba realizada.
