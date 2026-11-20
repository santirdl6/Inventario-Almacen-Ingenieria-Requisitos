# Data Model: HU-05 — Consultar existencias y reservas

**Fecha**: 2026-11-19 · **Plan**: [plan.md](plan.md) · **Decisiones**: [research.md](research.md)
**Fuente de verdad**: [modelo común](../../.specify/memory/project/data-model.md#compromisos-y-salidas--persona-b--hu-03-hu-05).
Este documento **concreta** CustomerOrder, CustomerOrderLine, Reservation y ReservationAllocation
para la migración de HU-05. No redefine sus invariantes. Las reglas que el modelo común no recoge
están marcadas **[P-xx]** y se proponen como diff en el [plan](plan.md#cambios-al-modelo-común-pendientes-de-a).

## Entidades existentes que se consumen sin cambios

| Entidad | Uso en HU-05 |
|---------|--------------|
| `Article` (HU-08) | Artículo de cada línea; `IsActive`, `AllowsFractional`, `TracksLot`, `BaseUnit`. La primera línea o reserva fija `IsDefinitionLocked` (`ArticleReferenceTracker`). |
| `Location` (HU-02) | Ubicación de cada asignación; `IsActive` lo comprueba `StockCore`. |
| `Lot` (H0) | Lote de cada asignación; `SIN-LOTE` para artículos sin seguimiento. |
| `StockBalance` (H0) | Única fuente de físico y reservado; disponible = físico − reservado calculado. |
| `StockMovement` / `StockEntry` (H0) | Diario de `Reservation` y `Release` escrito por `StockCore`. |
| `OperationResult` (A) | Resultado idempotente de alta, reserva y liberación. |

## CustomerOrder (tabla `CustomerOrders`)

| Campo | Tipo | Reglas |
|-------|------|--------|
| Id | uniqueidentifier, PK | Generado en la aplicación. |
| ExternalReference | nvarchar(64), CI_AS | Obligatoria y recortada; única entre todos los pedidos sin distinguir mayúsculas (índice `UX_CustomerOrders_ExternalReference`). |
| CustomerReference | nvarchar(200) | Obligatoria y recortada; texto libre. |
| State | nvarchar(16) | `Open`, `Fulfilled` o `Cancelled` (CHECK). HU-05 solo crea `Open`. HU-03 pasa a `Fulfilled`. **`Cancelled` no es alcanzable** (clarificación 3; desviación anotada en el plan). |
| Version | rowversion | ETag del pedido. Se **toca** en cada cambio de sus reservas o de lo servido (research D-07). El comando marca `State` como modificado: el dominio no tiene `Touch()`. |

Quién creó el pedido y cuándo constan en su `OperationResult` (`ActorId`, `RecordedAt`,
`SourceDocumentId = Id`). No se añaden columnas de auditoría.

## CustomerOrderLine (tabla `CustomerOrderLines`)

| Campo | Tipo | Reglas |
|-------|------|--------|
| Id | uniqueidentifier, PK | |
| CustomerOrderId | FK → CustomerOrders, restrict | |
| ArticleId | FK → Articles, restrict | |
| OrderedQuantity | decimal(18,3) | > 0 (CHECK). Sin fracciones si el artículo no las admite (comando). |
| ShippedQuantity | decimal(18,3) | 0 ≤ Shipped ≤ Ordered (CHECK). HU-05 lo crea a 0; solo HU-03 lo modifica. |

- **[P-01]** Unicidad `(CustomerOrderId, ArticleId)`, índice `UX_CustomerOrderLines_Order_Article`
  (clarificación 1: una línea por artículo).
- Pendiente = `OrderedQuantity − ShippedQuantity` (calculado, no almacenado).
- Orden de presentación: por SKU del artículo, porque la línea no tiene número. Lo aplica la
  interfaz con el SKU de `GET /customer-orders/{id}/context`. El detalle versionado ordena por
  Id de línea, para que su representación (y su ETag) no dependa de datos de otro agregado.

## Reservation (tabla `Reservations`)

| Campo | Tipo | Reglas |
|-------|------|--------|
| Id | uniqueidentifier, PK | Generado en el servidor; también es el documento origen `reservation` del movimiento `Reservation`. |
| CustomerOrderLineId | FK → CustomerOrderLines, restrict | Una línea puede tener varias reservas. |
| State | nvarchar(16) | `Active`, `Consumed` o `Released` (CHECK), derivado como en el modelo común y recalculado por cada escritor. |
| Version | rowversion | ETag de la reserva; se toca en cada liberación (y consumo de HU-03). |

## ReservationAllocation (tabla `ReservationAllocations`)

| Campo | Tipo | Reglas |
|-------|------|--------|
| Id | uniqueidentifier, PK | Identifica la asignación en las liberaciones (`allocationId`). |
| ReservationId | FK → Reservations, restrict | |
| ArticleId | FK → Articles, restrict | Igual al artículo de la línea (comando). |
| LocationId | FK → Locations, restrict | |
| LotId | FK compuesta `(ArticleId, LotId)` → Lots, restrict | Como `StockBalance`: el lote pertenece al artículo. |
| AllocatedQuantity | decimal(18,3) | > 0. |
| ConsumedQuantity | decimal(18,3) | ≥ 0; solo HU-03. |
| ReleasedQuantity | decimal(18,3) | ≥ 0; HU-05 lo incrementa en cada liberación. |
| RemainingQuantity | columna calculada persistida | `Allocated − Consumed − Released`, CHECK ≥ 0. |

- **[P-02]** Unicidad `(ReservationId, ArticleId, LocationId, LotId)`: una asignación por bucket y
  reserva. Es coherente con el rechazo de buckets repetidos de `StockCore`.
- Índice `IX_ReservationAllocations_Bucket (ArticleId, LocationId, LotId) INCLUDE (RemainingQuantity)`,
  para `GET /stock/reservations` y la conciliación.

## Invariantes que HU-05 hace cumplir (del modelo común)

1. Σ `RemainingQuantity` de las asignaciones de un bucket = `StockBalance.Reserved` de ese bucket.
   Reservar y liberar actualizan ambos en la misma transacción, con `StockCore` y los bloqueos del
   bucket. Prueba S-07.
2. Para cada línea: Σ remanente de sus reservas `Active` ≤ `Ordered − Shipped`.
3. Reservar no cambia `Physical`; liberar tampoco.
4. `ReleasedQuantity` solo aumenta. Cada liberación es un movimiento `Release` distinto con su
   `releaseId` como documento origen (`reservation-release`), y no se aplica dos veces.

## Estados y transiciones

```text
CustomerOrder:  (alta) → Open ──HU-03: todo servido──▶ Fulfilled
                Cancelled: definido en el modelo común, no alcanzable en HU-05

Reservation:    (reservar) → Active
                Active ──liberar hasta remanente 0, sin consumo total──▶ Released
                Active ──HU-03 consume hasta remanente 0──▶ Consumed
                (derivado: Active si queda remanente; si no, Consumed si Consumed = Allocated
                 en todas las asignaciones, y Released en otro caso, como el modelo común)
```

## Hechos del diario escritos mediante `StockCore`

| Comando | Kind / Effect | SourceDocumentType / Id | Reason | SupportReference | CounterpartyReference |
|---------|---------------|-------------------------|--------|------------------|-----------------------|
| Reservar | `Reservation` / `Reserve` | `reservation` / ReservationId | «Reserva para el pedido {ExternalReference}» | ExternalReference | CustomerReference |
| Liberar | `Release` / `Release` | `reservation-release` / releaseId | Motivo del usuario | `reservation:{ReservationId}` | CustomerReference |

Los dos hechos llevan como contraparte el cliente del pedido. Así cumplen la regla del PR #7 de A:
`CounterpartyReference` o `CounterpartyNotApplicableReason` en todo hecho nuevo, comprobado con 422
en `ApplyAsync` y con la CHECK SQL.

`OccurredAt` = instante del servidor; el actor sale de la sesión. La unicidad del diario
(tipo, id, `Kind`) impide aplicar dos veces la misma reserva o liberación.

## Migración

`<timestamp>_HU05_CustomerOrdersReservations`, generada después de
`20261119171332_HU08MovementCounterparty` (PR #7 de A; no se genera hasta su fusión).
**Construida**: `20261120113458_HU05_CustomerOrdersReservations`, regenerada sobre
`20261120102651_HU08ExclusiveMovementCounterparty` al fusionar el PR #8 (`939a88f`):

- Crea `CustomerOrders`, `CustomerOrderLines`, `Reservations` y `ReservationAllocations` con las PK,
  FK restrictivas, CHECK, índices únicos [P-01]/[P-02] y la columna calculada anteriores.
- `DENY DELETE` sobre las cuatro tablas para el rol `inventory_runtime`, igual que `StockBalances`
  en `H0_StockCore`.
- Sin datos iniciales. `Down` elimina solo lo que crea.
- `CustomerOrderArticleUsageContributor` usa el nombre de esta migración para saber si el esquema
  está aplicado.

## Proyecciones de lectura (sin tablas)

| Proyección | Fuente | Contrato |
|------------|--------|----------|
| `StockBuckets` (página + totales + `asOf`) | `StockBalanceReader` sobre StockBalances ⋈ Locations ⋈ Lots, SNAPSHOT | [GET /stock](contracts/stock-api.md#get-stock) y, con su propio DTO, `GET /articles/{id}/locations` de HU-02 |
| `ReservationCommitments` | ReservationAllocations ⋈ Reservations ⋈ CustomerOrderLines ⋈ CustomerOrders ⋈ Locations ⋈ Lots, remanente > 0 | [GET /stock/reservations](contracts/stock-api.md#get-stockreservations) |
| `CustomerOrderView` | Pedido, líneas, reservas y asignaciones, solo datos propios (ids, cantidades, referencias y estados), SNAPSHOT, con ETag de `CustomerOrder.Version` | [GET /customer-orders/{id}](contracts/stock-api.md#get-customer-ordersid) y [contrato HU-03](contracts/hu03-integration.md) |
| `CustomerOrderContext` | Articles, Locations y Lots referenciados por las líneas y asignaciones del pedido, SNAPSHOT, sin ETag y con `orderVersion` | [GET /customer-orders/{id}/context](contracts/stock-api.md#get-customer-ordersidcontext) |
