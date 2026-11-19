# Modelo de datos: HU-02 — Ubicaciones

**Fecha**: 2026-11-18, revisado el 2026-11-19 · **Plan**: [plan.md](plan.md) · **Decisiones**: [research.md](research.md)
**Fuente común**: [data-model.md global](../../.specify/memory/project/data-model.md#location).
Este documento concreta cómo HU-02 usa y amplía esas entidades; no sustituye al modelo común.
Los cambios que afectan al modelo común están en
[plan.md § Cambios al modelo común pendientes de A](plan.md#cambios-al-modelo-común-pendientes-de-a).

**Revisión 2026-11-19**: la unicidad física está validada por A; `MasterDataAudit` ya existe
(A) y HU-02 solo añade la entidad `Location`; la última zona de recepción se **impide**
(sin reconocimiento); los códigos de error se alinean con los de A.

## Location (existente en H0, ampliada)

| Campo | Tipo SQL | Regla | Origen |
|-------|----------|-------|--------|
| Id | uniqueidentifier PK | Generado en aplicación; identidad estable (FR-001). | H0 |
| Code | nvarchar(32), CI | Obligatorio, recortado, mayúsculas; único entre todas, incluidas inactivas (FR-002). | H0 |
| Aisle, Rack, Level | nvarchar(16) | Obligatorios, recortados, mayúsculas, no vacíos. | H0 |
| — | — | Collation `Latin1_General_100_CI_AS` e **índice único `UX_Locations_Aisle_Rack_Level`** (FR-003). | **HU-02, validado por A** |
| Kind | nvarchar(16) | `Storage` o `Receiving`. | H0 |
| IsActive | bit | Nace `true` (FR-004). | H0 |
| Version | rowversion | ETag para If-Match (FR-017), con `EntityTags` de A. | H0 |

No se añaden columnas. Almacén único sin zona ni posición (Clarifications, Q4 y sesión
2026-11-19).

### Reglas e invariantes (dominio + comando, bajo `location:{id}`)

| Regla | Condición | Rechazo | FR |
|-------|-----------|---------|----|
| Identificación bloqueada | Cambiar Code/Aisle/Rack/Level y existe alguna fila `StockBalances` de la ubicación ([D-03](research.md#d-03-tiene-movimientos-se-deriva-de-stockbalances)). | 409 `LocationIdentityLocked` | FR-011 |
| Ubicación ocupada | Desactivar o cambiar Kind con Σ Physical > 0 o Σ Reserved > 0. | 409 `LocationNotEmpty` (cantidades en `detail`) | FR-012 |
| Conteo abierto | Desactivar o cambiar Kind con conteo activo (HU-04, vía `ILocationUsage`). | 409 `CountScopeFrozen` | FR-012 |
| Cambio de estado aislado | PUT que cambia `isActive` y otro campo. | 422 `StateChangeMustBeSeparate` | FR-020 |
| Última recepción activa | Desactivar `Receiving` o pasarla a `Storage` dejaría 0 zonas `Receiving` activas; se evalúa con `locations:receiving` ([D-05](research.md#d-05-última-zona-de-recepción-activa-fr-021)). | 409 `NoActiveReceivingLeft`, sin cambios | FR-021 |
| Código repetido | Violación del índice único de Code. | 409 `DuplicateCode` (A; `errors.code`) | FR-002 |
| Lugar repetido | Otra ubicación tiene el mismo (Aisle, Rack, Level), comprobado antes de guardar; en carrera, violación de `UX_Locations_Aisle_Rack_Level`. | 409 `DuplicateLocationPlace` (`errors.aisle/rack/level`; el `detail` nombra el código existente, salvo en la carrera) | FR-003, US1.3 |
| Versión obsoleta | If-Match distinto de la versión vigente. | 412 `VersionConflict` (A); sin If-Match, 428 | FR-017 |
| Sin borrado | No hay operación de borrado; FK restringidas. | — (sin ruta DELETE) | FR-014 |

### Estados

```text
           alta (FR-004)
               │
               ▼
   ┌──────── Activa ─────────┐
   │  desactivar: vacía, sin │
   │  reservas ni conteo y,  │  reactivar (sin condiciones
   │  si es Receiving, no la │  de existencias)
   │  última activa          │
   ▼                         │
 Inactiva ───────────────────┘
```

No existe estado «eliminada». Una ubicación inactiva no admite Receive/Reserve (lo comprueba
`StockCore` tras el bloqueo) y sigue apareciendo en consultas e historial (FR-013).
Ship y Release sobre una ubicación inactiva no pueden darse con existencias, porque la
desactivación exige que esté vacía y sin reservas.

## MasterDataAudit (común, de A; HU-02 añade la entidad `Location`)

La tabla y `MasterDataAuditWriter` existen desde `HU08AuditInfrastructure`, con DENY de
UPDATE/DELETE al rol de ejecución. HU-02 añade `Location` al enum `MasterDataEntity` y al
CHECK `CK_MasterDataAudit_Entity` (migración `HU02_Locations`).

| Campo | Valor para HU-02 |
|-------|------------------|
| EntityType | `Location` |
| Action | `Create`, `Update`, `Deactivate`, `Reactivate` |
| Before / After | JSON de {code, aisle, rack, level, kind, isActive}; Before nulo en `Create`. |
| ActorId / OperationId / RecordedAt | Los pone `MasterDataAuditWriter` desde el `OperationContext`. |

## Entidades consumidas sin cambios

| Entidad | Uso en HU-02 |
|---------|--------------|
| StockBalance | Fuente única de cantidades por artículo/ubicación/lote (FR-006–FR-009, D-09) y de «tiene movimientos» (D-03). |
| Lot | Código de lote mostrado; `SIN-LOTE` se presenta como «Sin lote». |
| Article | Sku, Name, unidad e IsActive para mostrar cantidades con unidad y estado textual; búsqueda con `GET /articles` de A. |
| OperationResult | Idempotencia y replay de altas y cambios, gestionados por el ejecutor de A (D-06). |
| StockEntry / StockMovement | No se leen en HU-02; el historial por artículo lo ofrece A. |

## Proyecciones de lectura (DTO, no entidades)

- **LocationSummary**: id, code, aisle, rack, level, description («Pasillo P01 · Estantería
  E01 · Nivel N01»), kind, kindLabel, isActive, statusLabel.
- **LocationDetail**: LocationSummary + hasMovements, isEmpty, hasReservations, inOpenCount,
  isLastActiveReceiving. La versión viaja en la cabecera `ETag`.
- **ArticleLocationBucket**: location (summary), lotCode, physical, reserved.
  Totales del filtro: physical y reserved del artículo.
- **LocationStockLine**: article (id, sku, name, isActive), lotCode, physical, reserved, unit.

Cantidades como cadenas decimales con tres decimales, según el contrato HTTP común.
