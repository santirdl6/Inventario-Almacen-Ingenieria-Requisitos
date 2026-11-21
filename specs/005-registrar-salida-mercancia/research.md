# Research: HU-03 — Registrar la salida de mercancía

**Fecha**: 2026-11-20 · **Plan**: [plan.md](plan.md) · **Spec**: [spec.md](spec.md)
**Decisiones comunes que se consumen, sin redefinirlas**:
[R-03 y R-04](../../.specify/memory/project/research.md#r-03-integridad-concurrencia-y-agregación-de-stock),
[R-05](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs),
[cálculo de salida](../../.specify/memory/project/contracts/stock-operations.md#cálculo-de-salida),
[modelo común](../../.specify/memory/project/data-model.md#shipment-y-shipmentline) y, de HU-05,
[research](../004-consultar-existencias/research.md) (D-04, D-07, D-08, D-11, D-13, D-15, D-16) y el
[contrato de integración](../004-consultar-existencias/contracts/hu03-integration.md).

No quedan incógnitas `NEEDS CLARIFICATION` en el contexto técnico: stack, ejecutor, bloqueos, núcleo
(`StockCore.PrepareShipment`, operación `Ship`), lector del pedido y transporte de intenciones del
frontend existen en el código. Las decisiones siguientes concretan cómo HU-03 los consume. Las
lecciones de HU-05 se aplican desde el diseño y se citan donde corresponde. Dos decisiones (D-06 y
D-08) las tomó B durante `/speckit-plan` (2026-11-20) entre las opciones indicadas.

## D-01. Ubicación del código: `Features/Shipping`

**Decisión**: `Features/Shipping/{ShipmentCommands,ShipmentQueries,ShipmentDtos,ShipmentEndpoints,
ShipmentArticleUsage}.cs` y entidades en `Domain/Shipping/Shipment.cs`. `AddShipping()` y
`MapShippingEndpoints()` registran `/shipments`. La búsqueda por referencia (D-11) vive en
`Features/Stock/CustomerOrders`, porque lee el pedido de HU-05. El plan global ya reserva
`Features/Shipping` para HU-03 y HU-05 lo anticipó (su D-01).

**Alternativas**: dentro de `Features/Stock`. Se descarta porque mezclaría la propiedad del documento
de salida con la de reservas y pedidos, y HU-05 fijó la frontera en su contrato de integración.

## D-02. Ciclo del documento: borrador persistido, edición, eliminación y confirmación

**Decisión**: rutas del contrato común más las de la clarificación Q1:

| Ruta | Efecto | Permiso | Precondición |
|------|--------|---------|--------------|
| `POST /shipments` | Crea el borrador. | `Shipping.Record` | `Idempotency-Key` |
| `PUT /shipments/{id}` | Sustituye fecha del hecho y líneas del borrador. | `Shipping.Record` | `If-Match` de la salida |
| `DELETE /shipments/{id}` | Elimina el borrador completo (Q1). | `Shipping.Record` | `If-Match` de la salida |
| `POST /shipments/{id}/confirm` | Aplica la salida. | `Shipping.Record` | `If-Match` de la salida y `orderVersion` (D-05) |

Todas pasan por `InventoryOperationExecutor` con `Idempotency-Key` (el ejecutor exige la clave en
toda escritura), como `PUT` y `DELETE` de HU-02. Crear, editar y eliminar no escriben diario, así
que guardan dentro de `change` y devuelven `ETag` (como el alta de pedido de HU-05). Confirmar
escribe diario: no guarda dentro de `change` y responde 200 **sin** `ETag` (lección de HU-05 D-08,
FK `StockMovements.OperationId`).

Un borrador no retiene existencias. Al guardar se validan forma, pertenencia al pedido, lote del
artículo, existencia del bucket y suma por línea ≤ pendiente **en ese momento**; al confirmar se
revalida todo bajo bloqueo (spec FR-003, FR-004 y casos límite).

**Alternativas**: crear y confirmar en un solo paso. Se descarta: contradice la fila del contrato
común (`POST`, `PUT` de borrador y `/confirm`) y la clarificación Q1.

## D-03. Tipos de documento y orden de bloqueos

**Decisión**: un tipo nuevo, `shipment` (`^[a-z][a-z0-9-]{0,31}$`, la expresión vigente de
`InventoryLocks` y `StockCore`). Recurso `document:shipment:{shipmentId}`. Orden ordinal de todos
los tipos, porque `-` (0x2D) precede a `:` (0x3A) y `r` < `s`:

`customer-order-reference:` < `customer-order:` < `reservation-release:` < `reservation:` < `shipment:`

| Comando | Documentos (una sola llamada) | Ubicaciones | Artículos |
|---------|-------------------------------|-------------|-----------|
| Crear borrador | `shipment:{nuevo}` | — | los de las líneas |
| Editar borrador | `shipment:{id}` | — | los de las líneas anteriores y nuevas |
| Eliminar borrador | `shipment:{id}` | — | los de sus líneas |
| Confirmar | `customer-order:{o}`, `reservation:{r}` de cada reserva a consumir, `shipment:{id}` | las de las líneas | los de las líneas |

Crear, editar y eliminar toman el bloqueo de artículo porque crean o retiran referencias al
artículo (contrato de escritores, INT-HU03-01): así se serializan con la edición de definición y el
borrado de HU-08. No toman `customer-order`: no cambian el pedido y su validación del pendiente es
orientativa.

Confirmar sigue la regla 1 del [contrato HU-03](../004-consultar-existencias/contracts/hu03-integration.md#3-reglas-que-hu-03-debe-respetar).
`StockCore.ApplyAsync` vuelve a pedir `shipment:{id}`, que ya está retenido y no se repite. Las
reservas a bloquear se leen **antes** de los bloqueos (relaciones inmutables: línea de salida →
línea de pedido → reservas con remanente en el bucket). Todo escritor de reservas de HU-05 toca la
cabecera del pedido bajo `customer-order:{id}`, de modo que, si el `orderVersion` coincide tras
bloquear (D-05), el conjunto de reservas leído antes es el vigente. Si no coincide, 412 antes de
decidir nada.

**Alternativas**: no bloquear `reservation:{id}` y confiar solo en `customer-order`, que ya
serializa a todos los escritores de reservas. Se descarta porque la regla 1 del contrato lo exige y
protege frente a un escritor futuro que olvide el pedido.

## D-04. Confirmar: cálculo y efectos bajo bloqueo

**Decisión**: dentro de `change`, tras los bloqueos y en este orden, sin escribir hasta que todo pasa:

1. Salida existente (404) y en `Draft` (si no, 409 `DocumentAlreadyConfirmed`, más informativo que
   un 412). `If-Match` de la salida (428/412).
2. Pedido con `ICustomerOrderReader.ReadAsync` y `orderVersion` del cuerpo igual a su `Version`
   (412 si no, D-05). Pedido `Open` (409 `CustomerOrderNotOpen`, código de HU-05).
3. Por línea: pertenece al pedido; Σ cantidades por línea de pedido ≤ pendiente (422,
   `errors.lines[i].quantity`); conteo abierto en la ubicación (409 `CountScopeFrozen`, con
   `ILocationUsage.IsInOpenCountAsync`, `false` hasta HU-04).
4. Reserva propia por bucket: `own = Σ Remaining` de las `ActiveAllocations` de la línea en ese
   bucket; `consume = min(Q, own)` (regla 2). Se reparte entre esas asignaciones por Id ascendente.
5. Regla 5: para cada línea de pedido, Σ remanente activo tras consumir ≤ nuevo pendiente; si no,
   409 `ReservedStockConflict` indicando la cantidad sobrante, sin liberar nada.
6. `StockCore.PrepareShipment` con `ShipmentAllocation(articleId, locationId, lotCode, Q, consume)`
   y `ApplyAsync`: artículo inactivo permitido (R-05), físico, reservado y disponible del bucket
   (`InsufficientStock`), diario `ShipReserved`/`ShipAvailable` y saldo.
7. Efectos en el mismo commit: `ConsumedQuantity` de cada asignación y `DeriveState()` de cada
   reserva afectada; `ShippedQuantity` de cada línea de pedido; pedido `Fulfilled` si todas las
   líneas quedan con pendiente 0; filas de consumo (D-08); salida `Confirmed` con `OperationId`,
   `ActorId` y `RecordedAt`. Las líneas no cambian: su reserva propia consumida es la suma de sus
   filas de consumo (D-06, D-08).
8. Renovar versiones (regla 4): `Touch` de la cabecera del pedido **siempre**, de cada reserva cuyo
   consumo cambie y de la salida.

**Dominio**: HU-05 no tiene métodos para consumir ni servir. HU-03 añade a los archivos de dominio
de HU-05 (de B) `ReservationAllocation.Consume(q)`, `CustomerOrderLine.Ship(q)` y
`CustomerOrder.DeriveFulfilment()`, con las mismas reglas locales que `Release` (cantidad positiva,
nunca por encima del remanente o del pendiente). Es un cambio aditivo a código de B, sin tocar el
contrato publicado.

**Alternativas**: delegar el consumo en HU-05 mediante un servicio. Se descarta: el contrato fija
que HU-05 no ofrece otro cálculo y que HU-03 carga sus entidades tras los bloqueos.

## D-05. Precondiciones: versión de la salida y versión del pedido revisado

**Decisión**:

- `GET /shipments/{id}` devuelve el `ETag` de `Shipment.Version`. Editar, eliminar y confirmar
  exigen ese `If-Match`.
- Confirmar exige además `orderVersion` en el cuerpo: el token del `ETag` de
  `GET /customer-orders/{id}` que se usó para la revisión (lo devuelve también
  `GET /shipments/{id}/context`). Si otra salida, reserva o liberación cambió el pedido desde la
  revisión, 412 y la UI recarga la revisión (spec FR-012, US2.6 y Principio III: lo revisado es lo
  que se confirma).
- Toda salida confirmada renueva la versión del pedido y la de cada reserva consumida (regla 4),
  así que los `If-Match` de HU-05 detectan la salida (prueba H-12).

**Motivo**: el cuerpo de confirmar es la intención; `orderVersion` forma parte de la huella, así que
un reintento con la misma clave y otra versión da `IdempotencyConflict`, como cualquier cambio de
intención.

**Alternativas**: a) solo `If-Match` de la salida: el usuario confirmaría cifras de reserva propia y
ajena que ya no son las que revisó; b) un `If-Match` compuesto: no lo admite `EntityTags`. Se
descartan.

## D-06. Protección SQL de la salida confirmada: triggers (decisión de B)

**Decisión**: `Shipments` y `ShipmentLines` **no** llevan `DENY DELETE`, porque Q1 permite eliminar
borradores. La migración añade a mano dos triggers:

- `TR_Shipments_ConfirmedImmutable` (`AFTER UPDATE, DELETE`): `ROLLBACK` + `THROW` si alguna fila de
  `deleted` tenía `State = 'Confirmed'`.
- `TR_ShipmentLines_ConfirmedImmutable` (`AFTER INSERT, UPDATE, DELETE`): igual si la cabecera de
  alguna fila de `inserted` o `deleted` está confirmada.

La transición `Draft → Confirmed` actualiza una fila cuyo `deleted.State` es `Draft`, así que se
permite; cualquier cambio posterior de esa fila se rechaza.

Para que el trigger de líneas no dependa del orden en que EF emite las sentencias de una misma
`SaveChanges`, **la confirmación no modifica las líneas**: `OwnReservationConsumed` no se almacena en
la línea, sino que se obtiene como Σ de la tabla de consumos (D-08). Así, al confirmar solo cambian
la cabecera de la salida, las filas de HU-05 y las filas nuevas de consumo, y una línea de una salida
confirmada no cambia nunca. Editar un borrador inserta, actualiza o borra líneas de una cabecera en
`Draft`, que el trigger admite. La prueba de esquema (H-02) comprueba los triggers con el usuario
de ejecución `inventory_runtime`.

**Iteración del plan (2026-11-21, revisión de A de P-01 y P-02): consumos ligados a la operación que
confirma.** El `DENY UPDATE, DELETE` de D-08 no impide **insertar** filas de consumo nuevas en una
salida ya confirmada. Tampoco sirve un trigger de `INSERT` que rechace cuando la cabecera está
`Confirmed`, porque rompería la propia confirmación: EF no garantiza el orden entre el `UPDATE` de la
cabecera y los `INSERT` de consumos de la misma `SaveChanges`. Mecanismo fijado (decisión de B):

- La cabecera ya guarda la operación que la confirma. `Shipments.OperationId` se fija al confirmar
  (`CHECK State = 'Confirmed' ⇔ OperationId IS NOT NULL`, único filtrado) y
  `TR_Shipments_ConfirmedImmutable` lo vuelve inmutable. No hace falta otra columna
  `ConfirmedByOperationId`.
- Cada fila de consumo lleva `OperationId`, el de la operación que la crea: el `context.OperationId`
  del ejecutor dentro de `change`.
- `TR_ShipmentReservationConsumptions_ConfirmingOperation` (`AFTER INSERT`): `ROLLBACK` + `THROW` si
  alguna fila de `inserted` pertenece a una línea cuya cabecera está `Confirmed` con un `OperationId`
  distinto del de la fila.
- Complemento en `TR_Shipments_ConfirmedImmutable`: en la transición `Draft → Confirmed`, rechaza si
  ya existe algún consumo de sus líneas con un `OperationId` distinto del nuevo de la cabecera. Así,
  un consumo insertado de antemano en un borrador por otra operación no sobrevive a la confirmación.
- `HasTrigger` del trigger nuevo en `ShipmentReservationConsumptionConfiguration`, como en las otras
  dos tablas (plan, riesgos).

El resultado no depende del orden de EF:

- Si la cabecera se actualiza primero, el `INSERT` encuentra `Confirmed` con el mismo
  `OperationId` y se admite.
- Si el `INSERT` va primero, encuentra `Draft` y se admite. Después, el `UPDATE` comprueba que los
  consumos ya insertados llevan su `OperationId`.

Fuera de la confirmación, cualquier `INSERT` posterior lleva otra operación y se rechaza en la base
de datos.

Límites aceptados:

- Una fila con el **mismo** `OperationId` tras el commit pasaría el trigger. No hay camino para
  ello: `OperationResults` tiene ese id como PK y el ejecutor devuelve el resultado guardado sin
  volver a ejecutar.
- Un consumo insertado en un borrador que nunca se confirma no se detecta al insertarlo. La
  aplicación solo inserta al confirmar, y esa fila impediría eliminar el borrador (FK `Restrict`
  desde la línea), así que el fallo sería visible.

**Alternativas** a la iteración:

- (a) Trigger de `INSERT` contra la cabecera `Confirmed`: rompe la confirmación según el orden de
  EF.
- (b) Insertar los consumos en una segunda `SaveChanges` tras confirmar la cabecera: depende del
  orden de llamadas y no protege frente a otro escritor.
- (c) FK compuesta `(ShipmentId, OperationId)` hacia la cabecera: obliga a añadir `ShipmentId` y
  vuelve a depender de que la cabecera tenga ya su `OperationId` al insertar.

**Alternativas** (ofrecidas a B): a) solo en la aplicación, sin defensa SQL; b) tablas de borrador
separadas con `DENY` en las de confirmadas, que cambian el modelo común. B eligió los triggers.

## D-07. Contraparte, soporte y motivo del movimiento

**Decisión**: `StockCommandContext` de la salida:

- `Source = ("shipment", shipmentId)`; un movimiento `Shipment` por salida, con una entrada
  `ShipReserved` y/o `ShipAvailable` por bucket.
- `OccurredAt` = fecha del hecho de la salida. `StockCore` rechaza más de 5 minutos en el futuro
  (`AllowedClockSkew`); la UI y el borrador usan la misma tolerancia, que concreta «no posterior al
  registro» de FR-005.
- `Reason = "Salida del pedido {ExternalReference}"` (spec, Assumptions: motivo implícito).
- `SupportReference = ExternalReference` del pedido; el documento propio ya consta como
  `SourceDocumentType/Id`.
- `CounterpartyReference = CustomerReference` del pedido, **nunca** `CounterpartyNotApplicableReason`
  (lección 3 de HU-05; regla exclusiva de `HU08ExclusiveMovementCounterparty`).

## D-08. Registro de consumo por asignación (decisión de B)

**Decisión**: nueva tabla `ShipmentReservationConsumptions(Id, ShipmentLineId, ReservationAllocationId,
Quantity > 0, OperationId)`, una fila por asignación consumida en cada línea. Es append-only
(`DENY UPDATE, DELETE` para `inventory_runtime`) y tiene único `(ShipmentLineId,
ReservationAllocationId)`. `OperationId` es la operación que crea la fila. Un trigger de
`INSERT` exige que coincida con la que confirmó la cabecera (D-06, iteración 2026-11-21), así que no
se pueden añadir consumos a una salida ya confirmada. Invariantes:

- Σ consumos de una asignación = su `ConsumedQuantity` (conciliación H-14).
- Σ consumos de una línea de salida = su reserva propia consumida = `ShipReserved` del movimiento en
  ese bucket.
- Cada consumo de una salida confirmada lleva el `OperationId` de su cabecera.

`ShipmentLine.OwnReservationConsumed` del modelo común se mantiene como columna **calculada en la
lectura** (Σ de consumos), no almacenada, para que la línea confirmada no cambie (D-06). Es una
ampliación del modelo común: P-02.

**Alternativas** (ofrecidas a B): sin tabla, con la invariante agregada por línea de pedido y bucket.
Se descarta porque no reconstruye qué reserva concreta se consumió cuando hay varias en el mismo
bucket (constitución IV).

## D-09. Uso del artículo: contributor en `AddStockCore`

**Decisión**: `ShipmentArticleUsageContributor` implementa `IArticleUsageContributor` e
`IArticleFirstReferenceContributor` para `ShipmentLine` (la única entidad nueva con FK a `Articles`;
la tabla de consumos no la tiene). Se registra en `AddStockCore` (lección 4), no en `AddShipping`,
para que la cobertura de HU-08 no dependa del orden de registro y no responda 503
`DependencyUnavailable`. Con filas, `HasReferences = true`; devuelve «sin uso» solo si la migración
HU-03 no está aplicada, como los de HU-05.

Crear y editar un borrador llaman a `ArticleReferenceTracker.MarkAsync` bajo el bloqueo del artículo.
En la práctica la definición ya está fijada por la línea de pedido de HU-05, pero el escritor debe
hacerlo por sí mismo (contrato de escritores). Eliminar un borrador retira sus referencias; la marca
histórica no se revierte (R-05).

`Hu05SchemaTests` fija exactamente el conjunto de tipos con FK a `Article`; HU-03 lo amplía con
`ShipmentLine` (archivo de B). Las pruebas de A ya usan `Subset` desde HU-05.

## D-10. Migración `HU03_Shipments`

**Decisión**: aditiva, generada con `dotnet ef migrations add HU03_Shipments` sobre
`20261120113458_HU05_CustomerOrdersReservations` (la última presente; lección 5). Crea `Shipments`,
`ShipmentLines` y `ShipmentReservationConsumptions` con las restricciones de
[data-model.md](data-model.md#migración). A mano: los tres triggers (D-06; el de consumos y el
complemento de la cabecera, desde la iteración del 2026-11-21) y
`DENY UPDATE, DELETE ON ShipmentReservationConsumptions TO inventory_runtime`. No modifica
migraciones anteriores. Si se integra antes otra migración (PR #9 o de A), se regenera la local
(plan global §5).

## D-11. Localizar el pedido por referencia externa (FR-001a)

**Decisión**: `GET /customer-orders/lookup?externalReference=…` (Inventory.Read) en
`Features/Stock/CustomerOrders`. Compara con `StockDocuments.NormalizeReference` y la intercalación
CI de la columna, igual que la unicidad del alta. 200 con `{ id, externalReference, state }`; 404
`NotFound` con mensaje «No hay ningún pedido con esa referencia»; 422 sin referencia o con más de 64
caracteres. Sin lista, prefijo ni coincidencia aproximada (clarificación 4).

**Motivo**: HU-05 no construyó `GET /customer-orders` (su S1). Una consulta exacta cubre FR-001a sin
implementar la búsqueda paginada.

**Alternativas**: implementar S1 (prefijo y paginación). Descartado en la clarificación 4.

## D-12. Lecturas de la salida: detalle propio, contexto y lista del pedido

**Decisión** (lección 1 de HU-05):

- `GET /shipments/{id}`: solo datos propios y `ETag` de `Shipment.Version`: cabecera (`customerOrderId`,
  `state`, `occurredAt`, `recordedAt`, `actorId`, `operationId`) y líneas **ordenadas por Id** con
  `customerOrderLineId`, `articleId`, `locationId`, `lotId`, `quantity` y, en confirmadas,
  `ownReservationConsumed` y sus consumos por asignación. Todo cambia solo con escritores que tocan
  la salida.
- `GET /shipments/{id}/context`: sin `ETag`, `Cache-Control: no-store`, `SNAPSHOT`, con
  `shipmentVersion`, `orderVersion` y `asOf`: datos actuales de artículos (sku, nombre, unidad,
  estado, lote, fracciones), ubicaciones (código, descripción, estado) y lotes (código), el nombre del
  responsable, el pedido (referencias y pendiente por línea) y, para un borrador, las cifras de
  revisión por línea: físico, reservado, reservado propio, reservado ajeno y cantidad posible
  (`min(F, own + D)` acotado por el pendiente). Ordenado por Id de línea.
- `GET /shipments?customerOrderId=…&page=&pageSize=` (Inventory.Read): salidas del pedido, borradores
  y confirmadas, por `RecordedAt` descendente y Id; paginado con `Paging` de HU-02.

La UI comprueba que `shipmentVersion` del contexto coincide con el `ETag` del detalle y recarga si no,
como HU-05.

## D-13. Propuesta de orígenes (FR-006a)

**Decisión**: la propuesta se calcula en el **cliente** a partir de `GET /customer-orders/{id}` de
HU-05, que ya devuelve por asignación activa el bucket y su remanente. Al añadir una línea de pedido,
la UI precarga una fila por asignación con remanente, por Id, sin superar en total el pendiente; el
operario las cambia, quita o añade otras con el selector de buckets de `GET /stock?articleId=…`.
Nada se guarda sin la acción del operario. Sin reserva propia, la línea se añade vacía.

**Alternativas**: una ruta de propuesta en el servidor. Se descarta: duplica la lectura del pedido y
no aporta validación, que ocurre al guardar y al confirmar.

## D-14. Frontend sobre la base de A y de HU-05

**Decisión**: `features/shipping` sustituye el placeholder de `/salidas`:

| Ruta | Pantalla | Guard |
|------|----------|-------|
| `''` | Buscar pedido por referencia (D-11) | área |
| `pedidos/:orderId` | Salidas del pedido y «Nueva salida» | área |
| `pedidos/:orderId/nueva` | Preparar borrador con propuesta (D-13) | `permissionGuard('Shipping.Record')` |
| `:id` | Borrador (editar, eliminar, revisar y confirmar) o salida confirmada (solo lectura) | área |

- El área `salidas` de `operation-areas.ts` (archivo de B, H0) pasa de `Shipping.Record` a
  `Inventory.Read`, porque US4.3 deja consultar las salidas sin permiso de registro. Las acciones se
  muestran con `SessionService.has('Shipping.Record')`.
- Mutaciones con `MasterDataMutationsService` / `PendingOperationStore` y `appRoute` (lección 6), sin
  `idempotent-intent.ts`. `appRoute` = `/salidas/{shipmentId}` o `/salidas/pedidos/{orderId}`.
- Foco al primer `aria-invalid` o al aviso tras un rechazo (patrón C1 de HU-05). Revisión antes de
  confirmar, en un diálogo con `cdkTrapFocus` y Escape.
- En el selector de buckets, las filas con físico 0 quedan fuera por defecto (`includeEmpty=false`).

**Cambios en archivos de A** (P-06): `pending-operation.store.ts` (`mutationPermission()` reconoce
`/api/v1/shipments` y sus subrutas → `Shipping.Record`; `APP_ROUTE` admite `/salidas/…`) y
`operation-status.component.ts` (texto del enlace según la ruta: «Abrir salida» o «Abrir pedido»).
Sus pruebas van en specs nuevas de B, sin editar las de A.

## D-15. Pruebas

**Decisión**: xUnit con SQL Server real (`IdentityFixture`, `StockTestHarness`,
`FailAfterSaveInterceptor`, `LockBarrier` y `SaveBarrier`), en
`backend/tests/Inventario.IntegrationTests/Shipping/`, con la receta Docker de HU-05. Existencias con
`StockCore` (Receive) o la siembra D-17 de HU-05; pedidos y reservas con los comandos reales de HU-05.
Carreras con conexiones independientes **en ambos órdenes** (lección 7): salida/salida (V-05
extendido, 20 repeticiones), salida/reserva, salida/liberación, salida/desactivación del artículo
(INT-HU03-02), borrador/edición de definición y borrador/borrado del artículo (INT-HU03-01). Fallo
inducido antes del commit (V-08). Vitest para las pantallas y Playwright para E2E-02.

**Alternativas**: dobles de HU-05. Se descartan: INT-HU03-01 exige probar los escritores reales.
