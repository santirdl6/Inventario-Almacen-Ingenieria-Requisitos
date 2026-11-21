# Contrato HTTP: HU-03 — Salidas

**Base común**: [api.md](../../../.specify/memory/project/contracts/api.md#hu-03-y-hu-05--b-pedidos-de-cliente-reservas-y-salidas)
(sesión, antiforgery, `application/problem+json`, paginación, `Idempotency-Key`, `If-Match`/`ETag`,
`GET /operations/{id}`). Lecturas de HU-05 que se consumen sin cambios:
`GET /customer-orders/{id}` y `/context`, `GET /stock` ([stock-api.md de HU-05](../../004-consultar-existencias/contracts/stock-api.md)).
Las diferencias con la tabla común se proponen en el [plan](../plan.md#cambios-al-modelo-común-pendientes-de-a) (P-03…P-05).

Todas las rutas van bajo `/api/v1`, exigen sesión y comprueban el permiso en el endpoint y, en las
escrituras, otra vez en el ejecutor común. Las cantidades viajan como cadena con hasta tres decimales.

| Método y ruta | Permiso | Respuesta |
|---------------|---------|-----------|
| GET /customer-orders/lookup?externalReference= | Inventory.Read | 200 `CustomerOrderLookup`; 404; 422 |
| POST /shipments | Shipping.Record | 201 `ShipmentMutation` + `ETag` + `Location` |
| GET /shipments?customerOrderId=&page=&pageSize= | Inventory.Read | 200 página de `ShipmentSummary`; 404 si el pedido no existe |
| GET /shipments/{id} | Inventory.Read | 200 `ShipmentView` + `ETag`; 404 |
| GET /shipments/{id}/context | Inventory.Read | 200 `ShipmentContext`, sin `ETag`; 404 |
| PUT /shipments/{id} | Shipping.Record | 200 `ShipmentMutation` + `ETag` |
| DELETE /shipments/{id} | Shipping.Record | 204 |
| POST /shipments/{id}/confirm | Shipping.Record | 200 `ShipmentConfirmation`, sin `ETag` |

## GET /customer-orders/lookup

`externalReference` obligatorio (1–64 tras recortar). Comparación exacta sin distinguir mayúsculas
ni espacios extremos (research D-11).

```json
{ "id": "…", "externalReference": "PED-0042", "state": "Open" }
```

404 `NotFound`: «No hay ningún pedido con esa referencia.» 422 `ValidationFailed` con
`errors.externalReference`.

## POST /shipments y PUT /shipments/{id}

Cabeceras: `Idempotency-Key` (UUID); en `PUT`, `If-Match` de la salida.

```json
{
  "customerOrderId": "…",
  "occurredAt": "2026-11-20T09:30:00+01:00",
  "lines": [
    { "customerOrderLineId": "…", "locationId": "…", "lotId": "…", "quantity": "4" }
  ]
}
```

- `customerOrderId` solo en `POST`; en `PUT` se ignora si coincide y da 422 si difiere.
- Entre 1 y 100 líneas; `lotId` obligatorio (el de `SIN-LOTE` en artículos sin seguimiento, tal como
  lo devuelve `GET /stock`).
- Validación de forma (422, todos los errores a la vez): `errors["lines[i].customerOrderLineId" |
  "lines[i].locationId" | "lines[i].lotId" | "lines[i].quantity"]`, `errors.occurredAt` (futura),
  `errors.lines` (vacía, más de 100 o línea de pedido, ubicación y lote repetidos).
- Bajo bloqueo (`shipment:{id}` → artículos): pedido existente (404) y `Open` (409
  `CustomerOrderNotOpen`); línea del pedido; lote del artículo; bucket con saldo
  (409 `InsufficientStock` si no existe); fracciones; Σ por línea de pedido ≤ pendiente actual (422).
  En `PUT`: salida en `Draft` (409 `DocumentAlreadyConfirmed`) y `If-Match` (428/412).
  **Construido** (T007, `c09ae87`): tras 404 y 409 del pedido, primero los 422 de línea del
  pedido (`lines[i].customerOrderLineId`), ubicación inexistente (`lines[i].locationId`) y lote ajeno
  (`lines[i].lotId`); después 409 `InsufficientStock` del bucket sin existencias
  (`errors.lines[i].locationId`); y al final los 422 de fracciones y pendiente
  (`lines[i].quantity`). Un artículo inactivo se admite (FR-014).
- Respuesta: `{ "operationId", "shipment": ShipmentView }`, `ETag` y, en `POST`, `Location:
  /api/v1/shipments/{id}`.

## DELETE /shipments/{id}

Cabeceras `Idempotency-Key` e `If-Match`. Bajo bloqueo: `Draft` (si no, 409
`DocumentAlreadyConfirmed`), `If-Match` (428/412), eliminación de cabecera y líneas. 204 sin cuerpo;
el replay devuelve el mismo 204 (`ResultBody` null). Un `DELETE` posterior con otra clave: 404.

## POST /shipments/{id}/confirm

Cabeceras `Idempotency-Key` e `If-Match` de la salida. Cuerpo, que forma parte de la huella:

```json
{ "orderVersion": "\"AAAAAAAAB9E=\"" }
```

Orden de rechazos (research D-04): 404 → 409 `DocumentAlreadyConfirmed` → 428/412 de la salida →
412 si `orderVersion` no es la versión actual del pedido → 409 `CustomerOrderNotOpen` → 422 por
superar el pendiente (`errors.lines[i].quantity`) → 409 `CountScopeFrozen` → 409
`ReservedStockConflict` (remanente sobrante; `detail` con la línea y la cantidad a liberar) → 409
`InsufficientStock` (`detail` con físico, disponible y cantidad posible) → 503 `LockTimeout`.

**Construido** (T009, `488bb5a`):

- Sin `orderVersion` en el cuerpo, 422 `errors.orderVersion`, antes de leer nada.
- El `detail` de `InsufficientStock` lo redacta `StockCore`: dice el físico y el disponible del
  bucket, pero **no la cantidad posible** (incumplimiento G1 de
  [converge-1](../validation/converge-1.md)).
- La respuesta 200 sale sin `ETag`. La cabecera declara `DeclaredConsumptionCount` y
  `DeclaredConsumedQuantity` (Convergence C1, `a6ec4b2`).

```json
{
  "operationId": "…", "shipmentId": "…", "movementId": "…",
  "customerOrderId": "…", "orderState": "Open",
  "lines": [ { "id": "…", "quantity": "6.000", "ownReservationConsumed": "4.000" } ]
}
```

Sin `ETag` (el diario impide guardar dentro de `change`; HU-05 D-08). La UI vuelve a leer la salida y
el pedido. Repetir con la misma clave devuelve el resultado original; otra clave sobre la salida ya
confirmada, 409 `DocumentAlreadyConfirmed`.

## GET /shipments/{id} — solo datos propios

```json
{
  "id": "…", "customerOrderId": "…", "state": "Confirmed",
  "occurredAt": "…", "recordedAt": "…", "actorId": "…", "operationId": "…",
  "lines": [
    {
      "id": "…", "customerOrderLineId": "…", "articleId": "…", "locationId": "…", "lotId": "…",
      "quantity": "6.000", "ownReservationConsumed": "4.000",
      "consumptions": [ { "reservationAllocationId": "…", "reservationId": "…", "quantity": "4.000" } ]
    }
  ]
}
```

Líneas y consumos ordenados por Id. `ownReservationConsumed` es 0 y `consumptions` vacío en borradores.
`ETag` = `Shipment.Version`; `Cache-Control: no-store`.

## GET /shipments/{id}/context — datos actuales, sin ETag

```json
{
  "shipmentVersion": "\"…\"", "orderVersion": "\"…\"", "asOf": "…",
  "actorName": "operario@…",
  "order": { "id": "…", "externalReference": "PED-0042", "customerReference": "Cliente 7", "state": "Open" },
  "articles": [ { "id": "…", "sku": "…", "name": "…", "baseUnit": "UD", "isActive": true, "tracksLot": true, "allowsFractional": false } ],
  "locations": [ { "id": "…", "code": "P01-E01-N01", "aisle": "…", "rack": "…", "level": "…", "isActive": true } ],
  "lots": [ { "id": "…", "articleId": "…", "code": "L-001", "isUntracked": false } ],
  "review": [
    { "lineId": "…", "pending": "6.000", "physical": "10.000", "reserved": "7.000",
      "ownReserved": "4.000", "otherReserved": "3.000", "possible": "6.000" }
  ]
}
```

`review` solo en borradores, una fila por línea ordenada por Id: `possible = min(physical,
ownReserved + (physical − reserved), pending)`. Es orientativo: el servidor revalida al confirmar.
Leído en `SNAPSHOT`; la UI recarga si `shipmentVersion` no coincide con el `ETag` del detalle.

## GET /shipments?customerOrderId=

`customerOrderId` obligatorio (422 sin él; 404 si no existe). Paginación común (`Paging`, 422 fuera
de rango). Orden `recordedAt` descendente y Id. Fila `ShipmentSummary`:
`{ id, state, occurredAt, recordedAt, actorName, lineCount, totalQuantity }` (sin `ETag`).

## Errores nuevos

Ninguno. Se usan `NotFound`, `ValidationFailed`, `CustomerOrderNotOpen` (HU-05, P-05 de su plan),
`DocumentAlreadyConfirmed`, `InsufficientStock`, `ReservedStockConflict`, `CountScopeFrozen`,
`IdempotencyConflict`, `PreconditionFailed`/`PreconditionRequired`, `Forbidden` y `LockTimeout`.
