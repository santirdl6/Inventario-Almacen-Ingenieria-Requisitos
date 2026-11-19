---
description: "Tareas ejecutables de HU-08: artículos, familias y proveedores"
---

# Tasks: HU-08 — Mantener artículos, familias y proveedores

**Input**: [plan](plan.md), [spec aclarada](spec.md), [research](research.md),
[modelo](data-model.md), [HTTP](contracts/api.md), [UI](contracts/ui.md),
[integración](contracts/integration.md) y [quickstart](quickstart.md).
**Base obligatoria**: [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md),
[modelo común](../../.specify/memory/project/data-model.md),
[contrato HTTP común](../../.specify/memory/project/contracts/api.md) y
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md).
**Fecha**: 2026-11-12. **Responsable**: A, con las dependencias de B indicadas.

**Tests**: incluidos porque la spec exige pruebas independientes, fallos, concurrencia,
reintentos y evidencia medible, y la constitución exige SQL real para cambios de persistencia.
Las pruebas de cada historia se escriben antes de su implementación y deben demostrar el
fallo funcional esperado, no un entorno roto. No se repiten suites sin cambios o fallos
que lo justifiquen. Las 18 pruebas H0 previas no acreditan esta entrega.

**Organization**: US1 familias P1; US2 artículos P1; US3 proveedores P1; US4 retirada e
historial P1; US5 resultado fiable P2. Las garantías comunes de US5 se adelantan a fundamentos
para que ningún incremento anterior escriba sin protección; su recorrido completo se
valida en su fase. Las tareas se generaron pendientes; los marcadores X reflejan únicamente
implementación verificada en las evidencias de cada incremento, no aceptación completa.

## Format: `[ID] [P?] [Story] Description`

- Cada tarea tiene checkbox, ID secuencial, descripción verificable y rutas concretas.
- `[P]` indica trabajo en archivos distintos dentro de una ronda ya habilitada; no autoriza
  a saltar prerequisitos. Las rondas se detallan al final.
- Las etiquetas `[US1]`–`[US5]` solo aparecen en fases de historias.
- Las rutas son relativas a la raíz del repositorio. Las rutas nuevas nombran entregables
  previstos, no archivos cuya existencia se dé por hecha.
- Migraciones y snapshot se modifican de uno en uno, sobre la cadena integrada. Los nombres
  sin timestamp identifican el propósito; EF genera los nombres completos al implementarlas.
- **GATE-B** significa dependencia real: integrar el componente o registrar bloqueo y mantener
  la tarea pendiente. No sustituirlo por un mock/no-op productivo ni inventar una aprobación.

## Phase 1: Setup — Herramientas y entorno de trabajo

**Purpose**: preparar el trabajo sin reinstalar la aplicación ni reemplazar los cambios H0.

- [X] T001 Contrastar el estado real H0 y las entregas de B con `specs/002-gestionar-datos-maestros/contracts/integration.md`; registrar responsables, archivos definitivos del ejecutor/resultado, revisión pendiente y orden de integración en `specs/002-gestionar-datos-maestros/validation/coordination.md`, sin declarar revisión A+B realizada antes de recibirla.
- [X] T002 [P] Habilitar unitarios frontend en `frontend/package.json`, `frontend/package-lock.json`, `frontend/angular.json` y `frontend/tsconfig.spec.json`: fijar vitest/coverage-v8 5.1.0 y jsdom 31.0.0, builder `@angular/build:unit-test`, runner vitest y buildTarget `inventario:build:development`; conservar Angular 22.2.3/TypeScript 6.0.3 y Node 24 LTS ≥24.15.0.
- [X] T003 [P] Extender la preparación aislada en `backend/tests/Inventario.IntegrationTests/IdentityFixture.cs` y `backend/tests/Inventario.IntegrationTests/Fixtures/MasterDataTestData.cs` para usuarios gestor/lector/administrador, scopes y conexiones independientes; conservar bases/logins aleatorios y limpieza limitada a la ejecución, sin cargar saldos manuales ni reutilizar InventoryDev.
- [X] T004 Tras T002/T003, fijar @playwright/test 1.65.0 en `frontend/package.json`/`frontend/package-lock.json` y crear `frontend/playwright.config.ts` y `frontend/e2e/fixtures/master-data.fixture.ts` con aplicación real, cuentas controladas y cookies/CSRF; usar proyecto Compose dedicado `inventario-hu08-e2e`, DB_NAME=InventoryHu08E2E, APP_PORT=8082 y MAILPIT_PORT=8026, baseURL configurable HU08_E2E_BASE_URL y preparación de usuarios aislada coordinada con el fixture backend; documentar preparación/limpieza en `specs/002-gestionar-datos-maestros/quickstart.md`, sin tocar InventoryDev ni añadir API pública de usuarios.
- [X] T005 Verificar build de frontend y baseline SQL H0 con los comandos de `specs/002-gestionar-datos-maestros/quickstart.md`; guardar comandos, versiones, commit, resultados y limitaciones reales en `specs/002-gestionar-datos-maestros/validation/baseline.md`, distinguiendo pruebas existentes de suites todavía sin implementar.

**Checkpoint**: herramientas, fixtures y baseline preparados; T001 identifica qué dependencias
de B existen. No atribuir a esta fase implementación de maestros ni del núcleo de stock.

## Phase 2: Foundational — Garantías comunes que bloquean las mutaciones

**Purpose**: disponer de infraestructura real reutilizable por todos los catálogos.
El desarrollo preparatorio de modelos/maquetas puede avanzar; no habilitar endpoints de
escritura ni dar historias por terminadas hasta superar este checkpoint.

- [X] T006 Aportación común de A autorizada por el usuario, pendiente de revisión de B: implementar el único resultado en `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/OperationResult.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/OperationResultConfiguration.cs`; OperationId UUID único=Idempotency-Key, ActorId, RequestHash, Method, Resource, RequiredPermission, ResultStatus, ResultBody nullable, ResultHeaders ETag/Location, SourceDocumentId histórico sin FK al maestro y RecordedAt UTC; no crear otro resultado de MasterData.
- [X] T007 Aportación común de A autorizada, pendiente de revisión de B: implementar el ejecutor en `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/InventoryOperationExecutor.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/InventoryLocks.cs`: scope/DbContext limpio e ICurrentActor por intento, máximo tres intentos, applocks transaccionales de 5 s y orden operación → documentos → ubicaciones → artículos; UUID D minúsculo, orden ordinal y recursos documentales canónicos, sin commits autónomos.
- [X] T008 Completar intención/replay en `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/RequestFingerprint.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/OperationResponse.cs`: SHA-256 de método/ruta/JSON normalizado, actor aparte, sin If-Match/cookies/tokens; autorizar y recuperar resultado antes de cargar recurso o versión, conservar cuerpo/cabeceras y devolver 409 con distinta intención.
- [X] T009 Implementar precondiciones en `backend/src/Inventario.Api/Infrastructure/Http/EntityTags.cs`: ETag fuerte Base64 de rowversion entre comillas, If-Match en PUT/DELETE nuevos, 428 ausente, 400 mal formado/W/lista/comodín y 412 obsoleto; replay autorizado anterior a esas comprobaciones.
- [X] T010 [P] Crear `backend/src/Inventario.Api/Domain/MasterData/MasterDataAudit.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/MasterDataAuditConfiguration.cs` con «Id UUID, EntityType nvarchar(32), EntityId UUID histórico, Action nvarchar(24), Before/After nvarchar(max) JSON nullable según acción, ActorId UUID, RecordedAt datetimeoffset UTC, OperationId UUID. Solo inserciones.»; EntityId sin FK al maestro, ActorId/OperationId restrictivos e índices/operación sin duplicados.
- [X] T011 Implementar `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/MasterDataAuditWriter.cs`: «Acciones: Create, Update, Deactivate, Reactivate y Delete», «Before es null en Create y After es null en Delete», una evidencia por maestro/operación con snapshots y actor verificado; sin secretos ni evidencia nueva en replay.
- [X] T012 Generar `HU08AuditInfrastructure` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y adaptar `backend/src/Inventario.Api/Infrastructure/Persistence/DatabaseBootstrapper.cs` para inventory_runtime y «DENY UPDATE/DELETE sobre auditoría y resultados confirmados»; una cadena conservando Identity y sin login configurable embebido. Revisión B antes de integrar, sin aprobación ficticia.
- [X] T013 [P] Implementar `backend/src/Inventario.Api/Infrastructure/Http/MasterDataProblemMapper.cs` y conectar `backend/src/Inventario.Api/Infrastructure/Http/ApiProblems.cs`: ProblemDetails español, code/traceId/errors, códigos/estados del contrato, sin SQL ni confundir unicidad con DbUpdateConcurrencyException.
- [X] T014 [P] Crear `backend/src/Inventario.Api/Features/MasterData/MasterDataText.cs` y `backend/src/Inventario.Api/Features/MasterData/Queries/MasterDataQueryOptions.cs`: «Trim + ToUpperInvariant» Code/Sku, Name «Trim, no vacío, sin exigir unicidad», «Opcionales vacíos se normalizan a null»; q literal hasta 200, state active/inactive/all con active por defecto, page≥1, pageSize 25 máximo 100 y orden estable.
- [X] T015 [P] Implementar `backend/src/Inventario.Api/Domain/MasterData/IArticleUsageReader.cs`, `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleUsageReader.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleUsageCoverageValidator.cs`; cobertura de FK consumidoras, propiedad interna de StockPolicy y DependencyUnavailable si falta lector; vacío solo con ausencia de consumidores acreditada por el modelo, sin ocultar errores SQL.
- [X] T016 Registrar `backend/src/Inventario.Api/Features/MasterData/MasterDataServiceCollectionExtensions.cs`, `backend/src/Inventario.Api/Features/MasterData/MasterDataEndpoints.cs` y `backend/src/Inventario.Api/Program.cs` con /api/v1, sesión por defecto, antiforgery e ICurrentActor; solo rutas implementadas y dependencias por scope de intento.
- [X] T017 Adelantar el mínimo frontend común en `frontend/src/main.ts`, `frontend/src/app/app.routes.ts`, `frontend/src/app/core/session/session.service.ts`, `frontend/src/app/core/session/login-page.component.ts`, `frontend/src/app/core/session/permission.guard.ts` y `frontend/src/app/core/layout/app-layout.component.ts`; csrf/login/me/logout, token renovado, XSRF nativo, señales y permisos; revisión B pendiente, sin JWT/localStorage ni gestión de cuentas.
- [X] T018 [P] Implementar `frontend/src/app/core/http/api-problem.ts`, `frontend/src/app/shared/master-data-status.component.ts`, `frontend/src/app/shared/field-errors.component.ts` y `frontend/src/app/shared/query-state.component.ts` para carga/error/vacío, estados textuales y errores con foco; no presentar fallo como ausencia de coincidencias.
- [X] T019 Verificar `backend/tests/Inventario.IntegrationTests/Operations/OperationProtocolTests.cs` con SQL real: replay, actor/permiso, rollback, respuesta perdida, cabeceras, DENY, espera de applock y deadlock real con contexto nuevo por reintento; tablas técnicas no acreditan reglas de stock.
- [X] T020 Probar `frontend/src/app/core/session/session.service.spec.ts` y `frontend/src/app/core/session/permission.guard.spec.ts`: CSRF nativo/rotado, sesión frente a indisponibilidad, destinos y Administrador sin permiso de maestros.
- [X] T021 Implementar `frontend/src/app/core/http/master-data-mutations.service.ts` y `frontend/src/app/core/operations/pending-operation.store.ts`: intención inmutable, UUID estable, If-Match, comprobación por operación, persistencia/restauración temporal y logout sin payload; sin credenciales, tokens, reenvíos automáticos ni petición nueva con datos modificados bajo la misma clave.
- [X] T022 Registrar `backend/src/Inventario.Api/Infrastructure/Http/OperationEndpoints.cs` en `backend/src/Inventario.Api/Program.cs`: outcome reautorizado incluso sin recurso vivo, headers ETag/Location y 404 no definitivo, sin duplicar la implementación común.
- [X] T023 Registrar build/pruebas/identidad/roles/ejecutor reales en `specs/002-gestionar-datos-maestros/validation/foundation.md`; conservar límites de alcance y revisión pendiente, sin sustitutos permisivos ni aceptación ficticia de integración de stock.

**Checkpoint bloqueante**: el backend puede guardar maestro+auditoría+resultado con rollback
y replay; el frontend tiene sesión y transporte seguro. Las garantías no se posponen a US5.

## Phase 3: User Story 1 — Organizar familias (P1) — MVP

**Goal**: alta, consulta, edición, desactivación y reactivación de familias.
**Independent Test**: con gestor/lector y sin artículos ni movimientos, crear GENERAL,
buscarla, editar el nombre, desactivar/reactivar y volver a consultar; rechazar un código
duplicado por case/espacios o por familia inactiva. El caso con artículo asociado se añade
al integrar US2, sin convertirlo en prerequisito del primer incremento.

### Tests solicitados

- [X] T024 [P] [US1] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataFamilyTests.cs`: GET/POST/PUT, permisos, Location/ETag, mutation result, normalización/unicidad también en inactivos, estados, auditoría y resultado atómicos en SQL real.
- [X] T025 [P] [US1] Escribir `frontend/src/app/features/master-data/families/family-form.component.spec.ts`: alta incompleta, código duplicado sin perder campos, lector readonly y cancelación de la revisión de estado, sin depender de artículos/proveedores.

### Implementation

- [X] T026 [P] [US1] Crear `backend/src/Inventario.Api/Domain/MasterData/Family.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/FamilyConfiguration.cs` con «Id UUID generado en aplicación; Code nvarchar(64), Name nvarchar(200), IsActive bit, Version rowversion. Code y Name obligatorios.» y unicidad «Latin1_General_100_CI_AS» incluidos inactivos; normalizar, crear activa y conservar identidad.
- [X] T027 [US1] Generar `HU08Families` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y actualizar `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/InventoryDbContextModelSnapshot.cs`: solo Families con restricciones/índice/rowversion, sin recrear Articles ni introducir entidades futuras.
- [X] T028 [US1] Implementar `backend/src/Inventario.Api/Features/MasterData/Commands/FamilyCommands.cs` con ejecutor, bloqueo document:family, actor verificado, auditoría/resultado atómicos y precondición; sin DELETE/cascada a artículos y recurso compartido con las futuras asignaciones.
- [X] T029 [US1] Implementar `backend/src/Inventario.Api/Features/MasterData/Queries/FamilyQueries.cs`: Inventory.Read, filtros y orden estable, SNAPSHOT de conteo/items y detalle con ETag, incluidos inactivos.
- [X] T030 [US1] Registrar `backend/src/Inventario.Api/Features/MasterData/FamilyEndpoints.cs` desde `backend/src/Inventario.Api/Features/MasterData/MasterDataEndpoints.cs`: GET/POST/PUT, DTO/respuestas OpenAPI, permisos/CSRF, alta activa, PUT completo y rechazo de campos internos.
- [X] T031 [P] [US1] Crear `frontend/src/app/features/master-data/families/families-api.service.ts` y `frontend/src/app/features/master-data/families/family.models.ts`: DTO/Page, ETag y escrituras con transporte T021 y clave estable.
- [X] T032 [US1] Implementar `frontend/src/app/features/master-data/families/family-list.component.ts` y `frontend/src/app/features/master-data/families/family-form.component.ts`: PrimeNG/forms/signals, filtros, estados, teclado, revisión de estados y errores conservados; controles inicialmente deshabilitados hasta enlazado/carga para no perder interacción temprana.
- [X] T033 [US1] Integrar `frontend/src/app/features/master-data/master-data.routes.ts` y `frontend/src/app/core/layout/app-layout.component.ts` con carga lazy y permisos; /maestros/familias es el MVP y el destino de artículos se incorpora en US2.
- [X] T034 [US1] Ejecutar `frontend/e2e/master-data-families.spec.ts` en Chromium real con gestor/lector, estados, filtros y errores; validar SQL/unitarios y registrar Q-01/Q-02/Q-10 disponibles en `specs/002-gestionar-datos-maestros/validation/us1.md`, sin acreditar integración de artículos/historias todavía ausentes.

**Checkpoint MVP**: familias utilizables de extremo a extremo con garantías completas de
fundamentos. No exige proveedores, artículos de negocio ni movimientos de inventario.

## Phase 4: User Story 2 — Catálogo de artículos (P1)

**Goal**: ampliar Article H0, familia obligatoria, política inicial cero y definición protegida.
**Independent Test**: preparar una familia mediante US1/fixture, crear ART-01 UD entero/con
lote, buscar/editar nombre o familia y comprobar mínimo cero sin stock. Probar rechazo de
definición desde cada primera referencia real, incluso borrador retirado. La parte integrada
de referencia no puede acreditarse con un lector simulado.

### Tests solicitados

- [X] T036 [P] [US2] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataMigrationTests.cs` para H0 vacío y con UUID referenciados: expansión, preparación HTTP con sesión/permiso/CSRF/actor real, rechazo de ActorId en cuerpo y lote inválido, rollback/replay, lectura de FamilyId null, cierre, fallo sin familia y conservación de datos/Identity; verificar ruta ausente fuera de mantenimiento/esquema transitorio y ningún DDL desde API, sin inventar saldos.
- [X] T037 [P] [US2] Escribir `frontend/src/app/features/master-data/articles/article-form.component.spec.ts` para familia obligatoria, conservación de familia inactiva asignada, unidad visible, flags booleanos, valores de controles bloqueados incluidos en PUT y ausencia de stock/ubicación/mínimo editables en el alta.

### Implementation

- [X] T038 [US2] Ampliar la misma `backend/src/Inventario.Api/Domain/MasterData/Article.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ArticleConfiguration.cs` conservando «Id, Sku(64), Name(200), BaseUnit(32), AllowsFractional, TracksLot, IsActive y Version»; añadir «FamilyId UUID obligatorio al cerrar transición e IsDefinitionLocked bit inicialmente false», «false → true únicamente», FK a Family restrictiva y unidad normalizada; exponer comportamiento para primer uso y rechazar cambios de unidad/lotes/fracciones con marca o referencias, sin recrear identidades ni hacer editable la marca por HTTP.
- [X] T039 [P] [US2] Crear o completar la única `backend/src/Inventario.Api/Domain/Stock/StockPolicy.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/StockPolicyConfiguration.cs` con «PK/FK ArticleId con relación uno a uno; MinimumAvailable decimal(18,3) ≥ 0 y Version rowversion»; inicializar a cero con cada alta y no añadir configuración de mínimos/alertas de HU-06 a esta historia.
- [X] T040 [US2] Generar `HU08MasterDataExpand` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y actualizar `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` después de T038/T039 y de integrar la cadena: FamilyId temporalmente nullable, IsDefinitionLocked false y StockPolicy si no existe; conservar PK/índices/UUID y no crear todavía Suppliers ni reescribir migraciones aplicadas.
- [X] T041 [US2] Completar `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleUsageReader.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleUsageCoverageValidator.cs` con las contribuciones reales presentes antes de preparar datos H0; distinguir historial/referencias actuales y propiedad StockPolicy, dar lecturas coherentes y rechazar dependencia ausente, nunca deducir «sin uso» del saldo ni de un lector ficticio.
- [X] T042 [US2] Implementar `backend/src/Inventario.Api/Features/MasterData/Maintenance/PrepareHu08FamilyMapHandler.cs`, `backend/src/Inventario.Api/Features/MasterData/Maintenance/Hu08MaintenanceEndpoints.cs` y `backend/src/Inventario.Api/Features/MasterData/Maintenance/Hu08FamilyPreparationSnapshot.cs`: POST /maintenance/hu08/family-map solo con modo/esquema transitorio, sesión MasterData.Manage/CSRF e Idempotency-Key; entries de 1 a 100 con articleId único, sin ActorId; ICurrentActor, lectores T041 y ejecutor comunes, DTO FamilyId nullable y conexión sin DDL, lote atómico de asignaciones/políticas/auditoría/resultado con replay. Actualizar `backend/src/Inventario.Api/Program.cs` para no registrar escritores ordinarios en ese modo; documentar expansión→handler→cierre en `specs/002-gestionar-datos-maestros/quickstart.md` sin script de autoría manual.
- [X] T043 [US2] Generar `HU08RequireArticleFamily` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y actualizar `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` con precomprobación que falla si falta asociación y cierre «NOT NULL/FK restrictiva»; parar mantenimiento después de T042 y aplicar DDL externamente, sin ejecutar ambas migraciones a ciegas antes del mapa ni introducir datos de negocio en base vacía.
- [X] T044 [US2] Adaptar `backend/tests/Inventario.IntegrationTests/Fixtures/MasterDataTestData.cs`, `backend/tests/Inventario.IntegrationTests/H0ReferenceTests.cs` e `backend/tests/Inventario.IntegrationTests/IdentityFixture.cs` al modelo final, preparando familia y política antes de artículos; conservar las garantías H0 y las migraciones históricas sin mantener una vía productiva de Article incompleta.
- [X] T045 [US2] Integrar `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleReferenceTracker.cs`, `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleDefinitionReadiness.cs` y `backend/tests/Inventario.IntegrationTests/MasterDataFirstReferenceTests.cs`: antes de habilitar edición de definición, cada consumidor presente tiene lector y enlace probado de primer uso en su escritor real, bajo el bloqueo/transacción de Article. Exigir pruebas de crear→retirar referencia→rechazar edición y ambos órdenes referencia/edición cuando exista ese consumidor; si falta integración devolver DependencyUnavailable. Sin consumidores acreditar ausencia y permitir H1; registrar el gate en `specs/002-gestionar-datos-maestros/contracts/integration.md`, dejando los consumidores futuros para sus tareas cruzadas antes de habilitar sus escritores.
- [X] T046 [US2] Implementar `backend/src/Inventario.Api/Features/MasterData/Commands/ArticleCommands.cs` y el comportamiento inicial de `backend/src/Inventario.Api/Features/MasterData/Commands/ArticleLifecycleCoordinator.cs`: alta Article+StockPolicy+auditoría+resultado atómicos, PUT completo, bloqueos familia destino→artículo, asignación nueva solo activa y conservación de familia inactiva actual; usar T041/T045 y rechazar cambios de definición con gate incompleto sin omitir las demás reglas de PUT, revalidar estado y no escribir cantidades ni callbacks vacíos.
- [X] T047 [US2] Implementar `backend/src/Inventario.Api/Features/MasterData/Queries/ArticleQueries.cs` con q/state/familyId, orden estable y detalle con familia/unidad/flags/ETag y «canEditDefinition, definitionLockReason, canDelete, deleteBlockReason»; motivos null/FirstReference y null/History/References, SNAPSHOT y 503 si no pueden acreditarse capacidades con T041/T045, sin anunciar edición de definición que el gate impide.
- [X] T048 [US2] Crear `backend/src/Inventario.Api/Features/MasterData/ArticleEndpoints.cs` y registrarlo en `backend/src/Inventario.Api/Features/MasterData/MasterDataEndpoints.cs` para GET/POST/PUT con schemas/respuestas/cabeceras/permisos del contrato; requiere esquema final y T041/T045 antes de habilitar cambios de definición. No aceptar ActorId/RecordedAt/Version/OperationId/IsDefinitionLocked en cuerpos; las features futuras no habilitan sus escritores sin el mismo gate.
- [X] T049 [P] [US2] Crear `frontend/src/app/features/master-data/articles/articles-api.service.ts` y `frontend/src/app/features/master-data/articles/article.models.ts` con familia y capacidades tipadas, ETag opaco y envío de flags/isActive explícitos; usar T021 y conservar valores de definición cuando solo se cambien otros campos.
- [X] T050 [US2] Implementar `frontend/src/app/features/master-data/articles/article-list.component.ts` y `frontend/src/app/features/master-data/articles/article-form.component.ts`, integrar `frontend/src/app/features/master-data/master-data.routes.ts` con destino final /maestros→articulos, familia activa/actual inactiva, filtros y explicación textual de definición bloqueada; no pedir ubicación, cantidad inicial o mínimo.
- [X] T051 [US2] Crear y ejecutar `frontend/e2e/master-data-articles.spec.ts` y T035–T037 con datos aislados y login real; cubrir alta/consulta/filtros/edición/errores y guardar Q-02/Q-05/Q-11 disponibles en `specs/002-gestionar-datos-maestros/validation/us2.md`, distinguiendo primer uso integrado de pruebas parciales H1.
- [X] T052 [US2] Añadir a `backend/tests/Inventario.IntegrationTests/MasterDataFamilyTests.cs` la asociación conservada tras desactivar familia y a `backend/tests/Inventario.IntegrationTests/MasterDataConcurrencyTests.cs` ambos órdenes de nueva asignación frente a desactivación; ejecutar SQL real después de T046 y comprobar que no se desactiva Article en cascada.

**Evidencia US2, 2026-11-17**: [us2.md](validation/us2.md); 64 backend, 22 unitarias
frontend y 7 E2E aprobadas. T036–T052 verificadas para el corte H1. T035 tiene sus casos
HTTP/H1 ejecutados, pero permanece pendiente hasta demostrar US2.8 mediante un escritor
real que cree y retire la referencia; la marca histórica preparada no lo sustituye.
Las carreras de T052 incluyen alta y reasignación, ambos órdenes y espera SQL real.

**Checkpoint**: artículos utilizables con familia, mínimo cero y sin inventar stock; actualización
H0 validada. T045 es un prerequisito de habilitación para los consumidores presentes, no
una comprobación aplazada al final. Los futuros se acreditan en T075/T076 antes de habilitarse.

## Phase 5: User Story 3 — Proveedores utilizables en compras (P1)

**Goal**: mantener proveedor, contacto y estado sin desarrollar pedidos ni correo.
**Independent Test**: crear PROV-01, consultar y modificar contacto, guardar sin correo con
advertencia y rechazar correo inválido/código duplicado; desactivar/reactivar sin compras.
La conservación de documentos se comprueba posteriormente con HU-07 mediante T076.

### Tests solicitados

- [X] T053 [P] [US3] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataSupplierTests.cs` para GET/POST/PUT /suppliers: permisos, contrato, opcionales null, correo inválido, códigos ocupados incluidos inactivos, estados, ETag y auditoría; usar SQL real sin enviar pedidos ni correo.
- [X] T054 [P] [US3] Escribir `frontend/src/app/features/master-data/suppliers/supplier-form.component.spec.ts` para errores de contacto/código, campos válidos conservados, correo ausente permitido con aviso, readonly por permisos y revisión de estado.

### Implementation

- [X] T055 [P] [US3] Crear `backend/src/Inventario.Api/Domain/MasterData/Supplier.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/SupplierConfiguration.cs` con «Id UUID; Code nvarchar(64), Name nvarchar(200), Email nvarchar(256) nullable, TaxIdentifier nvarchar(64) nullable, IsActive bit, Version rowversion. Code y Name obligatorios; correo no vacío válido.»; aplicar T014, contacto con Trim sin unicidad y opcionales vacíos a null, sin validar un formato fiscal nacional no solicitado.
- [X] T056 [US3] Generar `HU08Suppliers` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y actualizar `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` sobre la cadena integrada, con restricciones/índice único/rowversion de Supplier; conservar las migraciones compartidas y no generarla simultáneamente con US1/US2.
- [X] T057 [US3] Implementar `backend/src/Inventario.Api/Features/MasterData/Commands/SupplierCommands.cs` con ejecutor, bloqueo documental de proveedor, auditoría/resultado y estado; no reescribir instantáneas, borrar ni cerrar documentos, y no añadir DELETE de proveedor.
- [X] T058 [US3] Implementar `backend/src/Inventario.Api/Features/MasterData/Queries/SupplierQueries.cs` con filtros/paginación consistentes, detalle/contacto y ETag; devolver ausencia de correo como dato válido y no afirmar que un correo fue enviado.
- [X] T059 [US3] Crear `backend/src/Inventario.Api/Features/MasterData/SupplierEndpoints.cs` y registrarlo en `backend/src/Inventario.Api/Features/MasterData/MasterDataEndpoints.cs` con schemas y errores contractuales, POST activo por defecto/PUT completo, permisos separados y cabeceras de concurrencia/idempotencia.
- [X] T060 [P] [US3] Crear `frontend/src/app/features/master-data/suppliers/suppliers-api.service.ts` y `frontend/src/app/features/master-data/suppliers/supplier.models.ts` con opcionales null, Page, ETag y delegación al transporte de mutaciones; no convertir correo/identificador fiscal en claves obligatorias.
- [X] T061 [US3] Implementar `frontend/src/app/features/master-data/suppliers/supplier-list.component.ts` y `frontend/src/app/features/master-data/suppliers/supplier-form.component.ts` con filtros, contactos, aviso de correo requerido para futuros envíos, teclado y confirmación de estado; conservar errores válidos y no crear flujo de compras.
- [X] T062 [US3] Integrar las rutas lazy y navegación de proveedores en `frontend/src/app/features/master-data/master-data.routes.ts` y `frontend/src/app/core/layout/app-layout.component.ts`, respetando Inventory.Read/MasterData.Manage y los incrementos ya integrados.
- [X] T063 [US3] Crear y ejecutar `frontend/e2e/master-data-suppliers.spec.ts` y T053/T054 con sesión real; guardar evidencia de consulta/edición/estados/validaciones Q-02/Q-10 en `specs/002-gestionar-datos-maestros/validation/us3.md`, sin presentar como probada la conservación de pedidos todavía ausentes.

**Checkpoint**: proveedor independiente de compras; su uso y snapshots históricos se completan
con el consumidor HU-07 sin ampliar HU-08 a envío de correo o gestión de pedidos.

## Phase 6: User Story 4 — Retirar artículos sin perder trazabilidad (P1)

**Goal**: distinguir borrado legítimo, desactivación y consulta de historial sin pérdida de evidencia.
**Independent Test**: usar un artículo sin referencias y otro con movimiento generado por el
núcleo real: el primero se elimina con confirmación y conserva auditoría/resultado; el segundo
solo se desactiva y conserva historia/unidades/ubicaciones. Consultar también siendo inactivo.

### Tests solicitados

- [X] T065 [P] [US4] Escribir `frontend/src/app/features/master-data/articles/article-lifecycle.component.spec.ts` y `frontend/src/app/features/master-data/articles/article-history.component.spec.ts` para confirmación con identidad/consecuencia, rechazo con alternativa de desactivar, historial de inactivo y distinción entre página vacía real y DependencyUnavailable.

### Implementation

- [X] T067 [US4] Implementar `backend/src/Inventario.Api/Features/MasterData/Commands/DeleteArticleHandler.cs` con replay antes de existencia/If-Match, bloqueo de artículo y referencias reales, 409 HistoryPreventsDeletion o eliminación atómica de Article/política propia+auditoría Delete+resultado 204; no borrar documentos/diario y conservar evidencia sin FK que impida el borrado.
- [X] T068 [US4] Completar `backend/src/Inventario.Api/Features/MasterData/Commands/ArticleLifecycleCoordinator.cs` y `backend/src/Inventario.Api/Features/MasterData/Commands/ArticleCommands.cs` con transición «activo ↔ inactivo, con auditoría y permisos» y sin cambios de cantidades; invocar el evaluador real de HU-06 en la misma transacción cuando esté integrado, o rechazar la dependencia ausente, sin cerrar compromisos existentes.
- [X] T069 [US4] Conectar `ArticleMovementQueries` a `backend/src/Inventario.Api/Features/Stock/Core/StockArticleMovementReader.cs` sobre StockMovement/StockEntry comunes: from/to ISO con zona e intervalo [from,to), actorId/locationId/lotId, página y orden recordedAt descendente/Id; cantidades como cadenas decimales y unidad histórica, actor/motivo/documento/soporte/contraparte/origen/destino/lote, sin filtrar por IsActive y sin historial alternativo.
- [X] T071 [US4] Ampliar `backend/src/Inventario.Api/Features/MasterData/ArticleEndpoints.cs` con DELETE /articles/{id} y GET /articles/{id}/movements, permisos/ETag/idempotencia y respuestas: 404 artículo ausente, página vacía solo con proveedor real y 503 DependencyUnavailable cuando falte diario/lector, preservando el comportamiento de PUT estado.
- [X] T072 [P] [US4] Añadir DTO y consulta de movimientos en `frontend/src/app/features/master-data/articles/article-history-api.service.ts` y `frontend/src/app/features/master-data/articles/article-history.models.ts`; conservar decimales exactos como cadenas y distinguir sus tres estados: datos/vacío confirmado/dependencia ausente.
- [X] T073 [US4] Implementar acciones y revisión en `frontend/src/app/features/master-data/articles/article-lifecycle.component.ts` e integrarlas en `frontend/src/app/features/master-data/articles/article-form.component.ts`: código/nombre, consecuencia, permiso+capacidad, rechazo explicativo, opción de desactivar y confirmación solo tras persistencia.
- [X] T074 [US4] Implementar `frontend/src/app/features/master-data/articles/article-history.component.ts` e integrar /maestros/articulos/:id/movimientos en `frontend/src/app/features/master-data/master-data.routes.ts`; mantener acceso desde inactivos, filtros y vocabulario del almacén, y mostrar integración pendiente sin fingir historial vacío.

**Checkpoint**: el corte H1 puede demostrar borrado sin uso y dependencia no disponible.
La aceptación completa de US4 exige evidencia real de T075/T076/T078, no solo ese corte.

## Phase 7: User Story 5 — Cambios autorizados y resultado fiable (P2)

**Goal**: completar el recorrido transversal de permisos, conflictos y operaciones inciertas.
**Independent Test**: sobre una familia ya disponible, usar lector/gestor y dos sesiones;
rechazar escritura no autorizada, explicar 412 conservando edición y recuperar una intención
con respuesta perdida sin duplicados. Extender después la misma evidencia a Article/Supplier.

### Tests solicitados

- [X] T079 [P] [US5] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataReliabilityTests.cs` para todos los catálogos: 401/403/CSRF, cambio de actor/contenido/ruta con misma clave, replay tras ETag obsoleto y borrado, fallos antes del commit y respuesta perdida después; las sondas de fallo viven solo en el fixture, no en un endpoint productivo.
- [X] T080 [P] [US5] Escribir `frontend/src/app/core/operations/pending-operation.store.spec.ts` y `frontend/src/app/core/http/master-data-mutations.service.spec.ts` para doble clic, clave estable, payload/ETag originales, timeout→consulta→replay, 404 no definitivo, recarga, expiración/cambio de identidad y eliminación del payload al logout, sin persistir credenciales/tokens.
- [X] T081 [P] [US5] Escribir `frontend/e2e/master-data-reliability.spec.ts` con dos contextos autenticados, conflicto real, formulario válido preservado, respuesta perdida y recuperación después de recarga; evitar simular permiso únicamente ocultando botones o cambiar la intención pendiente.

### Implementation e integración del recorrido

- [X] T082 [US5] Completar el tratamiento transversal en `backend/src/Inventario.Api/Infrastructure/Http/MasterDataProblemMapper.cs`, `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/InventoryOperationExecutor.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/OperationResponse.cs` conforme a T079: reautorización, ausencia de resultados de éxito parciales, replay sin mutación y excepciones SQL/concurrencia sin 500 indebido, reutilizando la base en lugar de crear otro protocolo.
- [X] T083 [US5] Crear `frontend/src/app/core/operations/operation-status.component.ts` y completar `frontend/src/app/core/operations/pending-operation.store.ts` con estado «Resultado pendiente de comprobar», consulta/reintento explícitos, restauración de intención original y eliminación al resolverla; un formulario nuevo o vacío nunca reutiliza una clave pendiente.
- [X] T084 [US5] Crear `frontend/src/app/shared/version-conflict.component.ts` e integrarlo en `frontend/src/app/features/master-data/families/family-form.component.ts`, `frontend/src/app/features/master-data/articles/article-form.component.ts` y `frontend/src/app/features/master-data/suppliers/supplier-form.component.ts`: preservar borrador local ante 412, cargar estado vigente y revisar sin fusión/sobrescritura automática; solo una nueva intención resuelta puede usar nueva clave.
- [X] T085 [US5] Completar `frontend/src/app/core/session/session.service.ts` y `frontend/src/app/core/http/master-data-mutations.service.ts` para 401/403/422/409/503 y Retry-After, reautenticación sin reenvío automático, separación de intención por actor y limpieza de formularios/payload al logout conservando únicamente la referencia aún pendiente para consulta.
- [X] T086 [US5] Verificar en `backend/tests/Inventario.IntegrationTests/MasterDataOperationAuthorizationTests.cs` que GET /operations usa RequiredPermission actual incluso tras borrar recurso, sin filtrar información a una cuenta sin permiso; comprobar outcome/cabeceras, 404 no observado y que un replay autorizado precede a comprobar existencia y versión.
- [X] T087 [US5] Integrar `frontend/src/app/core/operations/operation-status.component.ts` en los tres formularios de `frontend/src/app/features/master-data/families/family-form.component.ts`, `frontend/src/app/features/master-data/articles/article-form.component.ts` y `frontend/src/app/features/master-data/suppliers/supplier-form.component.ts`, con foco/mensajes y bloqueo de doble envío; diferenciar fallo validable de resultado incierto y mostrar éxito solo tras confirmación conservada.
- [X] T088 [US5] Ejecutar T079–T081/T086 y las regresiones justificadas de las historias afectadas; documentar Q-01/Q-03/Q-04/Q-06/Q-10 con conteos SQL de maestros/auditoría/resultados en `specs/002-gestionar-datos-maestros/validation/us5.md`, verificando 0 duplicados, 0 cambios parciales y 0 sobrescrituras silenciosas en los escenarios definidos.

**Checkpoint**: el recorrido de fiabilidad queda acreditado sobre maestros reales. Las
garantías básicas ya debían estar activas desde Phase 2 y los primeros incrementos.

## Phase 8: Polish & Cross-Cutting Concerns — Evidencia de cierre

**Purpose**: validar contratos finales, rendimiento, usabilidad, recuperación e integración;
no añadir nuevas funcionalidades de negocio ni completar historias de B por sustitución.

- [X] T089 [P] Crear y ejecutar `backend/tests/Inventario.IntegrationTests/MasterDataOpenApiTests.cs` para rutas/schemas/cabeceras/errores de maestros y operaciones realmente expuestas en Development, ausencia de borrados de familias/proveedores y decimales históricos como cadenas; no documentar endpoints inexistentes como implementados.
- [X] T090 [P] Implementar y ejecutar `frontend/e2e/master-data-performance.spec.ts` con fixture de 10.000 artículos auditados, 25 sesiones concurrentes y al menos 200 búsquedas por código/nombre/familia/estado; registrar p95, errores y entorno en `specs/002-gestionar-datos-maestros/validation/performance.md` y exigir SC-005: al menos 95 % en ≤2 s, sin ocultar muestras fallidas ni preparar stock por DML directo.
- [X] T093 Actualizar `README.md`, `specs/002-gestionar-datos-maestros/quickstart.md` y `specs/002-gestionar-datos-maestros/contracts/integration.md` con comandos comprobados, procedimiento de transición real, provisionamiento aislado E2E, componentes/rutas efectivamente integrados y limitaciones pendientes; no presentar pruebas escritas como ejecutadas.
- [X] T094 Ejecutar build backend/frontend, tests SQL, Vitest, Playwright y `dotnet ef migrations has-pending-model-changes` según `specs/002-gestionar-datos-maestros/quickstart.md`; registrar una ejecución final coherente con el mismo estado de código en `specs/002-gestionar-datos-maestros/validation/acceptance.md`, resolver fallos y repetir solo los checks que queden afectados por nuevas correcciones.

## Estado de continuación, 2026-11-18

76 pruebas SQL, 36 unitarias frontend, 11 E2E funcionales y SC-005 aprobadas.
SC-005 final: p95 1,677 s, 193/200 en ≤2 s, cero errores; primer fallo instrumentado
conservado en [rendimiento](validation/performance.md). Proveedores
T053–T063 y fiabilidad T079–T088 verificadas para maestros H1; detalle en
[US3](validation/us3.md), [US4](validation/us4.md), [US5](validation/us5.md) y
[aceptación técnica](validation/acceptance.md). La integración de diario/referencias,
las carreras con escritores reales, validación humana, restauración y revisión A+B
siguen pendientes. T064/T069/T070/T077 mantienen sus partes integradas abiertas;
la ejecución H1 no las convierte en historias completas.

## Estado de continuación, 2026-11-19

T069 implementada y verificada en `work/hu08-stock-history`, sobre la base
de la PR #4 fusionada y el stock de la PR #3 en revisión. La
[verificación de US4](validation/us4.md#validación-con-el-stock-h0-2026-11-19)
amplía la evidencia de T035, T064, T066, T070, T075, T077 y T078 con stock real.
El cierre de esas tareas requiere los escritores y escenarios restantes.

## Cierre de la entrega académica, 2026-11-19

El usuario aprobó la entrega académica de HU-08. Las tareas cerradas por traslado
o exclusión conservan sus criterios debajo, sin marca de prueba superada.
La [aceptación](validation/acceptance.md#cierre-de-la-entrega-académica-2026-11-19)
recoge los resultados y el alcance.

### Trasladadas con los consumidores

Se cierra la parte pendiente en HU-08. Cada módulo asumirá sus pruebas al integrar
sus escritores reales. HU-01 y HU-07 cubren recepciones y compras. HU-03, HU-04 y
HU-05 cubren salidas, conteos y reservas. HU-06 cubre la reacción de alertas.

- **T035** [P] [US2] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataArticleTests.cs` para alta/detalle/PUT/listas de /articles, familia activa/inactiva actual, política inicial cero, ausencia de saldos/movimientos y capacidades; US2.7 permite definición solo antes del primer uso y US2.8 la mantiene bloqueada tras retirar la referencia; comprobar auditoría/atomicidad y errores 400/409/412/422/503 con SQL real.
- **T064** [P] [US4] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataLifecycleTests.cs` para DELETE legítimo, rechazo por cada tipo de referencia/historial, estado y GET de movimientos de inactivo; comprobar eliminación de política propia y conservación de auditoría/OperationResult, sin tomar IsDefinitionLocked sola como prohibición de borrado.
- **T066** [P] [US4] Escribir `backend/tests/Inventario.IntegrationTests/MasterDataLifecycleConcurrencyTests.cs` con conexiones/barreras independientes para referencia/borrado y operación nueva/desactivación, ambos órdenes de commit y definición con borrador retirado; usar los escritores integrados, no llamadas secuenciales ni saldos insertados manualmente.
- **T070** [US4] Completar borrado/capacidades en `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleUsageReader.cs` y `backend/src/Inventario.Api/Features/MasterData/Queries/ArticleQueries.cs`: diferenciar historial, referencias y propiedades internas; coordinar limpieza segura de dimensiones vacías si B las crea antes del primer uso, sin convertirlas en prohibición permanente ni tocar dimensiones utilizadas.
- **T075** [US4] Integrar y verificar contribuciones reales de stock/reservas/salidas/conteos en `backend/src/Inventario.Api/Infrastructure/Persistence/ArticleUsageReader.cs` y su cobertura en `backend/tests/Inventario.IntegrationTests/MasterDataLifecycleConcurrencyTests.cs`; cada consumidor nuevo satisface T045 con lector/enlace de primer uso/pruebas antes de habilitar sus escritores. Mantener pendiente lo que aún no exista, sin sustituir sus historias por mocks ni implementar HU-03/HU-04/HU-05 completas aquí.
- **T076** [US4] Completar las pruebas de referencias y snapshots de compras/recepciones en `backend/tests/Inventario.IntegrationTests/MasterDataDocumentIntegrationTests.cs` y registrar conexiones en `specs/002-gestionar-datos-maestros/contracts/integration.md`: proveedor modificado/inactivo conserva pedidos, artículo referenciado no se borra/cambia definición y escritores reales satisfacen lector/enlace/pruebas de T045 antes de habilitarse. Requiere HU-07/HU-01 reales y permanece pendiente hasta su entrega.
- **T077** [US4] Crear y ejecutar `frontend/e2e/master-data-lifecycle.spec.ts` y T064–T066 para borrado/desactivación/reactivación/historial; comprobar evidencia, referencias y cantidades antes/después y guardar resultados disponibles Q-05/Q-07/Q-08/Q-09 en `specs/002-gestionar-datos-maestros/validation/us4.md`, sin certificar casos integrados por usar dobles.
- **T078** [US4] Completar `backend/tests/Inventario.IntegrationTests/MasterDataInactiveArticleIntegrationTests.cs` con artículo inactivo: rechazar pedidos/reservas/recepciones nuevos y permitir consulta, liberar reservas, servir compromisos existentes y ajustes autorizados según contratos comunes; verificar reacción de HU-06 cuando esté integrada y registrar Q-09/V-12 en `specs/002-gestionar-datos-maestros/validation/us4.md`.

### Fuera del alcance académico

T091 se excluye porque el proyecto de clase carece de tres gestores representativos.
T092 tiene evidencia del ensayo técnico; el ejercicio operativo queda para una
puesta en servicio. T095 conserva la revisión de las PR técnicas. T096 recoge la
aprobación académica; la aceptación operativa corresponde a los responsables.

- **T091** [P] Realizar las tareas SC-006 con tres gestores representativos —cada uno alta de familia/artículo/proveedor, corrección de campo y desactivación— y guardar participantes por rol, tareas, resultados, tiempos, incidencias y correcciones en `specs/002-gestionar-datos-maestros/validation/usability.md`; mantener pendiente si no existe evidencia humana real, sin sustituirla por Playwright.
- **T092** [P] Participar en la restauración conjunta V-14 y registrar conservación de maestros, referencias, auditoría y resultados de operación en `specs/002-gestionar-datos-maestros/validation/recovery.md`, siguiendo `.specify/memory/project/quickstart.md` y los objetivos aprobados por los responsables; documentar esta condición antes de producción sin inventar aprobación RPO/RTO ni otra política local.
- **T095** Revisar con B la cadena y el snapshot en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/InventoryDbContextModelSnapshot.cs`, las conexiones reales en `specs/002-gestionar-datos-maestros/contracts/integration.md` y el registro de coordinación en `specs/002-gestionar-datos-maestros/validation/coordination.md`; conservar migraciones aplicadas, registrar la revisión recibida y mantener pendiente el cierre compartido mientras falte.
- **T096** Completar la matriz FR/SC/Q, revisión constitucional y resultados por historia en `specs/002-gestionar-datos-maestros/validation/acceptance.md`; distinguir corte H1 demostrable de aceptación integrada, no marcar terminada HU-08 con T045/T075/T076/T078 u otra obligación requerida pendiente, y obtener la revisión de responsables prevista en la constitución antes de aceptar la entrega.

## Dependencies & Execution Order

### Phase dependencies y gates reales

1. Setup T001–T005 establece entorno e inventario de entregas.
2. Fundamentos T006–T023 bloquea mutaciones e incrementos completos y ya está verificada
   como aportación común de A autorizada por el usuario. No se creó una variante privada;
   B consume esta base y conserva su revisión pendiente antes de integrar StockCore.
3. US1 puede completarse sola tras fundamentos. US2 necesita Family de US1; US3 no depende
   del negocio de US1/US2, aunque sus migraciones/registro de rutas se serializan.
4. US4 necesita Article de US2. Su aceptación integrada depende además de las entregas reales
   indicadas en T075/T076/T078; empezar H1 no equivale a que esas entregas ya existan.
5. US5 puede probar su caso independiente sobre familias tras US1; su fase valida el conjunto
   después de US2/US3/US4. No es prerequisito circular de los mecanismos de fundamentos.
6. Polish cierra las historias deseadas y los gates humanos/integrados. La recuperación es
   condición de producción; su ausencia se declara, no se convierte en una prueba aprobada.

```text
Setup → Fundamentos → US1 (familias/MVP) → US2 (artículos) → US4 (retirada/historial)
                    └────────────────→ US3 (proveedores)
US1 → US5 sobre familias; US1 + US2 + US3 + US4 → validación completa US5
H0 B → ejecutor/resultados comunes
Consumidores A/B + HU-06 cuando integrada → pruebas de uso/estados/historial de US2/US4
Historias completas → contratos + rendimiento + usabilidad + revisión → aceptación
```

### Prerrequisitos internos de tareas

- Setup: T002 y T003 son independientes tras T001; T004 requiere T002/T003; T005 requiere T002–T004.
- Fundamentos: T006→T007→T008→T009; T010 puede prepararse al integrar T006, T011 requiere
  T010/T007, T012 requiere T006/T010/T011 y cadena revisada. T013/T014/T015/T018 son
  trabajo disjunto con contratos estables. T016 integra T006–T015; T017 integra sesión
  y base UI; T019/T020 verifican sus dependencias, T021 consume T017/T009/T008, T022
  consume resultado/permisos y T023 exige que todos los anteriores estén completos.
- US1: T024/T025/T026/T031 pueden prepararse al superar fundamentos; T027 requiere T026;
  T028→T029→T030 integra modelo/infraestructura; T032 requiere T031 y contrato conocido;
  T033 integra pantallas; T034 ejecuta el incremento completo.
- US2: T035/T036/T037 independientes tras fundamentos+US1; T038/T039 antes de T040;
  T041 completa lectores antes de T042; T042 prepara los datos con actor real antes de T043
  en bases con datos; T044 adapta fixtures después del cierre. T045 es el gate de lectores,
  enlaces de primer uso y pruebas de los consumidores presentes, anterior a T046/T047/T048.
  T049 puede prepararse por contrato y T050 integra el cliente. T051/T052 verifican el corte;
  los consumidores futuros completan su gate en T075/T076 antes de habilitar sus escritores.
- US3: T053/T054/T055/T060 pueden prepararse con fundamentos; T056 requiere T055 y turno de
  migración; T057/T058→T059; T061 requiere T060; T062/T063 integran y verifican.
- US4: T064/T065/T066 se preparan con US2; T067/T068 reutilizan lector/ejecutor; T069
  requiere proveedor de diario real o error explícito, T070 capacidades y T071 endpoints.
  T072 independiente por contrato; T073/T074 integran pantallas. T075/T076/T078 requieren
  consumidores reales; T077 no acredita esos casos si siguen pendientes.
- US5: T079/T080/T081 se pueden preparar en archivos distintos con maestros disponibles;
  T082 ajusta backend, T083/T084/T085 tienen cambios compartidos que se serializan;
  T086 verifica resultados, T087 integra formularios y T088 ejecuta la evidencia final.
- Final: T089/T090/T091/T092 son actividades independientes una vez habilitadas sus
  dependencias y entornos; T093 recoge sus resultados, T094 verifica el estado final,
  T095 registra revisión y T096 distingue aprobación completa de un cierre parcial.

### Restricciones de paralelismo

- Nunca generar simultáneamente T012/T027/T040/T042/T056 ni editar el snapshot en dos ramas
  sin regenerar la migración local aún no compartida sobre la cadena integrada.
- Generar cada migración sobre el modelo de su incremento y el último snapshot integrado,
  sin configuraciones no migradas de otra historia en el mismo árbol: el contexto descubre
  todas las configuraciones del ensamblado. La preparación de modelos en paralelo requiere
  ramas/worktrees separados o integración secuencial antes de autogenerar el esquema.
- Serializar `MasterDataEndpoints.cs`, `Program.cs`, `master-data.routes.ts`, layout,
  fixtures y los archivos de Article/lector/ejecutor tocados por varias fases.
- Un `[P]` no permite ejecutar una prueba mientras otra tarea cambia la misma base o su
  código dependiente. La escritura de tests separados puede ser paralela; su ejecución
  usa fixtures aislados y se realiza sobre un incremento estable.
- Rendimiento y validación humana solo se realizan en paralelo con entornos/datasets separados;
  sobre la misma aplicación se serializan para no falsear las medidas de SC-005.
- B conserva sus cuatro historias. Estas tareas le señalan interfaces y pruebas cruzadas,
  no lo reasignan a implementar las pantallas de HU-08 ni autorizan a A a sustituir su núcleo.

## Parallel Examples — Por historia

| Historia | Ronda habilitada | Trabajo paralelo seguro |
|----------|------------------|-------------------------|
| US1 | Fundamentos listos | T024 contrato SQL, T025 formulario, T026 Family y T031 cliente tipado; después se integra migración/handlers/pantallas. |
| US2 | US1 y fundamentos listos | T035 contrato SQL, T036 migración y T037 formulario son archivos distintos; preparar T049 contra contrato mientras se integra backend, sin generar otra migración. |
| US3 | Fundamentos listos | T053 SQL, T054 formulario, T055 Supplier y T060 cliente pueden prepararse sin artículos; T056 espera su turno de snapshot. |
| US4 | US2 disponible | T064 lifecycle SQL, T065 interfaz, T066 carreras y T072 cliente de historial; no ejecutar carreras hasta disponer de escritores reales. |
| US5 | Al menos US1 disponible | T079 backend, T080 store/transporte y T081 E2E se escriben por separado; completar la validación cruzada al integrar todos los catálogos. |

## Implementation Strategy

### MVP primero

Completar Setup + Fundamentos + US1 y demostrar familias con cuenta gestora/lectora,
errores, auditoría, versión y replay. Esta entrega aporta valor sin artículos ni stock.
No aplazar identidad, atomicidad o idempotencia con el argumento de que US5 es P2.

### Entrega incremental y coordinación

Continuar US2 y US3 en paralelo donde los archivos lo permitan, con una sola persona
generando cada migración. Ampliar US4 al disponer de sus consumidores y US5 para el recorrido
transversal. Registrar resultados parciales y mantener pendientes los gates integrados.
No hacer commits, pushes o PRs automáticamente como consecuencia de completar estas tareas;
seguir la solicitud explícita del usuario y el flujo de revisión del proyecto.

### Cobertura de requisitos y aceptación

| Requisitos/criterios | Tareas principales |
|---------------------|--------------------|
| FR-001 | T024–T034. |
| FR-002 | T035/T038/T040–T051. |
| FR-003 | T028/T038/T046/T050/T052. |
| FR-004 | T053–T063/T076. |
| FR-005 | T014/T024/T026/T035/T038/T053/T055. |
| FR-006 | T014/T029/T032/T047/T050/T058/T061/T074. |
| FR-007 | T016/T017/T020/T024/T035/T053/T079/T085/T086. |
| FR-008 | T039/T042/T046/T051. |
| FR-009 | T035/T038/T041/T045/T046/T048/T066/T075/T076. |
| FR-010 | T064/T066/T067/T070/T075/T076. |
| FR-011 | T010/T011/T064/T067/T070/T077. |
| FR-012 | T028/T032/T046/T057/T061/T068/T073/T077. |
| FR-013 | T045/T068/T075/T076/T078. |
| FR-014 | T064/T069/T071/T072/T074/T077. |
| FR-015 | T010–T012/T028/T036/T042/T046/T057/T067/T079/T088. |
| FR-016 | T007/T011/T019/T035/T079/T082/T088. |
| FR-017 | T009/T024/T035/T053/T079/T081/T084. |
| FR-018 | T006–T008/T019/T021/T022/T079–T088. |
| FR-019 | T007/T015/T028/T041/T045/T046/T048/T052/T066–T070/T075/T076/T078. |
| FR-020 | T017/T018/T025/T032/T037/T050/T054/T061/T065/T073/T074/T084/T087/T091. |
| FR-021 | T021/T032/T050/T061/T073/T079–T088. |
| SC-001/SC-002 | T034/T051/T063 y sus suites SQL/formularios. |
| SC-003 | T064/T067/T070/T075–T078. |
| SC-004 | T019/T052/T066/T079–T088. |
| SC-005 | T090. |
| SC-006 | T091, complementado por T034/T051/T063/T077/T081. |
| SC-007 | T010–T012/T064/T067/T068/T076/T078/T088. |
| Q-01–Q-11, principios I–V y recuperación | Evidencia por historia, T036/T041–T045/T052/T075–T078 y T089–T096. |

## Notes

Todos los checkboxes se generan sin marcar: este archivo es la salida de planificación
de tareas. Marcar cada uno solo al terminar su implementación y verificación
exigidas. No modificar los marcadores reviewer-owned del checklist de calidad para simular
aceptación de código. Conservar evidencia real de humanos, revisión A+B y dependencias.
