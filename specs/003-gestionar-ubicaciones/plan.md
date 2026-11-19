# Implementation Plan: HU-02 — Gestionar las ubicaciones de los artículos

**Branch**: `b/hu-02-ubicaciones` (directorio de feature `specs/003-gestionar-ubicaciones`)
| **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: especificación clarificada de [spec.md](spec.md) (sesión 2026-11-18).
**Responsable**: Persona B · **Hito**: H1 ([plan de coordinación](../001-reparto-specs-inventario/plan.md)).

**Base normativa y acuerdos consumidos**:
[constitución v1.0.0](../../.specify/memory/constitution.md) ·
[plan global](../../.specify/memory/project-plan.md) ·
[research común](../../.specify/memory/project/research.md) (R-02, R-03, R-04, R-06, R-08) ·
[modelo común](../../.specify/memory/project/data-model.md#location) ·
[contrato HTTP](../../.specify/memory/project/contracts/api.md#hu-02--b-ubicaciones) ·
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md#alcance-y-disciplina-de-escritura) ·
[contrato UI](../../.specify/memory/project/contracts/ui.md) ·
[guía común](../../.specify/memory/project/quickstart.md).

Artefactos de esta feature: [research.md](research.md) · [data-model.md](data-model.md) ·
[contracts/locations-api.md](contracts/locations-api.md) ·
[contracts/locations-ui.md](contracts/locations-ui.md) · [quickstart.md](quickstart.md).

## Summary

HU-02 completa el mantenimiento de ubicaciones sobre la entidad `Location` mínima de
`H0_StockCore` y ofrece las consultas de existencias por artículo y por ubicación.
El backend añade una feature vertical `Features/Locations` (comandos de alta, corrección,
desactivación y reactivación; consultas de lista, detalle, contenido y ubicaciones de un
artículo) y una migración aditiva `HU02_Locations` con la unicidad física y la tabla común
de auditoría. Los comandos siguen el orden de bloqueos del núcleo (`operation` →
`location` → `locations:receiving`), lo que coordina los cambios de estado con
recepciones y reservas; `StockCore` solo pasa a delegar en el helper común `AppLocks`, sin
cambiar su semántica. Las cantidades se leen exclusivamente
de `StockBalances`. El frontend sustituye el placeholder de `/ubicaciones` por cinco
pantallas standalone con PrimeNG.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (ASP.NET Core minimal APIs); TypeScript 6.0 /
Angular 22.2 — según [plan global](../../.specify/memory/project-plan.md#technical-context).

**Primary Dependencies**: EF Core 10 + SqlServer, ASP.NET Core Identity (policies por
permiso ya registradas), PrimeNG 22.1. Sin dependencias nuevas.

**Storage**: SQL Server 2025, `InventoryDbContext` único. Tablas existentes `Locations`,
`StockBalances`, `Lots`, `Articles`, `OperationResults`; nueva `MasterDataAudit` (común).

**Testing**: xUnit + `IdentityFixture` contra SQL Server real (patrón de `StockCoreTests`);
WebApplicationFactory para HTTP; Vitest/TestBed en frontend; Playwright cuando exista login UI.

**Target Platform**: la del plan global (SPA Chrome/Edge, API Linux/Windows x64, Compose local).

**Project Type**: aplicación web (monolito modular `backend/` + `frontend/`).

**Performance Goals**: SC-005 — p95 ≤ 2 s en búsqueda de ubicaciones y ubicaciones de un
artículo con 2.000 ubicaciones y 25 sesiones; localizar un artículo en < 30 s.

**Constraints**: migración solo aditiva sobre `H0_StockCore`; permisos en cada endpoint;
lecturas de stock desde `StockBalances`; ningún borrado; If-Match + Idempotency-Key en
mutaciones; bloqueo de 5 s y 3 reintentos como el núcleo.

**Scale/Scope**: un almacén, ~2.000 ubicaciones, 6 endpoints, 5 pantallas, 15 casos de
integración (L-01…L-15). Sin incógnitas abiertas: ver [research.md](research.md).

## Constitution Check

*GATE previo a Phase 0 y repetido tras Phase 1. PASS = conformidad del diseño, no evidencia
de implementación.*

| Principio | Compromiso de diseño en HU-02 | Antes | Después |
|-----------|-------------------------------|-------|---------|
| I. Correspondencia | Cantidades solo desde `StockBalances` alimentados por el núcleo; mantener ubicaciones no crea movimientos ni toca saldos (FR-004, FR-009, D-09). | PASS | PASS |
| II. Ubicación precisa | Código y lugar físico únicos en SQL (D-02); pasillo/estantería/nivel obligatorios también en recepción; ningún stock en inactiva gracias al bloqueo común (D-04); cantidades por ubicación y lote. | PASS | PASS |
| III. Uso intuitivo | Contrato UI de la feature: textos en español, estados con texto, revisión antes de desactivar, aviso de última zona de recepción, errores por campo conservando datos; validación con operarios (SC-006). | PASS | PASS |
| IV. Trazabilidad | Identificación inmutable desde el primer movimiento (D-03); nunca se borra; auditoría append-only con actor de sesión (D-07). | PASS | PASS |
| V. Integridad | Permisos por endpoint; transacción única con auditoría y OperationResult, con atomicidad ante fallo comprobada (L-15); idempotencia (D-06); If-Match; applocks compartidos con el núcleo; pruebas de concurrencia en SQL real. | PASS | PASS |
| Revisión y gobernanza | Cambios al modelo común identificados y no aplicados unilateralmente (sección siguiente); evidencia en quickstart. | PASS | PASS |

Sin violaciones; Complexity Tracking vacío.

## Project Structure

### Documentation (this feature)

```text
specs/003-gestionar-ubicaciones/
├── spec.md
├── plan.md                     # este documento
├── research.md                 # D-01…D-12
├── data-model.md               # Location ampliada, MasterDataAudit, proyecciones
├── quickstart.md               # L-01…L-15, rendimiento y operarios
├── contracts/
│   ├── locations-api.md
│   └── locations-ui.md
├── checklists/requirements.md
└── tasks.md                    # /speckit-tasks (no creado aquí)
```

### Source Code (repository root)

```text
backend/src/Inventario.Api/
├── Domain/
│   ├── Locations/Location.cs                   # + Correct, ChangeKind, Deactivate, Reactivate
│   ├── MasterData/MasterDataAudit.cs           # nueva (común, D-07)
│   └── BusinessRuleException.cs                # 409/412/422 genéricos fuera del núcleo
├── Features/Locations/
│   ├── LocationEndpoints.cs                    # MapLocationEndpoints + RequireAuthorization
│   ├── Commands/CreateLocation.cs
│   ├── Commands/UpdateLocation.cs              # corrección, tipo y estado (D-08)
│   ├── Commands/LocationCommandRunner.cs       # transacción, applocks, idempotencia, auditoría, BeforeCommit (L-15)
│   ├── Queries/ListLocations.cs
│   ├── Queries/GetLocation.cs
│   ├── Queries/GetLocationStock.cs
│   ├── Queries/GetArticleLocations.cs
│   ├── ILocationUsage.cs                       # existencias/reservas; HU-04 añade conteos
│   └── LocationDtos.cs
├── Features/Stock/Core/StockCore.cs            # delega en AppLocks (sin cambio de semántica; 26 pruebas previas intactas)
├── Infrastructure/
│   ├── Http/ApiProblems.cs                     # + BusinessRuleException en ApiExceptionHandler
│   ├── Http/HttpPreconditions.cs               # ETag/If-Match, Idempotency-Key
│   ├── Persistence/AppLocks.cs                 # sp_getapplock compartido (D-04)
│   ├── Persistence/Configurations/LocationConfiguration.cs        # + collation + índice único
│   ├── Persistence/Configurations/MasterDataAuditConfiguration.cs # nueva
│   ├── Persistence/DatabaseBootstrapper.cs     # + DENY UPDATE/DELETE en MasterDataAudit, DENY DELETE en StockBalances
│   └── Persistence/Migrations/<ts>_HU02_Locations.cs              # aditiva
└── Program.cs                                  # api.MapLocationEndpoints(); AddLocations()

backend/tests/Inventario.IntegrationTests/
├── LocationsTestSupport.cs                     # utilidades comunes (patrón de StockCoreTests)
├── LocationsInfrastructureTests.cs             # L-13, L-14, DENY, índice único, AppLocks ↔ StockCore
├── LocationsCreateTests.cs                     # US1: L-01…L-04
├── ArticleLocationsTests.cs                    # US2: L-05 (artículo), L-06
├── LocationStockTests.cs                       # US3: L-05 (ubicación)
├── LocationsUpdateTests.cs                     # US4: L-07, L-08, L-11, DELETE 405
├── LocationsConcurrencyTests.cs                # US5: L-09
└── LocationsHttpTests.cs                       # US5: L-10, L-12, L-15

frontend/src/app/features/locations/
├── locations.routes.ts                         # sustituye el placeholder
├── data/locations-api.service.ts
├── data/location.models.ts
├── data/idempotent-intent.ts                   # clave por intención en sessionStorage
├── list/location-list.page.ts
├── form/location-form.page.ts                  # alta y edición
├── detail/location-detail.page.ts              # contenido + desactivar/reactivar
├── detail/location-state-dialog.ts
└── article/article-locations.page.ts
```

**Structure Decision**: estructura del plan global (feature vertical `Locations` en backend
y frontend). Se tocan tres archivos transversales —`Program.cs`, `ApiProblems.cs`,
`DatabaseBootstrapper.cs`— y el helper de applock de `StockCore`; se revisan con A en el PR.

## Diseño por capas

1. **Dominio** (`Location`): métodos que normalizan con la misma rutina de H0 y aplican solo
   reglas locales; las que dependen de saldos o de otras ubicaciones se evalúan en el handler
   tras los bloqueos.
2. **Comandos**: `LocationCommandRunner` encapsula estrategia de ejecución (3 intentos,
   contexto limpio por intento), transacción READ COMMITTED, `AppLocks` en el orden de D-04,
   replay idempotente, If-Match, `SaveChanges` de ubicación + `MasterDataAudit` +
   `OperationResult` y commit. Violaciones 2601/2627 → 409 por campo.
3. **Consultas**: proyecciones AsNoTracking; las de existencias en transacción SNAPSHOT
   (página + totales + `asOf`). Búsqueda por prefijo sobre `Code` y `Aisle` (índices únicos
   existentes/nuevos).
4. **HTTP**: grupo `/locations` con `RequireAuthorization(PermissionCatalog.InventoryRead)` en
   GET y `LocationsManage` en POST/PUT; `/articles/{id}/locations` con `InventoryRead`.
   El filtro antiforgery del grupo `/api/v1` ya cubre las mutaciones.
5. **Frontend**: servicio tipado, signals de estado de operación (formulario → confirmando →
   confirmado / error / conflicto / pendiente de comprobar), reutiliza `ApiError`.

## Dependencias y riesgos

| Dependencia | Efecto | Mitigación |
|-------------|--------|------------|
| Validación de A de `MasterDataAudit` (FR-003 validada el 2026-11-19) | Bloquea la fusión de `HU02_Locations`. | Sección siguiente; si A rechaza, se retira el índice y se reabre la clarificación 1. |
| `b/h0-frontend-base` no está en esta rama | Frontend no puede empezar. | Integrar esa rama antes de las tareas de UI. |
| `GET /articles` (HU-08, A) | US2.1, tiempo de SC-005 y tarea 3 de SC-006. | Acceso provisional (desde el contenido de una ubicación o por id) solo para desarrollo y pruebas; criterios registrados como pendientes. |
| Login en la UI (común) | Playwright de HU-02, SC-001 por la interfaz, revisión de accesibilidad y validación con operarios (SC-006). | Pruebas HTTP con sesión real y Vitest entretanto; criterios de interfaz registrados como pendientes. |
| HU-04 conteos, HU-03/05 reservas de pedidos | Parte de FR-012. | `ILocationUsage`; reservas ya comprobables con `StockCore.ReserveAsync`. |
| Otra migración integrada antes | Snapshot obsoleto. | Regenerar `HU02_Locations` local (plan global §5). |

## Cambios al modelo común pendientes de A

Ninguno de estos archivos se edita en esta rama hasta la revisión de A en el PR. FR-003 está
**validada por A** (acuerdo con A, 2026-11-19). FR-014 es alcance de HU-02 y no cambia el
modelo común: la ausencia de DELETE se documenta solo en `contracts/locations-api.md`.

### `.specify/memory/project/data-model.md` — sección `### Location`

```diff
 ### Location

 Id, Code único, Aisle, Rack, Level, Kind (Storage o Receiving), IsActive, Version.
 Pasillo/estantería/nivel obligatorios incluso para una ubicación temporal identificada.
+La combinación (Aisle, Rack, Level), normalizada, es única entre todas las ubicaciones,
+incluidas las inactivas (HU-02 FR-003). Code, Aisle, Rack y Level no cambian desde que
+exista algún saldo/movimiento en la ubicación; Kind solo cambia si está vacía, sin
+reservas ni conteo abierto. Almacén único: sin zona, almacén ni posición.
 No se desactiva con stock, reservas o un conteo abierto. No se borra con historial.
 Un artículo puede aparecer en cualquier número de ubicaciones.

 **Base técnica H0 B (2026-11-16)**: `Domain.Locations.Location` y tabla `Locations` con
 todos los campos anteriores; Code (32) único sin distinguir mayúsculas, partes de 16
 caracteres normalizadas a mayúsculas. Sin endpoints ni reglas de desactivación: HU-02.
 Lot, StockBalance, StockMovement/StockEntry y OperationResult se crean en `H0_StockCore`
 según este modelo; ActorId de diario y resultados tiene FK restringida a AspNetUsers.
+
+**HU-02 B (pendiente de integrar)**: migración aditiva `HU02_Locations` con collation
+sin distinguir mayúsculas en Aisle/Rack/Level e índice único `UX_Locations_Aisle_Rack_Level`.
+Crea `MasterDataAudit` (sección Datos maestros) si aún no existe, append-only para el
+usuario de ejecución. Los cambios de estado adquieren `location:{id}` como el núcleo.
+El usuario de ejecución no tiene DELETE sobre `StockBalances`: un saldo nunca se borra,
+de modo que «la ubicación tiene saldo» equivale a «la ubicación tiene movimientos».
```

### `.specify/memory/project/contracts/stock-operations.md` — secciones `## Alcance y disciplina de escritura` e `## Implementación H0`

```diff
 - Desactivar un artículo o cambiar su mínimo adquiere el mismo bloqueo de artículo.
   Cambiar el estado de una ubicación adquiere el de ubicación. Nunca se adquiere
   ubicación después de artículo. Editar varios maestros sigue orden de IDs consistente.
+- Desactivar una ubicación Receiving o cambiar su tipo a Storage adquiere además el
+  recurso global `locations:receiving`, siempre después de `location:{id}` y nunca
+  después de un `article:{id}`. El núcleo de stock no lo adquiere (HU-02 FR-021).
```

```diff
 ## Implementación H0 (B, 2026-11-16)

+Desde HU-02, la adquisición de applocks vive en `Infrastructure/Persistence/AppLocks`,
+compartida por `StockCore` y los comandos de ubicaciones, con la misma semántica
+(Exclusive, propietario Transaction, 5 s, rollback explícito y LockTimeout).
+
 `Inventario.Api.Features.Stock.Core.StockCore` (scoped) expone ReceiveAsync, ReserveAsync,
```

### `.specify/memory/project/quickstart.md` — sección `### Núcleo de stock H0 (B)`

```diff
 El inicializador deniega UPDATE/DELETE al usuario de ejecución sobre StockMovements,
-StockEntries y OperationResults. Servicio scoped `Features/Stock/Core/StockCore` con
+StockEntries y OperationResults; desde HU-02, también UPDATE/DELETE sobre MasterDataAudit
+y DELETE sobre StockBalances. Servicio scoped `Features/Stock/Core/StockCore` con
```

Y añadir al final de esa sección la evidencia de HU-02 (fecha, entorno, número de pruebas
y resultado) que se registre en la sección «Evidencia» del quickstart de la feature.

### `.specify/memory/project/contracts/api.md` — sección `### HU-02 — B: ubicaciones`

```diff
 ### HU-02 — B: ubicaciones

 GET, POST /locations y GET, PUT /locations/{id}: code, aisle, rack, level, kind, isActive.
-Lectura Inventory.Read; mutación Locations.Manage. Código duplicado se rechaza.
+Lectura Inventory.Read; mutación Locations.Manage. Código duplicado (DuplicateLocationCode)
+y pasillo+estantería+nivel duplicados (DuplicateLocationPlace) se rechazan con 409, también
+frente a ubicaciones inactivas. Con movimientos, cambiar code/aisle/rack/level devuelve
+LocationIdentityLocked. Desactivar o cambiar kind con existencias o reservas devuelve
+LocationNotEmpty; con conteo abierto, CountScopeFrozen. Un PUT que cambia isActive no
+cambia otros campos. Retirar la última zona Receiving activa exige
+acknowledgeNoActiveReceiving=true; si falta, NoActiveReceivingLeft.
+GET /locations/{id}/stock devuelve el contenido de la ubicación por artículo y lote.
 GET /articles/{id}/locations devuelve todas sus ubicaciones con cantidades y lote,
 paginadas; filtrar isActive no oculta la historia del artículo.
+Las cantidades de ambas consultas proceden de StockBalances, no del diario.
```

Y en la tabla de errores comunes del mismo archivo (fila 409), añadir los códigos nuevos:

```diff
-| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict. |
+| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict, DuplicateLocationCode, DuplicateLocationPlace, LocationIdentityLocked, LocationNotEmpty, NoActiveReceivingLeft. |
```

### Lista de puntos a validar con A (T001)

Incluye los cambios de los diffs anteriores que afectan a código transversal y los que no
tienen diff documental.

- `MasterDataAudit` se implementa por primera vez en HU-02 (D-07); A debe confirmar que
  su HU-08 la reutilizará tal cual.
- Extracción de `AppLocks` desde `StockCore`, que pasa a delegar en él sin cambiar su
  semántica (las 26 pruebas existentes siguen sin cambios), y nueva rama
  `BusinessRuleException` en `ApiExceptionHandler` (archivos transversales).
- `DENY DELETE` sobre `StockBalances` para el usuario de ejecución (refuerzo de D-03).
- Recurso global `locations:receiving` y su posición en el orden de bloqueos (D-05).
- Alternativa D-08 (rutas de desactivación/reactivación separadas) si A la prefiere.

## Complexity Tracking

Sin violaciones constitucionales que justificar.
