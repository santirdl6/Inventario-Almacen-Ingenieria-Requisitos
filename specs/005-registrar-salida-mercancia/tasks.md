---

description: "Lista de tareas de HU-03 — Registrar la salida de mercancía"
---

# Tasks: HU-03 — Registrar la salida de mercancía

**Input**: documentos de diseño de `specs/005-registrar-salida-mercancia/`:
[plan.md](plan.md), [spec.md](spec.md), [research.md](research.md) (D-01…D-15),
[data-model.md](data-model.md), [contracts/](contracts/) y [quickstart.md](quickstart.md) (H-01…H-21, E2E-02).

**Presupuesto**: como máximo 20 tareas. Hay **18** (T001–T018), más la fase «Convergence», vacía
hasta `/speckit-analyze` y `/speckit-converge`, y «Si hay tiempo», fuera de la numeración.

**Iteración del 2026-11-21** (tras la revisión de tasks.md y la iteración del plan `786abc9`), sin
tareas nuevas:

- T004 incluye los consumos ligados a la operación que confirma y su caso de H-02.
- T011 añade la carrera de confirmar frente a `PUT` de definición.
- Se corrigen las marcas [P] y las dependencias de T010, T011 y T012.
- Se marca ⚠ PR #9 en T003, T006 y T018.
- T017 permite los tres archivos de fixtures de A con commit mínimo; cualquier otro archivo de A,
  preguntar.
- T014 y T016 añaden las pruebas de permisos de `/salidas`.

**Pruebas (TDD)**: cada tarea de código indica primero la prueba que debe fallar y **por qué**
(«rojo esperado»), que se anota en la tarea al hacerla, como en HU-05. Integración en SQL Server real
con la receta Docker de la [guía de HU-05](../004-consultar-existencias/quickstart.md#pruebas-automatizadas-backend)
(no hay SDK .NET local). Carreras con **conexiones independientes en ambos órdenes**, con
`Stock/LockBarrier.cs` y `SaveBarrier` de HU-05. Cubren:

- **INT-HU03-01** (contrato de escritores: borrador, retirada, carreras con definición y borrado);
- **INT-HU03-02** (servir artículos inactivos y carrera con la desactivación);
- **reglas 3 y 4** del [contrato HU-03](../004-consultar-existencias/contracts/hu03-integration.md#3-reglas-que-hu-03-debe-respetar)
  (consumo de reservas y renovación de la versión del pedido y de las reservas);
- la invariante **Σ consumos = `ConsumedQuantity`** (data-model, invariante 2) y el resto de H-14.

Sin tareas de rendimiento (SC-006) ni de validación con operarios (SC-007): ver «Desviaciones
registradas».

**Organización**: por historias de la spec. US1 (preparar y confirmar) y US2 (reservas) comparten el
comando de confirmación, así que T009–T010 llevan ambas etiquetas. US3 (inactivos) y US5
(autorización y fiabilidad) no tienen fase propia: sus pruebas van en T008, T010 y T011.

## Format: `[ID] [P?] [Story] Descripción`

- **[P]**: puede hacerse en paralelo (archivos distintos, sin dependencias pendientes).
- **[Story]**: historia de la spec (US1–US5).
- **⚑ commit aparte**: toca archivos de A, comunes o de B fuera de la feature (HU-05, H0). Va en su
  propio commit para separarlo en la revisión del PR.
- **[INT-HU03-01] / [INT-HU03-02]**: tarea receptora del
  [backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#hu-03--salidas--persona-b).
- **🔀 PR #9**: depende de la base de HU-05 tal como se fusione (ver «Dependencias externas»).
- **⚠ PR #9**: edita archivos de HU-05 (de B) que pueden cambiar en la revisión del PR #9 o en su
  fusión. Riesgo de conflicto al integrar con merge; antes de empezar, comprobar si han cambiado.

## Path Conventions

Backend en `backend/src/Inventario.Api/` y pruebas en
`backend/tests/Inventario.IntegrationTests/Shipping/`. Frontend en `frontend/src/app/` y E2E en
`frontend/e2e/`. Rutas según [plan.md § Project Structure](plan.md#project-structure).

---

## Phase 1: Coordinación con A (pendiente explícita, no bloquea el código)

- [ ] T001 ⏸ **PENDIENTE: solicitar la revisión de A en el PR de HU-03** (COORD-01), sin bloquear la
  implementación, de:
  - (a) los diffs de [plan.md § Cambios al modelo común](plan.md#cambios-al-modelo-común-pendientes-de-a):
    P-01 (borrador eliminable, salida confirmada inmutable, `OwnReservationConsumed` derivado),
    P-02 (tabla de consumos), P-03 y P-04 (`api.md`), P-05 (`stock-operations.md`), P-07 (tarea
    receptora de HU-04 en `integration-tasks.md`) y P-08 (evidencia en el quickstart común);
  - (b) los cambios en archivos de A de T013 (P-06) y, si llegara a hacer falta, los de T017;
  - (c) los triggers de D-06 como sustituto del `DENY DELETE` en `Shipments`/`ShipmentLines`, y
    los consumos ligados a la operación que confirma, que impiden insertarlos tras confirmar
    (iteración del plan del 2026-11-21, pedida por A al revisar P-01 y P-02).

  Registrar decisión, fecha y revisor en `plan.md` (nuevo apartado «Estado de la revisión de A»).
  Si A rechaza algo, ajustar solo las tareas afectadas: si rechaza P-02, T004 y T009 pasan a la
  invariante agregada de D-08 (alternativa).
- [ ] T002 ⏸ **PENDIENTE, bloqueada por T001 y por la fusión del PR #9**: aplicar los diffs aprobados
  P-01…P-05, P-07 y P-08 en `.specify/memory/project/data-model.md`,
  `.specify/memory/project/contracts/api.md`, `.specify/memory/project/contracts/stock-operations.md`,
  `.specify/memory/project/quickstart.md` (evidencia, tras T018) y
  `specs/001-reparto-specs-inventario/integration-tasks.md`. P-03 y P-05 amplían P-03 y P-06 de
  HU-05. A los aprobó y se aplicaron en `2894dad`, y ya están en esta rama por el merge `ca9f6db`.
  Por eso los diffs de HU-03 se redactan sobre el texto actual de esos documentos (analyze-1 H6).
  La fusión del PR #9 sigue siendo necesaria para que la base de HU-03 sea la definitiva. **No se aplican
  hasta la aprobación de A** (AGENTS.md, «Cambios compartidos»). Al tener evidencia (T018), marcar
  INT-HU03-01 e INT-HU03-02 en el backlog. ⚑ commit aparte.

---

## Phase 2: Foundational (bloquea todas las historias)

**Objetivo**: dominio, tablas, triggers y registro de la feature sobre la cadena única de migraciones.

- [X] T003 ⚠ PR #9 Línea base y dominio. Depende de nada. Riesgo: `Reservation.cs` y
  `CustomerOrder.cs` de HU-05.
  - Primero, ejecutar la suite backend con la receta Docker y anotar el número de pruebas en verde
    (última conocida en HU-05: 206/1/0) y Vitest (143/143).
  - **Rojo esperado**: crear `backend/tests/Inventario.IntegrationTests/Shipping/Hu03DomainTests.cs`
    (H-01) antes del código: no compila porque no existen `Shipment`, `ReservationAllocation.Consume`,
    `CustomerOrderLine.Ship` ni `CustomerOrder.DeriveFulfilment`.
  - Crear `backend/src/Inventario.Api/Domain/Shipping/Shipment.cs` con:
    - `Shipment`: «State `Draft` o `Confirmed`»; «Solo `Draft → Confirmed`»; `OccurredAt`
      obligatoria; `Confirm(operationId, actorId, recordedAt)`; `ReplaceLines(...)` solo en `Draft`.
    - `ShipmentLine`: «Quantity decimal(18,3) > 0»; una línea por `(CustomerOrderLineId,
      LocationId, LotId)` dentro de la salida («UX_ShipmentLines_Shipment_Bucket»); entre 1 y 100.
    - `ShipmentReservationConsumption`: «Quantity > 0»; una por `(ShipmentLineId,
      ReservationAllocationId)`.
  - ⚑ commit aparte (archivos de B de HU-05): en `Domain/Stock/Reservation.cs`,
    `ReservationAllocation.Consume(quantity)` («`q > 0`, `q ≤ Remaining`», recalcula
    `RemainingQuantity` como `Release`); en `Domain/Stock/CustomerOrder.cs`,
    `CustomerOrderLine.Ship(quantity)` («`Shipped ≤ Ordered`») y
    `CustomerOrder.DeriveFulfilment()` (`Open → Fulfilled` si todas las líneas quedan con pendiente
    0). `Hu05DomainTests` sigue sin cambios.
  - Sin configuraciones EF todavía: las entidades nuevas no entran en el modelo, así que
    `has-pending-model-changes` sigue limpio y la suite queda en verde.

  (D-04; data-model; FR-002, FR-008, FR-009.)

  **Hecho (2026-11-21)**:
  - Línea base: 207 aprobadas, 1 omitida, 0 fallidas (suite sobre `363de56`; desde entonces, la
    rama solo cambió documentos).
  - Rojo previo: `Hu03DomainTests` no compilaba, por CS1061 en `Consume`, `Ship` y
    `DeriveFulfilment` y por CS0234, al no existir `Domain.Shipping`.
  - Verde: 18 pruebas (H-01); suite 225/1/0; sin cambios de modelo.
  - Dos commits: `e5d5d96` (⚑ HU-05) y el de `Shipment.cs`.
- [X] T004 [INT-HU03-01] 🔀 PR #9 Esquema completo **en un solo commit** (los triggers y el modelo no
  se separan sin romper `has-pending-model-changes`). Depende de T003.
  - **Rojo esperado**: crear `Shipping/Hu03SchemaTests.cs` (H-02) y ampliar el conjunto exacto de
    `Stock/Hu05SchemaTests.cs` con `ShipmentLine`. Fallan porque las tablas, los triggers y el
    `DENY` no existen y el conjunto de tipos con FK a `Article` no incluye `ShipmentLine`.
  - **Consumo tardío** (H-02, iteración del plan del 2026-11-21), con `inventory_runtime`:
    - tras confirmar S con la operación X, un `INSERT` de consumo con otra operación Y falla en la
      base de datos y el número de consumos no cambia;
    - confirmar con X un borrador que ya tiene un consumo con Y se rechaza;
    - en una transacción manual, confirmar con X en los dos órdenes (`UPDATE` y luego `INSERT`, e
      `INSERT` y luego `UPDATE`) se admite.
    - **Rojo previo**: escribir primero la prueba con la tabla de consumos y su `OperationId` pero
      sin el trigger nuevo: el `INSERT` con Y se admite. Anotarlo antes de añadir el trigger en el
      mismo commit. Lo que confirma por la API lo cubre T010 (H-08).
  - Configuraciones en `Infrastructure/Persistence/Configurations/` (`ShipmentConfiguration.cs`,
    `ShipmentLineConfiguration.cs`, `ShipmentReservationConsumptionConfiguration.cs`) según
    [data-model](data-model.md):
    - `Shipments`: «CHECK State IN ('Draft','Confirmed')»; «`State = 'Confirmed'` ⇔ `OperationId IS
      NOT NULL`»; índice único filtrado de `OperationId`; `rowversion`; FK `CustomerOrders`
      (Restrict); `IX_Shipments_CustomerOrder (CustomerOrderId, RecordedAt DESC, Id)`.
    - `ShipmentLines`: FK `Shipments` (Cascade), `CustomerOrderLines`, `Articles` y `Locations`
      (Restrict); FK compuesta `(ArticleId, LotId) → Lots (ArticleId, Id)`; «CHECK Quantity > 0»;
      `UX_ShipmentLines_Shipment_Bucket`. Sin columna `OwnReservationConsumed` (D-06, D-08).
    - `ShipmentReservationConsumptions`: FK `ShipmentLines` y `ReservationAllocations` (Restrict);
      «CHECK Quantity > 0»; único `(ShipmentLineId, ReservationAllocationId)`; `OperationId`
      obligatorio, sin FK, que la confirmación rellena con `context.OperationId` (research D-06,
      iteración). La cabecera **no** necesita columna nueva: `Shipments.OperationId` ya es la
      operación que confirma.
    - `HasTrigger("TR_Shipments_ConfirmedImmutable")`, `HasTrigger("TR_ShipmentLines_ConfirmedImmutable")`
      y `HasTrigger("TR_ShipmentReservationConsumptions_ConfirmingOperation")` en sus tablas (EF no
      puede usar `OUTPUT` sin `INTO` con triggers; plan, riesgos).
  - `Features/Shipping/ShippingDocuments.cs`: tipo canónico `shipment` (D-03).
  - `Features/Shipping/ShipmentArticleUsage.cs`: `ShipmentArticleUsageContributor`
    (`IArticleUsageContributor` e `IArticleFirstReferenceContributor` para `ShipmentLine`; «sin uso»
    solo si la migración no está aplicada) y `ShipmentSchema.Migration` (D-09).
  - ⚑ en el mismo commit, por ser inseparable: registrar el contributor en
    `Features/Stock/Core/StockArticleUsage.cs`, `AddStockCore` (archivo de B, H0), no en
    `AddShipping` (lección 4: si no, HU-08 responde 503 `DependencyUnavailable`).
  - `dotnet ef migrations add HU03_Shipments` sobre `20261120113458_HU05_CustomerOrdersReservations`
    (D-10). A mano, en `Up`, los tres triggers de D-06 y el `DENY`:
    - `AFTER UPDATE, DELETE` en `Shipments`: `ROLLBACK` + `THROW` si la fila estaba `Confirmed`.
      Además, en la transición `Draft → Confirmed`, si ya hay consumos de sus líneas con otro
      `OperationId`.
    - `AFTER INSERT, UPDATE, DELETE` en `ShipmentLines`: igual si la cabecera está `Confirmed`.
    - `AFTER INSERT` en `ShipmentReservationConsumptions`: si la cabecera de la línea está
      `Confirmed` y su `OperationId` es distinto del de la fila.
    - `DENY UPDATE, DELETE ON OBJECT::dbo.ShipmentReservationConsumptions TO inventory_runtime`.
    - `Down` en orden inverso.
  - `Features/Shipping/ShipmentEndpoints.cs` con `AddShipping()` y `MapShippingEndpoints()` vacíos.
  - Verificar: `has-pending-model-changes` sin cambios, cadena desde cero, revertir y reaplicar;
    suite completa en verde y sin `DependencyUnavailable` en HU-08.
  - **Si la revisión del PR #9 cambia la migración de HU-05, o A fusiona otra antes**, regenerar
    `HU03_Shipments` sobre la última (plan global §5).

  (D-06 con su iteración, D-08–D-10; data-model, invariante 7; FR-004, FR-013, FR-017.)

  **Hecho (2026-11-21)**, en un solo commit:
  - Configuraciones EF con `HasTrigger` en las tres tablas.
  - `ShippingDocuments`, `ShipmentArticleUsageContributor` registrado en `AddStockCore`, y
    `AddShipping()` y `MapShippingEndpoints()` vacíos.
  - Migración `20261121114907_HU03_Shipments`, sobre `HU05_CustomerOrdersReservations`, con los
    tres triggers (solo `THROW`, decisión M3) y el `DENY`.
  - `Hu05SchemaTests`: conjunto exacto con `ShipmentLine`, y HU-05 deja de ser la última migración.
  - Rojo previo, en dos etapas:
    - (1) las 10 pruebas de `Hu03SchemaTests` fallaban, porque las entidades no estaban en el
      modelo ni existía la migración;
    - (2) con la tabla de consumos y su `OperationId` pero sin el trigger nuevo ni el complemento,
      el `INSERT` tardío con otra operación se admitía y confirmar sobre un consumo ajeno también
      («No exception was thrown»).
  - Verde: 14/14 en las pruebas de esquema de HU-03 y HU-05. Con `THROW`, el error es 51304 y no
    3609, `RollbackAsync` termina sin excepción y la conexión sigue utilizable.
  - Suite 235/1/0; `has-pending-model-changes` limpio; cadena desde cero (fixture); revertir a
    HU-05 y reaplicar con `dotnet ef database update` sobre una base temporal, ya eliminada.
  - Desviación: al borrar una salida confirmada con consumos, la FK (547) rechaza antes que el
    trigger. Es igual de seguro y está anotado en D-06.
- [X] T005 ⚑ commit aparte (archivo común). En `backend/src/Inventario.Api/Program.cs`, añadir el
  `using Inventario.Api.Features.Shipping;`, `builder.Services.AddShipping();` y
  `api.MapShippingEndpoints();` junto a los de Stock, sin otro cambio. Depende de T004. Sin rojo
  propio: la suite completa debe seguir en verde (guarda). **Hecho (2026-11-21)**: tres líneas en `Program.cs`; suite 235/1/0, igual que T004; `has-pending-model-changes` limpio.

**Checkpoint**: migración aplicada, triggers probados, suite previa en verde y feature registrada
sin rutas.

---

## Phase 3: User Stories 1, 2 y 3 — Preparar, respetar reservas y confirmar (P1/P2) 🎯 MVP

**Objetivo**: localizar el pedido, preparar el borrador y confirmar la salida con consumo de
reservas. **Prueba independiente**: H-08 (sin reservas) y H-10 (V-04).

- [X] T006 [P] [US1] ⚑ commit aparte (archivos de B de HU-05) ⚠ PR #9. Búsqueda exacta del pedido
  (D-11, FR-001a). Depende de T005. Riesgo: `CustomerOrderQueries.cs`, `CustomerOrderDtos.cs` y
  `StockEndpoints.cs`. La revisión del PR #9 ya cambió el contexto del pedido (C3,
  `creationOperationId`).
  - **Rojo esperado**: `Shipping/CustomerOrderLookupTests.cs` (H-03): « ped-0042 » → 200 con id y
    estado; inexistente → 404; vacía o > 64 → 422; sin sesión → 401; Administrador → 403;
    GestorMaestros → 200. Falla con 404 en todas porque la ruta no existe (cae en `/{id:guid}`).
  - `Features/Stock/CustomerOrders/CustomerOrderQueries.cs`: `LookupAsync(externalReference)` con
    `StockDocuments.NormalizeReference` y la intercalación CI de la columna; DTO
    `CustomerOrderLookup { id, externalReference, state }` en `CustomerOrderDtos.cs`.
  - `Features/Stock/StockEndpoints.cs`: `GET /customer-orders/lookup?externalReference=` con
    `Inventory.Read`, mapeada **antes** de `/{id:guid}`.

  (US1.11; FR-001a; [contrato](contracts/shipping-api.md#get-customer-orderslookup).)

  **Hecho (2026-11-21)**:
  - `LookupAsync`, `CustomerOrderLookup` y `GET /customer-orders/lookup` (con `no-store`).
  - Rojo previo: las 3 pruebas de `CustomerOrderLookupTests` fallaban porque la ruta no existía.
    Respondía el 404 genérico, sin id ni detalle propio.
  - Verde: 3/3 (H-03). Cubre « referencia » en minúsculas y con espacios; inexistente y parcial →
    404; vacía, en blanco, de 65 caracteres o ausente → 422; GestorMaestros y ResponsableAlmacen →
    200; Administrador → 403; sin sesión → 401.
  - Suite 238/1/0; sin cambios de modelo.
- [X] T007 [US1] Borrador: crear, editar, eliminar y detalle. Depende de T005.
  - En `Features/Shipping/`: `ShipmentCommands.cs` (crear, editar y eliminar; la confirmación es
    T009), `ShipmentQueries.cs` (`DetailAsync` en `SNAPSHOT`, solo datos propios, líneas **por Id**),
    `ShipmentDtos.cs` (`ShipmentWrite.Read(body)` normalizado como huella: ids en formato D,
    cantidades con tres decimales, `occurredAt` en UTC) y rutas en `ShipmentEndpoints.cs`:
    `POST /shipments`, `GET /shipments/{id}` (con `ETag` y `no-store`), `PUT` y `DELETE
    /shipments/{id}`, todas con `LocationEndpoints.Key(context)`.
  - Crear: id decidido fuera de `change`; validación de forma (todos los errores, 422 por línea);
    `DocumentsAsync([shipment:{id}])` → `ArticlesAsync` → pedido existente (404) y `Open` (409
    `CustomerOrderNotOpen`) → líneas del pedido, lote del artículo, bucket con saldo (409
    `InsufficientStock`), fracciones, Σ ≤ pendiente (422) → `ArticleReferenceTracker.MarkAsync` →
    `Add` → `SaveChangesAsync` → 201 con `ETag` y `Location`.
  - Editar: mismos bloqueos (artículos anteriores y nuevos), `Draft` (409
    `DocumentAlreadyConfirmed`), `EntityTags.RequireMatch` (428/412), sustituir líneas, `Touch`.
  - Eliminar: bloqueos de la salida y de sus artículos, `Draft`, `If-Match`, `Remove` → 204 con
    `ResultBody` null.
  - Pruebas en T008.

  (D-02, D-03, D-12; US1.1, US1.8–US1.9; FR-001–FR-005.)

  **Hecho (2026-11-21)**:
  - `ShipmentDtos`, `ShipmentCommands` (crear, editar y eliminar), `ShipmentQueries.DetailAsync` y las
    rutas `POST`, `GET`, `PUT` y `DELETE /shipments` en `ShipmentEndpoints`.
  - Bloqueos: `shipment:{id}` → artículos (anteriores y nuevos al editar). Después,
    `ArticleReferenceTracker.MarkAsync`.
  - **Desviación TDD**: para que la tarea tenga rojo propio, el núcleo de H-04…H-07 se escribe ya
    en `Shipping/ShipmentDraftTests.cs` (6 pruebas). T008 amplía ese archivo con las carreras, la
    retirada por edición, los permisos completos y `ShippingApiSupport.cs`.
  - Rojo previo: 6/6 respondían 404, porque las rutas no existían.
  - Verde: 6/6. Cubre crear (201, `ETag`, `Location`, sin cambios en stock, servido ni diario;
    replay idéntico), detalle propio con líneas por Id; forma, línea ajena, lote ajeno, fracción, Σ
    > pendiente (422), bucket sin existencias (409) y pedido inexistente (404); `PUT` con `ETag`
    vigente (200), viejo (412), sin `If-Match` (428) y con otro pedido (422); `DELETE` (204,
    replay 204, otra clave 404); escrituras 403 para GestorMaestros y ResponsableAlmacen, y lectura
    403 para Administrador.
  - Suite 244/1/0; sin cambios de modelo.
- [X] T008 [P] [US1] [US5] [INT-HU03-01] `Shipping/ShippingApiSupport.cs` y
  `Shipping/ShipmentDraftTests.cs`. Depende de T007. Escribir las pruebas contra las rutas mapeadas
  con cuerpo `NotImplemented` antes de completar T007 y anotar el rojo (501/500 en todas).
  - Soporte: sesión real por rol, pedidos y reservas con los comandos reales de HU-05
    (`StockApiSupport`), existencias con `StockCore` (Receive). Sin dobles de HU-05.
  - **Habitual**: H-04 (201 + `ETag`; existencias, servido y diario sin cambios; replay idéntico).
  - **Error**: H-05 (cada rechazo de forma y de base, sin filas creadas) y H-06 (`PUT` con `ETag`
    vigente, viejo, sin `If-Match`; `PUT`/`DELETE` de una confirmada → 409, tras T009).
  - **INT-HU03-01, contrato de escritores** (H-07): `DELETE` → 204, sin filas ni movimientos;
    `ShipmentArticleUsageContributor` deja de ver la línea; la del pedido sigue fijando la
    definición y rechazando el borrado; replay → 204; otra clave → 404.
  - **Retirada por edición** (H-07, analyze-1 H4): en un pedido con líneas de los artículos X e Y,
    un `PUT` del borrador que quita la única línea de X deja la de Y. El contributor deja de ver X
    en la salida y lo sigue viendo en Y. X conserva la definición bloqueada y el borrado rechazado
    por la línea de pedido. La marca histórica no se revierte.
  - **Carreras (INT-HU03-01), `LockBarrier`, ambos órdenes**: crear borrador / `PUT` de definición
    del artículo y crear borrador / `DELETE` del artículo (el escritor de A recibe 409 o 412; el
    artículo con referencias no se borra); **editar borrador / `PUT` de definición y editar
    borrador / `DELETE` del artículo** (analyze-1 H4); eliminar borrador / `DELETE` del artículo.
  - **Permisos** (parte de H-20): sin sesión 401; sin antiforgery 400; GestorMaestros y Comprador →
    403 en escrituras y 200 en `GET`; Administrador → 403 también en lectura.

  (US1.1, US1.6–US1.9, US5.1; FR-002–FR-005, FR-019.)

  **Hecho (2026-11-21)**: `ShippingApiSupport.cs` y `ShipmentDraftTests.cs`, que tiene 18 pruebas (las 6 de T007 más 12):
  - M1: `IdempotencyConflict` al crear, editar y eliminar.
  - H-07 completo: el contributor deja de ver la línea; la línea de pedido sigue fijando la definición
    y rechazando el `DELETE` (409 `HistoryPreventsDeletion`).
  - Retirada por edición: un `PUT` que quita la única línea del artículo X.
  - 10 carreras con `LockBarrier` y conexiones independientes, en ambos órdenes: crear/editar
    borrador frente a `PUT` de definición y `DELETE` del artículo, y eliminar borrador frente a
    `DELETE`.
  - H-20 del borrador: GestorMaestros, Comprador y ResponsableAlmacen → 403 al escribir y 200 al
    leer; Administrador → 403; sin sesión → 401; sin antiforgery → 400.
  - **Rojo**: no hubo rojo natural, porque T007 ya implementaba el comportamiento. Se comprobó con
    dos mutaciones temporales, sin commit: (1) sin `ArticlesAsync` al crear, fallan las 2 carreras
    en las que el borrador va primero; (2) con el contributor siempre «sin uso», fallan las 2 de
    retirada. Después se restauraron los archivos.
  - `PUT` y `DELETE` de una salida confirmada → 409 queda para T010, porque necesita `/confirm`.
  - Suite 256/1/0; sin cambios de modelo.
- [X] T009 [US1] [US2] Confirmar (D-04, D-05, D-07). Depende de T007.
  - `ShipmentCommands.ConfirmAsync` y `POST /shipments/{id}/confirm` (`Shipping.Record`), cuerpo
    `{ orderVersion }` en la huella:
    1. Antes de bloquear, relaciones inmutables: salida → pedido; líneas → línea de pedido, bucket y
       lote; reservas activas de esas líneas con remanente en esos buckets.
    2. `DocumentsAsync([customer-order:{o}, reservation:{r…}, shipment:{s}])` en **una** llamada →
       `LocationsAsync` → `ArticlesAsync` (D-03).
    3. Salida (404), `Draft` (409 `DocumentAlreadyConfirmed`), `If-Match` (428/412),
       `ICustomerOrderReader.ReadAsync` y `orderVersion` (412), `Open` (409), Σ ≤ pendiente (422),
       `IsInOpenCountAsync` (409 `CountScopeFrozen`).
    4. `consume = min(Q, Σ Remaining)` por bucket, repartido por Id de asignación (regla 2);
       regla 5 → 409 `ReservedStockConflict` con la cantidad a liberar.
    5. `StockCore.PrepareShipment` (`Source = ("shipment", id)`, `OccurredAt` de la salida,
       `Reason = "Salida del pedido {ExternalReference}"`, `SupportReference = ExternalReference`,
       `CounterpartyReference = CustomerReference`) y `ApplyAsync`.
    6. `Consume` + `DeriveState()` + `Touch` por reserva afectada; `Ship` por línea de pedido;
       `DeriveFulfilment()` y `Touch` del pedido **siempre**; filas de consumo con
       `OperationId = context.OperationId`; `Confirm` de la salida con el mismo id (el orden de EF
       entre ambos no importa, D-06). Sin `SaveChanges` dentro de `change`; 200 sin `ETag` (HU-05 D-08).
  - Pruebas en T010 y T011.

  (US1.3–US1.5, US2.1–US2.7, US3.1; FR-007–FR-012, FR-014–FR-016, FR-020.)

  **Hecho (2026-11-21)**:
  - `ShipmentCommands.ConfirmAsync`, `ShipmentConfirmWrite`, `ShipmentConfirmation` y
    `POST /shipments/{id}/confirm`, con los pasos 1–8 y el orden de rechazos del contrato.
  - Consumos creados con `context.OperationId`, el mismo de `Shipment.Confirm`; sin `SaveChanges`
    dentro de `change`.
  - **Desviación TDD**, como en T007: para que la tarea tenga rojo propio, el núcleo va en
    `Shipping/ShipmentConfirmTests.cs` (H-08 sin reservas y H-10, V-04). T010 completa el archivo.
  - Rojo previo: 2/2 fallaban porque `/confirm` no existía.
  - Verde: 2/2. H-08: físico 4, servido 6, `Fulfilled`, un movimiento `Shipment` con
    `ShipAvailable` 6, soporte = referencia y contraparte = cliente. H-10: propia 4 `Consumed`, ajena
    3 intacta, consumo de 4 con `ShipReserved` 4 y `ShipAvailable` 2, y conciliación.
  - Suite 258/1/0; sin cambios de modelo.
- [X] T010 [P] [US1] [US2] [US3] [US5] [INT-HU03-02] `Shipping/ShipmentConfirmTests.cs`. Depende de
  T009, de T008 (`ShippingApiSupport.cs`) y de T006 (pedido de H-03). [P] solo frente a T011:
  archivos de prueba distintos y las mismas dependencias. **Rojo esperado**: escribirlas con `/confirm` respondiendo 501 y
  anotarlo.
  - **Habitual**: H-08 (movimiento con soporte `PED-A`, contraparte = cliente y
    `CounterpartyNotApplicableReason` null), H-09 (parcial y dos orígenes, `Fulfilled` al final).
  - **Reservas (V-04)**: H-10 y H-11 (regla 5; tras liberar con HU-05, confirma).
  - **Regla 3**: consumo por asignación en orden de Id con dos reservas de la misma línea en el mismo
    bucket; `Reservation.State` `Active` → `Consumed`; `ConsumedQuantity` solo crece.
  - **Regla 4 / FR-012** (H-12): el `ETag` del pedido cambia con y sin reserva; el de la reserva solo
    si se consume; reservar y liberar con `ETag` viejo → 412; confirmar con `orderVersion` viejo →
    412.
  - **S-18 completo** (H-13): `ICustomerOrderReader` antes y después de servir, comparado con
    `GET /customer-orders/{id}`.
  - **Conciliación** (H-14): las siete invariantes de data-model, incluidas **Σ consumos =
    `ConsumedQuantity`** por asignación y que todo consumo lleve el `OperationId` de su cabecera, Σ consumos = `ShipReserved` por línea y
    `AssertJournalMatchesBalanceAsync`.
  - **INT-HU03-02** (H-15): servir con el artículo desactivado → 200 con los mismos límites; alta
    de pedido nuevo → 409 `ArticleInactive`. La indicación «inactivo» del contexto se prueba en T012.
  - **V-07** (H-18) y operario sin `Reservations.Manage` que consume reservas (parte de H-20).

  (US1.3–US1.5, US2, US3, US5.2; FR-007–FR-016, FR-021; SC-001, SC-002, SC-005.)

  **Hecho (2026-11-21)**: `ShipmentConfirmTests` tiene 11 pruebas: las 2 de T009 y 9 nuevas.
  - H-09 (parcial y dos orígenes, después `Fulfilled`).
  - H-10 continuación: A con posible 1 → 409 con «disponible 1.000»; B con 5 sobre posible 4 → 409.
  - H-11 (regla 5: 409 `ReservedStockConflict` con «liberar 4.000»; tras liberar con HU-05,
    confirma).
  - Regla 3: dos reservas de la misma línea y bucket, consumo por Id de asignación, `Consumed` y
    `Active`, `ConsumedQuantity` solo crece.
  - H-12 (regla 4): el pedido cambia con y sin reserva y solo la reserva consumida; reservar y
    liberar con `ETag` viejo → 412; `orderVersion` viejo → 412.
  - H-13 (S-18 completo, antes y después, con dos buckets y tres reservas).
  - H-15 (INT-HU03-02): preparar y confirmar con el artículo inactivo; los mismos límites; alta nueva
    → 409 `ArticleInactive`.
  - H-18 (V-07): replay idéntico, misma clave con otra `orderVersion` → `IdempotencyConflict`, otra
    clave → `DocumentAlreadyConfirmed`, y `PUT`/`DELETE` de la confirmada → 409, pendiente de T008.
  - H-20: GestorMaestros, Comprador y ResponsableAlmacen → 403; el Operario sin
    `Reservations.Manage` confirma consumiendo.
  - H-14: `AssertShipmentInvariantsAsync` (invariantes 1–7, incluida Σ consumos =
    `ConsumedQuantity`) en cada escenario confirmado.
  - **Rojo**: no hubo rojo natural, porque T009 ya implementaba el comportamiento. Se comprobó con
    mutaciones temporales, sin commit: sin `Touch` del pedido falla H-12; consumiendo por Id
    descendente falla la regla 3. Después se restauró el archivo.
  - Suite 267/1/0; sin cambios de modelo.
- [X] T011 [P] [US2] [US3] [US5] [INT-HU03-01] [INT-HU03-02] `Shipping/ShipmentConcurrencyTests.cs` y
  `Shipping/ShipmentReliabilityTests.cs`. Depende de T009 y de T008 (`ShippingApiSupport.cs`). [P]
  solo frente a T010.
  - **V-05 extendido** (H-16): 20 repeticiones, dos salidas de 8 sobre físico 10 de pedidos
    distintos → una 200 y otra 409; físico 2.
  - **Carreras con `LockBarrier`, ambos órdenes** (H-17):
    - salida / salida del mismo pedido que superan el pendiente (perdedora 412 por `orderVersion` o
      422);
    - salida / reservar y salida / liberar sobre el mismo bucket y la misma reserva (HU-05 recibe
      412 o confirma después; nunca reservado > físico ni consumido + liberado > asignado);
    - salida / desactivación del artículo (INT-HU03-02: ambas terminan; historial y saldos
      coherentes);
    - salida / `DELETE` del artículo (rechazado por referencias);
    - salida / `PUT` de definición del artículo (unidad, lote o fracciones), con conexiones
      independientes y en los dos órdenes con `LockBarrier`. En ambos órdenes, el `PUT` que cambia
      la definición se rechaza (409 por definición bloqueada o 412), la salida confirma con 200, la
      definición no cambia y no hay efectos parciales (INT-HU03-01).
  - **V-08** (H-19): `FailAfterSaveInterceptor` en crear, editar, eliminar y confirmar; ningún efecto
    ni `OperationResult`; el reintento con la misma clave aplica un solo efecto.
  - **Rojo esperado**: ninguno natural si T009 es correcto. Comprobar con una mutación temporal sin
    commit (quitar `customer-order:{o}` de los bloqueos o el `Touch` del pedido) que H-17 y H-12
    fallan; anotar y restaurar, como C2 de HU-05.
  - Repetir la suite de concurrencia tres veces.

  (US3.3, US5.4–US5.7; FR-020, FR-022; SC-003, SC-004.)

  **Hecho (2026-11-21)**: 16 pruebas.
  - `ShipmentConcurrencyTests`:
    - H-16 (V-05 extendido): 20 rondas, una 200 y una 409 `InsufficientStock`, físico 2.
    - H-17: 12 carreras con `LockBarrier` en ambos órdenes frente a otra salida del mismo pedido,
      reservar, liberar, desactivación (las dos 200), `PUT` de definición (409
      `ArticleDefinitionLocked`) y `DELETE` del artículo (409 `HistoryPreventsDeletion`). El
      perdedor por versión recibe 412.
    - 2 carreras más, frente a una reserva de otra línea del mismo pedido, que solo serializa
      `customer-order`.
  - `ShipmentReliabilityTests`: H-19 (V-08) en crear, editar, eliminar y confirmar.
  - **Rojo**: no hubo rojo natural. Mutaciones temporales, sin commit: (1) sin `Touch` del pedido,
    fallan 3 carreras (reservar tras confirmar y salida/salida en ambos órdenes); (2) sin
    `customer-order` en los bloqueos, al principio no falló nada, porque los bloqueos de bucket y
    artículo lo tapaban. Por eso se añadió la carrera X/Y del mismo pedido, que falla en ambos
    órdenes. Después se restauró el archivo.
  - Concurrencia (26 pruebas, con las de T008) repetida tres veces: 26/26 las tres.
  - Suite 283/1/0; sin cambios de modelo.

**Checkpoint backend del MVP**: H-01…H-19 en verde tres veces.

---

## Phase 4: User Story 4 — Consultar las salidas de un pedido (P2)

- [X] T012 [US4] [US1] Contexto con revisión y lista del pedido (D-12). Depende de T007, de T008
  (`ShippingApiSupport.cs`) y de T009. **No es paralela a T009**: las dos editan
  `ShipmentEndpoints.cs`, así que va después.
  - **Rojo esperado**: `Shipping/ShipmentQueryTests.cs` (H-21) falla con 404 en
    `/shipments/{id}/context` y `GET /shipments?customerOrderId=`.
  - `ShipmentQueries.ContextAsync`: `SNAPSHOT`, sin `ETag`, `no-store`; `shipmentVersion`,
    `orderVersion`, `asOf`, `actorName`, pedido, artículos, ubicaciones, lotes y, en borradores,
    `review` por línea **ordenado por Id** con `possible = min(physical, ownReserved + (physical −
    reserved), pending)`.
  - `ShipmentQueries.ListAsync(customerOrderId, page, pageSize)` con `Paging` de HU-02; orden
    `recordedAt` descendente e Id; 422 sin `customerOrderId`; 404 si el pedido no existe.
  - H-21: detalle con el **mismo** cuerpo y `ETag` tras renombrar el artículo; el contexto refleja el
    nombre y `shipmentVersion` = `ETag`; `review` de V-04 (propio 4, ajeno 3, posible 7 → 6 por el
    pendiente); lista paginada con borradores y confirmadas; con el artículo desactivado,
    `articles[].isActive = false` en el contexto de un borrador (US3.2).

  (US1.2, US4.1–US4.3; FR-006, FR-018.)

  **Hecho (2026-11-21)**:
  - `ShipmentQueries.ContextAsync` y `ListAsync`; `ShipmentContext`, `ShipmentReviewLine` y
    `ShipmentSummary`.
  - Rutas `GET /shipments/{id}/context` y `GET /shipments?customerOrderId=`, con `no-store`.
  - No hay un `Paging` común en el código: página y tamaño (1–100, 422) se validan como en
    `MasterDataQueryOptions`, y se reutiliza `PageResult<T>`.
  - Rojo previo: 4/4 de `ShipmentQueryTests` fallaban porque las rutas no existían.
  - Verde: 4/4 (H-21). Cubre el detalle con el mismo cuerpo y `ETag` tras renombrar y desactivar el
    artículo; contexto con el nombre nuevo, `isActive = false` y `shipmentVersion` = `ETag`;
    `review` de V-04 (propio 4, ajeno 3, posible 6 acotado por el pendiente; pedido B con posible 5),
    vacío tras confirmar; lista paginada con borrador y confirmadas, más reciente primero; 422 y
    404; Administrador → 403.
  - Suite 287/1/0; sin cambios de modelo.

---

## Phase 5: Frontend /salidas (US1–US4)

- [X] T013 ⚑ **tres commits aparte (archivos de A, P-06)**. Depende de nada del backend.
  - **Rojo esperado** en specs nuevas de B, sin tocar las de A:
    `features/shipping/data/pending-operation-hu03.spec.ts` (`mutationPermission('/api/v1/shipments')`
    es `null`; `begin(…, '/salidas/{uuid}')` descarta la ruta) y
    `features/shipping/data/operation-status-hu03.spec.ts` (el enlace dice «Abrir pedido» para una
    salida).
  - Commit (a) `frontend/src/app/core/operations/pending-operation.store.ts`, `mutationPermission()`:
    `^/api/v1/shipments$`, `^/api/v1/shipments/{uuid}$` y `^/api/v1/shipments/{uuid}/confirm$` →
    `Shipping.Record`.
  - Commit (b) mismo archivo, `APP_ROUTE`: admitir además `^/salidas/(pedidos/{uuid}|{uuid})$`.
  - Commit (c) `frontend/src/app/core/operations/operation-status.component.ts`: el texto del enlace
    es «Abrir salida» si `appRoute` empieza por `/salidas/` y no es de pedido, y «Abrir pedido» en
    otro caso. `route()` no cambia.
  - Nada más cambia en esos archivos. Vitest completo en verde tras cada commit.

  (D-14; shipping-ui § Navegación y recuperación.)

  **Hecho (2026-11-21)**, en tres commits ⚑ sobre los dos archivos de A que declara la tarea (no hay
  un tercero):
  - (a) `1249e44`: `mutationPermission()`.
  - (b) `ccc945e`: `APP_ROUTE` (constante `UUID` y alternativa `/salidas/(pedidos/)?{uuid}`).
  - (c) el de esta marca: `openLabel()` en `OperationStatusComponent`; `route()` no cambia.
  - Pruebas en las specs nuevas de B: `pending-operation-hu03.spec.ts` (4) y
    `operation-status-hu03.spec.ts` (3).
  - Rojo previo en cada una: `mutationPermission` devolvía null; `appRoute` de `/salidas` se
    descartaba; el enlace decía «Abrir pedido» para una salida.
  - Vitest 149, 151 y 154 tras cada commit (base 147); `tsc` de app y specs y `npm run build` en
    verde.
- [X] T014 [US1] [US4] Datos, búsqueda y pedido. Depende de T006, T012 y T013.
  - `features/shipping/data/shipping.models.ts` y `shipments-api.service.ts`: `lookup`, `detail(id)`
    con **dos lecturas** (detalle con `ETag` y contexto; relee hasta 3 veces si
    `shipmentVersion !== etag`), `listByOrder`, y las mutaciones con `pending.begin(…, appRoute)` +
    `send()` como HU-05 (sin `idempotent-intent.ts`; `isUncertain()` propio).
  - ⚑ commit aparte (archivos de B de H0): `operation-areas.ts`, área `salidas` con
    `Inventory.Read` (D-14), y su `operation-areas.spec.ts`.
  - `features/shipping/shipping.routes.ts`: sustituir el placeholder por `''`,
    `pedidos/:orderId`, `pedidos/:orderId/nueva` (`permissionGuard`, `Shipping.Record`) y `:id`.
  - `order-lookup.page.ts` y `order-shipments.page.ts` según
    [shipping-ui](contracts/shipping-ui.md#buscar-pedido): pendientes del pedido (detalle +
    contexto de HU-05), salidas con estado en texto, «Nueva salida» solo con `Shipping.Record` y
    pedido abierto, aviso de pedido servido.
  - **Rojo esperado** en `order-lookup.page.spec.ts` y `order-shipments.page.spec.ts` con esqueletos
    vacíos: encontrado navega; inexistente avisa, conserva el texto y enfoca el campo; servido sin
    «Nueva salida»; sin permiso, sin acciones.
  - **Rojo esperado** en `shipping.routes.spec.ts`: el `permissionGuard('Shipping.Record')` de
    `pedidos/:orderId/nueva` bloquea a una sesión con solo `Inventory.Read` y deja pasar con
    `Shipping.Record`. `''`, `pedidos/:orderId` y `:id` siguen abiertos con el área (D-14). Falla con
    el placeholder, que no tiene esas rutas.

  (US1.11, US4.1, US4.3; FR-001a, FR-018, FR-025.)

  **Hecho (2026-11-21)**:
  - `ed443f3` (⚑ H0): área `salidas` con `Inventory.Read` y su spec.
  - El commit de esta marca: `shipping.models.ts` y `shipments-api.service.ts` (lookup, `detail` con
    dos lecturas, `listByOrder` y mutaciones con `begin(…, appRoute)` + `send()`), las rutas con
    `permissionGuard` en `pedidos/:orderId/nueva`, `order-lookup.page` y `order-shipments.page`.
  - `shipment-form.page.ts` queda como esqueleto, porque las rutas lo cargan; lo construye T015.
  - Desviación (M2 del análisis): el total de una salida solo lleva unidad si todas las líneas del
    pedido comparten la misma; si no, se muestra «—» (FR-025).
  - Rojo previo:
    - spec de áreas, 3/3: el área pedía `Shipping.Record`;
    - rutas, 2/2 contra el placeholder: solo existía `''`;
    - búsqueda y pedido, 6/6, con los esqueletos vacíos.
  - Verde: 8/8 en las specs nuevas; Vitest 162/162 (base 154); `tsc` de app y specs sin errores.
- [X] T015 [US1] Preparar y editar el borrador. Depende de T014.
  - `shipment-form.page.ts` (nueva y `:id` en borrador): fecha del hecho por defecto ahora;
    «Añadir» por línea con pendiente; **propuesta** desde las asignaciones activas del pedido, por
    Id, acotada al pendiente, con «Propuesto desde la reserva» (D-13); selector de buckets con
    **todas** las combinaciones con existencias del artículo, mediante `StockApiService.allRows(articleId,
    false)` de HU-05 (C4, ya en la rama por `ca9f6db`; analyze-1 H3); «Guardar borrador»
    (`POST`/`PUT` con `appRoute`); «Eliminar borrador» con diálogo de confirmación y aviso al volver
    al pedido.
  - **Rojo esperado** en `shipment-form.page.spec.ts`: propuesta con dos buckets, sin reserva y
    acotada; el selector ofrece un bucket posterior al 25 de un artículo con 30 combinaciones;
    guardar envía clave, cuerpo e `If-Match`; 422 por línea conserva datos y enfoca el
    primer `aria-invalid` (patrón `focus-first-error.ts` de HU-05); 412 recarga sin perder lo escrito;
    eliminar con confirmación.

  (US1.1, US1.8–US1.10; FR-004–FR-006a, FR-024.)

  **Hecho (2026-11-21)**:
  - `shipment-form.page.ts` para el alta y el borrador. Fecha del hecho por defecto ahora.
  - Propuesta D-13 por Id de asignación, acotada al pendiente y marcada «Propuesto desde la reserva».
  - Selector de orígenes con `StockApiService.allRows` (analyze-1 H3).
  - Guardar con `POST`/`PUT` y `appRoute`; eliminar con confirmación en la página y vuelta al
    pedido con el aviso, que `order-shipments.page` lee de `history.state`.
  - Rechazos: 422 por línea con foco al primer `aria-invalid` (`focusFirstError`); 412 relee sin
    perder lo escrito.
  - Rojo previo: 6/6 contra el esqueleto vacío de T014, más 1 del aviso al volver.
  - Verde: `shipment-form.page.spec.ts` 6/6 (propuesta con dos buckets; sin reserva no propone;
    selector con 30 combinaciones; `POST` con clave, cuerpo y `appRoute`; 422 con foco; `PUT` con
    `If-Match` y 412 sin perder lo escrito; `DELETE` con confirmación). Más 1 en
    `order-shipments.page.spec.ts`.
  - Vitest 169/169 (base 154); `tsc` correcto.
- [X] T016 [US1] [US2] Revisar, confirmar y recuperar. Depende de T015.
  - `shipment-review-dialog.ts` (`cdkTrapFocus`, Escape): cifras de `review`, aviso en texto si la
    cantidad supera la posible, «Inactivo» en texto junto al artículo inactivo sin deshabilitar la
    confirmación (US3.2, FR-006; analyze-1 H5), nota de revalidación, «Confirmar salida» con
    `orderVersion` del contexto.
  - Resultados según [shipping-ui § Resultados y errores](contracts/shipping-ui.md#resultados-y-errores):
    200 → «Salida registrada. Operación …» y solo lectura; 409 `InsufficientStock` con el
    `detail`; 409 `ReservedStockConflict` con enlace a `/existencias/pedidos/:id`; 412 → recarga de la
    revisión; respuesta perdida → `OperationStatusComponent` con «Abrir salida».
  - Salida confirmada en `:id`: solo lectura, sin «Guardar», «Eliminar» ni «Revisar», y con
    «Salida confirmada con la operación {operationId}» tomado del detalle (`GET /shipments/{id}`).
    Así se ve también al reabrirla en otra sesión, por «Abrir salida» o por URL (FR-023, US4.2;
    analyze-1 H2, como C3 de HU-05).
  - Borrador en `:id` sin `Shipping.Record` (área con `Inventory.Read`, D-14): también en solo
    lectura, sin «Guardar», «Eliminar» ni «Revisar». Las acciones dependen de
    `SessionService.has('Shipping.Record')` y no solo del estado.
  - **Rojo esperado** en `shipment-review-dialog.spec.ts` y ampliación de `shipment-form.page.spec.ts`:
    cada resultado; foco al aviso en 409 sin campo; `orderVersion` enviado; «Inactivo» en la
    revisión con `isActive = false` y «Confirmar salida» habilitado; una salida confirmada abierta
    sin aviso previo muestra su `operationId`; un borrador abierto con
    solo `Inventory.Read` no muestra «Guardar», «Eliminar» ni «Revisar», y con `Shipping.Record` sí; tras recargar con una
    confirmación pendiente, el panel ofrece «Abrir salida» con `href` `/salidas/{id}` y «Comprobar»
    hace un único `GET /operations/{id}`.
  - Vitest completo, `tsc` de app y specs y `npm run build` en verde.

  (US1.2–US1.3, US2.2, US2.4, US5.3; FR-006, FR-010–FR-012, FR-023–FR-025.)

  **Hecho (2026-11-21)**:
  - `shipment-review-dialog.ts` (`cdkTrapFocus`, Escape): cifras de `review`, «Inactivo» y el aviso
    de cantidad mayor que la posible, ambos en texto y sin bloquear.
  - En `shipment-form.page`: «Revisar y confirmar» solo con el borrador guardado; confirmar con
    `If-Match` y `orderVersion`. Resultados según el contrato: 200 con la operación; 409
    `InsufficientStock` con foco al aviso; 409 `ReservedStockConflict` con enlace a
    `/existencias/pedidos/:id`; 412 relee.
  - Solo lectura para la salida confirmada y para el borrador sin `Shipping.Record`, con «Salida
    confirmada con la operación {id}» (analyze-1 H2).
  - `OperationStatusComponent` con «Abrir salida».
  - Las utilidades de prueba pasan a `testing/shipping-testing.ts`, para no ejecutar dos veces las
    pruebas de T015 al importarlas.
  - Rojo previo: 11 (4 del diálogo y 7 de la página) contra el esqueleto del diálogo y la página de
    T015.
  - Verde: `shipment-review-dialog.spec.ts` 4/4 y `shipment-confirm.page.spec.ts` 7/7; Vitest
    180/180 (base 154); `tsc` de app, specs y E2E sin errores; `npm run build` correcto.

---

## Phase 6: E2E-02 y cierre

- [X] T017 [US1] [US2] `frontend/e2e/shipping.spec.ts` con **E2E-02** sobre el fixture de A, sin
  modificarlo: la cuenta `responsable` (`ResponsableAlmacen`) y la siembra D-17 ya existen
  (`ef296cc`, `110df1a`, `4659302`). Depende de T005 y T016.
  1. Preparación por API con datos únicos por ejecución: artículo con lote, ubicación, 10 unidades
     sembradas y pedido de 6 (operario).
  2. El responsable reserva 4 por la interfaz de HU-05.
  3. El operario busca la referencia con espacios y minúsculas, prepara la salida con la propuesta
     (4) y cambia su cantidad a 6. No añade otra fila del mismo bucket, porque repetiría línea,
     ubicación y lote (FR-002; analyze-1 H1). Revisa (propio 4, posible 6) y confirma.
  4. Ve «Salida registrada» y el pedido servido; en `/existencias`, físico 4 y reservado 0. Al
     reabrir `/salidas/{id}` ve la operación de la confirmación (analyze-1 H2).
  - **Rojo esperado**: falla en el paso 3 si se ejecuta antes de T014–T016 (placeholder de
    `/salidas`); anotarlo.
  - **Si hiciera falta** otra cuenta o activación, se permite tocar `docker-compose.yml`,
    `frontend/e2e/fixtures/master-data.fixture.ts` y
    `backend/tests/Inventario.IntegrationTests/Fixtures/MasterDataTestData.cs` (A). Cada cambio va
    en su commit mínimo ⚑, con la prueba que lo cubre, y se incluye en la revisión de A (T001 b).
    **Cualquier otro archivo de A: preguntar antes de tocarlo.**
  - Playwright completo sin SC-005, sin intermitencias.

  **Hecho (2026-11-21)**:
  - `frontend/e2e/shipping.spec.ts` con E2E-02. Datos únicos por ejecución: SKU `E2E03-…`,
    ubicación `S01-…` y referencia `PED-S…`.
  - Recorrido: la siembra D-17 y el pedido se preparan por API; el responsable reserva 4 por la
    interfaz de HU-05; el operario busca « ped-s… » en `/salidas`, prepara con la propuesta (4) y
    cambia su cantidad a 6 (analyze-1 H1), guarda, revisa (propio 4, posible 6) y confirma.
  - Comprueba: «Salida registrada», «Salida confirmada con la operación», el pedido servido y, en
    `/existencias`, físico 4 y reservado 0. Al reabrir la salida se sigue viendo la operación (H2).
  - Sin cambios en el fixture ni en el compose de A: ya existen las cuentas `lector` y `responsable`.
  - **Rojo**: no se anotó. Playwright se ejecuta una sola vez, al final, y para entonces T014–T016 ya
    estaban construidas.
  - Verde: Playwright 17/17 con `--grep-invert SC-005` (base 16/16, más E2E-02), sin
    intermitencias; `tsc` E2E sin errores.
- [X] T018 ⚠ PR #9 Evidencia y verificación de la rama. Depende de T004–T017. Riesgo:
  `specs/004-consultar-existencias/contracts/hu03-integration.md`.
  - Suite backend completa **tres veces**, `has-pending-model-changes`, cadena desde cero, Vitest,
    `tsc` de app, specs y E2E, `npm run build` y Playwright.
  - Registrar fecha, entorno, commit y resultado por caso H en `quickstart.md` § «Evidencia».
  - ⚑ commit aparte (documento de B de HU-05): en
    `specs/004-consultar-existencias/contracts/hu03-integration.md` § 5, anotar como **construidas**
    las pruebas de salida de HU-03 (reglas 3 y 4, S-18 completo, V-04) con sus nombres.
  - Preparar para T002 la evidencia de INT-HU03-01/02 y P-08.

  **Hecho (2026-11-21)**:
  - Backend 287/1/0 tres veces seguidas sobre `8275c43` (base 207/1/0); modelo sin cambios.
  - Vitest 180/180 (base 147); `tsc` de app, specs y E2E y build correctos; Playwright 17/17.
  - Evidencia por caso H y lo preparado para T002 en [quickstart § Evidencia](quickstart.md#evidencia).
  - ⚑ `8275c43`: pruebas construidas anotadas en `hu03-integration.md` § 5.
  - T002 no se aplica: espera a la aprobación de A.

---

## Phase 7: Convergence

Tareas `C1…`, fuera de la numeración T001–T018 (siguen siendo 18 tareas).

- [X] C1 [INT-HU03-01] Cerrar P-02 a petición de A: SQL rechaza también un `INSERT` tardío de
  consumo con el **mismo** `OperationId` que la confirmación. Diseño en research D-06 y D-08
  (iteración Convergence C1, `01f7c64`), data-model (columnas e invariante 8) y H-02.

  **Hecho (2026-11-21)**:
  - `Shipments.DeclaredConsumptionCount` y `DeclaredConsumedQuantity`: los fija `Shipment.Confirm(…,
    count, sum)` en la misma sentencia que pasa a `Confirmed`. `CK_Shipments_OperationId` los exige
    en `Confirmed` y los prohíbe en `Draft`; `CK_Shipments_Declared` los quiere ≥ 0; son inmutables
    por `TR_Shipments_ConfirmedImmutable`.
  - `TR_ShipmentReservationConsumptions_ConfirmingOperation` añade 51305, que rechaza superar lo
    declarado con cualquier operación. El complemento de la cabecera añade 51306, que rechaza
    confirmar con más consumos de los declarados. Solo `THROW`, independiente del orden de EF y sin
    `CURRENT_TRANSACTION_ID()`.
  - `ShipmentCommands.ConfirmAsync` declara `plan.Count` y la suma de `plan`.
  - Migración regenerada sobre `HU05_CustomerOrdersReservations`: ahora es
    `20261121135335_HU03_Shipments` (antes `20261121114907`, sin fusionar en la integración), con la
    parte manual actualizada. `ShipmentSchema.Migration` sigue el nuevo nombre.
  - Rojo previo:
    - `A_late_consumption_with_the_same_operation_is_rejected_by_the_database` fallaba con «No
      exception was thrown»: el consumo con la misma operación se admitía.
    - Las demás pruebas (los dos órdenes con más consumos, el CHECK, H-08 y H-10 con los totales
      declarados, la invariante 8 y el dominio) no compilaban, porque faltaban las columnas.
  - Verde: Shipping 83 pruebas; suite 290\/1\/0 (base 287\/1\/0); `has-pending-model-changes`
    limpio. Aplicar, revertir a HU-05 y reaplicar con `dotnet ef database update` sobre una base
    temporal, ya eliminada.

- [X] C2 [G1 de converge-1] El 409 `InsufficientStock` de la confirmación explica también la
  cantidad posible (US2.2, FR-010, FR-024), sin tocar `StockCore`, que es compartido con HU-08.

  **Hecho (2026-11-21)**:
  - `ShipmentCommands.ConfirmAsync` captura el 409 `InsufficientStock` que lanza
    `StockCore.ApplyAsync` y lo relanza con el mismo estado, código y `errors`. Al `detail` de
    `StockCore` le añade, por cada línea rechazada, «Se pueden servir como máximo {P} {unidad} de
    {SKU} en {ubicación}.».
  - P = mín(físico, propia consumible + disponible). Se lee con `AsNoTracking` bajo los bloqueos
    ya tomados, para ignorar lo que `ApplyAsync` hubiera aplicado en memoria antes de rechazar.
  - `StockCore` y sus pruebas, y los demás flujos (reservas, HU-08), no cambian.
  - Pruebas:
    - `ShipmentConfirmTests.H10_us22_insufficient_stock_explains_the_possible_quantity`: el
      escenario US2.2 (físico 10, reserva ajena 3, salida de 8 → posible 7, nada cambia).
    - `H10_afterwards_…` comprueba ahora «como máximo 1.000» y «como máximo 4.000».
    - Vitest: el aviso de `shipment-confirm.page.spec.ts` muestra el texto completo.
  - Rojo:
    - Backend: las dos pruebas fallaban con «Assert.Contains() Failure: Sub-string not found».
    - Vitest: la UI ya muestra el `detail` tal cual, así que no hubo rojo natural. Una mutación
      temporal sin commit (cortar el `detail` tras el primer «).») hizo fallar la prueba, «expected
      'No se pueden servir 2.000 de ART-01 e…' to contain 'Se pueden servir como máximo 1.000 UD…'».
      Después se deshizo.
  - Verde: Shipping 85/85 y la spec de confirmación 7/7.

- [X] C3 [G2 de converge-1] Identificar el artículo de cada línea con «SKU · nombre», en la vista
  de solo lectura y en las filas del borrador. La vista de solo lectura muestra además la fecha de
  registro (US4.1, FR-018).

  **Hecho (2026-11-21)**:
  - `shipment-form.page.ts`:
    - `readonly-line` empieza por el artículo, que sale de `context.articles` de la salida.
    - Cada `shipment-line` lleva `line-article`, con el artículo de su línea de pedido (contexto del
      pedido).
    - La cabecera de solo lectura añade «Fecha de registro» con `recordedAt`.
    - No cambian la API ni el esquema.
  - Pruebas: dos en `shipment-form.page.spec.ts` (filas propuestas y borrador guardado) y una en
    `shipment-confirm.page.spec.ts` (confirmada: artículo, origen y fecha de registro).
  - Rojo: las tres fallaban porque faltaba el artículo, «expected 'N01 · sin lote · 2.000 UD ·
    Reserva p…' to contain 'ART-01 · Tornillo'» y «expected [ '', '' ] to deeply equal [ 'ART-01 ·
    Tornillo', …(1) ]».
  - Verde: specs de `/salidas` 36/36.

- [X] C4 [G4 de converge-1] Ante los 409 `CustomerOrderNotOpen`, `CountScopeFrozen` y
  `DocumentAlreadyConfirmed`, mostrar el motivo y recargar salida, contexto y pedido, como en el 412
  (shipping-ui § Resultados). G3 queda como límite aceptado (converge-1).

  **Hecho (2026-11-21)**:
  - `ShipmentFormPage.fail()`: esos tres códigos muestran el `detail` y llaman a `load(false)`, que
    relee salida, contexto y pedido sin perder las cantidades escritas. El foco va al aviso.
  - Una salida que otra sesión ya confirmó pasa a solo lectura, con su operación.
  - Vale para guardar, eliminar y confirmar; el 412 y los 409 de `ReservedStockConflict` e
    `InsufficientStock` no cambian.
  - Pruebas:
    - `shipment-confirm.page.spec.ts`: una por código al confirmar (motivo, foco y relectura; con
      `DocumentAlreadyConfirmed`, solo lectura con la operación).
    - `shipment-form.page.spec.ts`: guardar con `CustomerOrderNotOpen` conserva lo escrito.
  - Rojo: las cuatro fallaban porque no se releía, «Expected one matching request for criteria
    "Match URL: /api/v1/shipments/3000…0003", found none».
  - Verde: specs de `/salidas` 40/40.

---

## Si hay tiempo (fuera de las 18 tareas, en este orden)

- **X1** Mostrar en la salida confirmada los consumos por reserva (`consumptions` ya viene en la API,
  T007). La UI de T016 solo muestra la reserva propia consumida por línea.
- **X2** E2E del camino `ReservedStockConflict` → liberar con HU-05 → confirmar (H-11 ya lo cubre en
  integración).
- **X3** Paginación de la lista de salidas del pedido en la UI más allá de la primera página (la API
  la ofrece en T012).
- ~~X4~~ **Integrada en T015** tras `/speckit-analyze` (analyze-1 H3): el selector de buckets usa
  `StockApiService.allRows`, ya en la rama. El identificador X4 queda sin uso para no renumerar.

---

## Dependencies & Execution Order

```text
T001 ⏸ ─▶ T002 ⏸   (coordinación; T002 espera también a la fusión del PR #9 y a T018)
T003 ⚠ ─▶ T004 🔀 ─▶ T005 ─┬─▶ T006 ⚠ ───────────────────────────────────┐
                           └─▶ T007 ─┬─▶ T008 ─┐                          │
                                     └─▶ T009 ─┴─┬─▶ T010 (y T006)        │
                                                 ├─▶ T011                 │
                                                 └─▶ T012 ────────────────┤
T013 (independiente) ─────────────────────────────────────────────────────┴─▶ T014 ─▶ T015 ─▶ T016 ─▶ T017 ─▶ T018 ⚠
```

T010, T011 y T012 esperan a T008 (`ShippingApiSupport.cs`) y a T009. T012 va después de T009 porque
las dos editan `ShipmentEndpoints.cs`.

- **Historias**: US1/US2 (T006–T011) forman el MVP backend. US3 está dentro de T010 y T011. US4
  (T012) necesita el borrador y, por los archivos compartidos, va tras T008 y T009. US5 está repartida en T008, T010 y T011.
- **Dentro de cada tarea**: prueba en rojo → implementación → verde → commit.

### Dependencias externas

- **PR #9 (HU-05)**: toda la rama se apoya en su código. Solo **T004** depende de cómo se fusione:
  si la revisión cambia la migración de HU-05, se regenera `HU03_Shipments`. **T002** espera además
  a la fusión para aplicar los documentos comunes sobre los de HU-05. El resto no espera.
- **Riesgo de conflicto ⚠ con la revisión y la fusión del PR #9**: T003 (`Reservation.cs`,
  `CustomerOrder.cs`), T006 (`CustomerOrderQueries.cs`, `CustomerOrderDtos.cs`,
  `StockEndpoints.cs`) y T018 (`hu03-integration.md`) editan archivos de HU-05. No esperan a la
  fusión. Antes de empezar cada una, comprobar si esos archivos cambiaron en `origin/b/hu-05-consultar-existencias`
  e integrarlos con **merge** (no rebase), como `ca9f6db`, que ya trae C3, C4 (`8009807`,
  `363de56`) y T001/T002 (`2894dad`) de HU-05 (analyze-1 H6).
- **A**: **T001/T002** (aprobación de P-01…P-08) y **T013** (archivos de A: se implementa ya, pero la
  fusión espera a su revisión). **T017** solo si hiciera falta tocar `docker-compose.yml`,
  `master-data.fixture.ts` o `MasterDataTestData.cs`, que hoy no se prevé; cualquier otro archivo de
  A, preguntar antes.
- **HU-04**: `CountScopeFrozen` al confirmar, sin prueba hasta INT-HU04-03.

### Parallel Opportunities

- T006 en paralelo con T007 (archivos distintos).
- T008 en paralelo con T009: pruebas del borrador frente a comandos de confirmación, en archivos
  distintos.
- T010 y T011 en paralelo entre sí, cuando T008 y T009 estén listas.
- **No paralelas**: T012 con T009 (`ShipmentEndpoints.cs`), ni T010/T011/T012 antes de T008.
- T013 en cualquier momento, en paralelo con todo el backend.

## Implementation Strategy

1. **MVP backend**: T003–T011 (borrador, confirmación, reservas, inactivos, concurrencia y
   fiabilidad). Demostrable por API.
2. **Consulta**: T012.
3. **Interfaz y vídeo**: T013–T017.
4. **Cierre**: T018 y, tras la aprobación de A, T002.

Commits: uno por tarea o grupo coherente, con el formato `HU-03 (B): …`. Las tareas ⚑ siempre en
commit propio. Nunca `git add -A`.

## Desviaciones registradas (sin tareas)

| Desviación | Motivo | Consecuencia |
|------------|--------|--------------|
| SC-006 (p95 ≤ 3 s al confirmar con 25 sesiones) sin medir. | Requiere carga y datos sintéticos. | HU-03 se integra, pero no se da por aceptada ([plan § Complexity Tracking](plan.md#complexity-tracking)). |
| SC-007 / V-13 (validación con operarios, constitución III) sin realizar. | Sesiones con operarios fuera de esta entrega. | Igual; guion en el [quickstart](quickstart.md#fuera-de-alcance-de-esta-entrega). |
| `CountScopeFrozen` al confirmar sin prueba. | `IsInOpenCountAsync` devuelve `false` hasta HU-04. | Pendiente de INT-HU04-03. |
| Corrección de salidas confirmadas fuera de HU-03. | Clarificación Q3. | HU-04, con la tarea receptora de P-07. |
| T013, T014 (área), T003 y T006 tocan archivos de A o de B fuera de la feature. | Los exige el diseño (P-06, D-04, D-11, D-14). | Cada uno en su commit, señalado con ⚑. |
