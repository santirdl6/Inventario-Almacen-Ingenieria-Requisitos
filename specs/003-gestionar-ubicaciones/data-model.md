# Modelo de datos: HU-02 — Ubicaciones

**Fecha**: 2026-11-18 · **Plan**: [plan.md](plan.md) · **Decisiones**: [research.md](research.md)
**Fuente común**: [data-model.md global](../../.specify/memory/project/data-model.md#location).
Este documento concreta cómo HU-02 usa y amplía esas entidades; no sustituye al modelo común.
Los cambios que afectan al modelo común están en
[plan.md § Cambios al modelo común pendientes de A](plan.md#cambios-al-modelo-común-pendientes-de-a).

## Location (existente en H0, ampliada)

| Campo | Tipo SQL | Regla | Origen |
|-------|----------|-------|--------|
| Id | uniqueidentifier PK | Generado en aplicación; identidad estable (FR-001). | H0 |
| Code | nvarchar(32), CI | Obligatorio, recortado, mayúsculas; único entre todas, incluidas inactivas (FR-002). | H0 |
| Aisle, Rack, Level | nvarchar(16) | Obligatorios, recortados, mayúsculas, no vacíos (CHECK). | H0 |
| — | — | Collation `Latin1_General_100_CI_AS` y **índice único (Aisle, Rack, Level)** (FR-003). | **HU-02, pendiente de A** |
| Kind | nvarchar(16) | `Storage` o `Receiving` (CHECK). | H0 |
| IsActive | bit | Nace `true` (FR-004). | H0 |
| Version | rowversion | ETag para If-Match (FR-017). | H0 |

No se añaden columnas. Almacén único sin zona ni posición (Clarifications, Q4).

### Reglas e invariantes (dominio + handler, bajo `location:{id}`)

| Regla | Condición | Rechazo | FR |
|-------|-----------|---------|----|
| Identificación bloqueada | Cambiar Code/Aisle/Rack/Level y existe alguna fila `StockBalances` de la ubicación ([D-03](research.md#d-03-tiene-movimientos-se-deriva-de-stockbalances)). | 409 `LocationIdentityLocked` | FR-011 |
| Ubicación ocupada | Desactivar o cambiar Kind con Σ Physical > 0 o Σ Reserved > 0. | 409 `LocationNotEmpty` | FR-012 |
| Conteo abierto | Desactivar o cambiar Kind con conteo activo (HU-04, vía `ILocationUsage`). | 409 `CountScopeFrozen` | FR-012 |
| Cambio de estado aislado | PUT que cambia `isActive` y otro campo. | 422 `StateChangeMustBeSeparate` | FR-020 |
| Última recepción activa | Desactivar `Receiving` o pasarla a `Storage` deja 0 zonas `Receiving` activas, sin `acknowledgeNoActiveReceiving`. | 409 `NoActiveReceivingLeft` | FR-021 |
| Código repetido | Violación del índice único de Code. | 409 `DuplicateLocationCode` (campo `code`, id existente) | FR-002 |
| Lugar repetido | Violación del índice único físico. | 409 `DuplicateLocationPlace` (campos `aisle/rack/level`, id existente) | FR-003 |
| Sin borrado | No hay operación de borrado; FK restringidas. | — (sin ruta DELETE) | FR-014 |

### Estados

```text
           alta (FR-004)
               │
               ▼
   ┌──────── Activa ─────────┐
   │  desactivar: vacía, sin │
   │  reservas ni conteo     │  reactivar (sin condiciones
   ▼                         │  de existencias)
 Inactiva ───────────────────┘
```

No existe estado «eliminada». Una ubicación inactiva no admite Receive/Reserve (lo comprueba
`StockCore` tras el bloqueo) y sigue apareciendo en consultas e historial (FR-013).
Ship y Release sobre una ubicación inactiva no pueden darse con existencias, porque la
desactivación exige que esté vacía y sin reservas.

## MasterDataAudit (común, creada por HU-02 si no existe)

Campos del modelo común: Id, EntityType, EntityId, Action, Before, After, ActorId,
RecordedAt, OperationId. Para HU-02:

| Campo | Valor |
|-------|-------|
| EntityType | `Location` |
| Action | `Create`, `Update`, `Deactivate`, `Reactivate` |
| Before / After | JSON de {code, aisle, rack, level, kind, isActive}; Before nulo en `Create`. |
| ActorId | `ICurrentActor`; FK restringida a AspNetUsers. |
| OperationId | Clave de idempotencia de la petición; único junto a EntityId. |

Solo adición: el usuario SQL de ejecución no tiene UPDATE/DELETE (como el diario).
Índice (EntityType, EntityId, RecordedAt) para el historial de cambios de una ubicación.

## Entidades consumidas sin cambios

| Entidad | Uso en HU-02 |
|---------|--------------|
| StockBalance | Fuente única de cantidades por artículo/ubicación/lote (FR-006–FR-009, D-09) y de «tiene movimientos» (D-03). |
| Lot | Código de lote mostrado; `SIN-LOTE` se presenta como «Sin lote». |
| Article | Sku, Name, BaseUnit, IsActive para mostrar cantidades con unidad y estado textual. |
| OperationResult | Idempotencia de altas y cambios (D-06). |
| StockEntry / StockMovement | No se leen en HU-02; el historial por ubicación lo ofrece HU-08/HU-05. |

## Proyecciones de lectura (DTO, no entidades)

- **LocationSummary**: id, code, aisle, rack, level, description («Pasillo P01 · Estantería
  E01 · Nivel N01»), kind, isActive.
- **LocationDetail**: LocationSummary + version (ETag), hasMovements, isEmpty,
  hasReservations, inOpenCount, isLastActiveReceiving.
- **ArticleLocationBucket**: location (summary), lotCode, physical, reserved, unit.
  Totales del filtro: physical y reserved del artículo.
- **LocationStockLine**: article (id, sku, name, isActive), lotCode, physical, reserved, unit.

Cantidades como cadenas decimales con tres decimales, según el contrato HTTP común.
