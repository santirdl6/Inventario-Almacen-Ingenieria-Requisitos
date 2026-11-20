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
  - (b) los cambios en archivos de A y comunes de T005, T009, T016 (`mutationPermission()`, el
    campo `appRoute` de `PendingOperation` y `OperationStatusComponent.route()` / «Abrir pedido»)
    y T018;
  - (c) la alternativa `ReservationRelease` de research D-06;
  - (d) el endpoint de preparación de pruebas de D-17.

  Registrar decisión, fecha y revisor en `specs/004-consultar-existencias/plan.md` § «Estado de la
  revisión de A». Si A rechaza algo, ajustar solo las tareas afectadas.

  **Estado tras fusionar #7 (2026-11-20): sin marcar.** A ya revisó y aplicó P-07 y la parte de
  P-01 sobre responsabilidad y alcance. Esa parte dice que HU-05 crea y consulta el pedido, que no
  lo edita ni lo cancela y que `Cancelled` se conserva en el modelo común. Además, en su revisión
  del diseño A pidió el contexto sin ETag (D-07/D-11) y `appRoute`, ya incorporados. **Queda por
  coordinar con A**:
  - (a) de P-01, la unicidad `(CustomerOrderId, ArticleId)`, la comparación de `ExternalReference`
    sin mayúsculas ni espacios extremos y la renovación de la cabecera en cada cambio de reservas o
    servido; P-02…P-06, con P-03 ya ampliado con `GET /customer-orders/{id}/context`; y P-08;
  - (b), (c) y (d) completos.

  El PR #8 de A (XOR de contraparte, abierto) es compatible con el diseño: reservar y liberar solo
  pasan `CounterpartyReference`, y T017 solo `CounterpartyNotApplicableReason`.
- [ ] T002 ⏸ **PENDIENTE, bloqueada por T001: aplicar los diffs aprobados** (#7 ya está fusionado)
  P-01 (lo que falta), P-02…P-06 y P-08 en `.specify/memory/project/data-model.md`,
  `.specify/memory/project/contracts/api.md`, `.specify/memory/project/contracts/stock-operations.md`
  y `.specify/memory/project/quickstart.md` (evidencia, tras T014). **No toca** `research.md` (R-10)
  ni `specs/001-reparto-specs-inventario/plan.md`, que ya cambió #7 (P-07). **No se aplican hasta la
  aprobación de A** (AGENTS.md, «Cambios compartidos»). Al tener evidencia, marcar INT-HU05-01 e
  INT-HU05-02 en el [backlog de integración](../001-reparto-specs-inventario/integration-tasks.md).
  ⚑ commit aparte.

  **Estado tras fusionar #7 (2026-11-20): sin marcar.** Ya no hay que aplicar P-07 ni la parte de
  P-01 que #7 introdujo en `data-model.md` (párrafo «Responsabilidad revisada A+B»). Queda aplicar,
  tras la aprobación de A, el resto de P-01, P-02…P-06 y P-08.

---

## Phase 2: Foundational (bloquea todas las historias)

**Objetivo**: tablas, dominio y registro de la feature sobre la cadena única de migraciones.

- [X] T003 Línea base y dominio. Primero, ejecutar la suite backend con la receta Docker y anotar el
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

  **Estado final (2026-11-20): completada.** La parte pendiente (llamada «T003b»: las cuatro
  configuraciones) se hizo junto a T004, en el mismo commit, para que ningún commit quedara en rojo
  (decisión de B). Historial de la partición:
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
- [X] T004 [INT-HU05-01] Depende de T003 y, **para la migración**, de la fusión del PR #7 de A
  (**satisfecha**: #7 fusionado en `c904824` y traído a esta rama el 2026-11-20).
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

  **Hecho (2026-11-20, con T003b)**:
  - **Regenerada tras fusionar el PR #8** (4165bee): ahora es
    `20261120113458_HU05_CustomerOrdersReservations` sobre
    `20261120102651_HU08ExclusiveMovementCounterparty`, con el mismo `Up` y el `DENY DELETE`. La
    original, `20261120111847`, era la misma migración sobre `HU08MovementCounterparty`.
  - Migración original sobre `HU08MovementCounterparty`,
    con `DENY DELETE` en las cuatro tablas. El PR #8 de A no estaba fusionado; al traerlo, repetir
    `has-pending-model-changes` y regenerar la migración solo si la cadena cambia.
  - `StockDocuments.cs`, `CustomerOrderArticleUsageContributor` y `StockEndpoints.cs`
    (`AddStock()` y `MapStockEndpoints()` sin rutas).
  - **Cambio frente al plan**: el contributor se registra en `AddStockCore`, no en `AddStock()`.
    Así la cobertura no depende de T005 (decisión de B).
  - Pruebas `Hu05SchemaTests` (4) y 2 nuevas en `Hu05DomainTests`. Dos pruebas de A que fijaban
    exactamente {Lot, StockBalance, StockEntry} pasan a `Subset`, en commits aparte.
  - Resultado: suite 146/1/0; modelo sin cambios; cadena desde cero, revertir y reaplicar
    correctas.
- [X] T005 ⚑ commit aparte (archivo común). En `backend/src/Inventario.Api/Program.cs`, añadir
  `builder.Services.AddStock();` y `api.MapStockEndpoints();` junto a los de Locations, sin otro
  cambio (depende de T004). **Hecho (2026-11-20)**: tres líneas, contando el `using
  Inventario.Api.Features.Stock;` que necesitan las extensiones. Suite 146/1/0.

**Checkpoint**: migración aplicada, suite previa en verde y feature registrada sin rutas.

---

## Phase 3: User Story 1 — Consultar las existencias de un artículo (P1) 🎯 MVP

**Objetivo**: `GET /stock` sobre el agregado único. **Prueba independiente**: S-01.

- [X] T006 [US1] Depende de T005.
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

  **Hecho (2026-11-20)**:
  - `StockBalanceReader`, `StockQueries`, `StockDtos` y `GET /stock` en `StockEndpoints`.
    `articleId`, `locationId`, `lotId` e `includeEmpty` llegan como texto para responder 422 con
    el campo, en vez del 400 genérico.
  - Pruebas directas del lector en `StockBalanceReaderTests` (2): nulo si no hay artículo; orden,
    página, filtros, `includeEmpty` y totales. Usan `StockApiSupport` de T007, que por eso entra en
    este commit.
  - HU-02 (`LocationQueries.ArticleLocationsAsync`) no cambia: la delegación es T020.
- [X] T007 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Stock/StockApiSupport.cs` y
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

  **Hecho (2026-11-20)**: `StockQueryTests`, 5 pruebas HTTP con sesión real.
  - **S-01**: filas, filtros `locationId` y `lotId`, páginas 1 y 2 de tamaño 1 con totales 6/0/6.
  - **Disponible = físico − reservado**: 12/8/4 en la fila y en los totales; `lot.isUntracked`.
  - **S-05**: la fila a cero se oculta y aparece con `includeEmpty=true`; los totales no cambian.
  - **S-03**: sin `articleId`, vacío o no UUID → 422; inexistente → 404; ubicación o lote ajenos →
    200 vacío; `page=0`, `pageSize=0` o `pageSize=101` → 422.
  - **Permisos**: sin sesión → 401; Administrador → 403; GestorMaestros, Operario y
    ResponsableAlmacen → 200.

  `StockApiSupport.cs` se subió con T006 porque lo usan sus pruebas.

**Checkpoint**: la consulta funciona con existencias preparadas por el núcleo.

---

## Phase 4: User Story 2 — Registrar y consultar un pedido de cliente mínimo (P2)

**Objetivo**: alta y detalle del pedido. **Prueba independiente**: S-08.

- [X] T008 [US2] [INT-HU05-01] Depende de T004. Crear en `backend/src/Inventario.Api/Features/Stock/CustomerOrders/`
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

  **Hecho (2026-11-20)**:
  - `CustomerOrderCommands` (alta), `CustomerOrderQueries` (detalle y contexto, cada uno en
    SNAPSHOT), `CustomerOrderDtos` y `ICustomerOrderReader` con su implementación
    `CustomerOrderReader`, registrados en `AddStock()`. Las tres rutas, en `StockEndpoints` con
    `LocationEndpoints.Key`.
  - El cuerpo normalizado (`CustomerOrderWrite.Read`) es la huella. Lleva los textos recortados,
    los ids en formato D y las cantidades válidas con tres decimales. Lo inválido se conserva
    recortado para señalarlo por línea. La validación de forma va dentro de `change`, después de
    que el ejecutor autorice, así que un rol sin permiso recibe 403 antes que 422.
  - Bajo bloqueo, artículo inexistente → 422 por línea; inactivo → 409 `ArticleInactive` con los
    SKU en `detail` y `errors["lines[i].articleId"]`.
  - El detalle ordena líneas, reservas y asignaciones por Id (orden de `Guid` de .NET, en memoria).
    El contexto ordena artículos por SKU y ubicaciones y lotes por código.
  - `ICustomerOrderReader` lee sin seguimiento en la transacción del ejecutor y no toma bloqueos.
    Las pruebas están en T010.
- [X] T009 [P] ⚑ commit aparte (archivo de A). En
  `backend/src/Inventario.Api/Infrastructure/Http/MasterDataProblemMapper.cs`, añadir una rama:
  2601/2627 cuyo mensaje contenga `UX_CustomerOrders_ExternalReference` → 409 `DuplicateCode` con
  `errors.externalReference`. Va **antes** de la rama genérica de `_Code`; nada más cambia (D-10).
  **Hecho (2026-11-20)**: una rama de cinco líneas, con su comentario. La prueba que la cubre está en
  `CustomerOrderTests` (T010) y provoca la violación real del índice.
- [X] T010 [P] [US2] [INT-HU05-01] [INT-HU05-02] Crear
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

  **Hecho (2026-11-20)**: `CustomerOrderTests`, 15 pruebas. Rojo previo anotado: las 15 fallaban
  porque las rutas no existían (404; las carreras agotaban la barrera) y el mapper devolvía `null`.
  Tras T008 quedaba roja solo la del índice único, que pasó con T009. Resultado: suite 170/1/0
  (línea base 155/1/0), modelo sin cambios y carreras en verde tres veces.
  - **Habitual, error y permisos**: S-08 (alta, replay idéntico con `Location` y `ETag`, GET con el
    mismo `ETag` y cuerpo, definición bloqueada), duplicado « ped-… » → 409, S-09 (422 por línea,
    cabecera vacía, 1,5 en artículo entero, artículo inexistente, inactivo → 409 con SKU; nada
    creado), 403 para ResponsableAlmacen y GestorMaestros al crear, 200 al leer, Administrador 403,
    sin sesión 401, 404 en detalle y contexto.
  - **ETag**: renombrar y desactivar el artículo no cambia cuerpo ni `ETag`, y el contexto sí lo
    refleja con `orderVersion` igual. La **salida simulada** (regla 4: `ShippedQuantity` y cabecera
    en la misma transacción) cambia el `ETag`, y `orderVersion` lo sigue.
  - **Contrato de escritores**: `PUT` de definición → 409 `ArticleDefinitionLocked`, contexto del
    artículo con `References`, `DELETE` → 409 `HistoryPreventsDeletion` (sin 503).
  - **Carreras** en ambos órdenes contra desactivación, `PUT` de definición y `DELETE`.
  - T009: la violación real del índice se traduce a 409 `DuplicateCode`.
  - `ICustomerOrderReader` dentro del ejecutor, sin reservas (mínimo de S-18).

  **Desviaciones**:
  - Alta primero frente a `PUT` de la definición o `DELETE`: la primera referencia renueva la
    versión del artículo, así que el escritor con el `If-Match` leído antes recibe **412**. Con la
    versión actual recibe 409 (`ArticleDefinitionLocked` o `HistoryPreventsDeletion`). Es el mismo
    patrón que `MasterDataLifecycleConcurrencyTests` de A.
  - En la carrera con la desactivación, un pedido previo fija la definición para que la versión del
    artículo no cambie. Así la desactivación se decide solo por el bloqueo.

  **Queda para T012/T014** (necesitan reservar o liberar):
  - T012: el `ETag` cambia tras una reserva real; S-18 con asignaciones.
  - T014: el `ETag` cambia tras una liberación real; la prueba de la ubicación desactivada (exige
    una reserva liberada por completo y la ubicación vaciada).

  Las carreras de reserva y liberación ya estaban en T012/T014.

**Checkpoint**: se crean y consultan pedidos; HU-03 dispone del lector.

---

## Phase 5: User Story 3 — Reservar existencias para una línea de pedido (P2)

**Objetivo**: reservar y consultar compromisos. **Prueba independiente**: S-10.

- [X] T011 [US3] [INT-HU05-01] Depende de T006 y T008. Crear en
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

  **Hecho (2026-11-20)**:
  - `ReservationCommands.ReserveAsync`, `ReservationQueries` (`GET /reservations/{id}` y
    `GET /stock/reservations`, en SNAPSHOT) y `ReservationDtos`, con las tres rutas en
    `StockEndpoints`.
  - Antes de los bloqueos solo se leen relaciones inmutables: la línea (404 si no existe), su
    pedido y su artículo, y los lotes. Después se piden `customer-order` y `reservation` en una
    sola llamada, luego las ubicaciones y por último el artículo.
  - Tras los bloqueos, en este orden: `If-Match` del pedido, `Open`, lote del artículo (422
    `allocations[i].lotId`), ubicación activa (422 `allocations[i].locationId`), bucket repetido
    tras resolver `SIN-LOTE` (422), conteo abierto y pendiente (422 `errors.allocations`). Después,
    `StockCore` (`ArticleInactive`, `InsufficientStock`).
  - Un artículo sin seguimiento que aún no tiene `SIN-LOTE` → 409 `InsufficientStock`.
  - Soporte = referencia externa; contraparte = `CustomerReference`, nunca los dos campos. La
    cabecera del pedido se renueva marcando `State`; 201 sin `ETag`, con `Location`.
  - `JsonBody` (en `ReservationDtos.cs`) lee los cuerpos de reservar y liberar.
- [X] T012 [P] [US3] [INT-HU05-01] [INT-HU05-02] Crear
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

  **Hecho (2026-11-20)**: `ReservationTests`, 16 pruebas.
  - Rojo previo anotado: las 16 fallaban porque las rutas no existían (404, y las carreras
    agotaban la espera).
  - Con T011, 8 en verde y 8 carreras agotadas, por la barrera: `InventoryLocks` pide
    `sp_getapplock` con un comando ADO.NET propio, fuera de los interceptores de EF. Se cambió
    `LockBarrier` para retener la primera lectura EF de la fila (`Articles`/`Locations`) tras el
    bloqueo. Resultado: 16/16.
  - Cubre S-10 (incluido el `ETag` del pedido distinto tras reservar y el contexto con ubicación y
    lote) y S-11, además de errores, idempotencia, permisos, artículo desactivado, S-18 con
    asignaciones en dos buckets, V-05 (20 rondas) y la conciliación tras S-10, V-05 y las carreras.
  - **Carreras** en ambos órdenes contra la desactivación, el `PUT` de la definición y el `DELETE`
    del artículo y la desactivación de la ubicación. Usan `LockBarrier`, nuevo, porque los
    escritores que rechazan no guardan y `SaveBarrier` no los retiene.
  - `StockApiSupport` amplía sus ayudas (pedido, reserva y liberación por HTTP, escritores y
    conciliación) sin modificar `StockTestHarness`.
  - **Desviación**: con la reserva primero, la desactivación de la ubicación recibe 412 y no 409.
    El `ETag` de HU-02 incluye los totales y la reserva cambia el reservado. Con la desactivación
    primero, 409 `LocationNotEmpty`.

**Checkpoint**: reservas confirmadas visibles en la consulta; V-05 en verde.

---

## Phase 6: User Story 4 — Liberar reservas (P3) + fiabilidad (US5)

**Objetivo**: liberaciones parciales repetibles. **Prueba independiente**: S-14.

- [X] T013 [US4] Depende de T011. En `ReservationCommands.cs`, añadir **liberar** (D-06), con
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

  **Hecho (2026-11-20)**: `ReservationCommands.ReleaseAsync`, `ReleaseWrite` y `ReleaseResult`, y la
  ruta en `StockEndpoints`.
  - Antes de los bloqueos se leen la reserva (404), la línea y los lotes. Después se piden
    `customer-order`, `reservation-release:{releaseId}` y `reservation` en una sola llamada, luego
    las ubicaciones de la reserva y por último el artículo.
  - Tras los bloqueos: `If-Match` de la reserva, asignación de la reserva (422
    `allocations[i].allocationId`), remanente (409 `ReservedStockConflict`) y después `StockCore`.
    El `releaseId` ya aplicado lo rechaza el núcleo (409 `DocumentAlreadyConfirmed`).
  - La validación de forma rechaza una asignación repetida en la misma liberación (422).
  - Soporte `reservation:{id}` y contraparte `CustomerReference`. Se renuevan las cabeceras de la
    reserva y del pedido; `sourceDocumentId` = `releaseId`.
- [X] T014 [P] [US4] [INT-HU05-02] Crear `backend/tests/Inventario.IntegrationTests/Stock/ReleaseTests.cs` y
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

  **Hecho (2026-11-20)**: `ReleaseTests` (12) y `StockReliabilityTests` (1).
  - Rojo previo anotado: las 13 fallaban porque la ruta de liberar no existía (404; las carreras
    agotaban la espera; V-08 caía al llegar a la liberación).
  - Con T013 quedaba roja una carrera, por una expectativa equivocada. Con la liberación primero, la
    desactivación de la ubicación recibe 409 `LocationNotEmpty`, no 412: la revisión de HU-02 cubre
    las capacidades derivadas, no las cantidades. La prueba comprueba el orden con el reservado que
    lee el escritor (4 o 7). Se corrigió también el comentario equivalente de `ReservationTests`.
  - Cubre:
    - la extensión de V-04 (S-14), con el `ETag` de la reserva y del pedido distinto tras cada
      liberación, replay, `DocumentAlreadyConfirmed`, `IdempotencyConflict` por motivo y por
      cantidad, `ReservedStockConflict`, 422, 412 y 428;
    - S-15, con `DELETE` del artículo aún rechazado tras liberarlo todo;
    - la ubicación desactivada tras liberarlo todo y vaciarla, que no cambia el `ETag` del pedido y
      sí el contexto (pendiente de T010);
    - las carreras en ambos órdenes contra la desactivación, la definición y el borrado del
      artículo y la desactivación de la ubicación;
    - permisos (403 y 400 sin antiforgery) y V-08 en alta, reserva y liberación.
  - Evidencia: suite completa tres veces, 199/1/0, en `quickstart.md` § «Evidencia».

**Checkpoint backend**: S-01, S-03, S-05, S-08–S-12, S-14–S-16 y los permisos en verde tres
veces; evidencia registrada.

---

## Phase 7: Frontend /existencias (US1–US4, solo lo que sale en el vídeo)

- [X] T015 [US1] Depende de T006.
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

  **Hecho (2026-11-20)**:
  - `stock.models.ts`, `stock-api.service.ts`, `query/stock-query.page.ts` y `stock.css`. La ruta
    `''` sustituye el placeholder.
  - Las opciones de los filtros de ubicación y lote se toman de la consulta del artículo sin
    filtros y se conservan al filtrar. No hay otra llamada.
  - Spec `stock-query.page.spec.ts`, 7 casos, más de los dos pedidos: sin artículo no se consulta;
    totales desde `totals`; `includeEmpty`; filtros y «Actualizar»; «sin lote» e «Inactivo»;
    «No hay existencias para esta selección»; 404.
  - Rojo previo anotado: 7/7 fallaban con un esqueleto vacío del componente.
  - `operation-areas.spec.ts` (B, H0) marca `existencias` como área desarrollada.
  - Vitest 115/115 (línea base 108); `tsc` de app y spec limpio.
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
    - en el mismo `pending-operation.store.ts`, **ampliar la referencia** `PendingOperation` con el
      campo opcional `appRoute`. Hoy guarda `operationId`, `actorId`, `method`, `resource`,
      `payload` y `etag`, y al cerrar sesión solo conserva los cuatro primeros: ninguno lleva el id
      del pedido de una reserva o liberación. Cambios:
      - `begin()` acepta `appRoute`;
      - el manejador de `loggedOut` lo **conserva** junto a `operationId`, `actorId`, `method` y
        `resource`;
      - `restore()` lo valida: solo rutas `^/existencias/pedidos/(nuevo|{uuid})$`.

      `MasterDataMutationsService.mutate()` lo recibe como argumento opcional. Las intenciones de
      maestros y ubicaciones siguen igual (campo ausente).
    - en `frontend/src/app/core/operations/operation-status.component.ts`:
      - `route()` usa `operation.appRoute` cuando existe y, si no, las rutas actuales;
      - el panel muestra «Abrir pedido» con esa ruta, también sin cuerpo;
      - «Comprobar resultado» no cambia (`GET /operations/{operationId}`).

    Estos dos archivos van en **otro commit aparte** de la ampliación de `mutationPermission()`, al
    implementar. Nada más cambia en ellos. El comportamiento está en
    [stock-ui § Navegación y recuperación](contracts/stock-ui.md#navegación-y-recuperación-de-una-operación-pendiente).

    Specs unitarias:
    - la intención de HU-05 se restaura tras recargar y no se reutiliza con otro usuario, como
      `location-form.review.spec.ts`;
    - **cerrar sesión con una reserva pendiente y volver a entrar con el mismo usuario**:
      1. con `user-1`, reservar con respuesta perdida, de modo que queda pendiente con `appRoute`
         `/existencias/pedidos/{orderId}`;
      2. emitir `loggedOut` y comprobar que sessionStorage **no** contiene el cuerpo ni el
         `If-Match`, pero sí `operationId` y `appRoute`;
      3. recrear TestBed conservando sessionStorage y volver a iniciar sesión con `user-1`;
      4. el panel muestra «Abrir pedido» con `href` `/existencias/pedidos/{orderId}`, no ofrece
         «Repetir la misma intención», y «Comprobar resultado» hace un único
         `GET /api/v1/operations/{operationId}`;
      5. con `user-2`, la referencia no aparece.
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

- [X] T017 Depende de T005 y de la **fusión del PR #7 de A** (satisfecha, `c904824`; `CounterpartyNotApplicableReason` en
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

  **Hecho (2026-11-20)**:
  - `Features/Stock/Testing/StockSeedingEndpoints.cs`, mapeado por `MapStockEndpoints` sin tocar
    `Program.cs`.
  - La ruta solo existe si `Testing:EnableStockSeeding` es true (variable de entorno
    `Testing__EnableStockSeeding`) **y** el entorno no es Production.
  - Cuerpo `{ articleId, locationId, lotCode?, quantity }`; la huella es el cuerpo normalizado.
    `SupportNotApplicableReason` y `CounterpartyNotApplicableReason` llevan el motivo; nunca
    `CounterpartyReference`.
  - Tres pruebas en `StockQueryTests.cs`:
    - 404 por defecto;
    - 404 en Production con la opción activa, con control en Development, donde sin sesión → 401;
    - con la opción: 403 sin `Receiving.Record`, 200, replay, saldo y movimiento `Receipt` con
      ambos motivos.
  - Rojo previo anotado: las pruebas de la opción activa y de Production fallaban porque la ruta no
    existía. La de «por defecto» pasaba antes de implementar, como guarda.
  - **Desviación**: también se apaga en Production, además de la opción. Así lo pidió B (2026-11-20).
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

- [X] T020 ⚑ commit aparte (HU-02). En
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

  **Hecho (2026-11-20)**:
  - `ArticleLocationsAsync` delega en `StockBalanceReader`. `LocationQueries` recibe el lector por
    constructor. Se conservan el DTO, la ruta y el orden, con el desempate por Id de ubicación del
    lector.
  - El lector gana un parámetro opcional `activeLocationsOnly`, que filtra filas y totales y
    equivale a `includeInactiveLocations=false`.
  - El filtro de HU-02, `Physical ≠ 0 o Reserved ≠ 0`, equivale a `includeEmpty=false` porque
    0 ≤ reservado ≤ físico.
  - `DetailAsync`, `StockAsync` y `LocationEntityTags` no cambian.
  - Pruebas: S-02 en `StockQueryTests` y el filtro de activas en `StockBalanceReaderTests`. Rojo
    previo anotado: no compilaba por la falta del parámetro `activeLocationsOnly`. S-02 es de
    caracterización: verde antes del refactor, ejecutada junto a `ArticleLocationsTests`, y verde
    después. `ArticleLocationsTests` y `LocationReviewTests` no se tocan.

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
PR #7 de A (fusión, ✓ c904824) ─▶ T004 (migración) y T017
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
