# Research: HU-05 — Consultar existencias y reservas

**Fecha**: 2026-11-19 · **Plan**: [plan.md](plan.md) · **Spec**: [spec.md](spec.md)
**Decisiones comunes que se consumen, sin redefinirlas**:
[R-03 y R-04](../../.specify/memory/project/research.md#r-03-integridad-concurrencia-y-agregación-de-stock),
[R-05](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs),
[R-10/R-11](../../.specify/memory/project/research.md#r-10-desbloqueo-de-referencias-e-identidad-del-núcleo-h0),
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md) y
[modelo común](../../.specify/memory/project/data-model.md#compromisos-y-salidas--persona-b--hu-03-hu-05).

No quedaban incógnitas `NEEDS CLARIFICATION` en el contexto técnico: stack, ejecutor, núcleo y
pruebas existen en el código (`H0_StockCore`, `HU02_*`). Las decisiones siguientes concretan
cómo HU-05 los consume. Cada una indica qué se descartó.

## D-01. Ubicación del código: `Features/Stock` con tres subáreas

**Decisión**: `Features/Stock/{Queries,CustomerOrders,Reservations}` y entidades en
`Domain/Stock/{CustomerOrder,Reservation}.cs`. Un único `StockEndpoints.MapStockEndpoints`
registra `/stock`, `/customer-orders` y `/reservations`.

**Motivo**: el plan global asigna `Features/Stock` a «HU-05, reservas y servicio común»; el núcleo
`Features/Stock/Core` ya vive ahí. HU-03 añadirá `Features/Shipping` y leerá el pedido con el
contrato de integración, sin entrar en estas carpetas.

**Alternativas**: carpeta `Features/CustomerOrders` independiente. Se descarta porque fragmenta
la propiedad de las reservas, que solo tienen sentido sobre el pedido.

## D-02. Un único lector del agregado por artículo, compartido con HU-02

**Decisión**: `StockBalanceReader` (`Features/Stock/Queries`) es la única proyección de
`StockBalances ⋈ Locations ⋈ Lots` por artículo. Admite filtros opcionales `locationId` y
`lotId` y la inclusión de filas a cero, y suma `Physical` y `Reserved` antes de restar.
Se ejecuta en una transacción `SNAPSHOT` que devuelve página, totales, recuento y `asOf`.
`GET /stock` lo usa directamente. `LocationQueries.ArticleLocationsAsync` (HU-02,
`GET /articles/{id}/locations`) pasa a delegar en él y conserva su DTO, su ruta y su filtro
`includeInactiveLocations`.

**Relación entre las dos rutas**: `GET /stock` es la consulta de HU-05, con filtros, disponible y
opción de mostrar filas a cero. `GET /articles/{id}/locations` sigue siendo la vista «dónde está
este artículo» de HU-02, sin disponible ni filtros de lote. Ambas comparten la misma consulta SQL,
de modo que no hay dos cálculos que puedan divergir. Disponible = físico − reservado sale del
modelo común (`StockBalance.Available` y la suma tras agregar). No se crea otro contador.

**Alternativas**: a) redirigir HU-02 a `/stock`, que rompería su contrato y sus pruebas L-05/L-06
ya escritas; b) dos consultas paralelas, que duplican el agregado y es lo que el enunciado prohíbe.

## D-03. Filas a cero ocultas con opción de mostrarlas

**Decisión**: parámetro `includeEmpty` (por defecto `false`). Con `false` se filtra `Physical > 0`.
Basta porque `Reserved ≤ Physical`, así que una fila con reserva nunca tiene físico cero. Con
`true` aparecen todas las filas de `StockBalances` del artículo, que nunca se borran. Los totales
no cambian, porque las filas a cero suman cero. «Sin existencias» se decide con
`totals.physical == 0`, no con la página vacía.

**Alternativas**: el filtro `Physical ≠ 0 OR Reserved ≠ 0` de HU-02, equivalente por la
invariante. Se adopta `Physical > 0` en el lector común y HU-02 mantiene el mismo resultado.

## D-04. Tipos de documento y orden de bloqueos

**Decisión**: tipos canónicos (`^[a-z][a-z0-9-]{0,31}$`, el mismo que validan `InventoryLocks` y
`StockCore`):

| Tipo | Recurso de bloqueo | Uso |
|------|--------------------|-----|
| `customer-order-reference` | `document:customer-order-reference:{uuid5(referencia normalizada)}` | Serializa altas con la misma referencia externa. |
| `customer-order` | `document:customer-order:{orderId}` | Todo escritor del pedido, de sus líneas o de sus reservas (HU-05 y HU-03). |
| `reservation-release` | `document:reservation-release:{releaseId}` | Documento origen de la liberación para `StockCore` (`Release`). |
| `reservation` | `document:reservation:{reservationId}` | Documento origen de la reserva para `StockCore` (`Reserve`) y escritor de la reserva. |

Orden ordinal resultante, porque `-` (0x2D) precede a `:` (0x3A):
`customer-order-reference:` < `customer-order:` < `reservation-release:` < `reservation:`.
Cada comando pide **todos** sus documentos en **una sola** llamada a `Locks.DocumentsAsync`
(`InventoryLocks` ordena), después `LocationsAsync` con las ubicaciones de sus asignaciones y
`ArticlesAsync` con el artículo de la línea. Todo ello ocurre antes de leer el estado decisorio y
antes de `StockCore.ApplyAsync`, que encuentra los recursos retenidos y no los vuelve a pedir.

El UUID de la referencia es un UUID v5 determinista del texto normalizado (recorte y mayúsculas
invariantes) con un espacio de nombres fijo de HU-05. Así, dos altas concurrentes con la misma
referencia se serializan sin depender solo del índice único.

**Motivo**: el contrato exige el orden documentos → ubicaciones → artículos y la declaración de
todos los recursos antes de `ApplyAsync` (ejemplo de HU-01). Bloquear `customer-order` en reservar,
liberar y servir serializa todas las decisiones sobre el pendiente y las reservas de un pedido.

**Alternativas**: usar `reservation:{id}` como documento de la liberación. Se descarta porque la
unicidad del diario es (tipo, id, `Kind`) y solo permitiría una liberación por reserva, lo que
contradice la clarificación 5 y la extensión de V-04.

## D-05. Reservar: el cuerpo HTTP normalizado es la intención

**Decisión**: `OperationRequest.NormalizedBody` es el DTO HTTP normalizado:
`customerOrderLineId` y `allocations[]` con `locationId`, `lotId` (o `null`) y `quantity` en
cadena con tres decimales, en el orden recibido. El `StockCommand` se construye **dentro** de
`change`, tras resolver la línea y los lotes, con `StockCore.Prepare(StockOperation.Reserve, …)`:

- `Source = ("reservation", reservationId)`, con `reservationId` generado en el servidor.
- `OccurredAt = UtcNow` del servidor (reservar no es un hecho físico pasado).
- `Reason = "Reserva para el pedido {ExternalReference}"`.
- `SupportReference = ExternalReference` y `CounterpartyReference = CustomerReference`.

**Motivo**: el `StockCommand` necesita `LotCode` y el artículo de la línea, que solo se conocen
leyendo la base, y un `OccurredAt` del servidor variaría entre reintentos y rompería la huella. La
intención del usuario es el DTO; replays e `IdempotencyConflict` siguen funcionando con el
ejecutor común. Es la única diferencia con la receta H0 («`NormalizedBody` del `StockCommand` en la
`OperationRequest`») y se registra como concreción en el diff de `stock-operations.md`.

**Alternativas**: construir el `StockCommand` fuera del ejecutor con lecturas sin bloqueo. Se
descarta por la huella inestable y las lecturas previas al bloqueo.

## D-06. Liberar: `releaseId` estable como documento y motivo del usuario

**Decisión**: `POST /reservations/{id}/release` con `releaseId` (UUID del cliente, estable en
reintentos), `reason` (texto libre no vacío, ≤ 500, clarificación 4) y `allocations[]` con
`allocationId` y `quantity`. Dentro de `change`, `StockCore.Prepare(StockOperation.Release, …)`
recibe:

- `Source = ("reservation-release", releaseId)` y `OccurredAt = UtcNow`.
- `Reason` = el motivo del usuario.
- `SupportReference = "reservation:{reservationId}"` y `CounterpartyReference = CustomerReference`
  del pedido, ya bloqueado y leído. Es la misma contraparte que al reservar (D-05): el cliente. La
  referencia del pedido ya figura como soporte de la reserva (revisado tras `/speckit-analyze`, M2).
  Con eso se cumple la regla del PR #7: todo hecho nuevo lleva contraparte o motivo.

`StockCore` comprueba el reservado del bucket. HU-05 comprueba antes el **remanente de cada
asignación** (`ReservedStockConflict` si se supera), porque el bucket puede tener reservas de otros
pedidos. La operación del núcleo se llama `Release`, no `ReleaseReservation`: se usa el nombre
existente.

Un mismo `releaseId` con otra clave idempotente devuelve 409 `DocumentAlreadyConfirmed` del núcleo:
no se aplica dos veces. La misma clave repite el resultado original.

**Trazabilidad sin tabla nueva**: el movimiento `Release` conserva actor, instantes, motivo,
entradas por bucket y la referencia a la reserva. Las asignaciones guardan `ReleasedQuantity`
acumulado. No se añade una entidad `ReservationRelease`.

**Alternativas**: una tabla `ReservationRelease(Id, ReservationId)` para enlazar sin texto. Es más
consultable, pero amplía el modelo común. Queda como opción para A en el diff (P-06), no se aplica.

## D-07. Precondiciones: ETag del pedido y de la reserva como versión del agregado

**Decisión**:

- `GET /customer-orders/{id}` devuelve el `ETag` de `CustomerOrder.Version`. Reservar exige ese
  `If-Match`.
- `GET /reservations/{id}` devuelve el `ETag` de `Reservation.Version`. Liberar exige ese `If-Match`.
- Todo comando que cambie reservas o servido de un pedido **toca** la cabecera del pedido. Toda
  liberación toca también la cabecera de la reserva. Tocar = marcar `State` como modificado aunque
  no cambie. SQL Server actualiza `rowversion` en cualquier `UPDATE`, y EF añade la comprobación
  optimista `WHERE Version = @original`.
- HU-03 aplica la misma regla al servir (contrato de integración).

**Motivo**: las líneas y asignaciones no tienen versión propia en el modelo común. Sin tocar la
cabecera, una reserva o salida ajena no invalidaría el ETag y US5.4 no se cumpliría.

**Alternativas**: a) añadir `rowversion` a líneas y asignaciones y combinar un ETag compuesto,
que cambia el modelo y complica la precondición; b) una columna `Revision`, que también cambia el
modelo. Se descartan.

**Revisado en la revisión de A (M1)**: un ETag fuerte debe identificar la representación
**entera** (es el defecto R3 de HU-02). Por eso:

- Los detalles versionados (`GET /customer-orders/{id}` y `GET /reservations/{id}`) llevan
  **solo datos propios**: identificadores, cantidades, referencias del pedido y estados. Todos
  cambian únicamente mediante escritores que tocan la cabecera.
- Los datos que cambian por su cuenta salen del detalle y van a `GET /customer-orders/{id}/context`,
  sin ETag (D-11): SKU, nombre, unidad y estado del artículo; código y estado de la ubicación; código
  del lote.
- La renovación de la cabecera se mantiene: todo escritor del pedido o de la reserva (HU-05 al
  reservar y liberar, HU-03 al servir) la actualiza, así que el ETag cambia con cada cambio de los
  datos propios. T012 y T014 lo prueban al reservar y liberar. T010 prueba que **no** cambia al
  renombrar el artículo ni al cambiar el código de la ubicación, y que el contexto sí lo refleja.

Se descarta la redacción anterior, «datos informativos revalidados bajo bloqueo», porque dejaba un
ETag fuerte idéntico para representaciones distintas. También el digest de HU-02
(`LocationEntityTags`), porque obligaría a recalcularlo bajo bloqueo en cada `If-Match`. Se sigue
el precedente de HU-08 (`GET /articles/{id}/context`).

## D-08. Respuestas de reservar y liberar sin `ETag`

**Decisión**: `POST /reservations` responde 201 con `Location: /api/v1/reservations/{id}` y el cuerpo
`{ operationId, reservationId, customerOrderId, movementId }`. La liberación responde 200 con
`{ operationId, reservationId, releaseId, movementId, state }`. Ninguna lleva `ETag`. La UI refresca
el pedido y la reserva con GET para obtener las versiones nuevas (paso 4 de
[ui.md](../../.specify/memory/project/contracts/ui.md#estados-de-toda-operación)).

**Motivo**: `StockMovements.OperationId` tiene FK a `OperationResults`. El protocolo prohíbe
`SaveChanges` dentro de `change` antes de que el ejecutor añada el resultado, así que la nueva
`rowversion` no se conoce al construir la respuesta. El alta de pedido no genera movimiento: guarda
dentro de `change`, como HU-02, y devuelve `ETag`.

**Alternativas**: modificar el ejecutor común para reconstruir cabeceras tras el commit. Se
descarta porque cambia un componente compartido de A sin necesidad.

## D-09. Reglas de reservar evaluadas tras los bloqueos

Orden de validación dentro de `change`, sin escribir nada hasta que todas pasan:

1. Forma (422): al menos una asignación y como máximo 100; cantidades positivas con tres decimales;
   sin asignaciones repetidas para la misma ubicación y lote (caso límite de la spec; el núcleo
   también lo rechaza).
2. Línea y pedido existen (404). `If-Match` del pedido (428/412).
3. Pedido `Open` (409 `CustomerOrderNotOpen`, código nuevo).
4. Lote del artículo de la línea. `lotId` ajeno al artículo da 422. Un artículo sin seguimiento
   usa `SIN-LOTE`.
5. Ubicación en conteo abierto (409 `CountScopeFrozen`) con `ILocationUsage.IsInOpenCountAsync`,
   que devuelve `false` hasta HU-04.
6. Σ asignaciones + Σ remanente de las reservas activas de la línea ≤ pendiente
   (`Ordered − Shipped`). Si no, 422 `ValidationFailed` con `errors.allocations`, porque api.md
   asigna 422 a «suma de asignaciones incorrectas».
7. `StockCore.ApplyAsync`: artículo inactivo (`ArticleInactive`), ubicación inactiva, bucket sin
   saldo y disponible insuficiente (`InsufficientStock`).

**Concurrencia (V-05)**: dos reservas del último disponible sobre pedidos distintos comparten
`location:{id}` y `article:{id}`. La segunda lee el saldo tras el commit de la primera y recibe
`InsufficientStock`. Dos reservas del mismo pedido comparten además `customer-order:{id}`, y la
segunda ve el remanente de la primera.

## D-10. Alta de pedido

**Decisión**:

- Referencia externa: recorte, 1–64 caracteres. Se guarda como se escribió, con índice único y la
  intercalación `Latin1_General_100_CI_AS`, igual que `Sku`.
- Referencia del cliente: recorte, 1–200 caracteres.
- Líneas: entre 1 y 100, con artículo distinto en cada una (clarificación 1), cantidad positiva
  con tres decimales y sin fracciones si el artículo no las admite.
- Bloqueos: documentos `[customer-order-reference:{uuid5}, customer-order:{nuevoId}]` → artículos.
- Bajo bloqueo: existencia de la referencia (409 `DuplicateCode`, `errors.externalReference`),
  existencia y estado de los artículos (`ArticleInactive`) y
  `ArticleReferenceTracker.MarkAsync`, porque la primera referencia, incluso en borrador, fija la
  definición del artículo (R-05). Después `SaveChanges` y 201 con `ETag` y `Location`.
- El índice único es la defensa última. Una rama aditiva en `MasterDataProblemMapper` (archivo de A)
  traduce `UX_CustomerOrders_ExternalReference` a 409 `DuplicateCode`.

**Errores por línea**: `errors["lines[i].articleId" | "lines[i].quantity"]`. La validación de forma
reúne todos los errores de forma antes de responder. Las reglas que dependen de la base se evalúan
bajo bloqueo y se informan por línea.

**Sin edición ni cancelación** (clarificación 3): no existen `PUT`, `DELETE` ni `/cancel`. Las
rutas no definidas devuelven 404 o 405, como el resto de la API.

## D-11. Lectura de pedidos: detalle, contexto y búsqueda mínima

**Decisión**: `GET /customer-orders/{id}` (Inventory.Read) devuelve la cabecera, las líneas (pedido,
servido, pendiente y reservado remanente) y las reservas con sus asignaciones, en una transacción
`SNAPSHOT`, más el `ETag`. Solo lleva datos propios; los datos actuales de artículos, ubicaciones y
lotes van en `GET /customer-orders/{id}/context` (ver más abajo). Se añade `GET /customer-orders?search=&state=&page=&pageSize=`, que
busca por prefijo de referencia externa y no figura en api.md. Sin ella, la pantalla no puede
encontrar un pedido para reservarlo.

**Motivo**: la spec exige consultar el pedido (US2.4) y reservar sobre una línea (US3). La búsqueda
es solo lectura y no crea otra fuente de verdad.

**Alternativas**: navegar solo por el enlace del alta. Se descarta porque un pedido creado por otra
persona no sería alcanzable. Diff P-04 a api.md.

**Detalle propio y contexto actual (revisión de A, M1)**: la lectura de un pedido se hace en dos
rutas, con el patrón de `GET /articles/{id}/context` de HU-08 (api.md común y
`specs/002-gestionar-datos-maestros/contracts/api.md`):

- `GET /customer-orders/{id}`: `CustomerOrderView` con datos propios y `ETag`
  de `CustomerOrder.Version`. Por línea, `articleId`, `ordered`, `shipped`, `pending` y
  `reservedRemaining`. Por asignación, `articleId`, `locationId`, `lotId` y las cantidades.
  `GET /reservations/{id}` sigue la misma regla con `Reservation.Version`.
- `GET /customer-orders/{id}/context`: `CustomerOrderContext` sin `ETag` y con
  `Cache-Control: no-store`, leído en una transacción `SNAPSHOT`:
  - `orderVersion`: el mismo token que el `ETag` del detalle, con `EntityTags.Format`;
  - `asOf`;
  - los artículos de las líneas: `sku`, `name`, `baseUnit`, `isActive`, `tracksLot` y
    `allowsFractional`;
  - las ubicaciones de todas las asignaciones del pedido: `code`, pasillo, estantería, nivel e
    `isActive`;
  - sus lotes: `code` e `isUntracked`.

  Un solo contexto cubre el pedido y todas sus reservas.
- La UI combina ambas lecturas. Si `orderVersion` no coincide con el `ETag` del detalle, ha leído
  dos revisiones distintas y vuelve a cargar, como `articles-api.service.ts` de HU-08.

**Alternativa descartada**: incluir esos datos en el detalle, que deja el ETag fuerte igual cuando la
representación cambia (R3 de HU-02).

## D-12. Compromisos por artículo

**Decisión**: `GET /stock/reservations?articleId=&locationId=&lotId=&page=&pageSize=`
(Inventory.Read) devuelve una fila por asignación con remanente mayor que cero. Cada fila incluye el
pedido (id y referencia externa), la línea, la reserva, el estado, la ubicación, el lote y las
cantidades asignada, consumida, liberada y remanente. El total remanente cubre el filtro completo y
coincide con `totals.reserved` de `GET /stock` con el mismo filtro (conciliación S-07).

## D-13. Uso del artículo y de la ubicación

**Decisión**: `CustomerOrderArticleUsageContributor` (Features/Stock) implementa
`IArticleUsageContributor` e `IArticleFirstReferenceContributor` para `CustomerOrderLine` y
`ReservationAllocation`: con filas, `HasReferences = true`. `ArticleUsageCoverageValidator` exige
cubrir toda FK a `Articles`; sin el contributor, HU-08 respondería `DependencyUnavailable`. Igual que
`StockArticleUsageContributor`, devuelve «sin uso» solo si la migración HU-05 no está aplicada.
`ILocationUsage` no cambia, porque las reservas ya se ven en `StockBalances.Reserved`.

## D-14. Migración `HU05_CustomerOrdersReservations`

**Decisión**: una migración aditiva, generada con `dotnet ef migrations add` sobre
`20261119171332_HU08MovementCounterparty`, la del PR #7 de A, que cierra la cadena común (revisado
tras `/speckit-analyze`, H3). No se genera hasta que #7 se fusione. Crea las cuatro tablas del
modelo común con las restricciones de [data-model.md](data-model.md#migración) y
`DENY DELETE` para `inventory_runtime` en las cuatro, porque en esta entrega nada se borra (como
`StockBalances`). No modifica migraciones anteriores. Si otra migración se integra antes, se
regenera la local según el plan global §5.

## D-15. Frontend sobre la base de A

**Decisión**: `features/stock` sustituye el placeholder de `/existencias`:

- `''`: consulta de existencias, con un selector de artículo propio (SKU o nombre, incluidos
  inactivos) y la pestaña «Reservas». No se reutiliza `article-search` de HU-02 porque enlaza fijo
  a `/ubicaciones/articulo/:id` (desviación registrada en tasks.md).
- `pedidos`: búsqueda de pedidos.
- `pedidos/nuevo`: alta (`permissionGuard` con `Shipping.Record`).
- `pedidos/:id`: detalle, reservar y liberar. Los botones solo se muestran con
  `SessionService.has('Reservations.Manage')`.

Errores con `apiProblem()`. Los parámetros se leen con `ActivatedRoute`.

**Intención idempotente (revisado tras `/speckit-analyze`, C1)**: las tres mutaciones (alta de
pedido, reservar y liberar) usan el transporte común de A ya fusionado con las correcciones R1–R4
del PR #6, igual que HU-02 en `locations-api.service.ts`:

- `MasterDataMutationsService.mutate()` abre la intención en `PendingOperationStore` con actor,
  método, recurso, cuerpo congelado y `If-Match`, y envía `Idempotency-Key`.
- La intención se restaura al recargar (R1), pertenece a su actor (R2), conserva solo la referencia
  al cerrar sesión y bloquea otra intención del mismo recurso mientras sea incierta.
- «Comprobar» usa `check()` (`GET /operations/{id}`) y «Repetir la misma intención», `retry()`.
  El estado 7 se muestra con `OperationStatusComponent`.
- El `releaseId` viaja en el cuerpo congelado, así que se conserva en recargas y reintentos sin
  lógica propia.

No se usan las funciones de clave de `idempotent-intent.ts`: HU-02 las abandonó por los defectos
R1 y R2, y reutilizarlas los reintroduciría.

El almacén solo reconoce maestros y ubicaciones, así que T016 amplía dos archivos de A en un
commit aparte:

- `mutationPermission()`: `/api/v1/customer-orders` → `Shipping.Record`;
  `/api/v1/reservations` y `/api/v1/reservations/{id}/release` → `Reservations.Manage`.
- `OperationStatusComponent.route()`: lleva esos recursos a las rutas de `/existencias/pedidos`.
- `PendingOperation` gana el campo opcional `appRoute` (`/existencias/pedidos/{orderId}`), que se
  conserva al cerrar sesión. Ni el recurso de reservar ni el de liberar contienen el id del pedido,
  y el cuerpo que lo tiene se retira. Así el panel ofrece «Abrir pedido» y «Comprobar» sin el
  cuerpo ([stock-ui](contracts/stock-ui.md#navegación-y-recuperación-de-una-operación-pendiente)).
  Va en otro commit aparte al implementar.

**Alternativas**: a) mantener `idempotent-intent.ts`, que reintroduce R1 y R2 y se descarta;
b) un almacén propio de HU-05, que duplicaría el transporte común. También se descarta.

## D-16. Pruebas

**Decisión**: xUnit con SQL Server real, sobre `IdentityFixture`, `StockTestHarness` y
`FailAfterSaveInterceptor`, en `backend/tests/Inventario.IntegrationTests/Stock/`. Las existencias
se preparan con `StockCore` (Receive) mientras HU-01 no exista. Concurrencia con conexiones
distintas y 20 repeticiones (V-05). El fallo inducido antes del commit cubre V-08. La extensión
de V-04 cubre dos liberaciones parciales con `releaseId` distintos y la repetición de cada una.
Sin SDK local, se usa la receta Docker de la [guía común](../../.specify/memory/project/quickstart.md#núcleo-de-stock-h0-b-sobre-la-base-común-de-a).

E2E Playwright solo del recorrido del vídeo (quickstart E2E-01). Rendimiento (SC-006) y validación
con trabajadores (SC-007) quedan fuera de la entrega (Complexity Tracking).

## D-17. Existencias para el E2E sin HU-01

**Decisión**: `POST /api/v1/testing/stock-receipts`. Solo se registra cuando
`Testing:EnableStockSeeding` es `true`. El valor por defecto es `false`. `docker-compose.yml` solo
lo pasa desde el entorno (`Testing__EnableStockSeeding: ${TESTING_ENABLE_STOCK_SEEDING:-false}`), y
en producción no se define. Ejecuta `StockCore` (`Receive`) mediante el ejecutor común,
con permiso `Receiving.Record`, documento `test-receipt` y motivo «Preparación de pruebas E2E»: es
un movimiento normal del diario, no una escritura de saldos. Sin la opción, la ruta no existe (404).

**Trazabilidad (constitución IV; revisado tras `/speckit-analyze`, C2)**: no hay documento de
compra ni proveedor, así que el `StockCommandContext` lo hace explícito con
`SupportNotApplicableReason = "Preparación de pruebas E2E"` y
`CounterpartyNotApplicableReason = "Preparación de pruebas E2E"`. Sin ellos, el núcleo del PR #7 de A
rechaza el hecho con 422 y la CHECK `CK_StockMovements_Counterparty` impide la fila. El parámetro
`CounterpartyNotApplicableReason` solo existe tras fusionar #7, por lo que T017 depende de esa
fusión.

**Motivo**: la guía común exige preparar datos «mediante comandos auditados o un fixture de
pruebas, nunca escribiendo saldos». HU-01 aún no ofrece recepciones por HTTP y el vídeo necesita
existencias. Los fixtures E2E de A solo preparan sesión.

**Alternativas**: a) INSERT directo de saldos, prohibido; b) esperar a HU-01, que bloquea el vídeo;
c) E2E sin existencias, que no muestra disponible ni reservas. Se retira el endpoint cuando HU-01
ofrezca la recepción, y su registro en `Program.cs` (archivo común) se revisa con A.
