# Contrato HTTP: HU-04 — Conteos cíclicos

Concreta el [contrato común § HU-04](../../../.specify/memory/project/contracts/api.md#hu-04--b-conteos-y-ajustes).
Las rutas o errores que no están en él son propuestas P-03 y P-04 del [plan](../plan.md#cambios-al-modelo-común-pendientes-de-a).
Convenciones comunes: prefijo `/api/v1`, antiforgery en mutaciones, `Idempotency-Key` UUID en
toda escritura, `If-Match` con la versión del conteo en toda escritura sobre un conteo existente
(428 si falta, 412 si no coincide), `application/problem+json` y `GET /operations/{id}` para
comprobar resultados.

## Lecturas (`Inventory.Read`)

| Ruta | Respuesta |
|------|-----------|
| `GET /cycle-counts?state=&correctsShipmentId=&page=&pageSize=` | `{ items, page, pageSize, totalCount }`; cada item: `id`, `countCode`, `state`, `locationIds`, `locationCodes`, `createdBy`, `createdByName`, `createdAt`, `startedBy`, `startedByName`, `startedAt`, `closedBy`, `closedByName` (aprobó o canceló), `closedAt` (FR-023, Convergence C2). `correctsShipmentId` (opcional, Id válido o 422) deja los conteos con alguna línea vinculada a esa salida; solo lectura, `Inventory.Read` (US4.3, Convergence C3). Orden: `createdAt` descendente, luego `id`. |
| `GET /cycle-counts/{id}` | `ETag` fuerte. Solo datos propios (D-12): cabecera, `locations: [{ locationId, isFrozen }]` por `locationId`, `lines` por `id` con `articleId`, `locationId`, `lotCode`, `isFound`, `theoreticalPhysical`, `countedPhysical`, `difference`, `countedBy`, `countedAt`, `reason`, `reasonBy`, `correctsShipmentId` y, si está vinculada, `shipmentCorrectionNotice` (aviso P-07: no cambia lo servido ni el estado del pedido). 404 si no existe. |
| `GET /cycle-counts/{id}/context` | Sin `ETag`. `countVersion`; `articles` (sku, nombre, unidad, `allowsFractions`, `tracksLot`, `isActive`); `locations` (código, activa); `reservedByBucket`; `shipments` vinculadas (`externalReference` del pedido); `users` (nombre visible). |
| `GET /cycle-counts/{id}/lines/{lineId}/shipment-candidates?page=` | Salidas `Confirmed` con alguna línea en el bucket de la línea: `shipmentId`, `customerOrderId`, `externalReference`, `confirmedAt`, `quantity` en el bucket, `linkedCountLineIds`. Más reciente primero. |

## Escrituras

| Ruta | Permiso | Estado | Cuerpo | Éxito |
|------|---------|--------|--------|-------|
| `POST /cycle-counts` | Counts.Record | — | `{ locationIds: uuid[] }` (1–50, sin repetir, activas) | 201, `Location`, `ETag`, `operationId` |
| `PUT /cycle-counts/{id}` | Counts.Record | Draft | `{ locationIds }` | 200, `ETag` |
| `POST /cycle-counts/{id}/start` | Counts.Record | Draft | — | 200, `ETag`; líneas esperadas creadas |
| `PUT /cycle-counts/{id}/lines` | Counts.Record | Counting | `{ counted: [{ lineId, countedPhysical }], found: [{ articleId, locationId, lotCode?, countedPhysical }] }` (los motivos solo en `review`) | 200, `ETag` |
| `DELETE /cycle-counts/{id}/lines/{lineId}` | Counts.Record | Counting | — (solo hallazgos) | 204, `ETag` |
| `POST /cycle-counts/{id}/submit` | Counts.Record | Counting | — | 200, `ETag` |
| `PUT /cycle-counts/{id}/review` | Inventory.Adjust | AwaitingApproval | `{ lines: [{ lineId, reason, correctsShipmentId? }] }` | 200, `ETag` |
| `POST /cycle-counts/{id}/confirm` | Inventory.Adjust | AwaitingApproval | — | 200 `{ operationId, countId, movementId?, adjustedLines: [{ lineId, difference }], shipmentCorrections: [{ lineId, shipmentId, customerOrderId, orderChanged: false, notice }] }`, sin `ETag` |
| `POST /cycle-counts/{id}/cancel` | Inventory.Adjust | Draft, Counting, AwaitingApproval | `{ reason }` (1–500) | 200 `{ operationId, countId }`, sin `ETag` |

El cuerpo se normaliza antes de la idempotencia (cantidades, lotes en mayúsculas y orden), como en
HU-05 D-05: la misma intención con otro cuerpo da `409 IdempotencyConflict`.

## Errores

| Código | Cuándo |
|--------|--------|
| 401 / 403 / 400 | Sin sesión / sin el permiso de la fila / sin antiforgery. 403 antes de 422. |
| 404 | Conteo, línea o salida candidata inexistentes. |
| 412 / 428 | `If-Match` distinto o ausente. |
| 422 `ValidationFailed` | Forma, ubicación inactiva o repetida, cantidad negativa o con fracción no admitida, hallazgo fuera del alcance o sin lote en un artículo con lotes, línea esperada sin contar al enviar (`errors["lines[i].countedPhysical"]`), diferencia sin motivo al confirmar, vínculo a una salida no confirmada o que no tocó el bucket. |
| 409 `CountScopeFrozen` | Al iniciar, una ubicación ya está en otro conteo abierto (nombra `countCode`). En los escritores de B, operación sobre un alcance congelado. |
| 409 `CountStateConflict` | La acción no corresponde al estado actual (p. ej. contar en `AwaitingApproval`). Nuevo (P-04). |
| 409 `CountTheoreticalChanged` | Al confirmar, el físico de un bucket ya no es el teórico (D-05). Nuevo (P-04). |
| 409 `ReservedStockConflict` | Al confirmar, el contado de un bucket queda por debajo de su reservado; nombra bucket, contado y reservado. |
| 409 `DocumentAlreadyConfirmed` | Escritura sobre un conteo cerrado. |
| 409 `IdempotencyConflict` | Misma clave con otro cuerpo. |
| 503 `LockTimeout` | Bloqueo no obtenido: comprobar el resultado antes de repetir. |

## Efectos en otros contratos

- `ILocationUsage.IsInOpenCountAsync` devuelve el valor real (D-04). Sin cambios de forma en
  `GET /locations` (`inOpenCount`), `POST /shipments/{id}/confirm`, `POST /reservations`
  ni `PUT /locations/{id}`: empiezan a devolver `409 CountScopeFrozen` cuando corresponde.
- `POST /reservations/{id}/release` no cambia: sigue admitida sobre un alcance
  congelado (clarificación Q1).
- El historial de artículo muestra el movimiento `Adjustment` con documento `cycle-count`.
- Limitación P-07 (aprobada por A el 2026-11-21): vincular una línea a una salida corrige solo el
  físico. `Shipments`, `CustomerOrderLines.ShippedQuantity`, el estado del pedido y los consumos de
  reservas no cambian; la respuesta lo declara con `orderChanged: false` y `notice`.
