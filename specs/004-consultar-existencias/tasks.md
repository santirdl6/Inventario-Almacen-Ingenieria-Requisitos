---

description: "Lista de tareas de HU-05 — Consultar existencias y reservas"
---

# Tasks: HU-05 — Consultar existencias y reservas

**Input**: documentos de diseño de `specs/004-consultar-existencias/`:
[plan.md](plan.md), [spec.md](spec.md), [research.md](research.md) (D-01…D-17),
[data-model.md](data-model.md), [contracts/](contracts/) y [quickstart.md](quickstart.md) (S-xx, E2E-01).

**Presupuesto**: unas 50 h para toda la entrega, con tres historias más. Hay **20 tareas**
(T001–T020). Lo recortado está al final, en «Si hay tiempo», fuera de la numeración.

**Pruebas**: solo recorrido habitual, error y permisos, más **V-05** (S-12), la **extensión de V-04**
(liberaciones parciales repetidas, S-14), **V-08** (S-16) y el contrato de escritores de
**INT-HU05-01/02** (carreras, borrado, desactivación y conciliación, dentro de T010, T012 y T014),
en SQL Server real con la receta Docker
del [quickstart](quickstart.md#pruebas-automatizadas-backend), porque no hay SDK .NET local. E2E solo
del recorrido **E2E-01** del vídeo. Sin tareas de rendimiento (SC-006) ni de validación con
operarios (SC-007): ver «Desviaciones registradas».

**Organización**: por historias de la spec. US5 (autorización y fiabilidad) es transversal: sus
pruebas van dentro de las tareas de prueba de US1–US4, en lugar de tener una fase propia, para
respetar el límite de tareas.

## Format: `[ID] [P?] [Story] Descripción`

- **[P]**: puede hacerse en paralelo (archivos distintos, sin dependencias pendientes).
- **[Story]**: historia de la spec (US1–US5).
- **⚑ commit aparte**: toca archivos de A o comunes, o HU-02. Va en su propio commit para
  separarlo en la revisión del PR.
- **[INT-HU05-01] / [INT-HU05-02]**: tarea receptora del backlog de integración del PR #7
  (`specs/001-reparto-specs-inventario/integration-tasks.md`), como exige AGENTS.md. **COORD-01**:
  revisión cruzada de los cambios compartidos.

## Path Conventions

Backend en `backend/src/Inventario.Api/` y pruebas en
`backend/tests/Inventario.IntegrationTests/Stock/`. Frontend en `frontend/src/app/` y E2E en
`frontend/e2e/`. Rutas según [plan.md § Project Structure](plan.md#project-structure).

---

## Phase 1: Coordinación con A (pendiente explícita, no se aplica ahora)

- [ ] T001 ⏸ **PENDIENTE: solicitar la revisión de A en el PR** (COORD-01), sin bloquear la
  implementación, de:
  - (a) los diffs pendientes de [plan.md § Cambios al modelo común](plan.md#cambios-al-modelo-común-pendientes-de-a):
    P-01 (solo lo que falta tras #7), P-02…P-06 y P-08. P-07 ya está aplicado y aprobado en el
    PR #7 de A;
  - (b) los cambios en archivos de A y comunes de T005, T009, T016 (`mutationPermission()` y
    `OperationStatusComponent.route()`) y T018;
  - (c) la alternativa `ReservationRelease` de research D-06;
  - (d) el endpoint de preparación de pruebas de D-17.

  Registrar decisión, fecha y revisor en `specs/004-consultar-existencias/plan.md` § «Estado de la
  revisión de A». Si A rechaza algo, ajustar solo las tareas afectadas.
- [ ] T002 ⏸ **PENDIENTE, bloqueada por T001 y por la fusión de #7: aplicar los diffs aprobados**
  P-01 (lo que falta), P-02…P-06 y P-08 en `.specify/memory/project/data-model.md`,
  `.specify/memory/project/contracts/api.md`, `.specify/memory/project/contracts/stock-operations.md`
  y `.specify/memory/project/quickstart.md` (evidencia, tras T014). **No toca** `research.md` (R-10)
  ni `specs/001-reparto-specs-inventario/plan.md`, que ya cambió #7 (P-07). **No se aplican hasta la
  aprobación de A** (AGENTS.md, «Cambios compartidos»). Al tener evidencia, marcar INT-HU05-01 e
  INT-HU05-02 en `specs/001-reparto-specs-inventario/integration-tasks.md` (#7). ⚑ commit aparte.

---

## Phase 2: Foundational (bloquea todas las historias)

**Objetivo**: tablas, dominio y registro de la feature sobre la cadena única de migraciones.

- [ ] T003 Línea base y dominio. Primero, ejecutar la suite backend con la receta Docker y anotar el
  número de pruebas en verde. Después, crear en `backend/src/Inventario.Api/Domain/Stock/`:
  - `CustomerOrder.cs` con `CustomerOrder` y `CustomerOrderLine`:
    - «ExternalReference nvarchar(64), CI_AS, obligatoria y recortada».
    - «CustomerReference nvarchar(200), obligatoria y recortada».
    - «State `Open`, `Fulfilled` o `Cancelled`»; solo se crea `Open`.
    - «OrderedQuantity decimal(18,3) > 0».
    - «0 ≤ Shipped ≤ Ordered».
    - `Touch()`, que marca `State` como modificado.
  - `Reservation.cs` con `Reservation` y `ReservationAllocation`:
    - «Allocated > 0; Consumed ≥ 0; Released ≥ 0; Remaining = Allocated − Consumed − Released ≥ 0».
    - `Release(quantity)`, que solo aumenta `ReleasedQuantity`.
    - `DeriveState()` según [data-model § Estados](data-model.md#estados-y-transiciones).
    - `Touch()`.

  Crear además las cuatro configuraciones en
  `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/`
  (`CustomerOrderConfiguration.cs`, `CustomerOrderLineConfiguration.cs`,
  `ReservationConfiguration.cs`, `ReservationAllocationConfiguration.cs`):
  - Tablas `CustomerOrders`, `CustomerOrderLines`, `Reservations` y `ReservationAllocations`.
  - FK restrictivas, `rowversion` en `CustomerOrders` y `Reservations` y CHECK de cantidades y
    estados.
  - Índices únicos `UX_CustomerOrders_ExternalReference`, `UX_CustomerOrderLines_Order_Article`
    (P-01) y `(ReservationId, ArticleId, LocationId, LotId)` (P-02).
  - FK compuesta `(ArticleId, LotId) → Lots`.
  - `RemainingQuantity` como columna calculada persistida.
  - `IX_ReservationAllocations_Bucket (ArticleId, LocationId, LotId) INCLUDE (RemainingQuantity)`.

  (data-model; FR-009–FR-013, FR-015–FR-019.)

  **Estado (2026-11-20): parcial, sin marcar.** Por decisión de B, T003 se parte:
  - Hecho:
    - Línea base: 121/1/0, en `quickstart.md`, sección «Evidencia».
    - Dominio: `CustomerOrder.cs` y `Reservation.cs` con `Hu05DomainTests` (13 casos).
    - Suite completa 134/1/0 y modelo sin cambios.
  - **Pendiente, junto a la migración de T004 cuando se fusione el PR #7**: las cuatro
    configuraciones. `InventoryDbContext` las registra todas
    (`ApplyConfigurationsFromAssembly`), y sin la migración ni el contributor de T004 fallarían:
    - las tres pruebas `HasPendingModelChanges()` (`H0ReferenceTests`, `IdentityTests`,
      `StockCoreTests`);
    - `ArticleUsageCoverageValidator`, que daría 503 `DependencyUnavailable` en HU-08.
  - `Touch()` no está en el dominio: marcar `State` como modificado necesita el `DbContext`.
    Lo hace el comando con `db.Entry(x).Property(v => v.State).IsModified = true`, como
    `ArticleCommands.cs` de A (T008, T011, T013; research D-07).
- [ ] T004 [INT-HU05-01] Depende de T003 y, **para la migración**, de la fusión del PR #7 de A.
  - Generar `dotnet ef migrations add HU05_CustomerOrdersReservations` en
    `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/`, sobre
    `20261119171332_HU08MovementCounterparty` (#7), la última migración de la cadena común. No se
    genera antes de que #7 se fusione: hacerlo obligaría a regenerarla (plan global, §5). El resto
    de T004 (`StockDocuments.cs`, el contributor y `StockEndpoints.cs`) puede adelantarse.
  - Añadir `DENY DELETE ON OBJECT::dbo.{CustomerOrders,CustomerOrderLines,Reservations,ReservationAllocations} TO inventory_runtime` (P-02). `Down` revierte en orden inverso.
  - Crear `backend/src/Inventario.Api/Features/Stock/StockDocuments.cs`: tipos canónicos
    `customer-order-reference`, `customer-order`, `reservation-release` y `reservation`, y el UUID
    v5 de la referencia normalizada (recorte y mayúsculas invariantes) con un espacio de nombres
    fijo (D-04).
  - Crear `backend/src/Inventario.Api/Features/Stock/CustomerOrders/CustomerOrderArticleUsage.cs`:
    `IArticleUsageContributor` e `IArticleFirstReferenceContributor` para `CustomerOrderLine` y
    `ReservationAllocation` (con filas ⇒ `HasReferences`), y «sin uso» solo si la migración no
    está aplicada, como `StockArticleUsageContributor` (D-13). Es el lector autoritativo que exige
    el contrato de escritores de INT-HU05-01.
  - Crear `backend/src/Inventario.Api/Features/Stock/StockEndpoints.cs` con `AddStock()`
    (contributor y servicios) y `MapStockEndpoints()` vacío.
  - Verificar con la receta Docker `has-pending-model-changes`, la cadena desde cero y
    revertir/reaplicar la migración. La suite previa debe seguir en verde: la cobertura de FK a
    `Articles` no debe dar `DependencyUnavailable`.
- [ ] T005 ⚑ commit aparte (archivo común). En `backend/src/Inventario.Api/Program.cs`, añadir
  `builder.Services.AddStock();` y `api.MapStockEndpoints();` junto a los de Locations, sin otro
  cambio (depende de T004).

**Checkpoint**: migración aplicada, suite previa en verde y feature registrada sin rutas.

---

## Phase 3: User Story 1 — Consultar las existencias de un artículo (P1) 🎯 MVP

**Objetivo**: `GET /stock` sobre el agregado único. **Prueba independiente**: S-01.

- [ ] T006 [US1] Depende de T005.
  - Crear `backend/src/Inventario.Api/Features/Stock/Queries/StockBalanceReader.cs`: única
    proyección de `StockBalances ⋈ Locations ⋈ Lots` por artículo, con `locationId` y `lotId`
    opcionales e `includeEmpty`. Con `false`, solo `Physical > 0`. Totales `SUM(Physical)` y
    `SUM(Reserved)` del filtro completo, con disponible calculado tras sumar (FR-003). Página,
    totales y recuento en una transacción SNAPSHOT con `asOf`. Orden: código de ubicación, código
    de lote e Id (D-02, D-03).
  - Crear `Queries/StockQueries.cs` y `Queries/StockDtos.cs` según
    [GET /stock](contracts/stock-api.md#get-stock):
    - Sin `articleId` → 422 `errors.articleId`.
    - Artículo inexistente → 404.
    - Filtro ajeno → 200 vacío.
    - Paginación de `Paging` (422 fuera de rango).
    - `lot.isUntracked` para `SIN-LOTE`.
    - `article.isActive`.
  - Mapear `GET /stock` en `StockEndpoints.cs` con `.RequireAuthorization("Inventory.Read")`.

  (US1.1–US1.9; FR-001–FR-008.)
- [ ] T007 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Stock/StockApiSupport.cs` y
  `StockQueryTests.cs`, sobre `StockTestHarness` sin modificarlo.
  - Soporte: cliente HTTP con sesión real y antiforgery por rol (Operario, ResponsableAlmacen,
    GestorMaestros, Administrador) y existencias preparadas con `StockCore` (Receive/Ship).
  - **Habitual**: S-01 (2 + 4 en N01/N02; sin filtro, `locationId` y `lotId`; `pageSize=1` con
    totales 6/0/6) y S-05 (N01 a 0 con Ship: oculta por defecto y visible con `includeEmpty=true`;
    totales iguales).
  - **Error**: S-03 (sin `articleId` → 422; inexistente → 404; `locationId` ajeno → 200 vacío;
    `page=0` → 422).
  - **Permisos**: sin sesión → 401; Administrador → 403; GestorMaestros → 200.

  (US1, US5.1; FR-001–FR-004, FR-021; SC-001.)

**Checkpoint**: la consulta funciona con existencias preparadas por el núcleo.

---

## Phase 4: User Story 2 — Registrar y consultar un pedido de cliente mínimo (P2)

**Objetivo**: alta y detalle del pedido. **Prueba independiente**: S-08.

- [ ] T008 [US2] [INT-HU05-01] Depende de T004. Crear en `backend/src/Inventario.Api/Features/Stock/CustomerOrders/`
  `CustomerOrderCommands.cs`, `CustomerOrderQueries.cs`, `CustomerOrderDtos.cs` e
  `ICustomerOrderReader.cs`.
  - **Alta** (D-10), con `OperationRequest(…, "POST", "/api/v1/customer-orders", "Shipping.Record", DTO normalizado)`.
    - Validación de forma: textos recortados, 1–64 y 1–200; entre 1 y 100 líneas; cantidad
      positiva con tres decimales; artículo repetido → 422 por línea.
    - En `change`, `DocumentsAsync([customer-order-reference:{uuid5}, customer-order:{nuevo}])` →
      `ArticlesAsync` → referencia existente (409 `DuplicateCode`, `errors.externalReference`) →
      artículo inexistente (422) o inactivo (409 `ArticleInactive`) → fracciones (422) →
      `ArticleReferenceTracker.MarkAsync` → `Add` → `SaveChangesAsync` → 201 con
      `Location` y `ETag`.
  - **Detalle** `GET /customer-orders/{id}` (SNAPSHOT, `ETag` de `CustomerOrder.Version`), con DTO
    `CustomerOrderView` de **solo datos propios** (research D-07, D-11):
    - por línea, `articleId`, pedido, servido, pendiente y remanente;
    - reservas con asignaciones por ids (`articleId`, `locationId`, `lotId`) y cantidades.

    Orden estable por Id de línea, sin SKU: el SKU es de otro agregado y cambiaría la
    representación sin cambiar el ETag. La UI ordena por el SKU del contexto. El alta devuelve el
    mismo DTO con su `ETag`.
  - **Contexto** `GET /customer-orders/{id}/context` (SNAPSHOT, **sin `ETag`**,
    `Cache-Control: no-store`), con DTO `CustomerOrderContext`:
    - `orderVersion = EntityTags.Format(order.Version)` y `asOf`;
    - artículos de las líneas: `sku`, `name`, `baseUnit`, `isActive`, `tracksLot` y
      `allowsFractional`;
    - ubicaciones de todas las asignaciones: `code`, pasillo, estantería, nivel e `isActive`;
    - lotes: `code` e `isUntracked`.

    Igual que `GET /articles/{id}/context` de HU-08 ([contrato](contracts/stock-api.md#get-customer-ordersidcontext)).
  - **Lector** `ICustomerOrderReader` del [contrato HU-03](contracts/hu03-integration.md).
  - Mapear `POST /customer-orders` (`Shipping.Record`), `GET /customer-orders/{id}` y
    `GET /customer-orders/{id}/context` (`Inventory.Read`) en `StockEndpoints.cs`, con `Key(context)`
    como HU-02. `GET /reservations/{id}` (T011) sigue la misma regla de solo datos propios.

  (US2.1–US2.4; FR-009–FR-013.)
- [ ] T009 [P] ⚑ commit aparte (archivo de A). En
  `backend/src/Inventario.Api/Infrastructure/Http/MasterDataProblemMapper.cs`, añadir una rama:
  2601/2627 cuyo mensaje contenga `UX_CustomerOrders_ExternalReference` → 409 `DuplicateCode` con
  `errors.externalReference`. Va **antes** de la rama genérica de `_Code`; nada más cambia (D-10).
- [ ] T010 [P] [US2] [INT-HU05-01] [INT-HU05-02] Crear
  `backend/tests/Inventario.IntegrationTests/Stock/CustomerOrderTests.cs`.
  - **Habitual**: S-08 (alta de PED-1001 con ART-01×7 y ART-02×3 → 201 `Open`, servido 0 y
    `IsDefinitionLocked` true; replay con la misma clave → idéntico; GET con `ETag`).
  - **Error**: S-08 (« ped-1001 » con otra clave → 409 `DuplicateCode`) y S-09 (cantidad 0,
    artículo repetido, 1,5 en artículo entero → 422 por línea; artículo inactivo → 409; nada
    creado).
  - **Permisos**: ResponsableAlmacen y GestorMaestros POST → 403; GestorMaestros GET → 200.
  - **ETag de la representación entera** (revisión de A, research D-07):
    - Tras renombrar el artículo de una línea (`PUT /articles/{id}`), `GET /customer-orders/{id}`
      devuelve el **mismo** cuerpo y el **mismo** `ETag`. `GET /customer-orders/{id}/context` sí
      refleja el nombre nuevo, sin `ETag` y con `orderVersion` igual al `ETag`.
    - Ubicación: HU-02 no deja cambiar el código ni el tipo de una ubicación con movimientos
      (`LocationIdentityLocked`), y toda ubicación con una asignación los tiene. Por eso la prueba usa
      el dato de ubicación que sí cambia:
      1. con la reserva liberada por completo y la ubicación vaciada con `StockCore` (Ship desde
         `StockTestHarness`), se toma el `ETag`;
      2. se desactiva la ubicación (`PUT /locations/{id}`);
      3. el detalle conserva cuerpo y `ETag`, y `context.locations[].isActive` pasa a `false`.

      Si HU-02 llegara a permitir corregir el código con movimientos, se añadiría el mismo caso con
      `code`.
    - Tras una reserva, una liberación o una salida, el `ETag` **cambia** y `orderVersion` lo sigue.
      Reserva y liberación se ejecutan aquí con los comandos de T011/T013. La salida de HU-03, que
      aún no existe, se simula renovando la versión de la cabecera como exige
      [hu03-integration.md](contracts/hu03-integration.md), regla 4.
  - **Contrato de escritores** (INT-HU05-01; incluye S-19 y no se delega en la suite de HU-08):
    - Tras la primera línea de pedido, el `PUT` de unidad, lote o fracciones del artículo → 409, y
      `GET /articles/{id}/context` muestra la definición bloqueada.
    - `DELETE /articles/{id}` con solo una línea de pedido (sin movimientos ni saldo) → rechazo por
      referencias, sin `DependencyUnavailable`.
    - «Bloqueo tras retirar referencias»: **no aplica**. HU-05 no retira líneas ni asignaciones (sin
      edición ni cancelación y con `DENY DELETE`), así que la referencia nunca se retira. Se anota
      en la prueba.
  - **Carreras** (INT-HU05-01/02), con conexiones independientes y barreras como
    `MasterDataLifecycleConcurrencyTests` de A, en **ambos órdenes**:
    - alta frente a desactivación: alta primero → 201 y la desactivación respeta el compromiso;
      desactivación primero → alta 409 `ArticleInactive`;
    - alta frente a `PUT` de la definición: alta primero → `PUT` 409; `PUT` primero → alta con la
      definición nueva;
    - alta frente a `DELETE` del artículo: alta primero → `DELETE` rechazado; `DELETE` primero →
      alta 422 por artículo inexistente.

    Sin modificar `StockTestHarness`.

  (US2, US5.1; FR-009–FR-012.) Depende de T008 y T009.

**Checkpoint**: se crean y consultan pedidos; HU-03 dispone del lector.

---

## Phase 5: User Story 3 — Reservar existencias para una línea de pedido (P2)

**Objetivo**: reservar y consultar compromisos. **Prueba independiente**: S-10.

- [ ] T011 [US3] [INT-HU05-01] Depende de T006 y T008. Crear en
  `backend/src/Inventario.Api/Features/Stock/Reservations/` `ReservationCommands.cs` (reservar),
  `ReservationQueries.cs` y `ReservationDtos.cs`.
  - **Reservar** (D-05, D-09), con `OperationRequest(…, "POST", "/api/v1/reservations", "Reservations.Manage", DTO normalizado)`.
    - En `change`, se leen primero las relaciones inmutables (línea → pedido, lotes) y se toman
      los bloqueos: `DocumentsAsync([customer-order:{o}, reservation:{nuevo}])` → `LocationsAsync`
      → `ArticlesAsync`.
    - Después: `EntityTags.RequireMatch(ifMatch, order.Version)` → `Open` (si no, 409
      `CustomerOrderNotOpen`) → lote del artículo (422) → `ILocationUsage.IsInOpenCountAsync`
      (409 `CountScopeFrozen`) → Σ asignaciones + remanente activo ≤ pendiente (422
      `errors.allocations`).
    - `StockCore.Prepare(StockOperation.Reserve, context("reservation", id, UtcNow, "Reserva para el pedido {ext}", support = ext, CounterpartyReference = CustomerReference), allocations)`
      + `ApplyAsync`.
    - `Add` de la reserva `Active`, `order.Touch()` y 201 con `Location`, **sin** `ETag` (D-08).
    - Sin `SaveChanges` dentro de `change`.
  - **Lecturas**: `GET /reservations/{id}` (`ETag`) y `GET /stock/reservations` según
    [contrato](contracts/stock-api.md#get-stockreservations).
  - Mapear las tres rutas con `Reservations.Manage` o `Inventory.Read`.

  (US3.1–US3.8; FR-014–FR-017, FR-020, FR-022, FR-025.)
- [ ] T012 [P] [US3] [INT-HU05-01] [INT-HU05-02] Crear
  `backend/tests/Inventario.IntegrationTests/Stock/ReservationTests.cs`.
  - **Habitual**: S-10 (reservar 7 de 10 → físico 10, reservado 7, disponible 3 en `GET /stock`;
    `GET /stock/reservations` muestra el pedido y el bucket). El `ETag` de
    `GET /customer-orders/{id}` después de reservar **es distinto** del anterior (D-07).
  - **Error**: S-10 (superar el pendiente → 422; sin `If-Match` → 428; ETag obsoleto → 412;
    ubicación vacía → 409 `InsufficientStock`) y S-11 (dos asignaciones con una insuficiente →
    ninguna asignación ni movimiento).
  - **Idempotencia** (FR-023): misma clave y mismo cuerpo → replay idéntico con un solo movimiento;
    misma clave con otra cantidad → 409 `IdempotencyConflict` sin efectos.
  - **Permisos**: Operario → 403.
  - **Desactivación** (INT-HU05-02, US3.7): reservar con el artículo desactivado → 409
    `ArticleInactive` sin cambios.
  - **Conteo abierto** (US3.7, FR-016): **pendiente de INT-HU04-03**. `IsInOpenCountAsync`
    devuelve `false` hasta que HU-04 integre los conteos; la prueba de 409 `CountScopeFrozen` se
    añade entonces.
  - **Carreras** (INT-HU05-01/02), con conexiones independientes y barreras, en **ambos órdenes**:
    - reserva frente a desactivación: reserva primero → 201 y el compromiso queda; desactivación
      primero → 409 `ArticleInactive`;
    - reserva frente a `PUT` de la definición: la definición ya está bloqueada por la línea, así
      que el `PUT` → 409 en ambos órdenes y la reserva no se ve afectada;
    - reserva frente a `DELETE` del artículo: `DELETE` rechazado en ambos órdenes y reserva 201.
  - **V-05** (S-12): físico 10, dos conexiones reservan 7 para pedidos distintos, 20 repeticiones
    → exactamente una 201 y una 409 `InsufficientStock`; reservado nunca 14.
  - **Conciliación documental, diario y saldo** (INT-HU05-02, núcleo de S-07), tras S-10 y V-05:
    `AssertJournalMatchesBalanceAsync` y, por bucket, Σ `RemainingQuantity` de
    `ReservationAllocations` = `StockBalances.Reserved`.
  - **Publicación para HU-03** (INT-HU05-01, mínimo de S-18): `ICustomerOrderReader` dentro del
    ejecutor, tras bloquear el pedido, da el mismo pendiente y remanente por bucket que
    `GET /customer-orders/{id}`.

  (US3, US5.1; FR-016, FR-022–FR-025; SC-003, SC-004, SC-005.) Depende de T011.

**Checkpoint**: reservas confirmadas visibles en la consulta; V-05 en verde.

---

## Phase 6: User Story 4 — Liberar reservas (P3) + fiabilidad (US5)

**Objetivo**: liberaciones parciales repetibles. **Prueba independiente**: S-14.

- [ ] T013 [US4] Depende de T011. En `ReservationCommands.cs`, añadir **liberar** (D-06), con
  `OperationRequest(…, "POST", "/api/v1/reservations/{id}/release", "Reservations.Manage", DTO normalizado)`.
  - Validación de forma: `releaseId` no vacío; motivo recortado de 1 a 500 caracteres, si no 422
    `errors.reason`; `allocationId` sin repetir; cantidad positiva.
  - En `change`, bloqueos: `DocumentsAsync([customer-order:{o}, reservation-release:{rel}, reservation:{r}])`
    → `LocationsAsync` → `ArticlesAsync`.
  - Después: `RequireMatch(ifMatch, reservation.Version)` → asignación de la reserva (422) →
    cantidad ≤ remanente (409 `ReservedStockConflict`).
  - `StockCore.Prepare(StockOperation.Release, context("reservation-release", releaseId, UtcNow, motivo, support = "reservation:{r}", CounterpartyReference = CustomerReference), allocations)`
    + `ApplyAsync`. La contraparte es el cliente, como al reservar (D-06, regla de contraparte de #7).
  - `allocation.Release`, `DeriveState`, `Touch()` de la reserva y del pedido, y 200 con
    `{ operationId, reservationId, releaseId, movementId, state }`.
  - Sin comprobar artículo inactivo ni conteo (R-05).
  - Mapear `POST /reservations/{id}/release`.

  (US4.1–US4.6; FR-017–FR-019, FR-022, FR-023.)
- [ ] T014 [P] [US4] [INT-HU05-02] Crear `backend/tests/Inventario.IntegrationTests/Stock/ReleaseTests.cs` y
  `StockReliabilityTests.cs`.
  - **Extensión de V-04** (S-14): R1 libera 2 con motivo → 200; R2 libera 2 → 200; R1 con su clave
    → replay idéntico; R1 con otra clave → 409 `DocumentAlreadyConfirmed`; 5 sobre un remanente de
    3 → 409 `ReservedStockConflict`; sin motivo → 422; ETag obsoleto → 412. Al final: reservado 3,
    físico sin cambios y dos movimientos `Release` con motivo, actor y `CounterpartyReference` del
    cliente.
  - **ETag** (D-07): tras cada liberación, el `ETag` de `GET /reservations/{id}` y el de
    `GET /customer-orders/{id}` **son distintos** de los anteriores.
  - **Idempotencia** (FR-023): la clave de R1 con otro motivo o cantidad → 409 `IdempotencyConflict`
    sin efectos (el replay con el mismo cuerpo ya está en S-14).
  - **Habitual**: S-15 (liberar con el artículo desactivado → 200, permitido por INT-HU05-02;
    liberar todo → `Released`). Tras liberarlo todo, `DELETE /articles/{id}` **sigue rechazado**
    porque las líneas del pedido permanecen (INT-HU05-01).
  - **Carreras** (INT-HU05-02), con conexiones independientes y barreras, en **ambos órdenes**:
    liberación frente a desactivación → ambas 200, en cualquier orden; liberación frente a `PUT` de
    la definición → `PUT` 409 y liberación 200; liberación frente a `DELETE` → `DELETE` rechazado y
    liberación 200.
  - **Permisos**: Operario → 403; sin antiforgery → 400.
  - **V-08** (S-16): `StockTestHarness.FailingFactory` y `FailAfterSaveInterceptor` armado en
    alta, reserva y liberación → ningún pedido, reserva, movimiento ni `OperationResult`; repetir
    con la misma clave → un solo efecto.
  - **Conciliación documental, diario y saldo** (INT-HU05-02) al final de S-14 y tras V-08: la misma
    comprobación que en T012.
  - Después, ejecutar la suite completa tres veces con la receta Docker y registrar fecha,
    entorno, número de pruebas y resultado en `specs/004-consultar-existencias/quickstart.md`
    § «Evidencia».

  (US4, US5.1–US5.5; FR-018, FR-022–FR-024; SC-005.) Depende de T013.

**Checkpoint backend**: S-01, S-03, S-05, S-08–S-12, S-14–S-16 y los permisos en verde tres
veces; evidencia registrada.

---

## Phase 7: Frontend /existencias (US1–US4, solo lo que sale en el vídeo)

- [ ] T015 [US1] Depende de T006.
  - Crear `frontend/src/app/features/stock/data/stock.models.ts` y `stock-api.service.ts`
    (`GET /stock` y `GET /api/v1/articles?q=&state=all`).
  - Crear `frontend/src/app/features/stock/query/stock-query.page.ts` según
    [stock-ui § Consulta](contracts/stock-ui.md#consulta-de-existencias):
    - Selector de artículo propio, con texto «Elige un artículo para ver sus existencias».
    - Filtros de ubicación y lote (este último solo si `tracksLot`).
    - Casilla «Mostrar ubicaciones sin existencias».
    - Totales «Totales de todo el filtro» separados de la tabla, con unidad.
    - «sin lote», «Inactivo» en texto, «Datos a las {asOf}», «Actualizar» y «No hay existencias
      para esta selección».
    - Errores con `apiProblem()`.
    - Solo lee: no hay mutaciones ni intención idempotente en esta pantalla.
  - En `frontend/src/app/features/stock/stock.routes.ts`, sustituir el placeholder por `''`.
  - Spec unitaria `stock-query.page.spec.ts`: habitual (totales desde `totals`, `includeEmpty`) y
    error (404 → mensaje en la página).

  (US1; FR-001–FR-008, FR-028.)
- [ ] T016 [US2] Depende de T008, T011, T013 y T015.
  - Crear `frontend/src/app/features/stock/data/customer-orders-api.service.ts`. Su `detail(id)` hace
    **dos lecturas**, como `articles-api.service.ts` de HU-08:
    - `GET /customer-orders/{id}` con `observe: 'response'` para el `ETag` (datos propios);
    - `GET /customer-orders/{id}/context` (datos actuales, sin `ETag`).

    Si `context.orderVersion !== etag`, error «El pedido cambió durante la consulta» y nueva
    lectura. `GET /reservations/{id}` solo para obtener el `ETag` de la reserva. Las tres
    mutaciones van con
    `MasterDataMutationsService.mutate()`, como `locations-api.service.ts` de HU-02:
    - `POST /api/v1/customer-orders`;
    - `POST /api/v1/reservations` con el `ETag` del pedido;
    - `POST /api/v1/reservations/{id}/release` con el `ETag` de la reserva.

    `PendingOperationStore` conserva la clave, el cuerpo congelado y el `If-Match`. **No** se usan
    las funciones de clave de `idempotent-intent.ts`, que reintroducirían R1 y R2 del PR #6 (D-15).
  - ⚑ **commit aparte (archivos de A)**, antes de las pantallas:
    - en `frontend/src/app/core/operations/pending-operation.store.ts`, ampliar
      `mutationPermission()`: `^/api/v1/customer-orders$` → `Shipping.Record`, y
      `^/api/v1/reservations$` y `^/api/v1/reservations/{uuid}/release$` → `Reservations.Manage`;
    - en `frontend/src/app/core/operations/operation-status.component.ts`, que `route()` lleve el
      alta a `/existencias/pedidos/nuevo` y reservas y liberaciones a `/existencias/pedidos/{orderId}`
      (el `customerOrderLineId` o `reservationId` del cuerpo identifica el pedido; fijar el
      mecanismo al implementar).

    Nada más cambia en esos archivos. Spec unitaria: la intención de HU-05 se restaura tras recargar
    y no se reutiliza con otro usuario, como `location-form.review.spec.ts`.
  - Crear `orders/order-form.page.ts`: referencia, cliente y líneas; un artículo no puede
    elegirse dos veces; revisión antes de confirmar; errores por línea conservando datos; al
    confirmar, navegación al detalle.
  - Crear `orders/order-detail.page.ts`: pedido, servido, pendiente y reservado por línea, y
    reservas con asignaciones. SKU, nombre, unidad, «Inactivo», códigos de ubicación y de lote se
    toman del contexto, uniendo por id y ordenando las líneas por SKU. «Actualizar» y cada
    confirmación repiten las dos lecturas.
  - Crear `orders/reserve-dialog.ts`, que lista `GET /stock` del artículo con el máximo
    reservable.
  - Crear `orders/release-dialog.ts`: motivo obligatorio y `releaseId` generado al abrir. Viaja en
    el cuerpo congelado de la intención, así que se conserva en recargas y reintentos.
  - Los diálogos solo se muestran con `SessionService.has('Reservations.Manage')`. Tras
    confirmar, se vuelve a leer el pedido. Estados según [stock-ui](contracts/stock-ui.md),
    sección «Estados de operación»: «Resultado pendiente de comprobar» con
    `OperationStatusComponent`, `check()` y `retry()`; 409 y 412.
  - Rutas `pedidos/nuevo` (`permissionGuard`, `data: { permission: 'Shipping.Record' }`) y
    `pedidos/:id`, leyendo `id` con `ActivatedRoute`.
  - Specs unitarias:
    - habitual: alta, reservar y liberar envían las cabeceras; el detalle combina las dos lecturas y
      muestra el nombre del contexto;
    - error: 409 conserva las cantidades; con `orderVersion` distinto del `ETag` se vuelve a leer y
      no se habilita reservar con un `ETag` mezclado;
    - permisos: sin `Reservations.Manage` no hay botones.

  (US2–US4; FR-009, FR-013, FR-018, FR-026–FR-028.)

**Checkpoint frontend**: `npm test` y `npm run build` en verde; recorrido manual con existencias
preparadas.

---

## Phase 8: Preparación de pruebas y E2E-01 (vídeo)

- [ ] T017 Depende de T005 y de la **fusión del PR #7 de A** (`CounterpartyNotApplicableReason` en
  `StockCommandContext`).
  - Crear `backend/src/Inventario.Api/Features/Stock/Testing/StockSeedingEndpoints.cs` (D-17):
    `POST /api/v1/testing/stock-receipts`, que `MapStockEndpoints` mapea **solo** si
    `configuration.GetValue<bool>("Testing:EnableStockSeeding")`.
  - Ejecuta `StockCore` (`Receive`) con el ejecutor común, permiso `Receiving.Record`, documento
    `test-receipt` y motivo «Preparación de pruebas E2E».
  - Sin documento de compra ni proveedor, lo hace explícito (constitución IV):
    `SupportNotApplicableReason = "Preparación de pruebas E2E"` y
    `CounterpartyNotApplicableReason = "Preparación de pruebas E2E"`.
  - Una prueba en `StockQueryTests.cs`: sin la opción → 404; con la opción → movimiento `Receipt`
    en el diario con ambos motivos.
- [ ] T018 ⚑ commit aparte (archivos comunes y de A).
  - En `docker-compose.yml`, servicio `app`, añadir
    `Testing__EnableStockSeeding: ${TESTING_ENABLE_STOCK_SEEDING:-false}`.
  - En `frontend/e2e/fixtures/master-data.fixture.ts` (A), pasar
    `TESTING_ENABLE_STOCK_SEEDING=true` solo al proyecto Compose E2E y admitir cuentas
    `Operario` y `ResponsableAlmacen`, si no lo hace ya.
  - Nada más cambia.

  Depende de T017.
- [ ] T019 [US3] Crear `frontend/e2e/stock.spec.ts` con **E2E-01** usando el fixture de A:
  1. Preparar 10 de ART-01/L-001 en N01 con el endpoint de T017.
  2. El responsable abre `/existencias`, elige ART-01 y ve totales y filas.
  3. El operario crea PED-1001 en `/existencias/pedidos/nuevo`.
  4. El responsable abre `/existencias/pedidos/{id}` por URL, porque la búsqueda queda en «Si hay
     tiempo», y reserva 7 en N01. En `/existencias` ve disponible 3.
  5. Libera 2 con motivo y ve reservado 5 y disponible 5.

  Nada más se automatiza en E2E (quickstart E2E-01). Depende de T016 y T018.

---

## Phase 9: Refactor mínimo de HU-02 (al final; el PR #6 ya está fusionado)

- [ ] T020 ⚑ commit aparte (HU-02). En
  `backend/src/Inventario.Api/Features/Locations/Queries/LocationQueries.cs`,
  `ArticleLocationsAsync` pasa a delegar en `StockBalanceReader` (`includeEmpty=false` y el filtro
  `includeInactiveLocations` propio). Se conservan el DTO `ArticleLocations`, la ruta y el orden
  actuales; no se toca nada más de HU-02.
  - Añadir S-02 a `StockQueryTests.cs`: reservar 4 en N02; `GET /stock` y
    `GET /articles/{id}/locations` dan las mismas cantidades.
  - Ejecutar `Locations/ArticleLocationsTests.cs` (L-05 y L-06) sin cambiar sus aserciones.

  - No tocar `DetailAsync` ni `LocationEntityTags` (digest de A en `dfc1fd1`). Ejecutar también
    `Locations/LocationReviewTests.cs`.

  (D-02; FR-003.) Depende de T006. **Desbloqueada**: el PR #6 ya está fusionado (`45292a9`).

---

## Si hay tiempo (fuera de las 20 tareas, en este orden)

- **S1** `GET /customer-orders` con búsqueda por referencia y estado (P-04) y la pantalla
  `/existencias/pedidos` (`orders/order-list.page.ts`). Sin ella, en el vídeo se llega al pedido
  desde el alta o por URL.
- **S2** Pestaña «Reservas» de `/existencias` sobre `GET /stock/reservations` (US3.8 en
  interfaz). El endpoint sí entra en T011 y se prueba en T012.
- ~~S3~~ **Retirada** tras `/speckit-analyze` (C1). HU-05 usa el transporte común de A
  (D-15), así que no hay nada que trasladar. El identificador S3 queda sin uso para no renumerar.
- **S4** Pruebas no exigidas en esta entrega: S-04 (borrador no cuenta), la parte de consulta de
  S-06 (artículo inactivo visible en `/stock`), S-13 (dos reservas de la misma línea) y S-18
  completo (HU-03 lo amplía). **No se aplazan**, porque INT-HU05-01/02 exigen que cada módulo pruebe
  sus propios escritores y no basta la suite de HU-08:
  - S-19 (definición bloqueada y borrado rechazado): en T010.
  - Reservar con el artículo inactivo: en T012.
  - El núcleo de S-07 (conciliación documental, diario y saldo): en T012 y T014.
  - El mínimo de S-18: en T012.

---

## Dependencies & Execution Order

```text
T001 ⏸ ─▶ T002 ⏸            (coordinación; no bloquea nada; T002 espera además a #7)
PR #7 de A (fusión) ─▶ T004 (migración) y T017
T003 ─▶ T004 ─▶ T005 ─▶ T006 ─▶ T007
                  │      └──────────────▶ T015 ─┐
                  ├─▶ T008 ─▶ T010 (T009 ∥)     │
                  │     └─▶ T011 ─▶ T012        │
                  │            └─▶ T013 ─▶ T014 │
                  │                  └──────────┴─▶ T016 ─┐
                  └─▶ T017 ─▶ T018 ──────────────────────┴─▶ T019
T006 ─▶ T020 (al final)
```

- **Historias**: US1 (T006–T007) no depende de pedidos. US2 (T008–T010) solo de Foundational. US3
  necesita US1 (lector) y US2. US4 necesita US3. US5 está repartida en T007, T010, T012 y T014.
- **Dentro de cada historia**: implementación → pruebas → checkpoint. Las pruebas son de
  integración contra el endpoint ya mapeado.

### Parallel Opportunities

- T009 (mapper) en paralelo con T008.
- T007 con T008; T010 con T011; T012 con T013, porque los archivos son distintos.
- T015 (frontend de consulta) en cuanto T006 está listo, en paralelo con el backend de pedidos y
  reservas.
- T017 en paralelo con las fases 4–6.

## Implementation Strategy

1. **MVP**: T003–T007, la consulta de existencias (US1) con datos preparados por el núcleo.
   Demostrable sola.
2. **Compromisos**: T008–T014 (pedido, reservar, liberar y fiabilidad). Checkpoint backend con
   evidencia.
3. **Vídeo**: T015–T019 (pantallas y E2E-01).
4. **Integración HU-02**: T020. El PR #6 ya está fusionado.
5. **Coordinación**: T001 se pide al abrir el PR; T002 se aplica tras la aprobación de A.

Commits: uno por tarea o por grupo coherente con el formato `HU-05 (B): …`. Las tareas ⚑
siempre en commit propio. Nunca `git add -A`.

## Desviaciones registradas (sin tareas)

| Desviación | Motivo | Consecuencia |
|------------|--------|--------------|
| SC-006 (rendimiento: p95 ≤ 2 s con 10.000 artículos, 1.000.000 de entradas y 25 sesiones) sin medir. | Presupuesto de unas 50 h para cuatro historias; requiere datos sintéticos y carga. | HU-05 se integra, pero no se da por aceptada ([plan § Complexity Tracking](plan.md#complexity-tracking)). |
| SC-007 (validación con trabajadores, constitución III) sin realizar. | Requiere sesiones con operarios fuera de esta entrega. | Igual que la anterior; guion en el [quickstart](quickstart.md#fuera-de-alcance-de-esta-entrega). |
| Pruebas limitadas a habitual, error, permisos, V-05, V-04 ampliada, V-08 y el contrato de escritores de INT-HU05-01/02. | Recorte de presupuesto. | S-04, la consulta de S-06, S-13 y S-18 completo pasan a «Si hay tiempo» (S4). S-19, la conciliación de S-07 y el mínimo de S-18 están en T010/T012/T014. |
| FR-020 («mostrar las reservas activas de un artículo») cubierto solo en la API: `GET /stock/reservations` en T011, probado en T012. | La pestaña «Reservas» (S2) no sale en el vídeo y no cabe en el presupuesto. Los compromisos de cada pedido sí se ven en su detalle (T016). | La consulta por artículo se hace con la API hasta que se implemente S2 ([plan § Complexity Tracking](plan.md#complexity-tracking)). |
| Congelación por conteo abierto al reservar (US3.7, FR-016) sin prueba. | `IsInOpenCountAsync` devuelve `false` hasta que HU-04 integre los conteos. | Pendiente de INT-HU04-03 (backlog de integración del PR #7); allí se prueba 409 `CountScopeFrozen`. |
| Selector de artículo propio en lugar de `article-search` de HU-02 (recogido en research D-15). | El de HU-02 enlaza fijo a `/ubicaciones/articulo/:id`; reutilizarlo exigiría tocar HU-02. | Un componente pequeño duplicado; se unifica en una historia posterior. |
| Búsqueda de pedidos (P-04) y pestaña «Reservas» recortadas. | No salen en el vídeo. | El E2E abre el pedido por URL; S1 y S2 si hay tiempo. |
