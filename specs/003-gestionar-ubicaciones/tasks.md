---

description: "Desglose de tareas de HU-02 — Gestionar las ubicaciones de los artículos (iteración 2026-11-19)"
---

# Tasks: HU-02 — Gestionar las ubicaciones de los artículos

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/locations-api.md](contracts/locations-api.md),
[contracts/locations-ui.md](contracts/locations-ui.md), [quickstart.md](quickstart.md).
**Base común**: [constitución](../../.specify/memory/constitution.md) ·
[plan global](../../.specify/memory/project-plan.md) ·
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md).

**Iteración 2026-11-19**: rama `b/hu-02-ubicaciones-v2`, sobre la base integrada de A, y
recorte para la entrega del sábado. Se conservan los identificadores que siguen valiendo
(reformulados cuando cambia el cómo); las tareas nuevas empiezan en T069; las retiradas
figuran al final con su motivo. Por eso **el orden de ejecución es el de este documento,
no el numérico**.

**Tests**: incluidos (la constitución exige evidencia en SQL Server real). Por historia se
prueba el **recorrido habitual, el error principal y los permisos**; US5 concentra
concurrencia, versiones, idempotencia y atomicidad. Las pruebas se escriben primero.

**Código existente**: el backend parcial de `3b0c0ae` y el frontend de `8377a12` (rama
`b/hu-02-ubicaciones`) se **portan** con `git show <commit>:<ruta>` y se adaptan; no se
traen ni `AppLocks`, ni `BusinessRuleException`, ni `HttpPreconditions`, ni los cambios de
`package.json`/`angular.json` de esa rama.

## Format: `[ID] [P?] [Story] Descripción`

- **[P]**: paralelizable: archivo distinto y sin depender de tareas incompletas.
- **[US#]**: historia de [spec.md](spec.md).
- **⛔ BLOQUEADA**: no ejecutable hasta que llegue la dependencia indicada.
- Cada tarea cita los FR, escenarios (USx.y) y casos L-xx de [quickstart.md](quickstart.md).

## Path Conventions

- Backend: `backend/src/Inventario.Api/` · pruebas: `backend/tests/Inventario.IntegrationTests/Locations/`
- Frontend: `frontend/src/app/features/locations/` · E2E: `frontend/e2e/`
- Comandos: [quickstart.md](quickstart.md).

---

# Parte A — Backend

## Phase 1: Setup y coordinación

- [ ] T001 Coordinación con A **antes de fusionar** (no bloquea la implementación): pedir revisión en el PR de (1) `InventoryLocks.ReceivingZonesAsync()` —recurso `locations:receiving` en la fase de ubicaciones, después de `location:{id}`, nunca adquirido por `StockCore`— (D-05, FR-021) y (2) los cambios menores en archivos de A: `MasterDataEntity.Location` y el CHECK `CK_MasterDataAudit_Entity` (D-07), y la rama `DuplicateLocationPlace` de `MasterDataProblemMapper` (D-02). Registrar decisión, fecha y revisor en `specs/003-gestionar-ubicaciones/plan.md` § «Estado de la revisión de A». Si A pide otro recurso o fase, ajustar solo T003 y el caso L-11 de T030.
- [X] T002 Línea base en `b/hu-02-ubicaciones-v2`: confirmar que `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` termina en `20261118194940_H0_StockCore`; ejecutar la suite de `backend/Inventario.slnx` (88 pruebas) y `dotnet ef migrations has-pending-model-changes`, y en `frontend/` `npm test` (40) y `npm run build`; todo en verde y sin avisos.

---

## Phase 2: Foundational (prerrequisitos bloqueantes)

**⚠️ CRITICAL**: ninguna historia puede empezar hasta completar esta fase.

- [ ] T003 [P] Añadir `public Task ReceivingZonesAsync(CancellationToken cancellation = default) => AcquireAsync(2, ["locations:receiving"], cancellation);` en `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/InventoryLocks.cs`, con un comentario que explique que `location:` precede a `locations:` en orden ordinal y que `StockCore` no lo adquiere; ningún otro cambio (D-05; FR-021).
- [X] T005 [P] En `backend/src/Inventario.Api/Infrastructure/Http/MasterDataProblemMapper.cs`, antes de la rama de `_Code`, traducir 2601/2627 cuyo mensaje contenga `UX_Locations_Aisle_Rack_Level` a `409 DuplicateLocationPlace` con `errors.aisle`, `errors.rack` y `errors.level` y el `detail` «Ya existe una ubicación con ese pasillo, estantería y nivel, también puede estar inactiva.» (solo para la carrera entre dos altas: el caso habitual lo resuelve la comprobación previa de T020 y T032, que nombra el código existente); el código repetido sigue saliendo como `DuplicateCode` de A (D-02; FR-002, FR-003).
- [X] T007 [P] Añadir `Location` al enum `MasterDataEntity` en `backend/src/Inventario.Api/Domain/MasterData/MasterDataAudit.cs`, sin cambiar los valores existentes (D-07; FR-015).
- [X] T008 [P] En `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/MasterDataAuditConfiguration.cs`, cambiar el CHECK a `"[EntityType] IN ('Family','Supplier','Article','Location')"` (D-07; FR-015).
- [X] T009 [P] En `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/LocationConfiguration.cs`, sin cambiar lo existente: `UseCollation("Latin1_General_100_CI_AS")` en Aisle, Rack y Level e índice único **no filtrado** `.HasDatabaseName("UX_Locations_Aisle_Rack_Level")` sobre (Aisle, Rack, Level), que incluye las inactivas (D-02; FR-003).
- [X] T011 Generar `dotnet ef migrations add HU02_Locations` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` (depende de T008 y T009) y editarla para que (a) **antes** de crear el índice lance `THROW` con un mensaje en español si hay combinaciones (Aisle, Rack, Level) repetidas, sin borrar ni renombrar datos; (b) cambie la collation; (c) cree `UX_Locations_Aisle_Rack_Level`; (d) sustituya `CK_MasterDataAudit_Entity` por la versión con `'Location'`; `Down` revierte en orden inverso. `H0_StockCore` no se toca. Verificar `has-pending-model-changes`, la cadena desde cero y revertir/reaplicar `HU02_Locations` (D-01, D-02, D-07; FR-003, FR-015).
- [X] T012 [P] Portar `backend/src/Inventario.Api/Features/Locations/ILocationUsage.cs` desde `3b0c0ae`: `HasMovementsAsync` (EXISTS en `StockBalances` por `LocationId`), `GetTotalsAsync` (Σ Physical, Σ Reserved) e `IsInOpenCountAsync` = `false` hasta HU-04 (T034); recibe el `InventoryDbContext` del `OperationContext` cuando se usa dentro de un comando (D-03, D-10; FR-011, FR-012).
- [X] T013 [P] Portar `backend/src/Inventario.Api/Features/Locations/LocationDtos.cs` desde `3b0c0ae` **sin** `acknowledgeNoActiveReceiving`: `LocationWrite.Read(JsonElement body, bool create)` al estilo de `FamilyWrite` (solo `code`, `aisle`, `rack`, `level`, `kind` e `isActive`; otro campo → 422; tipos JSON inválidos → 400; recorta y pasa a mayúsculas sin validar reglas antes del replay); `LocationSummary`, `LocationDetail`, `ArticleLocationBucket`, `LocationStockLine`, `LocationMutation(OperationId, Resource)` y página `{ items, page, pageSize, totalCount }`; descripción «Pasillo X · Estantería Y · Nivel Z»; etiquetas «Almacenamiento»/«Zona de recepción» y «Activa»/«Inactiva»; cantidades `0.000`; paginación: «`page` empieza en 1 y `pageSize` vale 25 por defecto, con un máximo de 100», fuera de rango → `InventoryProblemException.Validation("page"|"pageSize", …)` (422) (FR-005, FR-019; contrato § Paginación).
- [X] T015 Crear `backend/src/Inventario.Api/Features/Locations/LocationEndpoints.cs` con `MapLocationEndpoints` (grupo `/locations` sin rutas todavía), `AddLocations` (registro scoped de `ILocationUsage`, `LocationCommands` y `LocationQueries`) y un `Key(HttpContext)` privado igual al de `FamilyEndpoints` (400 `InvalidRequest`); llamar `builder.Services.AddLocations()` y `api.MapLocationEndpoints()` en `backend/src/Inventario.Api/Program.cs` (depende de T012 y T013; FR-010).
- [X] T016 [P] Crear `backend/tests/Inventario.IntegrationTests/Locations/LocationsTestSupport.cs` reutilizando `Stock/StockTestHarness` (actores, `SeedAsync`, `RunAsync` de Receive/Reserve/Release/Ship, `AssertJournalMatchesBalanceAsync`) sin modificarlo, más: cliente HTTP con sesión real y antiforgery por rol (Operario, GestorMaestros, Administrador) como en las pruebas de maestros de A, `CreateLocationAsync` por HTTP y `AuditAsync(locationId)` (D-12).

**Checkpoint**: esquema migrado; 88 pruebas previas en verde.

---

## Phase 3: User Story 1 — Registrar las ubicaciones físicas del almacén (P1) 🎯 MVP

**Goal**: alta y consulta de ubicaciones con código único y lugar físico único.

**Independent Test**: L-01–L-03 por API (spec US1, «Independent Test»).

- [X] T018 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Locations/LocationsCreateTests.cs`: **habitual** L-01 (REC-01, P01-E01-N01, P01-E01-N02 → 201, activas y vacías, una auditoría `Create` con entidad `Location`, sin filas en `StockMovements` ni `StockBalances`; releídas con `GET /locations/{id}`; búsqueda por prefijo de código y de pasillo, filtros de tipo y estado, activas por defecto); **error** L-02 (` p01-e01-n01 ` frente a una existente y a una inactiva → 409 `DuplicateCode`; mismo lugar con otro código → 409 `DuplicateLocationPlace` cuyo `detail` contiene el código de la ubicación existente (US1.3); nada creado) y L-03 (pasillo `"   "` en Receiving → 422 `errors.aisle`; código de 33 o parte de 17 caracteres → 422; `page=0` → 422 `errors.page`); **permisos** (GestorMaestros: GET 200 y POST 403) (US1.1–US1.5; FR-001–FR-005, FR-010, FR-015; SC-001 por API, SC-002).
- [X] T019 [US1] En `backend/src/Inventario.Api/Domain/Locations/Location.cs`, añadir `static Normalize(...)` que reutilice `RequiredText` («Code: obligatorio, recortado, en mayúsculas, máx. 32»; «Aisle, Rack y Level: obligatorios, recortados, en mayúsculas, máx. 16»; vacío o solo espacios → error por campo) y `AuditSnapshot()` → `{code, aisle, rack, level, kind, isActive}`, sin cambiar el constructor de H0 (D-01; FR-001–FR-003).
- [X] T020 [US1] Crear `backend/src/Inventario.Api/Features/Locations/Commands/LocationCommands.cs` con `CreateAsync(Guid operation, LocationWrite write)`: `OperationRequest(operation, "POST", "/api/v1/locations", "Locations.Manage", cuerpo normalizado)`; dentro de `change`: `context.Locks.LocationsAsync([id])`, validar con `Location.Normalize` y `Kind` ∈ {`Storage`, `Receiving`} (422 por campo con `InventoryProblemException.Validation`), `isActive` distinto de `true` → 422, si otra ubicación tiene el mismo (Aisle, Rack, Level) → 409 `DuplicateLocationPlace` con `errors.aisle/rack/level` y un `detail` que nombra su código («Ese pasillo, estantería y nivel ya corresponden a la ubicación P01-E01-N01.»), añadir y `SaveChangesAsync`, `MasterDataAuditWriter.Record(..., MasterDataEntity.Location, id, Create, null, snapshot)`, `OperationResponse.Json(201, new LocationMutation(...), id, { ETag, Location })` (depende de T019; US1.1–US1.4; FR-001–FR-004, FR-015, FR-018).
- [X] T021 [P] [US1] Crear `backend/src/Inventario.Api/Features/Locations/Queries/LocationQueries.cs` con `ListAsync`: AsNoTracking; `search` por prefijo de Code o Aisle sin distinguir mayúsculas; `kind`; `status` (`active` por defecto, `inactive`, `all`); paginación de T013; orden estable por Code (US1.5; FR-005).
- [X] T022 [US1] En `LocationQueries.cs`, `DetailAsync(id)`: `LocationDetail` con `hasMovements`, `isEmpty`, `hasReservations` e `inOpenCount` de `ILocationUsage` y `isLastActiveReceiving` (Receiving activa y única activa); devuelve también la versión para el `ETag` (depende de T021; FR-005, FR-021).
- [X] T023 [US1] En `LocationEndpoints.cs`, mapear `POST /locations` (`.RequireAuthorization("Locations.Manage")`, `Key(context)`, `LocationWrite.Read(body, true)`), `GET /locations` y `GET /locations/{id}` (`.RequireAuthorization("Inventory.Read")`; `ETag` con `EntityTags.Format`, `Cache-Control: no-store`, 404 con `ApiProblems.Status(404)`) (depende de T020–T022; FR-005, FR-010).

**Checkpoint**: US1 por API; T018 en verde.

---

## Phase 4: User Story 2 — Conocer todas las ubicaciones de un artículo (P1)

**Goal**: ubicaciones de un artículo con cantidad por ubicación y lote y el total, desde `StockBalances`.

**Independent Test**: L-05 (artículo) y L-06.

- [X] T024 [P] [US2] Crear `backend/tests/Inventario.IntegrationTests/Locations/ArticleLocationsTests.cs`: **habitual** L-05 (2 + 4 de ART-01/L-001 en N01/N02 con el núcleo → buckets 2 y 4, total 6 igual al diario) y L-06 (dos lotes en una ubicación sin duplicar el total; `pageSize=1` con totales de todo el artículo; artículo sin existencias → 200 vacío y totales cero; artículo inactivo y ubicación inactiva visibles con su estado; tras un Ship, nuevo reparto); **error** (artículo inexistente → 404); **permisos** (Administrador → 403) (US2.2–US2.5; FR-006, FR-007, FR-009; SC-003).
- [X] T025 [US2] En `LocationQueries.cs`, `ArticleLocationsAsync`: transacción SNAPSHOT de solo lectura; `StockBalances` con `Locations`, `Lots` y `Articles`; sin buckets con Physical = 0 y Reserved = 0; `totals` de todo el artículo; `asOf`; `includeInactiveLocations=true` por defecto; «Sin lote» para `SIN-LOTE`; orden por código y lote (D-09; FR-006, FR-007, FR-009).
- [X] T026 [US2] En `LocationEndpoints.cs`, mapear `GET /articles/{id}/locations` con `.RequireAuthorization("Inventory.Read")` (depende de T025; FR-007, FR-010).

> US2.1 (búsqueda por código o nombre) usa `GET /articles?q=` de A: no hay trabajo de backend (véase T055).

---

## Phase 5: User Story 3 — Revisar qué contiene una ubicación (P2)

**Goal**: contenido de una ubicación por artículo y lote, con reservas.

**Independent Test**: L-05 (ubicación) y ubicación vacía.

- [ ] T027 [P] [US3] Crear `backend/tests/Inventario.IntegrationTests/Locations/LocationStockTests.cs`: **habitual** (N02 → ART-01, L-001, `4.000` UD; vacía → `items` vacío con datos, tipo y estado; tras Reserve, `hasReservations=true` y reservado en la fila; artículo inactivo con su estado); **error** (ubicación inexistente → 404); **permisos** (sin sesión → 401) (US3.1–US3.3; FR-008, FR-009).
- [ ] T028 [US3] En `LocationQueries.cs`, `StockAsync`: SNAPSHOT; filas de `StockBalances` con Physical o Reserved distinto de cero; artículo, lote, physical, reserved y unidad; `hasReservations`; `inOpenCount`; paginado; sin total global (D-09; FR-008).
- [ ] T029 [US3] En `LocationEndpoints.cs`, mapear `GET /locations/{id}/stock` con `.RequireAuthorization("Inventory.Read")` (depende de T028; FR-008, FR-010).

---

## Phase 6: User Story 4 — Corregir y retirar ubicaciones sin perder la trazabilidad (P2)

**Goal**: corrección, cambio de tipo, desactivación y reactivación (FR-011–FR-014, FR-021); sin borrado.

**Independent Test**: L-07, L-08 y L-11.

- [ ] T030 [P] [US4] Crear `backend/tests/Inventario.IntegrationTests/Locations/LocationsUpdateTests.cs`: **habitual** L-07 (corregir nivel sin movimientos → 200 y auditoría `Update`; el tipo cambia estando vacía) y L-08 (vaciar, desactivar → 200 `Deactivate`; Receive y Reserve del núcleo rechazados mientras está inactiva; sigue en `GET /articles/{id}/locations`; reactivar → 200 `Reactivate`); **error** L-07 (N01 tras recibir y tras dejarla a cero con Ship → 409 `LocationIdentityLocked`), L-08 (con existencias y con reserva viva → 409 `LocationNotEmpty`), L-11 (desactivar la única Receiving y pasarla a Storage → 409 `NoActiveReceivingLeft` sin cambios; con dos Receiving retiradas a la vez → exactamente una 200 y otra 409; además, dentro de una transacción, `ReceivingZonesAsync` tras `LocationsAsync` funciona y `LocationsAsync` tras `ReceivingZonesAsync` lanza la excepción de orden de `InventoryLocks`), 422 `StateChangeMustBeSeparate`, PUT sin cambios → 200 sin auditoría, `DELETE /locations/{id}` → 405 y la ubicación sigue; **permisos** (GestorMaestros PUT → 403) (US4.1–US4.7; FR-011–FR-015, FR-020, FR-021; SC-002, SC-004).
- [ ] T031 [US4] En `backend/src/Inventario.Api/Domain/Locations/Location.cs`, añadir `Correct(code, aisle, rack, level)` con la normalización de T019, `ChangeKind(kind)`, `Deactivate()` y `Reactivate()`, que devuelven si hubo cambio efectivo; las reglas de saldos quedan en el comando (depende de T019; FR-001, FR-011–FR-013).
- [ ] T032 [US4] En `LocationCommands.cs`, `UpdateAsync(Guid operation, Guid id, LocationWrite write, string? ifMatch)`: `OperationRequest(operation, "PUT", "/api/v1/locations/{id}", "Locations.Manage", cuerpo normalizado)`; en `change`: `LocationsAsync([id])` → leer (404 si no existe) → si es Receiving y se desactiva o pasa a Storage, `ReceivingZonesAsync()` → `EntityTags.RequireMatch(ifMatch, location.Version)` → si cambian `isActive` y otro campo → 422 `StateChangeMustBeSeparate` → si cambia el lugar y otra ubicación lo ocupa → 409 `DuplicateLocationPlace` nombrando su código (como en T020) → `LocationIdentityLocked` si cambian code/aisle/rack/level y `HasMovementsAsync` → `LocationNotEmpty` (cantidades en `detail`) o `CountScopeFrozen` al desactivar o cambiar de tipo → `NoActiveReceivingLeft` si quedarían 0 Receiving activas → aplicar, `SaveChangesAsync` y auditar `Update`, `Deactivate` o `Reactivate`; sin cambios efectivos → 200 sin auditoría; `detail` en español con la alternativa (data-model, tabla de reglas; FR-011–FR-013, FR-015–FR-017, FR-020, FR-021).
- [ ] T033 [US4] En `LocationEndpoints.cs`, mapear `PUT /locations/{id}` (`Locations.Manage`, `Key(context)`, `If-Match`) y comprobar que `DELETE /locations/{id}` responde 405 ProblemDetails y no el 404 del fallback de `/api`; si no, mapear un DELETE explícito que devuelva `ApiProblems.Status(405)` (depende de T032; FR-010, FR-014).
- [ ] T034 [US4] ⛔ **BLOQUEADA — depende de HU-04 (`CountLocation.IsFrozen`)**: implementar `IsInOpenCountAsync` en `backend/src/Inventario.Api/Features/Locations/ILocationUsage.cs`, añadir los casos de conteo abierto (409 `CountScopeFrozen`; `inOpenCount=true`) a `LocationsUpdateTests.cs` y `LocationStockTests.cs` y mostrar «En conteo» en `frontend/src/app/features/locations/detail/location-detail.page.ts`. Sin demostrar mientras dure: la parte de conteo de FR-012, US3.3, US4.3 y SC-004; queda «pendiente HU-04» en `quickstart.md`.

---

## Phase 7: User Story 5 — Guardar cambios con autorización y resultado fiable (P3)

**Goal**: permisos, concurrencia, conflictos de edición, reintentos y atomicidad.

**Independent Test**: L-04, L-09, L-10, L-12 y L-15.

- [ ] T035 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/Locations/LocationsReliabilityTests.cs`: **L-09** (20 repeticiones de desactivación y Receive simultáneos sobre una ubicación vacía: o la recepción confirma y la desactivación da `LocationNotEmpty`, o la desactivación confirma y la recepción se rechaza; nunca inactiva con Physical > 0); **L-10** (dos PUT con el mismo ETag → 200 y 412 `VersionConflict`; sin If-Match → 428; replay del primer PUT con su clave y el ETag ya obsoleto → mismo resultado; misma clave con otro cuerpo → 409 `IdempotencyConflict`); **L-04** (8 POST simultáneos con la misma clave → una ubicación y respuestas idénticas); **L-12** (sin sesión → 401; sin `X-XSRF-TOKEN` → 400 `AntiforgeryValidationFailed`; sin `Idempotency-Key` → 400 `InvalidRequest`; ProblemDetails con `code` y `detail` en español); **L-15** (con `StockTestHarness.FailingFactory` y `FailAfterSaveInterceptor` armado, un alta y una desactivación fallan sin dejar ubicación o cambio, auditoría ni `OperationResult`; repetir con la misma clave confirma un solo efecto y una sola auditoría) (US5.1–US5.4; FR-010, FR-015–FR-018; SC-004, SC-007).
- [ ] T037 [US5] Corregir cualquier fallo que revele T035 sin relajar las aserciones; ejecutar la suite backend completa tres veces y registrar fecha, entorno, número de pruebas y resultado en `specs/003-gestionar-ubicaciones/quickstart.md` § «Evidencia» (FR-016–FR-018; SC-007).

**Checkpoint (fin Parte A)**: L-01–L-12 y L-15 en verde, salvo conteos (T034 ⛔).

---

# Parte B — Frontend

## Phase 8: Frontend foundational

**Purpose**: base de la feature sobre el frontend de A. **Bloquea toda la Parte B.**

- [ ] T038 Comprobar en `frontend/` que la fusión de `b/h0-frontend-v2` (commit `896262a`) deja `npm test` y `npm run build` en verde y sin avisos, con el placeholder de `/ubicaciones` protegido por `Inventory.Read` (D-11).
- [ ] T039 [P] Portar `frontend/src/app/features/locations/data/location.models.ts` desde `8377a12`, sin `acknowledgeNoActiveReceiving`, con `LocationMutation { operationId; resource: LocationDetail }` y los códigos de error del contrato (`DuplicateCode`, `DuplicateLocationPlace`, `NoActiveReceivingLeft`, …); cantidades como `string`.
- [ ] T040 [P] Portar `frontend/src/app/features/locations/data/idempotent-intent.ts` y su `idempotent-intent.spec.ts` desde `8377a12` como **excepción documentada** (D-11): clave por intención con `crypto.randomUUID()`, sessionStorage con `try/catch` y respaldo en memoria, reutilizada en reintentos y borrada solo tras 2xx o 4xx; nunca datos sensibles (FR-018).
- [ ] T069 [P] Portar `frontend/src/app/features/locations/shared/operation.ts` y `frontend/src/app/features/locations/testing/locations-testing.ts` desde `8377a12`, sustituyendo `ApiError`/`asApiError` por `apiProblem()` de `core/http/api-problem.ts` (`status`, `code`, `detail`, `errors`) y el simulado de `/auth/me` por una `SessionService` con usuario y permisos fijados; sin `MessageService` (D-11; FR-019).
- [ ] T041 Portar `frontend/src/app/features/locations/data/locations-api.service.ts` y su spec desde `8377a12`: `list`, `get` (con `observe: 'response'` para el `ETag`), `create` (`Idempotency-Key`), `update` (`If-Match` + `Idempotency-Key`), `stock` y `articleLocations`; errores como `HttpErrorResponse` interpretados con `apiProblem()`; spec: cabeceras, misma clave tras error de red y clave nueva tras 2xx (depende de T039, T040 y T069; FR-017, FR-018).

---

## Phase 9: User Story 1 — UI de alta y búsqueda

**Independent Test**: desde `/ubicaciones`, crear REC-01 y P01-E01-N01, buscarlas y ver el error de duplicado sin perder lo escrito.

- [ ] T044 [P] [US1] Portar `frontend/src/app/features/locations/list/location-list.page.ts` desde `8377a12`: búsqueda por código o pasillo (inicializada desde el parámetro de URL `search`, que usa el enlace de T045), filtros de tipo y estado, «Incluir inactivas», tipo y estado con texto, «No hay ubicaciones que coincidan con la búsqueda» con botón para limpiar filtros; botón «Nueva ubicación» solo si `SessionService.has('Locations.Manage')` (US1.5; FR-005, FR-019).
- [ ] T045 [P] [US1] Portar `frontend/src/app/features/locations/form/location-form.page.ts` (modo alta) desde `8377a12`: Código (máx. 32), Pasillo, Estantería y Nivel (máx. 16), Tipo; obligatoriedad con «*» y texto; «Proponer código»; 422 → errores por campo con `aria-describedby`; 409 `DuplicateCode`/`DuplicateLocationPlace` → marca los campos, muestra el `detail` (que nombra la ubicación existente) y enlaza a `/ubicaciones?search=<código>` o `?search=<pasillo>`; conserva los datos válidos y lleva el foco al primer error; éxito → mensaje en la página con el código y navegación al detalle; sin `MessageService` (US1.1–US1.4; FR-001–FR-003, FR-019, FR-020).
- [ ] T046 [US1] Sustituir el placeholder en `frontend/src/app/features/locations/locations.routes.ts` por `''` (lista) y `nueva` (formulario, `canActivate: [permissionGuard]`, `data: { permission: 'Locations.Manage' }`) con `loadComponent` (depende de T044 y T045; FR-010).
- [ ] T047 [P] [US1] Portar y adaptar `form/location-form.page.spec.ts` y `list/location-list.page.spec.ts`: **habitual** (alta y lista con estados como texto); **error** (campos en blanco o solo espacios bloquean el envío; un 409 conserva los valores y lleva el foco al campo); **permisos** (sin `Locations.Manage`, no aparece «Nueva ubicación») (US1.2–US1.5; FR-019).

---

## Phase 10: User Story 2 — UI de ubicaciones de un artículo

**Independent Test**: buscar ART-01 por código o nombre y ver 2 + 4 = 6 UD con `asOf` y total separado de la página.

- [ ] T048 [P] [US2] Portar `frontend/src/app/features/locations/article/article-locations.page.ts` desde `8377a12`, leyendo `articleId` con `ActivatedRoute`: total encima de la tabla y separado de la página; filas por ubicación y lote («Sin lote»); «Inactiva»/«Inactivo»; vacío → «Este artículo no está almacenado en ninguna ubicación» sin tratarlo como error; «Actualizar» y `asOf`; sin el aviso del acceso provisional (US2.2–US2.5; FR-007, FR-019).
- [ ] T055 [P] [US2] Crear `frontend/src/app/features/locations/article/article-search.ts` (desbloqueada: `GET /articles` de A existe): búsqueda por SKU o nombre con `GET /api/v1/articles?q=&state=all&page=1&pageSize=25`, resultados con código, nombre y estado en texto, y enlace a `/ubicaciones/articulo/:articleId` (US2.1; FR-007).
- [ ] T049 [US2] Añadir en `frontend/src/app/features/locations/locations.routes.ts` las rutas `articulo` (búsqueda) y `articulo/:articleId` (depende de T046, T048 y T055).
- [ ] T050 [P] [US2] Portar y adaptar `article/article-locations.page.spec.ts` y crear `article/article-search.spec.ts`: **habitual** (búsqueda → enlace; total desde `totals` y no de sumar la página); **error** (artículo inexistente → mensaje en la página); estado vacío y textos de estado (US2.1–US2.4).

---

## Phase 11: User Story 3 — UI de contenido de una ubicación

**Independent Test**: abrir P01-E01-N02 y ver ART-01, L-001, 4 UD; abrir una vacía y ver «Esta ubicación está vacía».

- [ ] T052 [US3] Portar `frontend/src/app/features/locations/detail/location-detail.page.ts` desde `8377a12`, leyendo `id` con `ActivatedRoute` y sin `MessageService`: datos físicos, tipo y estado; contenido con lote, cantidad y unidad, reservado con texto y «Inactivo»; «Esta ubicación está vacía»; aviso de reservas; espacio para «En conteo» (T034); botones Editar, Desactivar y Reactivar solo con `Locations.Manage` (US3.1–US3.3; FR-008, FR-019).
- [ ] T053 [US3] En `location-detail.page.ts`, enlace «Ver todas sus ubicaciones» → `/ubicaciones/articulo/:articleId` en cada fila, y ruta `:id` en `locations.routes.ts` (depende de T049 y T052).
- [ ] T054 [P] [US3] Portar y adaptar `detail/location-detail.page.spec.ts`: **habitual** (contenido y vacía); **error** (404 → mensaje en la página); **permisos** (sin `Locations.Manage`, sin botones de gestión; nunca «Eliminar») (US3.1–US3.3; FR-014).

---

## Phase 12: User Story 4 — UI de corrección y retirada

**Independent Test**: corregir el nivel de una ubicación nueva; ver bloqueada la identificación de una con movimientos; desactivar con revisión; no poder retirar la última zona de recepción.

- [ ] T056 [US4] Ampliar `form/location-form.page.ts` con el modo edición desde `8377a12` (ruta `:id/editar` con `permissionGuard` y `Locations.Manage` en `locations.routes.ts`; `id` desde `ActivatedRoute`): carga con su `ETag`; con `hasMovements`, Código, Pasillo, Estantería y Nivel de solo lectura con el texto de FR-011 y «Crear otra ubicación»; `LocationIdentityLocked` y `LocationNotEmpty` junto al campo (US4.1, US4.2; FR-011, FR-012, FR-019).
- [ ] T057 [P] [US4] Portar `frontend/src/app/features/locations/detail/location-state-dialog.ts` desde `8377a12` **sin reconocimiento**: revisión con código, descripción, contenido y consecuencia; si `isLastActiveReceiving`, explica que no puede retirarse la única zona de recepción activa y que antes debe crearse o activarse otra, sin botón de confirmar; un 409 `NoActiveReceivingLeft` o `LocationNotEmpty` muestra el `detail` (con enlace al contenido en el segundo) y **no reintenta**; el foco vuelve al botón de origen (US4.3–US4.5, US4.7; FR-012, FR-013, FR-020, FR-021).
- [ ] T058 [US4] Conectar `location-state-dialog.ts` desde `location-detail.page.ts` y recargar el detalle al confirmar; ninguna acción «Eliminar» (depende de T052 y T057; US4.6; FR-014).
- [ ] T059 [P] [US4] Portar y adaptar `detail/location-state-dialog.spec.ts`: **habitual** (desactivar y reactivar con revisión); **error** (última zona de recepción: sin confirmar y con explicación; 409 sin reintento; `LocationNotEmpty` con enlace); ausencia de «Eliminar» (US4.3–US4.7; FR-021).

---

## Phase 13: User Story 5 — UI de autorización, conflictos y resultado incierto

**Independent Test**: dos pestañas editan la misma ubicación (412 sin perder lo escrito); cortar la red al guardar → «Resultado pendiente de comprobar» → «Comprobar» con la misma clave.

- [ ] T060 [US5] En `form/location-form.page.ts`, portar desde `8377a12` el 412 («Otra persona ha cambiado esta ubicación», «Ver datos vigentes» en panel aparte y «Descartar mis cambios») y el resultado pendiente (error de red o timeout → «Resultado pendiente de comprobar» con «Comprobar», que repite con la misma clave); botón deshabilitado mientras confirma; nunca éxito sin 2xx (US5.2, US5.3; FR-017, FR-018, FR-020).
- [ ] T061 [US5] Aplicar el resultado pendiente en `location-state-dialog.ts` y mostrar un 403 como «Tu cuenta no tiene permiso para modificar ubicaciones», manteniendo la consulta (depende de T057; US5.1; FR-010, FR-020).
- [ ] T062 [P] [US5] Portar y adaptar `form/location-form.conflict.spec.ts`: el 412 conserva lo escrito; tras error de red, «Comprobar» reutiliza la clave; doble clic → una sola petición; 403 con mensaje en la página (US5.1–US5.3).

---

## Phase 14: E2E (recorrido principal)

- [ ] T063 Crear `frontend/e2e/locations.spec.ts` (desbloqueada: login de A) con `./fixtures/master-data.fixture`: con la cuenta `lector` (Operario), iniciar sesión → `/ubicaciones` → crear REC-01 y P01-E01-N01 → intentar un código repetido y comprobar que se conserva lo escrito → buscar → abrir el detalle vacío → desactivar con revisión → reactivar, todo con teclado; con `gestor` (GestorMaestros), la lista se ve y no hay botones de gestión y `/ubicaciones/nueva` lleva a `/sin-permiso`. Ejecutar con `npm run test:e2e -- e2e/locations.spec.ts` (SC-001 por la interfaz; FR-010, FR-019, FR-020).

---

## Phase 15: Polish y cierre

- [ ] T067 Verificación final: suite backend completa, `has-pending-model-changes`, `npm test`, `npm run build` y T063; actualizar en `specs/003-gestionar-ubicaciones/quickstart.md` § «Evidencia» y § «Pendiente por dependencias» el estado de T034 (todos los FR).
- [ ] T068 Cierre de coordinación con A, **cuando T001 esté aprobado y el PR revisado**: aplicar los diffs de `plan.md` § «Cambios al modelo común pendientes de A» a `.specify/memory/project/data-model.md`, `.specify/memory/project/contracts/api.md` (incluida la paginación con 422 y la fila 409), `.specify/memory/project/contracts/stock-operations.md` (`locations:receiving`) y `.specify/memory/project/quickstart.md` (evidencia de HU-02), ajustados a lo implementado; marcar resuelta la sección de pendientes de `plan.md` con la referencia a la revisión (FR-003, FR-021; AGENTS.md, «Cambios compartidos»).

---

## Tareas retiradas en la iteración 2026-11-19

| Tarea | Motivo |
|-------|--------|
| T004 `BusinessRuleException` | Sustituida por `InventoryProblemException` de A. |
| T006 `HttpPreconditions` | Sustituida por `EntityTags` y el `Key()` de los endpoints de A (T015). |
| T010 DENY en el inicializador | Ya aplicados por las migraciones `H0_StockCore` y `HU08AuditInfrastructure`; validados por A. |
| T014 `LocationCommandRunner` | Sustituido por `InventoryOperationExecutor` de A (D-06). |
| T017 Pruebas de infraestructura (L-13, L-14) | DENY cubiertos por `StockCoreTests` y `OperationProtocolTests`; índice por T018; bloqueos por T030 y T035 (D-12). |
| T036 `LocationsHttpTests` | Fusionada en T035 (un solo archivo de fiabilidad). |
| T042 `location-permissions.ts` | Sustituida por `SessionService.has()` y `permissionGuard` de A. |
| T043 Specs del servicio | Fusionadas en T040 y T041. |
| T051 Acceso provisional a artículos | Innecesario: T055 desbloqueada. |
| T064 Rendimiento SC-005 | Fuera de la entrega del sábado (D-13; desviación en Complexity Tracking). |
| T065 Revisión de accesibilidad separada | Integrada en las specs de Vitest y en T063 (D-13). |
| T066 Validación con operarios SC-006 | Fuera de la entrega del sábado (D-13; desviación en Complexity Tracking). |

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Fase 1)**: T001 bloquea solo la fusión; T002 primero.
- **Foundational (Fase 2)**: bloquea todas las historias. T011 depende de T008 y T009; T015 de T012 y T013.
- **Parte A, US1–US5 (Fases 3–7)**: dependen de la Fase 2. US2 y US3 no dependen de US1 (preparan datos con `LocationsTestSupport`). US4 depende de T019 (mismo `Location.cs`) y de T020 (mismo `LocationCommands.cs`). US5 prueba los endpoints de US1 y US4.
- **Parte B (Fases 8–14)**: T038 bloquea la Parte B; cada fase de UI depende del endpoint de su historia; T063 depende de US1, US3 y US4 en backend y frontend.
- **Polish (Fase 15)**: T067 al final; T068 además de la aprobación de A.

### Bloqueos externos

| Tarea | Dependencia | Criterios sin demostrar mientras dure | Qué sigue funcionando |
|-------|-------------|---------------------------------------|------------------------|
| T034 ⛔ | HU-04: `CountLocation.IsFrozen` | Conteo abierto en FR-012, US3.3, US4.3 y SC-004. | Existencias y reservas en FR-012; `inOpenCount` es `false`. |

### Archivos compartidos (sin [P] entre sí)

`LocationEndpoints.cs` (T015, T023, T026, T029, T033), `LocationCommands.cs` (T020, T032),
`LocationQueries.cs` (T021, T022, T025, T028), `Location.cs` (T019, T031),
`locations.routes.ts` (T046, T049, T053, T056), `location-form.page.ts` (T045, T056, T060),
`location-detail.page.ts` (T052, T053, T058), `location-state-dialog.ts` (T057, T061) y
`quickstart.md` (T037, T067): en el orden de este documento.

### Within Each User Story

Tests (deben fallar) → dominio → comandos y consultas → endpoint → checkpoint.

### Parallel Opportunities

- Fase 2: T003, T005, T007, T008, T009, T012, T013 y T016 a la vez.
- Tras la Fase 2: los tests T018, T024, T027, T030 y T035 a la vez (archivos distintos).
- Parte B: T039, T040 y T069 a la vez; luego, por historia, las páginas y specs marcadas [P].

## Parallel Example: User Story 1

```bash
# Con la Fase 2 terminada:
Task: "T018 LocationsCreateTests.cs"
Task: "T021 LocationQueries.ListAsync"      # archivo distinto de T019/T020
# Tras T019:
Task: "T020 LocationCommands.CreateAsync"
# Después: T022 y T023 en serie.
```

## Implementation Strategy

### MVP (US1)

Fase 1 → Fase 2 → Fase 3: alta y búsqueda por API con unicidad, auditoría e idempotencia.
Desbloquea a A para elegir destinos activos en HU-01 (`GET /locations?status=active`).

### Entrega del sábado

1. Parte A completa (US1→US5): L-01–L-12 y L-15.
2. Parte B por historias, portando `8377a12`: US1 → US2 → US3 → US4 → US5.
3. T063 (E2E del recorrido principal) y T067.
4. Fuera: SC-005 y SC-006 (desviación registrada); T034 hasta HU-04; T068 tras la revisión de A.

## Notes

- Total activo: 57 tareas (1 bloqueada por HU-04); 12 retiradas.
- No se crea ningún endpoint de artículos en HU-02: la búsqueda es de A.
- No hacer commit desde estas tareas sin indicación expresa.
