# Modelo de datos — HU-06

**Fecha**: 2026-11-21 · Base: [spec](spec.md), [decisiones](research.md), [modelo global](../../.specify/memory/project/data-model.md#reposición-y-mensajería--persona-a--hu-06-hu-07) y [protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md).

## Entidades y esquemas

### 1. `StockPolicy` (Existente, ampliada su gestión)

Tabla: `StockPolicies`

| Campo | Tipo | Nulo | Restricciones / Descripción |
|---|---|---|---|
| `ArticleId` | `uniqueidentifier` | No | PK, FK restrictiva a `Articles(Id)`. Relación 1:1. |
| `MinimumAvailable` | `decimal(18,3)` | No | Umbral de disponibilidad. CHECK (`MinimumAvailable >= 0`). Inicializado a 0 en el alta del artículo por HU-08. |
| `Version` | `rowversion` | No | Concurrencia optimista (`timestamp` de SQL Server). Cambia en cada actualización. |

### 2. `StockAlert` (Nueva entidad para episodios de alerta)

Tabla: `StockAlerts`

| Campo | Tipo | Nulo | Restricciones / Descripción |
|---|---|---|---|
| `Id` | `uniqueidentifier` | No | PK (UUID v4 generado en aplicación o base). |
| `ArticleId` | `uniqueidentifier` | No | FK restrictiva a `Articles(Id)`. No admite eliminación en cascada. |
| `State` | `nvarchar(16)` | No | CHECK (`State IN ('Active', 'Resolved')`). Estado del episodio. |
| `PhysicalSnapshot` | `decimal(18,3)` | No | Existencias físicas totales del artículo en el almacén al evaluar. |
| `ReservedSnapshot` | `decimal(18,3)` | No | Existencias reservadas totales del artículo al evaluar. |
| `AvailableSnapshot` | `decimal(18,3)` | No | Existencias disponibles (`Physical − Reserved`) al evaluar. |
| `MinimumSnapshot` | `decimal(18,3)` | No | Umbral mínimo vigente al momento de la evaluación. |
| `SuggestedQuantity` | `decimal(18,3)` | No | Cantidad sugerida calculada: `max(0, MinimumSnapshot − AvailableSnapshot)`. |
| `OpenedAt` | `datetimeoffset` | No | Instante UTC en que se abrió el episodio. |
| `UpdatedAt` | `datetimeoffset` | No | Instante UTC de la última actualización de métricas mientras sigue activa. |
| `ResolvedAt` | `datetimeoffset` | Sí | Instante UTC en que el disponible recuperó el mínimo o el artículo fue desactivado. Obligatorio si `State = 'Resolved'`. |

---

## Índices y restricciones de consistencia

1. **Unicidad de alerta activa por artículo**:
   ```sql
   CREATE UNIQUE INDEX IX_StockAlerts_ArticleId_Active
   ON StockAlerts(ArticleId)
   WHERE State = 'Active';
   ```
   Garantiza a nivel de base de datos que ningún artículo pueda tener más de un episodio activo simultáneamente, resolviendo cualquier condición de carrera en evaluaciones concurrentes.

2. **Índice de consulta operativa**:
   ```sql
   CREATE INDEX IX_StockAlerts_State_OpenedAt
   ON StockAlerts(State, OpenedAt DESC)
   INCLUDE (ArticleId, AvailableSnapshot, MinimumSnapshot, SuggestedQuantity);
   ```
   Optimiza la vista del panel `/alertas` para filtrar por alertas activas y ordenación temporal descendente sin lecturas de tabla completa.

3. **Índice histórico por artículo**:
   ```sql
   CREATE INDEX IX_StockAlerts_ArticleId_OpenedAt
   ON StockAlerts(ArticleId, OpenedAt DESC);
   ```
   Permite auditar la frecuencia y recurrencia de episodios de rotura pasados para un artículo específico.

4. **CHECK Constraints**:
   - `CK_StockAlerts_State`: `State IN ('Active', 'Resolved')`.
   - `CK_StockAlerts_ResolvedAt`: `(State = 'Active' AND ResolvedAt IS NULL) OR (State = 'Resolved' AND ResolvedAt IS NOT NULL)`.
   - `CK_StockAlerts_SuggestedQuantity`: `SuggestedQuantity >= 0`.

---

## Ciclo de vida y máquina de estados

```text
               [Available < MinimumAvailable]
              ---------------------------------> [Active]
             |                                     |
             |                                     |  [Available >= MinimumAvailable]
[Sin Alerta] |                                     |  o [Article.IsActive = false]
             |                                     v
              <--------------------------------- [Resolved] (Histórico inmutable)
```

- **Transición a Active**: Cuando `Available < MinimumAvailable` en un artículo activo y no existe alerta activa previa. Se asigna `OpenedAt = UpdatedAt = UtcNow`, `ResolvedAt = null`.
- **Actualización de Active**: Cuando `Available < MinimumAvailable` y ya existe alerta activa. Se actualizan las fotos (`PhysicalSnapshot`, `ReservedSnapshot`, `AvailableSnapshot`, `SuggestedQuantity`) y `UpdatedAt = UtcNow`. `OpenedAt` permanece inmutable.
- **Transición a Resolved**: Cuando `Available >= MinimumAvailable` o cuando el artículo se desactiva (`IsActive = false`). Se actualiza `State = 'Resolved'`, `ResolvedAt = UtcNow`, `UpdatedAt = UtcNow`.
