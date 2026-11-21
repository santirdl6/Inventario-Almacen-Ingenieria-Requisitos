# Data Model: HU-03 — Registrar la salida de mercancía

**Fuente de verdad**: [modelo común](../../.specify/memory/project/data-model.md#shipment-y-shipmentline).
Este documento concreta columnas, restricciones y transiciones de HU-03. Las ampliaciones al modelo
común se proponen como diff en el [plan](plan.md#cambios-al-modelo-común-pendientes-de-a) (P-01, P-02)
y no se aplican en esta rama. Las entidades de HU-05 se consumen según su
[modelo](../004-consultar-existencias/data-model.md) y su
[contrato de integración](../004-consultar-existencias/contracts/hu03-integration.md).

Tipos comunes: cantidades `decimal(18,3)` (`StockColumns.Quantity`), UUID generados por el servidor
(`ValueGeneratedNever`), `rowversion` como versión y fechas `datetimeoffset`.

## Shipment (tabla `Shipments`)

| Columna | Tipo | Regla |
|---------|------|-------|
| Id | uniqueidentifier | PK. Lo decide el comando de alta fuera de `change` (estable en reintentos). |
| CustomerOrderId | uniqueidentifier | FK `CustomerOrders` (Restrict). Inmutable tras el alta. |
| State | nvarchar(16) | `Draft` o `Confirmed` (CHECK). Solo `Draft → Confirmed`. |
| OccurredAt | datetimeoffset | Fecha y hora del hecho; obligatoria; ≤ ahora + 5 min (D-07). Editable en borrador. |
| RecordedAt | datetimeoffset | Último registro: alta o edición del borrador; al confirmar, la confirmación. |
| ActorId | uniqueidentifier | Quien registró `RecordedAt` (sesión, nunca el cliente). El autor del alta consta en su `OperationResult`. |
| OperationId | uniqueidentifier null | Operación de confirmación. CHECK `CK_Shipments_OperationId`: en `Draft`, `OperationId`, `DeclaredConsumptionCount` y `DeclaredConsumedQuantity` son null; en `Confirmed`, ninguno lo es. Índice único filtrado. Sin FK (como `OperationResult.SourceDocumentId`). |
| DeclaredConsumptionCount | int null | Convergence C1: número de filas de consumo que la confirmación inserta; se fija en la misma sentencia que pasa a `Confirmed`. CHECK `>= 0`. Inmutable (trigger). |
| DeclaredConsumedQuantity | decimal(18,3) null | Convergence C1: suma de esas filas, en la misma sentencia. CHECK `>= 0`. Inmutable (trigger). |
| Version | rowversion | `ETag` de la salida. |

Índice `IX_Shipments_CustomerOrder (CustomerOrderId, RecordedAt DESC, Id)` para la lista del pedido.

## ShipmentLine (tabla `ShipmentLines`)

| Columna | Tipo | Regla |
|---------|------|-------|
| Id | uniqueidentifier | PK; orden estable de las líneas en todas las lecturas. |
| ShipmentId | uniqueidentifier | FK `Shipments` (Cascade, solo efectivo al eliminar un borrador; el trigger impide borrar confirmadas). |
| CustomerOrderLineId | uniqueidentifier | FK `CustomerOrderLines` (Restrict). Debe pertenecer al pedido de la salida (comando). |
| ArticleId | uniqueidentifier | FK `Articles` (Restrict). Igual al artículo de la línea de pedido (comando). |
| LocationId | uniqueidentifier | FK `Locations` (Restrict). |
| LotId | uniqueidentifier | FK compuesta `(ArticleId, LotId)` → `Lots (ArticleId, Id)` (Restrict), como `StockBalance` y `ReservationAllocation`. `SIN-LOTE` para artículos sin seguimiento. |
| Quantity | decimal(18,3) | CHECK `> 0`. Sin fracciones si el artículo no las admite (comando). |

Único `UX_ShipmentLines_Shipment_Bucket (ShipmentId, CustomerOrderLineId, LocationId, LotId)` (spec
FR-002). Como la línea de pedido es única por artículo (P-01 de HU-05), equivale a un bucket por
salida, que es lo que exige `StockCore` (un bucket por comando).

`OwnReservationConsumed` del modelo común **no se almacena**: se obtiene como Σ
`ShipmentReservationConsumptions.Quantity` de la línea (research D-06, D-08). Vale 0 en borradores y
está entre 0 y `Quantity` en confirmadas.

## ShipmentReservationConsumption (tabla `ShipmentReservationConsumptions`) — nueva, P-02

| Columna | Tipo | Regla |
|---------|------|-------|
| Id | uniqueidentifier | PK. |
| ShipmentLineId | uniqueidentifier | FK `ShipmentLines` (Restrict). |
| ReservationAllocationId | uniqueidentifier | FK `ReservationAllocations` (Restrict). Misma línea de pedido y bucket que la línea de salida (comando). |
| Quantity | decimal(18,3) | CHECK `> 0`. |
| OperationId | uniqueidentifier | Operación que crea la fila (`context.OperationId` de la confirmación). Obligatoria. Sin FK, como `Shipments.OperationId`. |

Único `(ShipmentLineId, ReservationAllocationId)`. Append-only: `DENY UPDATE, DELETE` a
`inventory_runtime`. Solo se insertan al confirmar. `TR_ShipmentReservationConsumptions_ConfirmingOperation`
rechaza un `INSERT` cuya cabecera esté `Confirmed` con otro `OperationId` o que supere los totales declarados
(`DeclaredConsumptionCount`, `DeclaredConsumedQuantity`; Convergence C1). El complemento de
`TR_Shipments_ConfirmedImmutable` rechaza confirmar si ya hay consumos de otra operación (research
D-06, iteración 2026-11-21). La regla vale en cualquier orden de sentencias dentro de la
confirmación.

## Cambios de estado en entidades de HU-05 (los aplica la confirmación)

| Entidad | Campo | Cambio | Regla |
|---------|-------|--------|-------|
| `ReservationAllocation` | `ConsumedQuantity` | `+ consumo` vía `Consume(q)` (nuevo, D-04) | `q > 0`, `q ≤ Remaining`; `Remaining` sigue siendo columna calculada. |
| `Reservation` | `State`, `Version` | `DeriveState()` y `Touch` si cambió algún consumo | `Active` → `Consumed` si no queda remanente y todo se consumió; `Released` si hubo liberaciones. |
| `CustomerOrderLine` | `ShippedQuantity` | `+ Σ cantidades` vía `Ship(q)` (nuevo, D-04) | `Shipped ≤ Ordered` (CHECK existente de HU-05). |
| `CustomerOrder` | `State`, `Version` | `Fulfilled` si todas las líneas quedan con pendiente 0; `Touch` siempre | `Open → Fulfilled`; `Cancelled` sigue sin ser alcanzable. |

## Transiciones

```text
Shipment:        (alta) → Draft ──PUT──▶ Draft
                             │──DELETE──▶ (no existe; sin movimientos, porque nunca los tuvo)
                             └──confirm──▶ Confirmed   (inmutable: trigger; sin reversión, Q3)

CustomerOrder:   Open ──confirm con pendiente 0 en todas las líneas──▶ Fulfilled
Reservation:     Active ──confirm consume hasta remanente 0──▶ Consumed (o Released, si hubo liberación)
```

## Invariantes comprobables (quickstart H-14)

1. Por bucket: Σ `Remaining` de asignaciones = `StockBalance.Reserved` (S-07 de HU-05, tras cada salida).
2. Por asignación: Σ consumos = `ConsumedQuantity`.
3. Por línea de salida confirmada: Σ consumos = entrada `ShipReserved` del movimiento en su bucket, y
   `Quantity` − Σ consumos = entrada `ShipAvailable`.
4. Por línea de pedido: `ShippedQuantity` = Σ `Quantity` de sus líneas de salidas confirmadas, y
   Σ remanente activo ≤ pendiente.
5. Diario y saldo: `AssertJournalMatchesBalanceAsync` de HU-05.
6. Una sola fila `StockMovements` de tipo `Shipment` por salida confirmada (unicidad común
   tipo/documento/Kind).
7. Por salida confirmada: todo consumo de sus líneas tiene `OperationId` = `Shipments.OperationId`,
   así que no se añaden consumos de otra operación tras confirmar. La garantizan los triggers de la
   base de datos y se prueba en H-02; H-14 la comprueba además en la conciliación.
8. Por salida confirmada (Convergence C1): número de consumos = `DeclaredConsumptionCount` y su suma
   = `DeclaredConsumedQuantity`. SQL impide superarlos, también con el mismo `OperationId`, así que
   ningún consumo se añade tras confirmar. Que se alcancen exactamente lo comprueba H-14.

## Hecho del diario

| Campo | Valor (research D-07) |
|-------|-----------------------|
| Kind / SourceDocumentType / SourceDocumentId | `Shipment` / `shipment` / `Shipment.Id` |
| OccurredAt / RecordedAt / ActorId / OperationId | De la salida y de la sesión |
| Reason | `Salida del pedido {ExternalReference}` |
| SupportReference | `ExternalReference` del pedido |
| CounterpartyReference | `CustomerReference` del pedido (nunca `CounterpartyNotApplicableReason`) |
| Entradas | Por bucket: `ShipReserved` (consumo) y/o `ShipAvailable` (resto) |

## Migración

`20261121135335_HU03_Shipments` (construida; regenerada en Convergence C1, antes `20261121114907`), generada sobre
`20261120113458_HU05_CustomerOrdersReservations` (research D-10):

- Tablas, FK, CHECK e índices de este documento.
- A mano:
  - `TR_Shipments_ConfirmedImmutable`, con el complemento de la transición `Draft → Confirmed` (51302
    otra operación; 51306 más consumos de los declarados, C1);
  - `TR_ShipmentLines_ConfirmedImmutable`;
  - `TR_ShipmentReservationConsumptions_ConfirmingOperation` (D-06: 51304 otra operación; 51305 por
    encima de lo declarado, C1);
  - `DENY UPDATE, DELETE ON OBJECT::dbo.ShipmentReservationConsumptions TO inventory_runtime`.
- `HasTrigger` en las tres configuraciones EF. Todos los triggers solo hacen `THROW` (decisión M3).
- `Down` elimina triggers, permisos y tablas en orden inverso.
- Constante `ShipmentSchema.Migration` para el contributor de uso (D-09).
