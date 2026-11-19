# Implementation Plan: HU-02 — Gestionar las ubicaciones de los artículos

**Branch**: `b/hu-02-ubicaciones-v2` (directorio de feature `specs/003-gestionar-ubicaciones`)
| **Date**: 2026-11-19 (2.ª iteración; 1.ª del 2026-11-18) | **Spec**: [spec.md](spec.md)

**Input**: especificación clarificada de [spec.md](spec.md) (sesiones 2026-11-18 y 2026-11-19).
**Responsable**: Persona B · **Hito**: H1 ([plan de coordinación](../001-reparto-specs-inventario/plan.md)).
**Objetivo de esta iteración**: alinear el diseño con la base común integrada de A
(`b/h0-stock-service-v2` + `b/h0-frontend-v2`) y recortar el alcance para la entrega del sábado.

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

## Cambios respecto a la iteración del 2026-11-18

| Antes (1.ª iteración) | Ahora | Motivo |
|------------------------|-------|--------|
| `AppLocks` propio extraído de `StockCore` | `InventoryLocks` de A + método nuevo `ReceivingZonesAsync` (`locations:receiving` en la fase de ubicaciones) | La base de A ya serializa por fases; un segundo helper duplicaría el protocolo (D-04, D-05). |
| `LocationCommandRunner` (transacción, replay, `OperationResult`, `BeforeCommit`) | `InventoryOperationExecutor` + `OperationResult` de A | Mismo protocolo que maestros y stock; L-15 usa el `FailAfterSaveInterceptor` existente (D-06). |
| `MasterDataAudit` creada por HU-02 | `MasterDataAudit` y `MasterDataAuditWriter` de A; HU-02 solo añade la entidad `Location` | La tabla ya existe desde `HU08AuditInfrastructure` (D-07). |
| `BusinessRuleException`, `HttpPreconditions` | `InventoryProblemException`, `EntityTags` y el patrón `Key()` de los endpoints de A | Un solo modelo de errores y de precondiciones. |
| DENY en `DatabaseBootstrapper` | Ya aplicados por migraciones (`H0_StockCore`, `HU08AuditInfrastructure`) | Validado por A; sin trabajo en HU-02. |
| Última zona de recepción: aviso con `acknowledgeNoActiveReceiving` | Se impide con 409 `NoActiveReceivingLeft` | Acuerdo con A, 2026-11-19 (spec FR-021). |
| Códigos `DuplicateLocationCode`, `PreconditionFailed`, `IdempotencyKeyRequired`; extras `existingLocationId`, `physical`, `reserved` | `DuplicateCode` (A), `VersionConflict` (A), `InvalidRequest` (A); solo `errors` por campo | `InventoryProblemException` no lleva extensiones; se evita ampliarla. |
| Frontend con `ApiError`, interceptor, `MessageService`, `location-permissions.ts`, inputs de ruta | `apiProblem()`, `SessionService.has()`, `permissionGuard`, parámetros leídos con `ActivatedRoute`, avisos en la página | Base de A sin duplicados (`b/h0-frontend-v2`). |
| T055 y E2E bloqueados | Desbloqueados: `GET /articles` y login de A existen | Entregas de A. |
| Rendimiento (T064) y validación con operarios (T066) en alcance | Fuera de alcance de la entrega del sábado, con desviación registrada | Recorte acordado; véase Complexity Tracking. |

## Summary

HU-02 completa el mantenimiento de ubicaciones sobre la entidad `Location` de `H0_StockCore`
y ofrece las consultas de existencias por artículo y por ubicación. El backend añade una
feature vertical `Features/Locations` cuyos comandos se ejecutan con el
`InventoryOperationExecutor` de A (idempotencia, replay, permiso, transacción y
`OperationResult`), bloquean con `InventoryLocks` (`location:{id}` y, para retirar zonas de
recepción, `locations:receiving`) y auditan con `MasterDataAuditWriter`. Una migración
aditiva `HU02_Locations` añade la unicidad física y admite `Location` en la auditoría.
Las cantidades se leen solo de `StockBalances`. El frontend porta las pantallas ya escritas
en `8377a12` a la base de A y sustituye el placeholder de `/ubicaciones`.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (ASP.NET Core minimal APIs); TypeScript 6.0 /
Angular 22.2 — según [plan global](../../.specify/memory/project-plan.md#technical-context).

**Primary Dependencies**: EF Core 10 + SqlServer, ASP.NET Core Identity, PrimeNG 22.1.
Sin dependencias nuevas; en frontend, las versiones de prueba de A (Vitest 5.1.0,
jsdom 31.0.0, Playwright 1.65.0).

**Storage**: SQL Server 2025, `InventoryDbContext` único. Tablas existentes `Locations`,
`StockBalances`, `Lots`, `Articles`, `OperationResults`, `MasterDataAudit`.

**Testing**: xUnit + `IdentityFixture` contra SQL Server real, reutilizando `StockTestHarness`
y `FailAfterSaveInterceptor`; Vitest/TestBed; Playwright con el fixture de A.

**Target Platform**: la del plan global.

**Project Type**: aplicación web (monolito modular `backend/` + `frontend/`).

**Performance Goals**: SC-005 queda fuera de la entrega del sábado (Complexity Tracking);
el diseño mantiene consultas paginadas por índice para no impedir medirlo después.

**Constraints**: migración solo aditiva posterior a `20261118194940_H0_StockCore`; permiso
en cada endpoint y en el ejecutor; lecturas de stock desde `StockBalances`; sin borrado;
If-Match + Idempotency-Key en mutaciones.

**Scale/Scope**: un almacén, 6 endpoints, 5 pantallas, casos L-01…L-12 y L-15.
Sin incógnitas abiertas: ver [research.md](research.md).

## Constitution Check

*GATE previo a Phase 0 y repetido tras Phase 1. PASS = conformidad del diseño, no evidencia
de implementación.*

| Principio | Compromiso de diseño en HU-02 | Antes | Después |
|-----------|-------------------------------|-------|---------|
| I. Correspondencia | Cantidades solo desde `StockBalances` alimentados por el núcleo; mantener ubicaciones no crea movimientos ni toca saldos (FR-004, FR-009, D-09). | PASS | PASS |
| II. Ubicación precisa | Código y lugar físico únicos en SQL (D-02); pasillo/estantería/nivel obligatorios también en recepción; ningún stock en inactiva gracias al bloqueo común (D-04); nunca sin zona de recepción activa (D-05). | PASS | PASS |
| III. Uso intuitivo | Textos en español, estados con texto, revisión antes de desactivar, errores por campo conservando datos (contrato UI). La validación con operarios (SC-006) queda fuera de esta entrega. | PASS* | PASS* |
| IV. Trazabilidad | Identificación inmutable desde el primer movimiento (D-03); nunca se borra; auditoría append-only de A con actor de sesión (D-07). | PASS | PASS |
| V. Integridad | Ejecutor de A: permiso, transacción única con auditoría y `OperationResult`, replay, If-Match; atomicidad ante fallo (L-15); bloqueos compartidos con el núcleo y concurrencia en SQL real (L-09). | PASS | PASS |
| Revisión y gobernanza | Cambios en archivos de A y en documentos comunes identificados y no aplicados unilateralmente; evidencia en quickstart. | PASS | PASS |

\* Desviación registrada en Complexity Tracking: la entrega del sábado no aporta la
validación con trabajadores ni la medición de rendimiento. HU-02 no puede darse por
**aceptada** hasta aportarlas; sí puede integrarse.

## Project Structure

### Documentation (this feature)

```text
specs/003-gestionar-ubicaciones/
├── spec.md
├── plan.md                     # este documento
├── research.md                 # D-01…D-13
├── data-model.md               # Location ampliada, uso de MasterDataAudit, proyecciones
├── quickstart.md               # L-01…L-12, L-15, E2E y evidencia
├── contracts/
│   ├── locations-api.md
│   └── locations-ui.md
├── checklists/requirements.md
└── tasks.md                    # /speckit-tasks
```

### Source Code (repository root)

```text
backend/src/Inventario.Api/
├── Domain/
│   ├── Locations/Location.cs                   # + Normalize, AuditSnapshot, Correct, ChangeKind, Deactivate, Reactivate
│   └── MasterData/MasterDataAudit.cs           # (A) + MasterDataEntity.Location
├── Features/Locations/
│   ├── LocationEndpoints.cs                    # MapLocationEndpoints, AddLocations, Key()
│   ├── Commands/LocationCommands.cs            # alta y PUT sobre InventoryOperationExecutor
│   ├── Queries/LocationQueries.cs              # lista, detalle, contenido, ubicaciones de un artículo
│   ├── ILocationUsage.cs                       # existencias/reservas; HU-04 añade conteos
│   └── LocationDtos.cs
├── Infrastructure/
│   ├── Http/MasterDataProblemMapper.cs         # (A) + 2601/2627 de UX_Locations_Aisle_Rack_Level
│   ├── Persistence/Operations/InventoryLocks.cs              # (A) + ReceivingZonesAsync
│   ├── Persistence/Configurations/LocationConfiguration.cs   # + collation + índice único
│   ├── Persistence/Configurations/MasterDataAuditConfiguration.cs # (A) CHECK con 'Location'
│   └── Persistence/Migrations/<ts>_HU02_Locations.cs         # aditiva
└── Program.cs                                  # AddLocations(); api.MapLocationEndpoints()

backend/tests/Inventario.IntegrationTests/Locations/
├── LocationsTestSupport.cs                     # sobre StockTestHarness + cliente HTTP con sesión
├── LocationsCreateTests.cs                     # US1
├── ArticleLocationsTests.cs                    # US2
├── LocationStockTests.cs                       # US3
├── LocationsUpdateTests.cs                     # US4
└── LocationsReliabilityTests.cs                # US5: concurrencia, versiones, idempotencia, atomicidad

frontend/src/app/features/locations/           # portado desde 8377a12 y adaptado a la base de A
├── locations.routes.ts                         # sustituye el placeholder; permissionGuard en alta/edición
├── data/{location.models,locations-api.service,idempotent-intent}.ts
├── shared/operation.ts                         # estados de operación sobre apiProblem()
├── list/location-list.page.ts
├── form/location-form.page.ts
├── detail/{location-detail.page,location-state-dialog}.ts
└── article/{article-locations.page,article-search}.ts
frontend/e2e/locations.spec.ts                  # recorrido principal con el fixture de A
```

**Structure Decision**: estructura del plan global. Se modifican cinco archivos de A, todos
con cambios aditivos de pocas líneas y señalados para su revisión en el PR:
`InventoryLocks.cs`, `MasterDataAudit.cs` (enum), `MasterDataAuditConfiguration.cs` (CHECK),
`MasterDataProblemMapper.cs` y `Program.cs`. No se toca `StockCore`.

## Diseño por capas

1. **Dominio** (`Location`): normaliza con la rutina de H0 y aplica solo reglas locales; las
   que dependen de saldos o de otras ubicaciones se evalúan en el comando tras los bloqueos.
2. **Comandos**: `LocationCommands` construye un `OperationRequest` (método, recurso
   canónico `/api/v1/locations[/{id}]`, permiso `Locations.Manage`, cuerpo normalizado) y,
   dentro de `change`: `context.Locks.LocationsAsync([id])` → (si se retira una zona de
   recepción) `context.Locks.ReceivingZonesAsync()` → leer → `EntityTags.RequireMatch` →
   reglas → `SaveChanges` de la ubicación → `MasterDataAuditWriter.Record` →
   `OperationResponse.Json` con `ETag`. El ejecutor guarda `OperationResult` y confirma.
3. **Consultas**: proyecciones AsNoTracking; las de existencias en transacción SNAPSHOT
   (página + totales + `asOf`). Paginación con las mismas reglas que
   `MasterDataQueryOptions` (422 fuera de rango).
4. **HTTP**: grupo `/locations` con `RequireAuthorization("Inventory.Read")` en GET y
   `"Locations.Manage"` en POST/PUT; `/articles/{id}/locations` con `Inventory.Read`.
   Antiforgery del grupo `/api/v1` ya activo.
5. **Frontend**: servicio tipado; errores con `apiProblem()`; permisos con
   `SessionService.has('Locations.Manage')` y `permissionGuard` en `nueva` y `:id/editar`;
   parámetros con `ActivatedRoute`; mensajes de éxito y error en la propia página;
   `idempotent-intent.ts` como excepción documentada (D-11).

## Dependencias y riesgos

| Dependencia | Efecto | Mitigación |
|-------------|--------|------------|
| Revisión de A de `ReceivingZonesAsync` y de los cambios en sus archivos | Bloquea la fusión, no la implementación. | Cambios aditivos y probados; si A prefiere otro recurso o fase, solo cambia T003 y L-11. |
| HU-04 conteos | Parte de FR-012 (T034 ⛔). | `ILocationUsage.IsInOpenCountAsync` devuelve `false` hasta HU-04. |
| HU-03/HU-05 reservas de pedidos | Reservas reales en FR-012. | Reservas comprobables con `StockCore` (operación `Reserve`). |
| HU-01 recepciones por HTTP | E2E no puede preparar existencias por la interfaz. | El E2E cubre alta, búsqueda, detalle y retirada; las existencias se prueban por API (L-05). |
| Otra migración integrada antes | Snapshot obsoleto. | Regenerar `HU02_Locations` local (plan global §5). |

## Cambios al modelo común pendientes de A

Ninguno de estos documentos se edita en esta rama hasta la revisión de A en el PR (T068).
FR-003 (unicidad física) y el `DENY DELETE` sobre `StockBalances` están **validados por A**
(acuerdo con A, 2026-11-19). FR-014 es alcance de HU-02 y no cambia el modelo común: la
ausencia de DELETE se documenta solo en `contracts/locations-api.md`.

### `.specify/memory/project/data-model.md` — sección `### Location`

```diff
 ### Location

 Id, Code único, Aisle, Rack, Level, Kind (Storage o Receiving), IsActive, Version.
 Pasillo/estantería/nivel obligatorios incluso para una ubicación temporal identificada.
+La combinación (Aisle, Rack, Level), normalizada, es única entre todas las ubicaciones,
+incluidas las inactivas (HU-02 FR-003). Code, Aisle, Rack y Level no cambian desde que
+exista algún saldo/movimiento en la ubicación; Kind solo cambia si está vacía, sin
+reservas ni conteo abierto. Almacén único: sin zona, almacén ni posición.
+No puede retirarse la última ubicación Receiving activa (HU-02 FR-021).
 No se desactiva con stock, reservas o un conteo abierto. No se borra con historial.
 Un artículo puede aparecer en cualquier número de ubicaciones.
```

Y en la sección de auditoría de datos maestros: `EntityType` admite también `Location`.

### `.specify/memory/project/contracts/stock-operations.md` — sección `## Alcance y disciplina de escritura`

```diff
 - Desactivar un artículo o cambiar su mínimo adquiere el mismo bloqueo de artículo.
   Cambiar el estado de una ubicación adquiere el de ubicación. Nunca se adquiere
   ubicación después de artículo. Editar varios maestros sigue orden de IDs consistente.
+- Retirar una ubicación Receiving (desactivarla o pasarla a Storage) adquiere además
+  `locations:receiving` con `InventoryLocks.ReceivingZonesAsync`, en la fase de
+  ubicaciones y después de `location:{id}` (orden ordinal). El núcleo de stock no lo
+  adquiere (HU-02 FR-021).
```

### `.specify/memory/project/quickstart.md`

Añadir la evidencia de HU-02 (fecha, entorno, número de pruebas y resultado) que se
registre en la sección «Evidencia» del quickstart de la feature.

### `.specify/memory/project/contracts/api.md` — sección `### HU-02 — B: ubicaciones`

```diff
 ### HU-02 — B: ubicaciones

 GET, POST /locations y GET, PUT /locations/{id}: code, aisle, rack, level, kind, isActive.
-Lectura Inventory.Read; mutación Locations.Manage. Código duplicado se rechaza.
+Lectura Inventory.Read; mutación Locations.Manage. Código duplicado (DuplicateCode) y
+pasillo+estantería+nivel duplicados (DuplicateLocationPlace) se rechazan con 409, también
+frente a ubicaciones inactivas. Con movimientos, cambiar code/aisle/rack/level devuelve
+LocationIdentityLocked. Desactivar o cambiar kind con existencias o reservas devuelve
+LocationNotEmpty; con conteo abierto, CountScopeFrozen. Un PUT que cambia isActive no
+cambia otros campos. Retirar la última zona Receiving activa devuelve NoActiveReceivingLeft.
+GET /locations/{id}/stock devuelve el contenido de la ubicación por artículo y lote.
 GET /articles/{id}/locations devuelve todas sus ubicaciones con cantidades y lote,
 paginadas; filtrar isActive no oculta la historia del artículo.
+Las cantidades de ambas consultas proceden de StockBalances, no del diario.
+En los GET paginados de HU-02, page < 1 o pageSize fuera de 1–100 se rechazan con 422
+ValidationFailed (errors.page / errors.pageSize); no se recortan al límite.
```

Y en la tabla de errores comunes del mismo archivo (fila 409), añadir los códigos nuevos:

```diff
-| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict. |
+| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict, DuplicateLocationPlace, LocationIdentityLocked, LocationNotEmpty, NoActiveReceivingLeft. |
```

### Lista de puntos a validar con A (T001)

**Validados** (acuerdo con A, 2026-11-19): unicidad física (FR-003); `DENY DELETE` sobre
`StockBalances` (ya aplicado en `H0_StockCore`); FR-014 sin cambio del modelo común.

**Pendiente**:

- `InventoryLocks.ReceivingZonesAsync()`: recurso `locations:receiving` en la fase de
  ubicaciones (2), siempre después de `location:{id}`; nunca lo adquiere `StockCore` (D-05).

**Cambios menores en archivos de A, incluidos en el mismo PR para su revisión**:

- `MasterDataEntity.Location` y el CHECK `CK_MasterDataAudit_Entity` (D-07).
- Rama de `MasterDataProblemMapper` para el índice físico → 409 `DuplicateLocationPlace` (D-02).
- Alternativa D-08 (rutas separadas de desactivación/reactivación) si A la prefiere.

### Estado de la revisión de A

**PARCIAL** (2026-11-19). Validados los puntos anteriores. Pendiente `ReceivingZonesAsync`
y los cambios menores, que se revisan en el PR; no bloquean la implementación.

## Complexity Tracking

| Desviación | Por qué se acepta para el sábado | Alternativa más simple descartada | Resolución y responsable |
|------------|----------------------------------|-----------------------------------|--------------------------|
| SC-006 sin validación con operarios (constitución III y «Revisión y criterios de aceptación»: los cambios en flujos de trabajo aportan validación con trabajadores). | Requiere sesiones con operarios que no pueden organizarse antes del sábado; la entrega se integra, pero HU-02 queda **sin aceptar**. | Simular la validación con el equipo: no cumple el principio III. | Persona B; antes de la aceptación de HU-02, con el guion de [quickstart.md](quickstart.md#fuera-de-alcance-de-esta-entrega). |
| SC-005 sin medición de rendimiento (p95 ≤ 2 s; localizar en < 30 s). | Necesita datos sintéticos y carga concurrente; el diseño paginado e indexado no lo impide. | Medir con datos mínimos: no representa 2.000 ubicaciones ni 25 sesiones. | Persona B; antes de la aceptación de HU-02. |
