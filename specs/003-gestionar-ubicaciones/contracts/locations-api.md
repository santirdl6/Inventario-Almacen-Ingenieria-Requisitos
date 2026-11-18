# Contrato HTTP: HU-02 — Ubicaciones

**Base**: [contrato HTTP común](../../../.specify/memory/project/contracts/api.md#hu-02--b-ubicaciones)
(tipos, paginación, ETag/If-Match, Idempotency-Key, ProblemDetails, sesión y antiforgery).
Este documento concreta los recursos de HU-02 sin repetir esas reglas. Los añadidos que
afectan al contrato común se proponen en
[plan.md § Cambios pendientes de A](../plan.md#cambios-al-modelo-común-pendientes-de-a).

Todas las rutas cuelgan de `/api/v1`, requieren sesión y **comprueban el permiso en el
propio endpoint** (`RequireAuthorization(policy)`); ni `StockCore` ni los handlers
de consulta autorizan por sí mismos.

| Método y ruta | Permiso | Resultado |
|---------------|---------|-----------|
| GET /locations | Inventory.Read | 200 página de LocationSummary. |
| GET /locations/{id} | Inventory.Read | 200 LocationDetail + `ETag`. 404 si no existe. |
| POST /locations | Locations.Manage | 201 LocationDetail + `Location` + `ETag`. |
| PUT /locations/{id} | Locations.Manage | 200 LocationDetail + `ETag`. |
| GET /locations/{id}/stock | Inventory.Read | 200 contenido de la ubicación desde StockBalances. |
| GET /articles/{id}/locations | Inventory.Read | 200 ubicaciones del artículo desde StockBalances. |

No existe `DELETE /locations/{id}` (FR-014): la ruta no se registra y responde 405.

## GET /locations

Query: `search` (prefijo de código o pasillo, sin distinguir mayúsculas), `kind`
(`Storage`|`Receiving`), `status` (`active` por defecto | `inactive` | `all`),
`page`, `pageSize`. Orden estable por `code`.

```json
{ "items": [ { "id": "…", "code": "P01-E01-N01", "aisle": "P01", "rack": "E01",
  "level": "N01", "description": "Pasillo P01 · Estantería E01 · Nivel N01",
  "kind": "Storage", "kindLabel": "Almacenamiento", "isActive": true,
  "statusLabel": "Activa" } ], "page": 1, "pageSize": 25, "totalCount": 1 }
```

Los selectores de destino/origen de otras historias usan `status=active`.

## GET /locations/{id}

LocationSummary más `hasMovements`, `isEmpty`, `hasReservations`, `inOpenCount`
(siempre `false` hasta HU-04) e `isLastActiveReceiving`. Cabecera `ETag: "<rowversion base64>"`.

## POST /locations

Cabeceras: `Idempotency-Key` (UUID, obligatoria; si falta, 400 `IdempotencyKeyRequired`),
`X-XSRF-TOKEN`.

```json
{ "code": "P01-E01-N01", "aisle": "P01", "rack": "E01", "level": "N01", "kind": "Storage" }
```

Normalización: recortar y pasar a mayúsculas; longitudes 32 (code) y 16 (partes).
Crea activa y sin existencias; no genera movimientos (FR-004). Auditoría `Create`.

## PUT /locations/{id}

Cabeceras: `If-Match` (sin ella 428), `Idempotency-Key`, `X-XSRF-TOKEN`.

```json
{ "code": "P01-E01-N01", "aisle": "P01", "rack": "E01", "level": "N02",
  "kind": "Storage", "isActive": true, "acknowledgeNoActiveReceiving": false }
```

Orden de evaluación: forma → idempotencia (replay antes de If-Match) → bloqueos
`location:{id}` y, si procede, `locations:receiving` → If-Match (412 si obsoleta) →
reglas de [data-model.md](../data-model.md#reglas-e-invariantes-dominio--handler-bajo-locationid) →
escritura + auditoría (`Update`/`Deactivate`/`Reactivate`) + OperationResult → commit.
Una petición sin cambios efectivos devuelve 200 sin auditoría.

## GET /locations/{id}/stock

Query: `page`, `pageSize`. Solo buckets con físico o reservado distinto de cero.

```json
{ "asOf": "2026-11-18T10:00:00Z", "location": { "id": "…", "code": "P01-E01-N02", "isActive": true },
  "items": [ { "article": { "id": "…", "sku": "ART-01", "name": "…", "isActive": true },
    "lotCode": "L-001", "physical": "4.000", "reserved": "0.000", "unit": "UD" } ],
  "hasReservations": false, "inOpenCount": false, "page": 1, "pageSize": 25, "totalCount": 1 }
```

Sin total global porque los artículos tienen unidades distintas.

## GET /articles/{id}/locations

Query: `page`, `pageSize`, `includeInactiveLocations` (por defecto `true`: el estado no
oculta existencias, FR-007). Orden por código de ubicación y lote.

```json
{ "asOf": "…", "article": { "id": "…", "sku": "ART-01", "name": "…", "unit": "UD", "isActive": true },
  "totals": { "physical": "6.000", "reserved": "0.000" },
  "items": [ { "location": { "id": "…", "code": "P01-E01-N01", "description": "…",
      "kind": "Storage", "isActive": true }, "lotCode": "L-001",
      "physical": "2.000", "reserved": "0.000" } ],
  "page": 1, "pageSize": 25, "totalCount": 2 }
```

`totals` agrega todo el artículo, no la página (Edge Cases). Página y totales se leen en
la misma instantánea SNAPSHOT. Artículo sin existencias: `items` vacío y totales cero, 200.

## Errores específicos

| Estado | code | Cuándo | Extras |
|--------|------|--------|--------|
| 400 | IdempotencyKeyRequired | Falta o no es UUID. | — |
| 409 | DuplicateLocationCode | Código ya usado, también por una inactiva. | `errors.code`, `existingLocationId` |
| 409 | DuplicateLocationPlace | Pasillo+estantería+nivel ya usados. | `errors.aisle/rack/level`, `existingLocationId` |
| 409 | LocationIdentityLocked | Cambio de identificación con movimientos. | `errors.<campo>` |
| 409 | LocationNotEmpty | Desactivar o cambiar tipo con existencias o reservas. | `physical`, `reserved` |
| 409 | CountScopeFrozen | Ídem con conteo abierto (HU-04). | — |
| 409 | NoActiveReceivingLeft | Quedaría sin zonas de recepción activas sin reconocimiento. | — |
| 409 | IdempotencyConflict | Clave reutilizada con otro contenido, recurso o actor. | `operationId` |
| 412 / 428 | PreconditionFailed / PreconditionRequired | If-Match obsoleto / ausente. | ETag vigente |
| 422 | ValidationFailed / StateChangeMustBeSeparate | Campos vacíos, longitudes, tipo inválido; estado + datos. | `errors` |
| 503 | LockTimeout | Bloqueo no obtenido en 5 s. | `Retry-After`, `operationId` |

Los mensajes `detail` están en español y dicen cómo corregir (FR-019), p. ej.
«La ubicación tiene movimientos: su código y su posición forman parte del historial.
Crea una ubicación nueva y desactiva esta cuando quede vacía.»
