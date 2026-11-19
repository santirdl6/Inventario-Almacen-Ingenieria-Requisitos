# Contrato HTTP: HU-02 — Ubicaciones

**Base**: [contrato HTTP común](../../../.specify/memory/project/contracts/api.md#hu-02--b-ubicaciones)
(tipos, paginación, ETag/If-Match, Idempotency-Key, ProblemDetails, sesión y antiforgery).
Este documento concreta los recursos de HU-02 sin repetir esas reglas. Los añadidos que
afectan al contrato común se proponen en
[plan.md § Cambios pendientes de A](../plan.md#cambios-al-modelo-común-pendientes-de-a).

**Revisión 2026-11-19**: se retira `acknowledgeNoActiveReceiving` (la última zona de
recepción se impide); los códigos se alinean con la base de A (`DuplicateCode`,
`VersionConflict`, `InvalidRequest`); sin extensiones fuera de `errors`; se recupera la
regla de paginación con 422.

Todas las rutas cuelgan de `/api/v1`, requieren sesión y **comprueban el permiso en el
propio endpoint** (`RequireAuthorization(policy)`); las mutaciones lo comprueban además en
el `InventoryOperationExecutor` de A.

| Método y ruta | Permiso | Resultado |
|---------------|---------|-----------|
| GET /locations | Inventory.Read | 200 página de LocationSummary. |
| GET /locations/{id} | Inventory.Read | 200 LocationDetail + `ETag`. 404 si no existe. |
| POST /locations | Locations.Manage | 201 `{ operationId, resource: LocationDetail }` + `Location` + `ETag`. |
| PUT /locations/{id} | Locations.Manage | 200 `{ operationId, resource: LocationDetail }` + `ETag`. |
| GET /locations/{id}/stock | Inventory.Read | 200 contenido de la ubicación desde StockBalances. |
| GET /articles/{id}/locations | Inventory.Read | 200 ubicaciones del artículo desde StockBalances. |

No existe `DELETE /locations/{id}` (FR-014): la ruta no se registra y responde 405.

**Paginación** (los tres GET paginados): `page` empieza en 1 y `pageSize` vale 25 por
defecto, con un máximo de 100. Un `page` menor que 1, o un `pageSize` menor que 1 o mayor que
100, **se rechaza con 422 `ValidationFailed`** y `errors.page` / `errors.pageSize`; no se
recorta al límite, para que el cliente no muestre una página distinta de la que pidió. Es la
misma regla que `MasterDataQueryOptions` de A.

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
(siempre `false` hasta HU-04) e `isLastActiveReceiving`. Cabecera `ETag` con el formato de
`EntityTags` de A.

El ETag fuerte cubre la representación completa y la rowversion: SHA-256 de sus valores
serializados, Base64 entre comillas. No es la rowversion aislada: una recepción/reserva
o un cambio de la última zona activa puede cambiar las capacidades sin editar Location.
Detalle y capacidades se leen en SNAPSHOT. PUT revalida el token bajo los bloqueos;
si cambió una capacidad desde la revisión, responde 412. Con el token vigente, retirar
la última zona sigue dando 409 NoActiveReceivingLeft. El replay precede a esa validación.

## POST /locations

Cabeceras: `Idempotency-Key` (UUID obligatorio; si falta o no es válido, 400
`InvalidRequest`), `X-XSRF-TOKEN`.

```json
{ "code": "P01-E01-N01", "aisle": "P01", "rack": "E01", "level": "N01", "kind": "Storage" }
```

Solo se admiten esos campos (otro campo → 422). Normalización: recortar y pasar a
mayúsculas; longitudes 32 (code) y 16 (partes). Crea activa y sin existencias; no genera
movimientos (FR-004). Auditoría `Create`.

## PUT /locations/{id}

Cabeceras: `If-Match` (sin ella 428), `Idempotency-Key`, `X-XSRF-TOKEN`.

```json
{ "code": "P01-E01-N01", "aisle": "P01", "rack": "E01", "level": "N02",
  "kind": "Storage", "isActive": true }
```

Orden de evaluación: forma → ejecutor (permiso, `operation:{key}`, replay antes de If-Match)
→ `location:{id}` y, si se retira una zona `Receiving`, `locations:receiving` → If-Match
(412 `VersionConflict` si está obsoleto) → reglas de
[data-model.md](../data-model.md#reglas-e-invariantes-dominio--comando-bajo-locationid) →
escritura + auditoría (`Update`/`Deactivate`/`Reactivate`) → `OperationResult` y commit.
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
La búsqueda del artículo por código o nombre (US2.1) usa `GET /articles?q=` de A.

## Errores específicos

| Estado | code | Cuándo | Campos |
|--------|------|--------|--------|
| 400 | InvalidRequest | Falta `Idempotency-Key` o no es UUID; If-Match mal formado. | — |
| 403 | Forbidden | Sin `Locations.Manage` (endpoint o ejecutor). | — |
| 409 | DuplicateCode | Código ya usado, también por una inactiva (mapper de A). | `errors.code` |
| 409 | DuplicateLocationPlace | Pasillo+estantería+nivel ya usados; el `detail` nombra el código de la ubicación existente (US1.3), salvo en una carrera resuelta por el índice. | `errors.aisle/rack/level` |
| 409 | LocationIdentityLocked | Cambio de identificación con movimientos. | `errors.<campo>` |
| 409 | LocationNotEmpty | Desactivar o cambiar tipo con existencias o reservas; cantidades en `detail`. | — |
| 409 | CountScopeFrozen | Ídem con conteo abierto (HU-04). | — |
| 409 | NoActiveReceivingLeft | La retirada dejaría el almacén sin zonas de recepción activas. | — |
| 409 | IdempotencyConflict | Clave reutilizada con otro contenido, recurso o actor. | — |
| 412 / 428 | VersionConflict / PreconditionRequired | If-Match obsoleto / ausente. | — |
| 422 | ValidationFailed / StateChangeMustBeSeparate | Campos vacíos, longitudes, caracteres distintos de A–Z, 0–9 y guion en code/aisle/rack/level, tipo inválido, campo no admitido; `page` < 1 o `pageSize` fuera de 1–100; estado + datos. | `errors` |
| 503 | LockTimeout | Bloqueo no obtenido en 5 s. | `Retry-After` |

Los mensajes `detail` están en español y dicen cómo corregir (FR-019), p. ej.
«La ubicación tiene movimientos: su código y su posición forman parte del historial.
Crea una ubicación nueva y desactiva esta cuando quede vacía.» o
«No puede quedar el almacén sin zonas de recepción activas. Crea o activa otra zona de
recepción antes de retirar esta.»
