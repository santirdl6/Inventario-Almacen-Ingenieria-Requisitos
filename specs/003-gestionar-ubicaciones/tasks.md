---

description: "Desglose de tareas de HU-02 — Gestionar las ubicaciones de los artículos"
---

# Tasks: HU-02 — Gestionar las ubicaciones de los artículos

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/locations-api.md](contracts/locations-api.md),
[contracts/locations-ui.md](contracts/locations-ui.md), [quickstart.md](quickstart.md).
**Base común**: [constitución](../../.specify/memory/constitution.md) ·
[plan global](../../.specify/memory/project-plan.md) ·
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md).

**Tests**: incluidos. La spec y el quickstart exigen evidencia L-01…L-15 en SQL Server real
(constitución, «Revisión y criterios de aceptación»). En cada historia, las pruebas se
escriben primero y deben fallar antes de implementar.

**Organization**: Parte A (backend) completa US1→US5 antes de la Parte B (frontend), que
repite el orden US1→US5. Cada historia es entregable y comprobable por separado: primero
por API (Parte A) y después por la interfaz (Parte B).

## Format: `[ID] [P?] [Story] Descripción`

- **[P]**: se puede ejecutar en paralelo: archivo distinto y sin depender de tareas incompletas.
- **[US#]**: historia de [spec.md](spec.md) a la que pertenece.
- **⛔ BLOQUEADA**: no ejecutable hasta que se entregue la dependencia indicada.
- Cada tarea cita los FR, escenarios (USx.y) y casos L-xx de [quickstart.md](quickstart.md) que cubre.

## Path Conventions

- Backend: `backend/src/Inventario.Api/` · pruebas: `backend/tests/Inventario.IntegrationTests/`
- Frontend: `frontend/src/app/features/locations/` · E2E: `frontend/e2e/`
- Comandos de prueba backend: [quickstart.md § Pruebas automatizadas backend](quickstart.md#pruebas-automatizadas-backend).

---

# Parte A — Backend

## Phase 1: Setup y coordinación

**Purpose**: validar con A los cambios compartidos y fijar la línea base antes de tocar el esquema.

- [ ] T001 Coordinación con A **antes de la migración** (bloquea T009, T010 y T011): revisar en el PR o en una reunión registrada (1) la unicidad física pasillo+estantería+nivel entre todas las ubicaciones, incluidas las inactivas, sin distinguir mayúsculas (FR-003, clarificación Q1); (2) «una ubicación no se borra nunca, tenga o no historial» (FR-014, Q3); (3) la creación por HU-02 de la tabla común `MasterDataAudit` con exactamente los campos del modelo común (D-07, FR-015); (4) `DENY DELETE` sobre `StockBalances` para el usuario de ejecución (refuerzo de D-03) y la extracción de `AppLocks`, en la que `StockCore` delega sin cambiar su semántica (D-04), ambos transversales; (5) el nuevo recurso de bloqueo global `locations:receiving` y su posición en el orden del protocolo: siempre después de `location:{id}`, nunca después de un `article:{id}`, y nunca adquirido por `StockCore` (D-05, FR-021). Registrar decisión, fecha y revisor en una subsección «Estado de la revisión de A» al final de la sección «Cambios al modelo común pendientes de A» de `specs/003-gestionar-ubicaciones/plan.md`. Si A rechaza (1) o (3), detener T009–T011 y volver a ejecutar `/speckit-clarify`.
- [ ] T002 Línea base: actualizar la rama con lo último integrado, confirmar que `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` termina en `20261116194714_H0_StockCore` (si hay una migración posterior, se generará `HU02_Locations` encima de ella; plan global, sincronización §5), y ejecutar sobre `backend/Inventario.slnx` las 26 pruebas existentes y `dotnet ef migrations has-pending-model-changes`; ambos deben pasar sin cambios.

---

## Phase 2: Foundational (prerrequisitos bloqueantes)

**Purpose**: infraestructura compartida por las cinco historias: bloqueos, errores, precondiciones HTTP, auditoría, esquema, runner de comandos y soporte de pruebas.

**⚠️ CRITICAL**: ninguna historia puede empezar hasta completar esta fase.

- [ ] T003 [P] Extraer el helper de applock de `StockCore` a `backend/src/Inventario.Api/Infrastructure/Persistence/AppLocks.cs` (clase estática interna). `AcquireAsync(DatabaseFacade database, string resource, CancellationToken)` lleva el cuerpo actual de `StockCore.AcquireAsync` **sin cambiar su semántica**: `sys.sp_getapplock` en modo `Exclusive`, `@LockOwner = N'Transaction'`, `AppLocks.TimeoutMilliseconds = 5000`, recurso `nvarchar(255)` en minúsculas y sobre la transacción actual; si el resultado es negativo, rollback explícito y `StockOperationException.LockTimeout(...)` con el mismo mensaje. Añadir los helpers `OperationResource(Guid)`, `DocumentResource(string, Guid)`, `LocationResource(Guid)` y `ArticleResource(Guid)`, que producen exactamente las cadenas actuales, y la constante `ReceivingLocations = "locations:receiving"`. En `backend/src/Inventario.Api/Features/Stock/Core/StockCore.cs`, `AcquireAsync` delega en `AppLocks`, los recursos se construyen con los helpers, se conserva el orden operación → documento → ubicaciones → artículos y `StockCore.LockTimeoutMilliseconds` queda como alias de `AppLocks.TimeoutMilliseconds`; ningún otro cambio en `StockCore`. Aceptación: las 26 pruebas existentes (incluidas las 8 de `StockCoreTests`) pasan **sin modificar ningún archivo de pruebas** (D-04; FR-016).
- [ ] T004 [P] Crear `backend/src/Inventario.Api/Domain/BusinessRuleException.cs` con `Status`, `Code`, `Message`, `Errors` (campo → mensajes) y `Extensions` (p. ej. `existingLocationId`, `physical`, `reserved`), y fábricas para 409, 412, 422 y 428 (FR-019).
- [ ] T005 Ampliar `ApiExceptionHandler` y `ApiProblems.Create` en `backend/src/Inventario.Api/Infrastructure/Http/ApiProblems.cs` para convertir `BusinessRuleException` en ProblemDetails con `code`, `errors`, extensiones y `traceId`; añadir los títulos de 409, 412 y 428 en español; mantener `Retry-After` en los 503 (depende de T004; FR-019, L-12).
- [ ] T006 [P] Crear `backend/src/Inventario.Api/Infrastructure/Http/HttpPreconditions.cs`: ETag como rowversion en base64 entre comillas; lectura de `If-Match` (si falta, 428 `PreconditionRequired`); lectura de `Idempotency-Key` como UUID (si falta o es inválida, 400 `IdempotencyKeyRequired`) (FR-017, FR-018; L-10, L-12).
- [ ] T007 [P] Crear `backend/src/Inventario.Api/Domain/MasterData/MasterDataAudit.cs` con los campos del modelo común «Id, EntityType, EntityId, Action, Before, After, ActorId, RecordedAt, OperationId», constructor interno y fábrica `ForLocation(action, before, after, actorId, recordedAt, operationId)`, donde Action es `Create`, `Update`, `Deactivate` o `Reactivate` y Before es nulo en `Create` (D-07; FR-015).
- [ ] T008 Crear `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/MasterDataAuditConfiguration.cs`: tabla `MasterDataAudit`, `Id` generado en la aplicación, `EntityType nvarchar(64)`, `Action nvarchar(16)` con CHECK de los cuatro valores, `Before` y `After` `nvarchar(max)` (JSON), `RecordedAt datetimeoffset`, FK `ActorId` → `AspNetUsers` con borrado restringido, índice `(EntityType, EntityId, RecordedAt)` e índice único `(EntityId, OperationId)` (depende de T007; FR-015).
- [ ] T009 [P] Ampliar `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/LocationConfiguration.cs` sin cambiar lo existente: `UseCollation("Latin1_General_100_CI_AS")` en Aisle, Rack y Level, e índice único **no filtrado** `UX_Locations_Aisle_Rack_Level` sobre (Aisle, Rack, Level), que incluye las ubicaciones inactivas (requiere T001; D-02; FR-003).
- [ ] T010 [P] En `backend/src/Inventario.Api/Infrastructure/Persistence/DatabaseBootstrapper.cs`, junto a los DENY del diario, añadir `DENY DELETE ON OBJECT::dbo.StockBalances` (UPDATE sigue permitido porque el núcleo actualiza cantidades) y `DENY UPDATE, DELETE ON OBJECT::dbo.MasterDataAudit`; ambos idempotentes al reinicializar (requiere T001; refuerzo de D-03, D-07; FR-011, FR-015; L-13, L-14).
- [ ] T011 Generar la migración aditiva con `dotnet ef migrations add HU02_Locations` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y editar `<timestamp>_HU02_Locations.cs` para que (a) **antes** de crear el índice ejecute una comprobación que lance `THROW` con un mensaje en español si existen combinaciones (Aisle, Rack, Level) duplicadas, sin borrar ni renombrar datos; (b) cambie la collation de las tres columnas; (c) cree `UX_Locations_Aisle_Rack_Level` y la tabla `MasterDataAudit`. `20261116194714_H0_StockCore.cs` no se toca. Verificar `has-pending-model-changes` (depende de T008 y T009; requiere T001; D-01, D-02; FR-003, FR-015).
- [ ] T012 [P] Crear `backend/src/Inventario.Api/Features/Locations/ILocationUsage.cs` con la interfaz y la implementación `StockBalanceLocationUsage` (scoped): `HasMovementsAsync` (EXISTS en `StockBalances` por `LocationId`, con el índice (LocationId, ArticleId)), `GetTotalsAsync` (Σ Physical y Σ Reserved) e `IsInOpenCountAsync`, que devuelve `false` hasta HU-04 (véase T034) (D-03, D-10; FR-011, FR-012).
- [ ] T013 [P] Crear `backend/src/Inventario.Api/Features/Locations/LocationDtos.cs`: peticiones `CreateLocationRequest` y `UpdateLocationRequest` (con `acknowledgeNoActiveReceiving`); respuestas `LocationSummary`, `LocationDetail`, `ArticleLocationBucket`, `LocationStockLine` y página `{ items, page, pageSize, totalCount }`; descripción «Pasillo X · Estantería Y · Nivel Z»; etiquetas «Almacenamiento»/«Zona de recepción» y «Activa»/«Inactiva»; cantidades como cadena `0.000` (data-model «Proyecciones de lectura»; FR-005, FR-019).
- [ ] T014 Crear `backend/src/Inventario.Api/Features/Locations/Commands/LocationCommandRunner.cs`: estrategia de ejecución (3 intentos y `ChangeTracker.Clear()` en cada uno); transacción READ COMMITTED; `AppLocks` en el orden `operation` → `location:{id}` → `locations:receiving` (este último solo si se pide); búsqueda de `OperationResult` con el replay **antes** de evaluar If-Match; `IdempotencyConflict` si cambian el actor, el recurso o la huella; huella SHA-256 del cuerpo normalizado (recortado y en mayúsculas); persistencia conjunta de ubicación, `MasterDataAudit` y `OperationResult` (`Resource` = `locations` o `locations/{id}`); traducción de los errores SQL 2601/2627 según el nombre del índice a 409 `DuplicateLocationCode` (campo `code`) o `DuplicateLocationPlace` (campos `aisle`, `rack` y `level`), en ambos casos con `existingLocationId`; punto de inyección interno `BeforeCommit` (`Func<CancellationToken, Task>?`), ejecutado después de `SaveChanges` y antes del commit, igual que en `StockCore`, para la prueba de atomicidad L-15 (depende de T003–T008, T012 y T013; D-04, D-06; FR-002, FR-003, FR-015, FR-016, FR-018; SC-007).
- [ ] T015 Crear `backend/src/Inventario.Api/Features/Locations/LocationEndpoints.cs` con `MapLocationEndpoints` (grupo `/locations` sin rutas todavía) y `AddLocations` (registro de `ILocationUsage` y de los handlers). Llamar `builder.Services.AddLocations()` y `api.MapLocationEndpoints()` en `backend/src/Inventario.Api/Program.cs` (depende de T012; FR-010).
- [ ] T016 Crear `backend/tests/Inventario.IntegrationTests/LocationsTestSupport.cs` con el patrón de `StockCoreTests` y sin modificar ese archivo: `ApplicationDatabase()` con la conexión restringida, `ActorAsync(role)`, `SeedArticleAsync(tracksLot)`, operaciones `ReceiveAsync`, `ReserveAsync`, `ReleaseAsync` y `ShipAsync` mediante `StockCore` con un `TestActor` propio, `CreateLocationAsync` directo para preparar datos, `AssertJournalMatchesBalanceAsync` y un cliente HTTP con sesión real (csrf → login → `X-XSRF-TOKEN`), como en `IdentityTests` (D-12).
- [ ] T017 [P] Crear `backend/tests/Inventario.IntegrationTests/LocationsInfrastructureTests.cs` (`IClassFixture<IdentityFixture>`) con: **L-13** (después de Receive, Reserve, Release y Ship, para cada ubicación, «tiene saldo» ⇔ «tiene entradas»); **L-14** (UPDATE y DELETE en `MasterDataAudit` con el usuario de ejecución → error SQL 229); `DELETE` en `StockBalances` con el usuario de ejecución → error SQL 229 (no se prueba ningún UPDATE directo sobre saldos); el índice único rechaza `p01/e01/n01` frente a `P01/E01/N01` también con la ubicación inactiva; las cadenas de `AppLocks.LocationResource(id)`, `OperationResource` y `ArticleResource` coinciden con las que usa `StockCore`, y un `location:{id}` tomado con `AppLocks` en otra conexión hace que `StockCore.ReceiveAsync` termine en `LockTimeout` (503), lo que demuestra que los comandos de HU-02 y el núcleo se coordinan sobre el mismo recurso (depende de T003, T010, T011 y T016; FR-003, FR-011, FR-015, FR-016).

**Checkpoint**: esquema migrado, bloqueos compatibles con el núcleo y runner listo. Pasan las 26 pruebas previas y `LocationsInfrastructureTests`.

---

## Phase 3: User Story 1 — Registrar las ubicaciones físicas del almacén (P1) 🎯 MVP

**Goal**: alta y consulta de ubicaciones con código único y pasillo, estantería y nivel únicos.

**Independent Test**: L-01–L-04 por API; crear REC-01, P01-E01-N01 y P01-E01-N02; buscarlas; intentar un código y un lugar repetidos (spec US1, «Independent Test»).

### Tests for User Story 1

- [ ] T018 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/LocationsCreateTests.cs` con **L-01** (201, activa y vacía, auditoría `Create`, sin filas en `StockMovements` ni `StockBalances`), **L-02** (` p01-e01-n01 ` frente a un código existente y frente a uno inactivo → `DuplicateLocationCode`; mismo lugar con otro código → `DuplicateLocationPlace`; ningún registro creado), **L-03** (pasillo `"   "` en tipo Receiving → 422 con `errors.aisle`; longitudes mayores de 32 o 16 → 422), **L-04** (8 POST simultáneos con la misma clave → una ubicación y respuestas idénticas; 2 POST simultáneos del mismo código con claves distintas → uno 201 y otro 409), y la búsqueda de US1.5 (prefijo de código o de pasillo, filtros de tipo y estado; activas por defecto; sin resultados → lista vacía); cada alta se vuelve a leer con `GET /locations/{id}` y conserva sus datos (US1.1–US1.5; FR-001–FR-005, FR-015, FR-018; SC-001 por API, SC-002).

### Implementation for User Story 1

- [ ] T019 [US1] En `backend/src/Inventario.Api/Domain/Locations/Location.cs`, añadir `AuditSnapshot()` → `{code, aisle, rack, level, kind, isActive}` y un `Normalize` estático público que reutilice `RequiredText` (Code: obligatorio, recortado, en mayúsculas, «nvarchar(32)»; Aisle, Rack y Level: obligatorios, recortados, en mayúsculas, «nvarchar(16)»), sin cambiar el constructor de H0 (D-01; FR-001–FR-003).
- [ ] T020 [US1] Crear `backend/src/Inventario.Api/Features/Locations/Commands/CreateLocation.cs`: validar la forma y devolver 422 por campo (vacío, solo espacios, longitud, Kind distinto de `Storage` o `Receiving`); crear la ubicación activa mediante el runner con `Resource` = `locations`; auditoría `Create`; respuesta 201 con `LocationDetail`, `Location` y `ETag`; ningún movimiento ni saldo (depende de T019; US1.1–US1.4; FR-001–FR-004, FR-015, FR-018).
- [ ] T021 [P] [US1] Crear `backend/src/Inventario.Api/Features/Locations/Queries/ListLocations.cs`: AsNoTracking; `search` por prefijo de Code o Aisle sin distinguir mayúsculas; `kind`; `status` (`active` por defecto, `inactive`, `all`); `page` desde 1, `pageSize` 25 por defecto y 100 como máximo; orden estable por Code (US1.5; FR-005).
- [ ] T022 [P] [US1] Crear `backend/src/Inventario.Api/Features/Locations/Queries/GetLocation.cs`: `LocationDetail` con `hasMovements`, `isEmpty`, `hasReservations` e `inOpenCount` obtenidos de `ILocationUsage`, más `isLastActiveReceiving`, y `ETag`; 404 si no existe (FR-005, FR-021).
- [ ] T023 [US1] En `LocationEndpoints.cs`, mapear `POST /locations` (`RequireAuthorization(PermissionCatalog.LocationsManage)`, `Idempotency-Key`), `GET /locations` y `GET /locations/{id}` (`RequireAuthorization(PermissionCatalog.InventoryRead)`) (depende de T020–T022; FR-005, FR-010).

**Checkpoint**: US1 operativa por API; T018 en verde.

---

## Phase 4: User Story 2 — Conocer todas las ubicaciones de un artículo (P1)

**Goal**: consulta de las ubicaciones de un artículo, con cantidad por ubicación y lote y el total del artículo, leída de `StockBalances`.

**Independent Test**: L-05 (parte de artículo) y L-06; recibir 2 + 4 de ART-01/L-001 en dos ubicaciones y comprobar los buckets 2 y 4 con total 6 (spec US2).

### Tests for User Story 2

- [ ] T024 [P] [US2] Crear `backend/tests/Inventario.IntegrationTests/ArticleLocationsTests.cs` con **L-05** (artículo: buckets 2 y 4, total 6 igual a la suma del diario, mediante `AssertJournalMatchesBalanceAsync`), **L-06** (dos lotes en la misma ubicación aparecen por separado sin duplicar el total; artículo sin existencias → 200 con `items` vacíos y totales cero; artículo inactivo y ubicación inactiva siguen apareciendo con su estado), totales con `pageSize=1` que cubren todo el artículo y no solo la página, US2.5 (después de un `Ship`, la consulta refleja el nuevo reparto) y 404 para un artículo inexistente (US2.1–US2.5 consultando por identificador; FR-006, FR-007, FR-009; SC-003).

### Implementation for User Story 2

- [ ] T025 [US2] Crear `backend/src/Inventario.Api/Features/Locations/Queries/GetArticleLocations.cs`: transacción SNAPSHOT de solo lectura; `StockBalances` unidas con `Locations`, `Lots` y `Articles`; excluir los buckets con Physical = 0 y Reserved = 0; `totals` de todo el artículo; `asOf` del servidor; `includeInactiveLocations=true` por defecto; `SIN-LOTE` como «Sin lote»; orden por código de ubicación y lote; nada recalculado desde el diario (D-09; FR-006, FR-007, FR-009).
- [ ] T026 [US2] En `LocationEndpoints.cs`, mapear `GET /articles/{id}/locations` con `RequireAuthorization(PermissionCatalog.InventoryRead)` (depende de T025; FR-007, FR-010).

> **US2.1 en backend**: no hay trabajo de HU-02. La búsqueda por código o nombre la aporta `GET /articles` de HU-08 (véase T055 ⛔).

**Checkpoint**: US1 y US2 funcionan por API de forma independiente.

---

## Phase 5: User Story 3 — Revisar qué contiene una ubicación (P2)

**Goal**: contenido de una ubicación por artículo y lote, con el indicador de reservas.

**Independent Test**: L-05 (parte de ubicación): P01-E01-N02 lista ART-01, L-001, 4 UD; una ubicación vacía se indica como vacía (spec US3).

### Tests for User Story 3

- [ ] T027 [P] [US3] Crear `backend/tests/Inventario.IntegrationTests/LocationStockTests.cs` con **L-05** (ubicación: P01-E01-N02 → ART-01, L-001, `4.000` UD), US3.2 (ubicación vacía → `items` vacío con sus datos físicos, tipo y estado), US3.3 (después de `ReserveAsync`, `hasReservations=true` y el reservado aparece en la fila), artículo inactivo con su estado y 404 para una ubicación inexistente (US3.1–US3.3; FR-008, FR-009).

### Implementation for User Story 3

- [ ] T028 [US3] Crear `backend/src/Inventario.Api/Features/Locations/Queries/GetLocationStock.cs`: transacción SNAPSHOT; filas de `StockBalances` de la ubicación con Physical o Reserved distinto de cero; artículo (sku, name, isActive), lote, physical, reserved y unidad; `hasReservations`; `inOpenCount` de `ILocationUsage`; paginado; sin total global, porque las unidades son distintas (D-09; FR-008).
- [ ] T029 [US3] En `LocationEndpoints.cs`, mapear `GET /locations/{id}/stock` con `RequireAuthorization(PermissionCatalog.InventoryRead)` (depende de T028; FR-008, FR-010).

> El indicador de conteo abierto de US3.3 depende de HU-04 (T034 ⛔).

**Checkpoint**: US1–US3 por API.

---

## Phase 6: User Story 4 — Corregir y retirar ubicaciones sin perder la trazabilidad (P2)

**Goal**: corrección de datos, cambio de tipo, desactivación y reactivación con las reglas de FR-011, FR-012, FR-014 y FR-021; sin borrado.

**Independent Test**: L-07, L-08 y L-11; corregir el nivel de una ubicación sin movimientos; intentar desactivar una con existencias, vaciarla, desactivarla y reactivarla (spec US4).

### Tests for User Story 4

- [ ] T030 [P] [US4] Crear `backend/tests/Inventario.IntegrationTests/LocationsUpdateTests.cs` con **L-07** (corregir el nivel sin movimientos → 200 y auditoría `Update`; en N01 después de recibir y después de dejarla a cero con `Ship` → 409 `LocationIdentityLocked`; el tipo sí cambia estando vacía), **L-08** (desactivar con existencias → 409 `LocationNotEmpty` con `physical` y `reserved`; con una reserva viva → 409; vaciar y desactivar → 200 `Deactivate`; con la ubicación inactiva, `StockCore.ReceiveAsync` y `ReserveAsync` son rechazados; la ubicación sigue en `GET /articles/{id}/locations`; reactivar → 200 `Reactivate`), **L-11** (desactivar la única Receiving sin reconocimiento → 409 `NoActiveReceivingLeft` sin cambios; con `acknowledgeNoActiveReceiving=true` y una clave nueva → 200; pasar la única Receiving a Storage → el mismo aviso; dos Receiving desactivadas en paralelo sin reconocimiento → como mucho una devuelve 200), 422 `StateChangeMustBeSeparate`, un PUT sin cambios → 200 sin auditoría, y US4.6 (`DELETE /locations/{id}` → 405 y la ubicación sigue existiendo); después de cada corrección, desactivación y reactivación, `GET /locations/{id}` muestra el resultado (US4.1–US4.7; FR-011–FR-015, FR-020, FR-021; SC-001 por API, SC-002, SC-004).

### Implementation for User Story 4

- [ ] T031 [US4] En `backend/src/Inventario.Api/Domain/Locations/Location.cs`, añadir `Correct(code, aisle, rack, level)` con la misma normalización que el constructor, `ChangeKind(kind)`, `Deactivate()` y `Reactivate()`, que devuelven si hubo cambio efectivo; las reglas que dependen de los saldos se quedan en el handler (depende de T019; FR-001, FR-011–FR-013).
- [ ] T032 [US4] Crear `backend/src/Inventario.Api/Features/Locations/Commands/UpdateLocation.cs` con este orden: forma → 422 `StateChangeMustBeSeparate` si cambian `isActive` y otro campo → runner (`Resource` = `locations/{id}`; pide `locations:receiving` si la ubicación es Receiving y se desactiva o pasa a Storage) → If-Match (412 si está obsoleto) → `LocationIdentityLocked` si cambian code, aisle, rack o level y `HasMovementsAsync` → `LocationNotEmpty` o `CountScopeFrozen` al desactivar o cambiar de tipo → `NoActiveReceivingLeft` si quedarían 0 Receiving activas y falta el reconocimiento → aplicar y auditar `Update`, `Deactivate` o `Reactivate`; sin cambios efectivos → 200 sin auditoría; mensajes `detail` en español con la alternativa (data-model, tabla de reglas; FR-011–FR-013, FR-015–FR-017, FR-020, FR-021).
- [ ] T033 [US4] En `LocationEndpoints.cs`, mapear `PUT /locations/{id}` (`LocationsManage`, If-Match e `Idempotency-Key` obligatorios) y comprobar que `DELETE /locations/{id}` responde 405 ProblemDetails `MethodNotAllowed` y no el 404 del fallback de `/api`; si el fallback lo intercepta, mapear un DELETE explícito que devuelva 405 (depende de T032; FR-010, FR-014).

### ⛔ Bloqueada en US4 (y US3.3)

- [ ] T034 [US4] ⛔ **BLOQUEADA — depende de HU-04 (`CountLocation.IsFrozen`)**. Implementar `IsInOpenCountAsync` en `StockBalanceLocationUsage` (`backend/src/Inventario.Api/Features/Locations/ILocationUsage.cs`) y añadir los casos de conteo abierto a `LocationsUpdateTests.cs` (desactivar o cambiar de tipo → 409 `CountScopeFrozen`) y a `LocationStockTests.cs` (`inOpenCount=true`), y mostrar «En conteo» en el espacio reservado de `location-detail.page.ts` (T052). **Criterios que quedan sin demostrar mientras esté bloqueada**: la parte de conteo abierto de FR-012, US3.3, US4.3 y SC-004. Mientras tanto, dejar en `quickstart.md` esa evidencia parcial como «pendiente HU-04», sin darla por demostrada.

**Checkpoint**: US1–US4 por API, salvo la parte de conteos.

---

## Phase 7: User Story 5 — Guardar cambios con autorización y resultado fiable (P3)

**Goal**: demostrar permisos, concurrencia, conflictos de edición y reintentos sobre los endpoints ya construidos.

**Independent Test**: L-09, L-10 y L-12 (spec US5).

### Tests for User Story 5

- [ ] T035 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/LocationsConcurrencyTests.cs` con **L-09**: 20 repeticiones de desactivación (`UpdateLocation`) y `StockCore.ReceiveAsync` simultáneas con `Task.WhenAll` sobre contextos independientes en una ubicación vacía; en cada una, o la recepción confirma y la desactivación da `LocationNotEmpty`, o la desactivación confirma y la recepción es rechazada por ubicación desactivada; nunca hay una ubicación inactiva con Physical > 0 (US5.4; FR-016; SC-004).
- [ ] T036 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/LocationsHttpTests.cs` con sesión real y antiforgery: **L-10** (dos PUT con el mismo ETag → 200 y 412; PUT sin If-Match → 428; repetir el primer PUT con su clave después del éxito, con el ETag ya obsoleto → el mismo resultado guardado), **L-12** (cuenta GestorMaestros → GET 200 y POST/PUT 403; sin sesión → 401; sin `X-XSRF-TOKEN` → 400 `AntiforgeryValidationFailed`; sin `Idempotency-Key` → 400 `IdempotencyKeyRequired`; clave reutilizada con otro cuerpo → 409 `IdempotencyConflict`), `location:{id}` retenido con `AppLocks` en otra conexión → 503 `LockTimeout` con `Retry-After`, ProblemDetails con `code`, `traceId` y `detail` en español, y **L-15** (sin HTTP, con el handler directo y el `BeforeCommit` de T014: un fallo inducido después de `SaveChanges` y antes del commit en un alta y en una desactivación no deja ni ubicación o cambio, ni fila de `MasterDataAudit`, ni `OperationResult`; repetir con la misma clave confirma un solo efecto con una sola auditoría) (US5.1–US5.3; FR-010, FR-015, FR-017–FR-019; SC-007; constitución, «Revisión y criterios de aceptación»).

### Implementation for User Story 5

- [ ] T037 [US5] Corregir en `LocationCommandRunner.cs`, `HttpPreconditions.cs` o `ApiProblems.cs` cualquier fallo que revelen T035 y T036 sin relajar las aserciones. Ejecutar la suite backend completa tres veces seguidas y registrar fecha, entorno, número de pruebas y resultado en una sección «Evidencia» de `specs/003-gestionar-ubicaciones/quickstart.md` (FR-016–FR-018; SC-007).

**Checkpoint (fin Parte A)**: L-01–L-15 en verde, salvo la parte de conteos (T034 ⛔). El backend de HU-02 se puede revisar y fusionar por separado.

---

# Parte B — Frontend

## Phase 8: Frontend foundational

**Purpose**: base de la feature Angular. **Bloquea toda la Parte B.**

- [ ] T038 Integrar la rama `b/h0-frontend-base` en `b/hu-02-ubicaciones` (layout, rutas diferidas, `core/http/api-error.ts`, interceptor) y comprobar `npx --no-install ng build` en `frontend/` (D-11).
- [ ] T039 [P] Crear `frontend/src/app/features/locations/data/location.models.ts` con los tipos de [locations-api.md](contracts/locations-api.md): `LocationSummary`, `LocationDetail`, `LocationKind = 'Storage' | 'Receiving'`, peticiones, `ArticleLocations`, `LocationStock` y `Page<T>`; cantidades como `string` (depende de T038).
- [ ] T040 [P] Crear `frontend/src/app/features/locations/data/idempotent-intent.ts`: clave por intención con `crypto.randomUUID()`, guardada en sessionStorage con `try/catch` (funciona aunque no haya almacenamiento), reutilizada en los reintentos y borrada solo tras una respuesta definitiva (2xx o 4xx); nunca guarda datos sensibles (contrato UI común, estado 7; FR-018).
- [ ] T041 Crear `frontend/src/app/features/locations/data/locations-api.service.ts`: `list`, `get` (con `observe: 'response'` para leer el `ETag`), `create` (`Idempotency-Key`), `update` (`If-Match` + `Idempotency-Key`), `stock` y `articleLocations` bajo `/api/v1`; el XSRF lo pone HttpClient; los errores llegan como `ApiError` (depende de T039 y T040; FR-017, FR-018).
- [ ] T042 [P] Crear `frontend/src/app/features/locations/data/location-permissions.ts`: lee `GET /api/v1/auth/me` y expone el signal `canManage` (`Locations.Manage`) solo para orientar la interfaz; el servidor sigue siendo la barrera (FR-010).
- [ ] T043 [P] Crear `frontend/src/app/features/locations/data/locations-api.service.spec.ts` e `idempotent-intent.spec.ts` (Vitest/TestBed con `HttpTestingController`): cabeceras `If-Match` e `Idempotency-Key`; la misma clave tras un error de red; una clave nueva tras un 2xx (depende de T040 y T041).

---

## Phase 9: User Story 1 — UI de alta y búsqueda

**Independent Test**: desde `/ubicaciones`, crear REC-01 y P01-E01-N01, buscarlas y ver el error de duplicado sin perder lo escrito.

- [ ] T044 [P] [US1] Crear `frontend/src/app/features/locations/list/location-list.page.ts` (standalone, PrimeNG Table): búsqueda por código o pasillo, filtros de tipo y estado, «Incluir inactivas», tipo y estado con texto e icono, mensaje «No hay ubicaciones que coincidan con la búsqueda» con botón para limpiar filtros, botón «Nueva ubicación» si `canManage` (US1.5; FR-005, FR-019).
- [ ] T045 [P] [US1] Crear `frontend/src/app/features/locations/form/location-form.page.ts` en modo alta (formulario reactivo tipado): Código (máx. 32), Pasillo, Estantería y Nivel (máx. 16), Tipo; obligatoriedad con «*» y texto; botón «Proponer código» → `P01-E01-N01` editable; 422 → errores por campo con `aria-describedby`; 409 de duplicado → marca el campo y enlaza a `existingLocationId`; los datos válidos se conservan y el foco va al primer error; éxito → mensaje con el código y navegación al detalle (US1.1–US1.4; FR-001–FR-003, FR-019, FR-020).
- [ ] T046 [US1] Sustituir el placeholder en `frontend/src/app/features/locations/locations.routes.ts` por las rutas `''` (lista) y `nueva` (formulario) con `loadComponent` (depende de T044 y T045).
- [ ] T047 [P] [US1] Crear `frontend/src/app/features/locations/form/location-form.page.spec.ts` y `list/location-list.page.spec.ts`: campos en blanco o solo con espacios bloquean el envío; un 409 conserva los valores; los estados se muestran como texto (US1.2–US1.5).

---

## Phase 10: User Story 2 — UI de ubicaciones de un artículo

**Independent Test**: abrir las ubicaciones de ART-01 y ver 2 + 4 = 6 UD con `asOf` y total separado de la página.

- [ ] T048 [P] [US2] Crear `frontend/src/app/features/locations/article/article-locations.page.ts`: total del artículo encima de la tabla y separado de la página; filas por ubicación y lote («Sin lote»); ubicaciones inactivas marcadas «Inactiva»; artículo inactivo marcado «Inactivo»; vacío → «Este artículo no está almacenado en ninguna ubicación», sin tratarlo como error; botón «Actualizar» y `asOf` visible (US2.1–US2.5; FR-007, FR-019).
- [ ] T049 [US2] Añadir la ruta `articulo/:articleId` en `frontend/src/app/features/locations/locations.routes.ts` (depende de T046 y T048).
- [ ] T050 [P] [US2] Crear `frontend/src/app/features/locations/article/article-locations.page.spec.ts`: el total viene de `totals` y no de sumar la página; estado vacío; textos de estado (US2.2–US2.4).
- [ ] T051 [US2] **Acceso provisional sin búsqueda de artículos, solo para desarrollo y pruebas; no es el recorrido de los operarios** (no bloquea US2–US5, pero no satisface US2.1 ni sirve para medir el tiempo de SC-005 ni la tarea 3 de SC-006, porque exige saber de antemano en qué ubicación está el artículo): en `frontend/src/app/features/locations/article/article-locations.page.ts`, mostrar mientras falte HU-08 el aviso «Para localizar un artículo, ábrelo desde el contenido de una ubicación», y aceptar el enlace directo `/ubicaciones/articulo/:articleId`. El punto de entrada para operarios es el enlace «Ver todas sus ubicaciones» de cada fila del contenido de una ubicación (T053). No se crea ningún endpoint de artículos en HU-02, para no duplicar el contrato de A (D-11; US2.1 parcial).

---

## Phase 11: User Story 3 — UI de contenido de una ubicación

**Independent Test**: abrir P01-E01-N02 y ver ART-01, L-001, 4 UD; abrir una vacía y ver «Esta ubicación está vacía».

- [ ] T052 [US3] Crear `frontend/src/app/features/locations/detail/location-detail.page.ts`: datos físicos, tipo y estado; tabla de contenido (artículo, lote, cantidad con unidad, reservado con texto, artículo «Inactivo»); «Esta ubicación está vacía»; aviso textual de reservas; espacio reservado para «En conteo» cuando HU-04 lo aporte; botones Editar, Desactivar y Reactivar si `canManage` (US3.1–US3.3; FR-008, FR-019).
- [ ] T053 [US3] En `location-detail.page.ts`, añadir a cada fila del contenido el enlace «Ver todas sus ubicaciones» → `/ubicaciones/articulo/:articleId` (entrada provisional de US2.1 descrita en T051), y añadir la ruta `:id` en `locations.routes.ts` (depende de T049 y T052).
- [ ] T054 [P] [US3] Crear `frontend/src/app/features/locations/detail/location-detail.page.spec.ts`: estado vacío, reservas con texto y enlace a las ubicaciones del artículo (US3.1–US3.3).

### ⛔ Bloqueada en US2 (frontend)

- [ ] T055 [US2] ⛔ **BLOQUEADA — depende de `GET /articles` de HU-08 (A)**. Crear `frontend/src/app/features/locations/article/article-search.ts` (búsqueda por SKU o nombre con autocompletado PrimeNG, usando el contrato de A), integrarlo en `article-locations.page.ts` en lugar del aviso provisional de T051 y completar la evidencia de US2.1 en `quickstart.md`. **Criterios que quedan sin demostrar mientras esté bloqueada**: US2.1 (búsqueda por código o nombre), el tiempo de SC-005 (localizar en < 30 s desde la pantalla de inicio) y la tarea 3 de SC-006 (localizar un artículo repartido). Hasta entonces, quedan «pendiente HU-08» (US2.1; FR-007; SC-005, SC-006).

---

## Phase 12: User Story 4 — UI de corrección y retirada

**Independent Test**: corregir el nivel de una ubicación nueva; ver bloqueados los campos de identificación de una con movimientos; desactivar con revisión previa; aviso de última zona de recepción.

- [ ] T056 [US4] Ampliar `frontend/src/app/features/locations/form/location-form.page.ts` con el modo edición (ruta `:id/editar`, añadida en `locations.routes.ts`): carga con su `ETag`; si `hasMovements`, Código, Pasillo, Estantería y Nivel pasan a solo lectura con el texto de FR-011 y el enlace «Crear otra ubicación»; el tipo es editable; `LocationIdentityLocked` y `LocationNotEmpty` se muestran junto al campo (US4.1, US4.2; FR-011, FR-012, FR-019).
- [ ] T057 [P] [US4] Crear `frontend/src/app/features/locations/detail/location-state-dialog.ts` (PrimeNG Dialog accesible): revisión con código, descripción, contenido y consecuencia antes de desactivar o reactivar; `LocationNotEmpty` → físico y reservado con enlace al contenido; si `isLastActiveReceiving` o la respuesta es 409 `NoActiveReceivingLeft`, muestra «No quedará ninguna zona de recepción activa» y, al confirmar, reenvía con `acknowledgeNoActiveReceiving=true` y una **clave nueva**; el foco vuelve al botón de origen al cerrar (US4.3–US4.5, US4.7; FR-012, FR-013, FR-020, FR-021).
- [ ] T058 [US4] Conectar `location-state-dialog.ts` desde `location-detail.page.ts` y recargar el detalle al confirmar; no existe ninguna acción «Eliminar» en la interfaz (depende de T052 y T057; US4.6; FR-014).
- [ ] T059 [P] [US4] Crear `frontend/src/app/features/locations/detail/location-state-dialog.spec.ts`: aviso de última zona de recepción, nueva clave al reconfirmar y ausencia de la acción de borrado (US4.6, US4.7; FR-021).

---

## Phase 13: User Story 5 — UI de autorización, conflictos y resultado incierto

**Independent Test**: dos pestañas editan la misma ubicación (412 sin perder lo escrito); cortar la red al guardar → «Resultado pendiente de comprobar» → «Comprobar» con la misma clave.

- [ ] T060 [US5] En `frontend/src/app/features/locations/form/location-form.page.ts`, gestionar el 412: «Otra persona ha cambiado esta ubicación» con «Ver datos vigentes» (panel aparte, sin perder lo escrito) y «Descartar mis cambios»; error de red o timeout → estado «Resultado pendiente de comprobar» con botón «Comprobar», que repite la petición con la misma clave de `idempotent-intent`; botón deshabilitado mientras se confirma; nunca se muestra éxito sin una respuesta 2xx (US5.2, US5.3; FR-017, FR-018, FR-020).
- [ ] T061 [US5] Aplicar el mismo estado de resultado pendiente en `location-state-dialog.ts`, y un 403 → «Tu cuenta no tiene permiso para modificar ubicaciones» manteniendo la consulta (depende de T057; US5.1; FR-010, FR-020).
- [ ] T062 [P] [US5] Crear `frontend/src/app/features/locations/form/location-form.conflict.spec.ts`: el 412 conserva lo escrito; tras un error de red, «Comprobar» reutiliza la clave; doble clic → una sola petición (US5.2, US5.3).

---

## Phase 14: E2E

- [ ] T063 ⛔ **BLOQUEADA — depende del login en la interfaz (trabajo común H0/A, sin entregar)**. Crear `frontend/e2e/locations.spec.ts` (Playwright) con el inicio de sesión real y los recorridos L-01 (alta y duplicado), L-05 (ubicaciones de un artículo y contenido) y L-08 (desactivar y reactivar con revisión), además de una comprobación de navegación por teclado (SC-001 por la interfaz). T018, T024, T027, T030 y T036 cubren por HTTP con sesión real las reglas de servidor, pero **no** el comportamiento de la interfaz. **Criterios que quedan sin demostrar mientras esté bloqueada**: SC-001 por la interfaz, la parte de interfaz de FR-019 (teclado, foco, mensajes) y de FR-020 (revisión antes de confirmar, éxito solo tras persistir) más allá de lo que comprueba Vitest, y las tareas manuales T065 y T066, que necesitan iniciar sesión en la interfaz (SC-001; FR-019, FR-020; constitución III, contrato UI común).

---

## Phase 15: Polish, aceptación y cierre de coordinación

- [ ] T064 [P] Rendimiento SC-005: preparar con `StockCore` 2.000 ubicaciones y saldos sintéticos en una base aislada, lanzar 25 sesiones concurrentes contra `GET /locations?search=` y `GET /articles/{id}/locations`, y registrar el p95 (objetivo ≤ 2 s) y los errores, sin descartarlos, en la sección «Evidencia» de `specs/003-gestionar-ubicaciones/quickstart.md` (SC-005).
- [ ] T065 [P] ⛔ **Depende del login en la interfaz** (mismo bloqueo que T063): revisión de accesibilidad de las cinco pantallas (solo teclado, foco tras error y en los diálogos, nombres accesibles de las tablas, ningún estado comunicado solo con color) y corrección de lo encontrado en `frontend/src/app/features/locations/` (FR-019, constitución III).
- [ ] T066 ⛔ **Parcialmente bloqueada**: las tareas 1, 2 y 4 de SC-006 dependen del login en la interfaz (T063); la tarea 3 y el tiempo de SC-005 dependen además de la búsqueda de artículos (T055) y no se miden con el acceso provisional de T051. Validación con al menos tres operarios representativos (actividad humana; la tarea prepara el guion y la plantilla): alta, corrección de un campo inválido, localizar un artículo repartido en menos de 30 s desde la pantalla de inicio y desactivar una ubicación vacía; comprobar también que los resultados siguen visibles al volver a abrir cada ubicación; documentar participantes, resultados, incidencias y correcciones en `specs/003-gestionar-ubicaciones/quickstart.md` (SC-001, SC-005, SC-006).
- [ ] T067 Verificación final: suite backend completa, `has-pending-model-changes`, `ng test --watch=false` y `ng build --configuration production`; actualizar la sección «Pendiente por dependencias» de `quickstart.md` con el estado de T034, T055 y T063 (todos los FR).
- [ ] T068 Cierre de coordinación con A, **solo cuando T001 esté aprobado y el PR revisado**: aplicar los diffs propuestos en `plan.md` § «Cambios al modelo común pendientes de A» a `.specify/memory/project/data-model.md` (sección `### Location`, incluido el DENY DELETE sobre `StockBalances`), a `.specify/memory/project/contracts/api.md` (sección `### HU-02 — B: ubicaciones` y la fila 409 de la tabla de errores), a `.specify/memory/project/contracts/stock-operations.md` (recurso `locations:receiving`, su posición en el orden de bloqueos y `AppLocks` compartido) y a `.specify/memory/project/quickstart.md` (lista de DENY del inicializador y evidencia de HU-02), ajustados a lo que se haya implementado realmente (p. ej. la alternativa de D-08 si A la eligió); marcar como resuelta la sección de pendientes de `plan.md`, con la referencia a la revisión de A (FR-003, FR-014; AGENTS.md, «Cambios compartidos»).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Fase 1)**: T001 bloquea T009, T010 y T011 (esquema). T002 no tiene dependencias.
- **Foundational (Fase 2)**: depende de la Fase 1 y bloquea todas las historias.
- **Parte A, US1–US5 (Fases 3–7)**: dependen de la Fase 2. Las fases de consulta (US2, US3) no dependen de US1, porque preparan los datos con `LocationsTestSupport`. US4 depende de T019 (US1) porque edita el mismo `Location.cs`. US5 prueba los endpoints de US1 y US4.
- **Parte B (Fases 8–14)**: T038 bloquea toda la Parte B. Cada fase de UI depende del endpoint de su historia en la Parte A.
- **Polish (Fase 15)**: depende de las historias que se quieran entregar; T068 depende además de la aprobación de A.

### Bloqueos externos

| Tarea | Dependencia | Criterios sin demostrar mientras dure | Qué sigue funcionando |
|-------|-------------|---------------------------------------|------------------------|
| T034 ⛔ | HU-04: `CountLocation.IsFrozen` | Conteo abierto en FR-012, US3.3, US4.3 y SC-004. | Existencias y reservas en FR-012; `inOpenCount` es `false`. |
| T055 ⛔ | HU-08 (A): `GET /articles` | US2.1; tiempo de SC-005; tarea 3 de SC-006. | Acceso provisional de T051 y T053 para desarrollo y pruebas. |
| T063 ⛔ (y T065, parte de T066) | Login en la interfaz | SC-001 por la interfaz; FR-019 y FR-020 en la interfaz; accesibilidad (T065); SC-006 (T066). | Pruebas HTTP con sesión real (T036) y Vitest. |

### Archivos compartidos (sin [P])

`LocationEndpoints.cs` (T015, T023, T026, T029, T033), `Location.cs` (T019, T031),
`locations.routes.ts` (T046, T049, T053, T056), `location-form.page.ts` (T045, T056, T060),
`location-detail.page.ts` (T052, T053, T058) y `quickstart.md` (T037, T064, T066, T067):
sus tareas se ejecutan en el orden de los ID.

### Within Each User Story

Tests (deben fallar) → dominio → comandos y consultas → endpoint → checkpoint.

### Parallel Opportunities

- Fase 2: T003, T004, T006, T007, T012 y T013 a la vez; T009 y T010 a la vez cuando T001 esté aprobado.
- Al terminar la Fase 2: los tests T018, T024, T027 y T030 a la vez (archivos distintos).
- US1: T021 y T022 a la vez después de T019 y T020.
- US5: T035 y T036 a la vez.
- Parte B: T039, T040 y T042 a la vez; dentro de cada historia, las páginas y specs marcadas [P].

## Parallel Example: User Story 1

```bash
# Con la Fase 2 terminada:
Task: "T018 LocationsCreateTests.cs (L-01…L-04)"
# Tras T019 + T020:
Task: "T021 ListLocations.cs"
Task: "T022 GetLocation.cs"
# Después: T023 (LocationEndpoints.cs) en serie.
```

## Implementation Strategy

### MVP (US1)

Fase 1 → Fase 2 → Fase 3: alta y búsqueda de ubicaciones por API, con unicidad, auditoría
e idempotencia; L-01–L-04 en verde. Esto ya desbloquea a A para elegir destinos activos en
HU-01 (`GET /locations?status=active`).

### Entrega incremental

1. Parte A completa (US1→US5): backend revisable y fusionable con L-01–L-15 (sin conteos).
2. Parte B por historias: US1 UI → US2 UI (con el acceso provisional) → US3 → US4 → US5.
3. Cuando lleguen HU-08, HU-04 y el login de la interfaz: T055, T034 y T063.
4. Cierre: T064–T068.

## Notes

- El plan preveía `LocationsTests.cs` y `LocationsHttpTests.cs`; aquí las pruebas se dividen
  por historia para que cada una sea comprobable y paralelizable por separado. El patrón
  y la cobertura no cambian.
- No se crea ningún endpoint de artículos en HU-02: la búsqueda pertenece a HU-08 (A).
- No hacer commit desde estas tareas sin indicación expresa.
