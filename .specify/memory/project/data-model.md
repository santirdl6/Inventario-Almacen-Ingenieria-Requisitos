# Modelo de datos común

**Fecha**: 2026-10-25 · **Base**: [plan global](../project-plan.md), [decisiones](research.md).
Modelo de diseño; no contiene migraciones ni afirma que las tablas ya existan.

## Convenciones e invariantes

- UUID como identificador; códigos de negocio únicos normalizados. Campos obligatorios
  NOT NULL; cadenas con longitud limitada y espacios extremos eliminados.
- Cantidades decimal(18,3), precios decimal(18,2), moneda única configurada en el almacén.
  Cada artículo define unidad base y si admite fracciones; no se permiten conversiones
  implícitas. La unidad base, el seguimiento por lote y la admisión de fracciones solo
  pueden cambiarse si el artículo no tiene documentos, reservas ni movimientos asociados;
  la primera referencia, incluso en un documento en borrador, bloquea los tres campos.
- Fechas datetimeoffset en UTC; distinguir OccurredAt (hecho) y RecordedAt (registro
  del servidor). ActorId se obtiene de Identity; nunca del cuerpo HTTP.
- rowversion en agregados editables y saldos. Es token opaco de concurrencia, no una fecha.
- Foreign keys con borrado restringido en documentos, movimientos y referencias históricas.
  Los documentos guardan instantáneas de código, descripción, unidad y contraparte relevantes.
- El saldo nace en cero y solo cambia mediante el protocolo de [stock](contracts/stock-operations.md).
  Stock inicial, correcciones e importaciones generan movimientos; no se insertan saldos sueltos.
- Por bucket: Physical ≥ 0; Reserved ≥ 0; Reserved ≤ Physical;
  Available = Physical − Reserved. Available se calcula, no se mantiene como tercer contador.
- Physical coincide con la suma de efectos físicos del diario; Reserved con la suma de
  reservas remanentes, y con los efectos reservados del diario. Se verifica por conciliación.

## Datos maestros — Persona A / HU-08

### Family

Id, Code único, Name, IsActive, Version. Una familia tiene varios artículos; un artículo
pertenece a una familia. No se elimina una familia referenciada; puede desactivarse.

### Supplier

Id, Code único, Name, Email, TaxIdentifier opcional, IsActive, Version.
Email válido y obligatorio para enviar pedidos. Un proveedor tiene varios pedidos.
No se borra si tiene documentos referenciados; sus cambios no reescriben las instantáneas.

### Article

Id, Sku único, Name, FamilyId, BaseUnit, AllowsFractional, TracksLot, IsActive,
IsDefinitionLocked, Version.
Crear el artículo crea su StockPolicy con mínimo cero, para que GET/PUT tenga versión inicial.
No se elimina con historial de stock; tampoco con documentos o reservas referenciados.
La desactivación conserva todos los enlaces y se audita. Reglas de operaciones pendientes
en R-05; el backend comprueba el estado bajo el bloqueo compartido de artículo.

Concreción de diseño HU-08: IsDefinitionLocked parte de false y se fija a true junto con
la primera referencia documental, reserva o movimiento, incluso en borrador. Es monotónica:
retirar una línea no restablece false. La edición de unidad/lotes/fracciones exige marca
false y ausencia de referencias reales; la marca sola no impide el borrado si ya no hay
referencias ni historial. Todos los escritores de referencias participan bajo el mismo
bloqueo y transacción. Esta columna se añadió a la misma Article en el corte HU-08 H1
del 2026-11-17, junto con familia obligatoria y política cero. La integración de los
escritores documentales/stock futuros sigue pendiente; evidencia en
[US2](../../../specs/002-gestionar-datos-maestros/validation/us2.md).

**Base técnica H0 A → B (2026-11-12)**: implementados `Article` y tabla `Articles` con
Id, Sku, Name, BaseUnit, AllowsFractional, TracksLot, IsActive y Version. La migración
`20261112105657_H0ArticleReference` no crea todavía FamilyId ni StockPolicy; la asociación
obligatoria a familia y el mínimo inicial cero siguen siendo requisitos de las altas
de HU-08. No hay endpoint de alta provisional ni seed de artículos de negocio.
Al añadir la familia, HU-08 deberá asignar explícitamente una familia válida a cualquier
referencia H0 existente antes de exigir el campo, sin borrar ni sustituir artículos.

En esta base, Id se genera en la aplicación. Sku admite 64 caracteres, Name 200 y BaseUnit
32; se eliminan espacios extremos y Sku/BaseUnit se convierten a mayúsculas. El índice
único de Sku usa comparación SQL sin distinguir mayúsculas; campos obligatorios no vacíos
y rowversion se comprueban también en SQL. B referencia `Articles.Id` desde Lot,
StockBalance y StockEntry con borrado restringido; no duplica Article ni aplaza sus claves
foráneas. La revisión cruzada de esta preparación se registra antes de integrar los cambios.

### MasterDataAudit

Id, EntityType, EntityId, Action, Before, After, ActorId, RecordedAt, OperationId.
Registro append-only para altas, modificaciones y desactivaciones. No guarda secretos.

HU-08 concreta acciones Create/Update/Deactivate/Reactivate/Delete; Before/After son JSON,
nulos según la acción. EntityId no tiene FK al maestro, para conservar evidencia de un
borrado permitido; ActorId y OperationId tienen referencias restrictivas a Identity y al
resultado común. El rol SQL de ejecución no puede UPDATE/DELETE sobre la auditoría.
EntityType admite Family, Supplier, Article y Location, con el mismo escritor append-only.

## Dimensiones y existencias — Persona B / HU-02, HU-05

### Location

Id, Code único, Aisle, Rack, Level, Kind (Storage o Receiving), IsActive, Version.
Pasillo/estantería/nivel obligatorios incluso para una ubicación temporal identificada.
La combinación Aisle/Rack/Level normalizada es única, incluidas inactivas. Code y partes
solo admiten A–Z, 0–9 y guion en mayúsculas, con CHECK binario. Code/Aisle/Rack/Level
quedan bloqueados desde el primer movimiento; Kind solo cambia vacía y sin reservas
ni conteo abierto. No puede retirarse la última Receiving activa. HU-02 no ofrece DELETE.
No se desactiva con stock, reservas o un conteo abierto. No se borra con historial.
Un artículo puede aparecer en cualquier número de ubicaciones.

**Base técnica H0 B (2026-11-18)**: `Domain.Locations.Location` y tabla `Locations` con
todos los campos anteriores; Code (32) único sin distinguir mayúsculas, partes de 16
caracteres normalizadas a mayúsculas. Sin endpoints ni reglas de desactivación: HU-02.
Lot, StockBalance y StockMovement/StockEntry se crean en `20261118194940_H0_StockCore`,
después de `HU08Suppliers` y sobre la base común de A: no hay otra tabla de resultados.
`StockMovements.OperationId` tiene FK restrictiva a `OperationResults` y ActorId a
AspNetUsers. El rol `inventory_runtime` no tiene UPDATE/DELETE sobre StockMovements y
StockEntries, ni DELETE sobre StockBalances (aprobado por A): un saldo nunca se borra, de
modo que tener saldo equivale a tener historial.

### Lot

Id, ArticleId, Code, IsUntracked. Unicidad (ArticleId, Code).
Cada artículo sin seguimiento tiene un bucket SIN-LOTE; para artículos con seguimiento
se exige código real, y no se usa SIN-LOTE. Todas las filas de StockBalance tienen LotId
no nulo. No hay series, caducidad ni selección FEFO en esta base.

### StockBalance

ArticleId, LocationId, LotId, Physical, Reserved, Version.
Clave única (ArticleId, LocationId, LotId); LotId debe pertenecer al mismo ArticleId,
mediante FK compuesta hacia Lot. CHECK de cantidades y Reserved ≤ Physical.
La unicidad cubre también el primer saldo creado concurrentemente.

Índices previstos: artículo/ubicación/lote para consulta; ubicación/artículo para conteos.
Consulta global y filtrada agrega Physical y Reserved y calcula Available después de sumar.
No debe restar dos veces las reservas ni usar un total global con un filtro de lote local.

## Compras y recepciones — Persona A / HU-07, HU-01

### PurchaseOrder y PurchaseOrderLine

Cabecera: Id, Number único, SupplierId, SupplierSnapshot, State, SendStatus,
ExpectedDeliveryDate, CreatedBy, CreatedAt, SentAt opcional, Version.

Línea: Id, PurchaseOrderId, ArticleId, ArticleSnapshot, OrderedQuantity,
ReceivedQuantity, UnitPrice, ExpectedDeliveryDate. Cantidad pedida > 0; precio ≥ 0;
0 ≤ ReceivedQuantity ≤ OrderedQuantity; Pending = OrderedQuantity − ReceivedQuantity.
El acumulado recibido coincide con las recepciones confirmadas y sus correcciones válidas.

Transiciones:

- Draft → Sent: el worker confirma aceptación del correo por SMTP.
- Sent → PartiallyReceived: existe recepción y queda pendiente.
- Sent o PartiallyReceived → Received: todas las líneas tienen pendiente cero.
- SendStatus: None → Pending → Accepted; error agotado → Failed, con reintento explícito.
  Pending y Failed no convierten el pedido en enviado. Una vez creada la intención de
  envío se bloquea la edición, incluso en Failed: el reintento conserva la misma instantánea.
  Solo Draft con SendStatus None permite editar. Un pedido enviado ya no edita sus líneas.

El comando de envío guarda la instantánea del documento y el outbox atómicamente.
SMTP no se ejecuta dentro de la transacción. Correcciones posteriores a una recepción
deben recalcular estado y pendientes bajo el bloqueo del pedido; no se elimina la recepción.

Concreción de diseño HU-07 (2026-11-20, revisión A+B pendiente): fechas previstas son
fechas civiles SQL date; instantes de hechos/registro siguen UTC. Currency única se
captura en el pedido. Las líneas tienen UUID estable independiente de ArticleId y pueden
repetir artículo. Snapshots solo cambian por reasignación explícita mientras Draft/None.
Cada cambio de líneas/acumulados/estado representado toca la cabecera para avanzar su
rowversion; HU-01 debe conservar esa propiedad al confirmar/corregir recepciones.
PurchaseOrderLine aporta contributor real de uso/primer enlace HU-08. Mappings, auditoría
de compras y reglas particulares en el
[modelo HU-07](../../../specs/007-gestionar-pedidos-proveedores/data-model.md).

### Receipt, ReceiptLine y ReceiptAllocation

Cabecera: Id, PurchaseOrderId, SupplierDocumentReference opcional, State
(Draft/Confirmed), OccurredAt, RecordedAt, ActorId, OperationId, Version.

Línea: Id, ReceiptId, PurchaseOrderLineId, ReportedQuantity, AcceptedQuantity,
DiscrepancyReason opcional. Reported ≥ 0; Accepted ≥ 0; Accepted ≤ Reported.
Al menos una línea de la recepción debe aceptar cantidad positiva. Se registra la diferencia
entre lo pendiente antes de recibir y lo aceptado; una entrega parcial conserva pendientes.

Asignación: Id, ReceiptLineId, LocationId, LotId, Quantity > 0.
La suma de asignaciones coincide con AcceptedQuantity. No se confirma con destino desconocido.
Solo AcceptedQuantity aumenta stock y recibido acumulado; excesos, faltas o sustituciones
observados quedan como incidencia. Un artículo ajeno al pedido no se acepta como stock.
Una recepción confirmada no se edita ni se confirma de nuevo.

## Compromisos y salidas — Persona B / HU-03, HU-05

### CustomerOrder y CustomerOrderLine

Referencia mínima local del pedido: Id, ExternalReference única, CustomerReference,
State (Open/Fulfilled/Cancelled), Version; líneas con Id, CustomerOrderId, ArticleId,
OrderedQuantity > 0 y ShippedQuantity entre cero y lo pedido.
Una línea por artículo: (CustomerOrderId, ArticleId) es único (HU-05, clarificación 1).
ExternalReference se compara sin distinguir mayúsculas ni espacios extremos.
Cada cambio de reservas o servido toca la cabecera para renovar su rowversion (ETag).
No incluye precios comerciales, facturación ni integración ERP automática.
Su alta mínima controlada permite vincular y probar las salidas de las historias.

Responsabilidad revisada A+B, 2026-11-19: HU-05 implementa el alta y consulta de este
pedido junto con reservar/liberar; HU-03 consume las mismas entidades para salidas y
actualiza ShippedQuantity y el estado derivado en su confirmación atómica. No se crea
un segundo pedido ni se cambian los estados/identidades por adelantar el soporte a H2.
Editar/cancelar pedidos no forma parte del alta/consulta mínima de la entrega HU-05;
Cancelled se conserva en el modelo para evolución posterior.

### Reservation y ReservationAllocation

Cabecera: Id, CustomerOrderLineId, State (Active/Consumed/Released), Version.
Asignación: Id, ReservationId, ArticleId, LocationId, LotId, AllocatedQuantity,
ConsumedQuantity, ReleasedQuantity. Remaining = Allocated − Consumed − Released ≥ 0.
Una asignación por bucket y reserva: (ReservationId, ArticleId, LocationId, LotId) único;
LotId con FK compuesta (ArticleId, LotId) como StockBalance. Remaining es columna calculada.
Pedidos, líneas, reservas y asignaciones no se borran (DENY DELETE a inventory_runtime).
Cada liberación toca la cabecera de la reserva (ETag) y la del pedido.

Las asignaciones remanentes suman Reserved por bucket; nunca exceden lo pendiente del
pedido descontando otras reservas activas. Reservar aumenta Reserved, no Physical.
Liberar disminuye Reserved sin cambiar Physical. Estado derivado: Active si queda remanente;
Consumed si no queda y se consumió todo; Released si no queda y hubo liberación.

### Shipment y ShipmentLine

Cabecera: Id, CustomerOrderId, State (Draft/Confirmed), OccurredAt, RecordedAt,
ActorId, OperationId, Version.
Línea: Id, ShipmentId, CustomerOrderLineId, ArticleId, LocationId, LotId,
Quantity > 0, OwnReservationConsumed entre cero y Quantity.

Cantidad no superior al pendiente del pedido. Physical disminuye en Quantity;
Reserved disminuye solo en OwnReservationConsumed. La parte sin reserva debe caber
en Available. No se consumen reservas de otro pedido ni se descuenta dos veces la propia.
Después de servir, el remanente de reservas de la línea no puede superar su nuevo pendiente;
si el origen elegido deja reservas sobrantes en otra ubicación, se requiere liberarlas
o cambiar la asignación antes de confirmar, sin liberación silenciosa.

Concreción de HU-03 (P-01, aprobada por A el 2026-11-21;
[modelo de HU-03](../../../specs/005-registrar-salida-mercancia/data-model.md)):

- Un borrador puede eliminarse completo: no deja movimientos, porque nunca los tuvo, y retira sus
  referencias al artículo. Una salida confirmada es inmutable (triggers `TR_Shipments_ConfirmedImmutable`
  y `TR_ShipmentLines_ConfirmedImmutable`, solo `THROW`, en lugar de `DENY DELETE`) y no se revierte:
  las diferencias se corrigen con el ajuste de HU-04, que corrige el físico pero no lo servido ni el
  estado del pedido.
- `OperationId` es obligatorio solo en `Confirmed` (único filtrado).
- Único `(ShipmentId, CustomerOrderLineId, LocationId, LotId)`: un bucket por línea de pedido en cada
  salida.
- `OwnReservationConsumed` no se almacena: es Σ de `ShipmentReservationConsumption` de la línea.

### ShipmentReservationConsumption

P-02 de HU-03, aprobada por A el 2026-11-21 con el detalle por asignación.
Id, ShipmentLineId, ReservationAllocationId, Quantity > 0, OperationId. Único
`(ShipmentLineId, ReservationAllocationId)`. Append-only (`DENY UPDATE, DELETE`); solo se inserta al
confirmar, con el `OperationId` de la operación que confirma la salida.

La cabecera declara al confirmar, en la misma sentencia que la pasa a `Confirmed`,
`DeclaredConsumptionCount` y `DeclaredConsumedQuantity` (inmutables). Los triggers rechazan un
consumo de otra operación y cualquier consumo que supere lo declarado, también con el mismo
`OperationId`, en cualquier orden de sentencias dentro de la confirmación.

**Límite (HU-03 D-06)**: sin restricciones diferidas, SQL impone solo el **máximo**: ningún consumo
supera lo declarado. Que la confirmación por API inserte **exactamente** lo declarado lo comprueba la
prueba H-14 de HU-03 (invariante 8) en cada confirmación, no la base de datos.

## Conteos — Persona B / HU-04

### CycleCount, CountLocation y CountLine

Cabecera: Id, State (Draft/Counting/AwaitingApproval/Confirmed/Cancelled), CreatedBy,
StartedAt, ApprovedBy opcional, ApprovedAt opcional, Version.
CountLocation: CountId, LocationId, IsFrozen; índice único filtrado por LocationId
cuando IsFrozen = true para impedir dos conteos activos sobre la misma ubicación.

CountLine: Id, CountId, ArticleId, LocationId, LotId, TheoreticalPhysical,
CountedPhysical ≥ 0, Difference, Reason, CountedBy, CountedAt.
Difference = CountedPhysical − TheoreticalPhysical. Se incluyen buckets esperados
y artículos/lotes hallados físicamente que no aparecían en el stock teórico.

Al iniciar se congelan ubicaciones completas y se captura el teórico, bajo bloqueos.
No hay transacción abierta mientras el trabajador cuenta. Se bloquean recepciones,
salidas, traslados y nuevas reservas sobre el alcance; sí puede liberarse una reserva
para resolver una discrepancia antes de cerrar. El almacén suspende también el movimiento físico.

Draft → Counting → AwaitingApproval → Confirmed; Draft/Counting/AwaitingApproval
pueden cancelarse explícitamente sin actualizar stock, liberando el alcance.
El ajuste exige motivo, identidad de quien cuenta y autorización de una cuenta con
permiso Inventory.Adjust; autorización registrada aunque la misma persona tenga ambos permisos.
No se confirma si el físico contado quedaría por debajo del reservado remanente.
Confirmar añade movimientos de ajuste y libera congelación en la misma transacción.

## Diario y operaciones — común, B propone / A integra

### StockMovement y StockEntry

Movimiento: Id, Kind (Receipt/Shipment/Adjustment/Reservation/Release/Correction),
SourceDocumentType, SourceDocumentId, OriginalMovementId opcional, Reason,
ActorId, OccurredAt, RecordedAt, OperationId, SupportReference opcional,
SupportNotApplicableReason cuando no existe soporte externo, CounterpartyReference
o CounterpartyNotApplicableReason cuando no hay contraparte externa.

CounterpartyNotApplicableReason es nvarchar(500) nullable. Los comandos normalizan
vacíos a null. Para movimientos nuevos se exige exactamente un valor: la referencia
cuando existe contraparte o el motivo cuando no aplica.
`20261119171332_HU08MovementCounterparty` añadió el motivo. La migración posterior
`20261120102651_HU08ExclusiveMovementCounterparty` sustituye la CHECK por la versión
exclusiva y conserva todas las filas confirmadas, incluidas las que guardaron ambos campos.

`WITH NOCHECK` mantiene habilitada la restricción para las inserciones nuevas y deja
`is_not_trusted = 1` en SQL Server. Revalidar con `WITH CHECK CHECK CONSTRAINT` requiere
comprobar previamente la compatibilidad de todo el historial y respetar su inmutabilidad.
La ausencia histórica se presenta como «Contraparte no registrada».
DENY UPDATE/DELETE sigue vigente.

Entrada: Id, MovementId, ArticleId, LocationId, LotId, Quantity > 0,
BaseUnitSnapshot, Effect, PhysicalDelta, ReservedDelta.

| Effect | PhysicalDelta | ReservedDelta |
|--------|---------------|---------------|
| Receive / AdjustUp | +Quantity | 0 |
| ShipAvailable / AdjustDown | −Quantity | 0 |
| Reserve | 0 | +Quantity |
| Release | 0 | −Quantity |
| ShipReserved | −Quantity | −Quantity |

Una salida mixta tiene entradas separadas de ShipReserved y ShipAvailable, sumando
la cantidad expedida. Una diferencia cero no genera entrada de cantidad cero.
Las correcciones enlazan al original, indican motivo y usan efectos compensatorios
permitidos por las invariantes actuales. En caso de dependencias consumidas, se rechaza
la reversión ciega y se exige resolver la cadena de documentos afectados.

Diario solo de adición; rol SQL de ejecución sin UPDATE/DELETE sobre sus tablas.
Confirmar recepción, salida o conteo tiene unicidad por tipo/documento/Confirm;
crear reserva tiene unicidad por ReservationId. Liberaciones parciales y correcciones
son hechos distintos con ReleaseId/CorrectionId propios: cada uno se aplica una sola vez,
sin impedir varias liberaciones legítimas de una misma reserva. Auditoría de reservas
conserva el histórico reservado reconstruible.

### OperationResult

OperationId UUID único, ActorId, RequestHash, Method, Resource, RequiredPermission,
ResultStatus, ResultBody, ResultHeaders, SourceDocumentId, RecordedAt. Solo se persiste junto a un commit exitoso.
No se purga en la base inicial. La consulta requiere autorización sobre el recurso original.
La ausencia de fila no prueba por sí sola un fallo: una transacción puede seguir ejecutándose.

OperationId coincide con Idempotency-Key. ResultBody admite null —por ejemplo DELETE 204—;
ResultHeaders conserva JSON con ETag/Location reproducibles, nunca cookies ni tokens.
RequiredPermission permite reautorizar la consulta aun cuando el recurso ya se eliminó.
SourceDocumentId es referencia histórica sin FK que impida ese borrado. Resolver replay
antes de cargar el recurso o evaluar If-Match, devolviendo el resultado histórico guardado.
Los resultados confirmados son append-only también para el usuario SQL de ejecución.

### ContingencyReference

Id externo único del comprobante, Type, OccurredAt, ResponsibleReference,
Details, ReconciledOperationId, ReconciledAt. La incorporación invoca los mismos comandos
y el mismo protocolo, no edita saldos. Evita duplicar hechos registrados durante una caída.
Procedimiento de soporte de los movimientos existentes; no crea una novena historia funcional.

## Reposición y mensajería — Persona A / HU-06, HU-07

### StockPolicy y StockAlert

StockPolicy: ArticleId único, MinimumAvailable ≥ 0, Version.
StockAlert: Id, ArticleId, State (Active/Resolved), PhysicalSnapshot, ReservedSnapshot,
AvailableSnapshot, MinimumSnapshot, SuggestedQuantity, OpenedAt, UpdatedAt, ResolvedAt.
Índice único filtrado para un solo episodio Active por artículo.

Un mínimo cero no alerta con disponibilidad no negativa. Si Available < Minimum,
crea/actualiza episodio; al recuperar el mínimo lo resuelve. Sugerencia = Minimum − Available
si positiva. Cambios de saldo, reserva o mínimo evalúan la política en la misma transacción.
La desactivación resuelve el aviso y suspende nuevas alertas; reactivación reevalúa la política.

### OutboxMessage

Id, Type (PurchaseOrderEmail), DocumentId, PayloadSnapshot, MessageId estable,
State (Pending/Processing/Sent/Failed), Attempts, NextAttemptAt, LeaseUntil,
LastError, CreatedAt, SentAt. Unicidad de intención por pedido y versión de envío.
Worker reclama con lease para recuperación tras caída; nunca guarda credenciales en Payload.
Entrega al menos una vez; tras aceptación SMTP puede repetirse si se pierde el acuse local.

Concreción HU-07: una PurchaseOrderDelivery inmutable por pedido en este alcance conserva
payload/documento/MIME, destinatario/remitente, MessageId y autorización originales;
OutboxMessage la referencia 1:1 y mantiene autoridad activa, ciclo/contadores, owner,
generación de lease e incertidumbre. Retry manual crea otra OperationResult pero conserva
Delivery/MessageId/documento. PurchaseOrderAudit y PurchaseDeliveryEvent son append-only;
Delivery también deniega UPDATE/DELETE al rol runtime. No modificar OperationResult para
reflejar progreso. El [modelo local](../../../specs/007-gestionar-pedidos-proveedores/data-model.md)
detalla FK/constraints y evolución aditiva implementadas localmente mediante
`20261120111422_HU07PurchasingOutbox`; evidencia de migración en
[foundation](../../../specs/007-gestionar-pedidos-proveedores/validation/foundation.md), revisión A+B pendiente.

## Identidad y permisos — común

Tablas de ASP.NET Core Identity con claves Guid; roles Operario, ResponsableAlmacen,
Comprador, GestorMaestros y Administrador. Políticas por operación, no autorización
basada únicamente en esconder botones. Usuarios históricos no se borran si son actores
referenciados; se deshabilita su acceso. Permisos y errores en [API](contracts/api.md).

H0 implementa `ApplicationUser`, `IdentityRole<Guid>` y las tablas estándar AspNetUsers,
AspNetRoles, AspNetUserRoles, AspNetUserClaims, AspNetRoleClaims, AspNetUserLogins y
AspNetUserTokens. La migración `H0Identity` amplía la migración inicial sin modificarla.
Correo único validado por Identity, contraseñas hasheadas y bloqueo de acceso tras fallos.
Roles y cuenta administrativa se siembran de forma repetible durante la inicialización.
Las policies derivan los permisos del catálogo de roles en servidor, sin tabla de permisos
adicional. No hay altas, borrados o cambios de roles públicos en esta base.

`ICurrentActor` ofrece `Task<Guid> GetRequiredActorIdAsync(CancellationToken)` desde Domain.
La implementación Identity comprueba sesión autenticada, claim de identidad Guid no vacío
y usuario existente. Sin contexto HTTP o usuario válido lanza `UnauthenticatedActorException`;
el transporte HTTP lo convierte en 401 con code `Unauthenticated`. ActorId nunca se toma
del cuerpo del comando. Los fixtures pueden sustituir la interfaz; si hay claves foráneas
a usuarios, el actor de pruebas debe existir en Identity.

## Evolución y conciliación

Una cadena de migraciones compartida, con índices/constraints incluidos. No hay cambios
de esquema automáticos al arrancar producción. Consultas de conciliación comparan diario,
saldos, reservas, recibidos y expedidos; una diferencia bloquea la aceptación y se resuelve
con investigación y corrección trazable, nunca mediante UPDATE manual del saldo.

Las migraciones preparan el rol estable `inventory_runtime`; el bootstrap añade al usuario
de ejecución después de crearlo. DENY UPDATE/DELETE de tabla sobre diario, auditoría y
resultados confirmados se aplica al rol, sin embeber el nombre configurable de un login.
Esta extensión de diseño se integra con B; no afirma que el rol ya exista en H0.
