# Contrato HTTP: HU-05 — Existencias, pedidos de cliente y reservas

**Base común**: [api.md](../../../.specify/memory/project/contracts/api.md): tipos, paginación,
ETag/If-Match, `Idempotency-Key`, antiforgery, ProblemDetails y `GET /operations/{id}`. Este
documento solo concreta las rutas de HU-05. Las diferencias con api.md se proponen en el
[plan](../plan.md#cambios-al-modelo-común-pendientes-de-a) (P-03…P-05). Decisiones en
[research.md](../research.md).

Todas las rutas van bajo `/api/v1`, exigen sesión y declaran su permiso con
`RequireAuthorization`. Las mutaciones lo vuelven a comprobar en `InventoryOperationExecutor`.
Las cantidades viajan como cadenas con tres decimales.

| Método y ruta | Permiso | Resultado |
|---------------|---------|-----------|
| GET /stock | Inventory.Read | 200 `StockQueryResult`. |
| GET /stock/reservations | Inventory.Read | 200 `ReservationCommitments`. |
| GET /customer-orders | Inventory.Read | 200 página de `CustomerOrderSummary` (**P-04**, no está en api.md). **No implementada en esta entrega** (S1 de tasks.md). |
| POST /customer-orders | Shipping.Record | 201 `CustomerOrderView` + `Location` + `ETag`. |
| GET /customer-orders/{id} | Inventory.Read | 200 `CustomerOrderView` (solo datos propios) + `ETag`; 404. |
| GET /customer-orders/{id}/context | Inventory.Read | 200 `CustomerOrderContext` (datos actuales de artículos, ubicaciones y lotes, con `orderVersion`), **sin `ETag`**; 404. |
| POST /reservations | Reservations.Manage | 201 `ReservationResult` + `Location`, sin `ETag` (D-08). |
| GET /reservations/{id} | Inventory.Read | 200 `ReservationView` + `ETag`; 404. |
| POST /reservations/{id}/release | Reservations.Manage | 200 `ReleaseResult`, sin `ETag`. |

Las mutaciones exigen antiforgery (`X-XSRF-TOKEN`) e `Idempotency-Key` (UUID no vacío; si falta o
no es válido, 400 `InvalidRequest`). La ruta de preparación de pruebas de research D-17 se describe
al final; no forma parte de la API de producción. Responden con `Cache-Control: no-store`. No existen `PUT`,
`PATCH` ni `DELETE` de pedidos o reservas (clarificación 3).

## GET /stock

Parámetros:

- `articleId` (obligatorio).
- `locationId` y `lotId` (opcionales).
- `includeEmpty` (bool, por defecto `false`).
- `page` (≥ 1) y `pageSize` (1–100, por defecto 25).

Fuera de rango, 422 con `errors.page` o `errors.pageSize`, como HU-02.

- Sin `articleId`, o con un UUID inválido: 422 `ValidationFailed` con `errors.articleId`
  («Elige un artículo»).
- Artículo inexistente: 404. Un `locationId` o `lotId` inexistente o ajeno al artículo devuelve 200
  sin filas y con totales a cero (caso límite de la spec, sin error técnico).
- Página, totales y recuento salen de una única transacción SNAPSHOT (`asOf` = su inicio).
- Totales: `SUM(Physical)` y `SUM(Reserved)` del filtro completo; `available = physical − reserved`
  calculado tras sumar.
- Con `includeEmpty=false` solo hay filas con `physical > 0`. Los totales no dependen de
  `includeEmpty` ni de la página.
- Orden estable: código de ubicación, código de lote e Id de ubicación.

```json
{
  "asOf": "2026-11-19T10:00:00Z",
  "article": { "id": "…", "sku": "ART-01", "name": "…", "baseUnit": "UD", "isActive": true, "tracksLot": true },
  "totals": { "physical": "6.000", "reserved": "0.000", "available": "6.000" },
  "items": [
    { "location": { "id": "…", "code": "P01-E01-N01", "aisle": "P01", "rack": "E01", "level": "N01", "kind": "Storage", "isActive": true },
      "lot": { "id": "…", "code": "L-001", "isUntracked": false },
      "physical": "2.000", "reserved": "0.000", "available": "2.000" }
  ],
  "page": 1, "pageSize": 25, "totalCount": 2
}
```

En artículos sin seguimiento, `lot.isUntracked = true`. La UI muestra «sin lote», nunca `SIN-LOTE`.

**Relación con HU-02**: `GET /articles/{id}/locations` (contrato de HU-02) y esta ruta usan el mismo
`StockBalanceReader`. La de HU-02 conserva su DTO sin `available` y su filtro de ubicaciones
inactivas. No hay un segundo agregado.

## GET /stock/reservations

Parámetros: `articleId` (obligatorio), `locationId`, `lotId`, `page` y `pageSize` (mismas reglas).
Devuelve una fila por asignación con remanente mayor que cero, ordenada por referencia externa,
ubicación y lote, junto con `totals.remaining` del filtro completo y `asOf`.

```json
{
  "asOf": "…",
  "totals": { "remaining": "7.000" },
  "items": [
    { "customerOrder": { "id": "…", "externalReference": "PED-1001", "state": "Open" },
      "customerOrderLineId": "…", "reservationId": "…", "allocationId": "…", "reservationState": "Active",
      "location": { "id": "…", "code": "P01-E01-N01" }, "lot": { "id": "…", "code": "L-001", "isUntracked": false },
      "allocated": "7.000", "consumed": "0.000", "released": "0.000", "remaining": "7.000" }
  ],
  "page": 1, "pageSize": 25, "totalCount": 1
}
```

## GET /customer-orders

**Estado**: no implementada en esta entrega (S1). Se conserva el diseño para cuando se haga.

Parámetros:

- `search`: prefijo de la referencia externa, sin distinguir mayúsculas.
- `state`: `Open`, `Fulfilled` o `all`; por defecto `Open`.
- `page` y `pageSize`.

Devuelve una página de `{ id, externalReference, customerReference, state, lineCount }` ordenada
por referencia externa.

## POST /customer-orders

```json
{ "externalReference": "PED-1001", "customerReference": "Cliente Norte",
  "lines": [ { "articleId": "…", "quantity": "7" } ] }
```

Normalización: recorte de textos; cantidad con tres decimales en la huella; líneas en el orden
recibido. Resultado: 201 con `{ operationId, resource: CustomerOrderView }`,
`Location: /api/v1/customer-orders/{id}` y `ETag`. El pedido queda `Open`, con servido 0 y sin
reservas.

Errores específicos:

| Estado | code | Cuándo | Campos |
|--------|------|--------|--------|
| 409 | DuplicateCode | Referencia externa ya usada, comparada sin mayúsculas ni espacios extremos. | `errors.externalReference` |
| 409 | ArticleInactive | Alguna línea con artículo desactivado. | `detail` nombra el SKU |
| 422 | ValidationFailed | Referencias vacías o largas; 0 o más de 100 líneas; cantidad no positiva, con más de tres decimales o fraccionaria en un artículo entero; artículo inexistente; artículo repetido. | `errors["lines[i].articleId"]`, `errors["lines[i].quantity"]`, … |

Como se construyó (T008, `7378daf`):

- La validación de forma se hace dentro del ejecutor, después de autorizar, así que un rol sin
  `Shipping.Record` recibe 403 antes que un 422.
- `ArticleInactive` también trae `errors["lines[i].articleId"]`.
- En `resource`, las líneas van ordenadas por Id.

## GET /customer-orders/{id}

`CustomerOrderView`, leído en SNAPSHOT y con `ETag` de `CustomerOrder.Version`. Lleva **solo datos
propios** del pedido, sus líneas, reservas y asignaciones: identificadores, cantidades, referencias
y estados. Todos cambian únicamente mediante escritores que renuevan la versión de la cabecera
(reservar, liberar y, en HU-03, servir), así que el `ETag` fuerte identifica la representación
entera (research D-07 y D-11):

```json
{ "id": "…", "externalReference": "PED-1001", "customerReference": "Cliente Norte", "state": "Open",
  "lines": [
    { "id": "…", "articleId": "…",
      "ordered": "7.000", "shipped": "0.000", "pending": "7.000", "reservedRemaining": "7.000",
      "reservations": [
        { "id": "…", "state": "Active", "allocations": [
          { "id": "…", "articleId": "…", "locationId": "…", "lotId": "…",
            "allocated": "7.000", "consumed": "0.000", "released": "0.000", "remaining": "7.000" } ] } ] } ] }
```

Renombrar un artículo, desactivarlo o cambiar el código de una ubicación **no** cambia esta
representación ni su `ETag`: esos datos están en el contexto.

## GET /customer-orders/{id}/context

`CustomerOrderContext` con los datos actuales que cambian por su cuenta. Sigue el patrón de
`GET /articles/{id}/context` de HU-08: va **sin `ETag`**, con `Cache-Control: no-store`, y se lee
en una transacción SNAPSHOT. Un solo contexto cubre el pedido y todas sus reservas: los artículos
de las líneas, y las ubicaciones y lotes de todas las asignaciones.

```json
{ "orderVersion": "\"AAAAAAAAB9E=\"", "asOf": "2026-11-19T10:00:00Z",
  "articles": [ { "id": "…", "sku": "ART-01", "name": "…", "baseUnit": "UD", "isActive": true,
                  "tracksLot": true, "allowsFractional": false } ],
  "locations": [ { "id": "…", "code": "P01-E01-N01", "aisle": "P01", "rack": "E01", "level": "N01", "isActive": true } ],
  "lots": [ { "id": "…", "articleId": "…", "code": "L-001", "isUntracked": false } ],
  "creationOperationId": "…" }
```

- `orderVersion` es el mismo token que el `ETag` del detalle (`EntityTags.Format`). Si la UI recibe
  un detalle y un contexto de revisiones distintas, vuelve a leer ambos.
- `creationOperationId` (Convergence C3, FR-026): la operación del alta, leída del
  `OperationResult` registrado (`POST /api/v1/customer-orders` con `SourceDocumentId` = pedido). Es
  `null` solo si no existe ese resultado. No forma parte del pedido ni de su `ETag`, y no cambia el
  esquema.
- Pedido inexistente: 404. No es una precondición: reservar y liberar siguen exigiendo el `If-Match`
  del detalle versionado.

## POST /reservations

Cabeceras: `Idempotency-Key` e `If-Match` con el ETag del pedido de la línea.

```json
{ "customerOrderLineId": "…",
  "allocations": [ { "locationId": "…", "lotId": "…", "quantity": "7" } ] }
```

`lotId` se omite o va a `null` en artículos sin seguimiento. Resultado: 201 con
`{ operationId, reservationId, customerOrderId, movementId }` y
`Location: /api/v1/reservations/{reservationId}`. Sin `ETag`: la UI vuelve a leer el pedido.

| Estado | code | Cuándo |
|--------|------|--------|
| 404 | NotFound | Línea inexistente. |
| 409 | CustomerOrderNotOpen (**P-05**, nuevo) | El pedido no está `Open`. |
| 409 | InsufficientStock | Bucket sin saldo o con disponible menor que la cantidad (lo detecta `StockCore` y el `detail` indica el disponible). |
| 409 | ArticleInactive | Artículo desactivado. |
| 409 | CountScopeFrozen | Ubicación en conteo abierto (activo desde HU-04). |
| 409 | IdempotencyConflict | Misma clave con otro cuerpo, recurso o actor. |
| 412 / 428 | VersionConflict / PreconditionRequired | `If-Match` obsoleto o ausente. |
| 422 | ValidationFailed | Sin asignaciones o más de 100; cantidad inválida; bucket repetido; lote ajeno al artículo o ausente si el artículo lo exige; ubicación inactiva; Σ asignaciones + remanente activo de la línea > pendiente (`errors.allocations`). |

Como se construyó (T011, `ad64a17`):

- Los errores por asignación usan `errors["allocations[i].locationId"]` (ubicación inexistente o
  inactiva, bucket repetido) y `errors["allocations[i].lotId"]` (lote ajeno o ausente).
- El bucket repetido se detecta después de resolver `lotId: null` como `SIN-LOTE`.
- Un artículo sin seguimiento que nunca tuvo existencias (sin lote `SIN-LOTE`) responde 409
  `InsufficientStock`.
| 503 | LockTimeout | Bloqueo no obtenido en 5 s; comprobar `operationId` antes de repetir. |

## GET /reservations/{id}

`ReservationView`: `{ id, state, customerOrderId, customerOrderLineId, articleId,
allocations: [ … como en el pedido, solo con ids y cantidades … ] }`, con `ETag` de
`Reservation.Version`. Solo lleva datos propios. Los nombres y códigos actuales salen de
`GET /customer-orders/{customerOrderId}/context`, que cubre también las reservas del pedido.

## POST /reservations/{id}/release

Cabeceras: `Idempotency-Key` e `If-Match` con el ETag de la reserva.

```json
{ "releaseId": "…", "reason": "El cliente reduce el pedido",
  "allocations": [ { "allocationId": "…", "quantity": "2" } ] }
```

- `releaseId` es un UUID del cliente: estable al reintentar y distinto en cada liberación legítima.
- `reason` es texto libre obligatorio, recortado, de 1 a 500 caracteres.
- Se admiten varias asignaciones, cada una una sola vez.
- Se permite con el artículo desactivado o la ubicación en conteo abierto (R-05).

Resultado: 200 con `{ operationId, reservationId, releaseId, movementId, state }`.

| Estado | code | Cuándo |
|--------|------|--------|
| 404 | NotFound | La reserva no existe. |
| 409 | ReservedStockConflict | La cantidad supera el remanente de la asignación, o no queda remanente. |
| 409 | DocumentAlreadyConfirmed | El `releaseId` ya se aplicó con otra clave idempotente. |
| 409 | IdempotencyConflict | Misma clave con otro contenido. |
| 412 / 428 | VersionConflict / PreconditionRequired | `If-Match` de la reserva obsoleto o ausente. |
| 422 | ValidationFailed | Sin motivo (`errors.reason`); `releaseId` vacío; `allocationId` ajeno a la reserva o repetido; cantidad inválida. |

Como se construyó (T013, `de9ff61`):

- `errors["allocations[i].allocationId"]` señala la asignación repetida (forma) o ajena (tras los
  bloqueos).
- El `releaseId` ya aplicado con otra clave lo rechaza el núcleo (409 `DocumentAlreadyConfirmed`),
  después del `If-Match` y del remanente.
| 503 | LockTimeout | Igual que al reservar. |

## Huella idempotente

Para cada mutación, el ejecutor común calcula la huella con el método, la ruta canónica y el JSON
normalizado del DTO (research D-05). La misma clave con la misma intención reproduce el resultado
guardado, incluidas `Location` y, en el alta, `ETag`, aunque el `If-Match` ya esté obsoleto.

## POST /testing/stock-receipts (solo pruebas, research D-17)

Existe solo si `Testing:EnableStockSeeding` es `true` (variable de entorno
`Testing__EnableStockSeeding`, `false` por defecto en `docker-compose.yml`) **y** el entorno no es
Production. En cualquier otro caso la ruta no existe (404). Permiso `Receiving.Record`, antiforgery e
`Idempotency-Key` como el resto.

```json
{ "articleId": "…", "locationId": "…", "lotCode": "L-001", "quantity": "10" }
```

Ejecuta `StockCore` (`Receive`) con el documento `test-receipt`, el motivo «Preparación de pruebas
E2E» y ese mismo texto en `SupportNotApplicableReason` y `CounterpartyNotApplicableReason`.
Responde 200 con el resultado del núcleo. Se retira cuando HU-01 ofrezca la recepción.
