---

description: "Lista de tareas de HU-04 — Realizar inventarios cíclicos y ajustar diferencias"
---

# Tasks: HU-04 — Realizar inventarios cíclicos y ajustar diferencias

**Input**: documentos de diseño de `specs/006-realizar-inventarios-ciclicos/`:
[plan.md](plan.md), [spec.md](spec.md), [research.md](research.md) (D-01…D-16),
[data-model.md](data-model.md), [contracts/](contracts/) y [quickstart.md](quickstart.md) (C-01…C-20, E2E-03).

**Presupuesto**: como máximo 20 tareas. Hay **18** (T001–T018), más la fase «Convergence», vacía
hasta `/speckit-analyze` y `/speckit-converge`, y «Si hay tiempo», fuera de la numeración.

**Orden por prioridad de entrega**: primero el núcleo (conteo → diferencias → ajuste y congelación
`CountScopeFrozen`, con INT-HU04-01/02/03 y las carreras, T003–T011); después la corrección de salidas
por conteo vinculado (T012); al final el frontend y el E2E (T013–T017). Si el tiempo se acaba, lo que
quede fuera es lo de la fase 6 en adelante y lo de «Si hay tiempo», sin dejar el núcleo a medias.

**Pruebas (TDD)**: cada tarea de código indica la prueba que debe fallar primero y **por qué** («rojo
esperado»), que se anota en la tarea al hacerla. Si no hay rojo natural, se comprueba con una mutación
temporal sin commit, anotada aquí. Integración en SQL Server real con la receta Docker de specs/003 (no
hay SDK .NET local). Carreras con **conexiones independientes en ambos órdenes** (`Stock/LockBarrier.cs`).
Cada commit debe quedar en verde por sí solo (suite completa, `has-pending-model-changes`, y en frontend
`tsc` + Vitest).

**Organización**: por historias de la spec. US5 (hallazgos) va con el registro del contado (T008)
porque comparte comando; US6 (autorización y fiabilidad) tiene su tarea de pruebas (T011) y sus
comprobaciones en cada comando.

## Format: `[ID] [P?] [Story] Descripción`

- **[P]**: puede hacerse en paralelo (archivos distintos, sin dependencias pendientes). Solo donde es real.
- **[Story]**: historia de la spec (US1–US6).
- **⚑ commit aparte**: toca archivos de A, comunes o de B fuera de la feature (H0, HU-02, HU-05). Va en
  su propio commit para separarlo en la revisión del PR.
- **[INT-HU04-0x]**: tarea receptora del [backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#hu-04--conteos-y-ajustes--persona-b).
- **🔀 PR #10**: depende de la base de HU-03 (migración `HU03_Shipments`, `Shipments`,
  `ShipmentCommands`) tal como se fusione. Antes de empezar, comprobar si el PR #10 ha cambiado esa base.

## Path Conventions

Backend en `backend/src/Inventario.Api/` y pruebas en
`backend/tests/Inventario.IntegrationTests/CycleCounts/`. Frontend en
`frontend/src/app/features/cycle-counts/` y E2E en `frontend/e2e/`. Rutas según
[plan.md § Project Structure](plan.md#project-structure).

---

## Phase 1: Coordinación con A (no bloquea el código)

- [X] T001 Solicitar la revisión de A en el PR de HU-04 (COORD-01) de los diffs P-01…P-08 de
  [plan.md § Cambios al modelo común](plan.md#cambios-al-modelo-común-pendientes-de-a): P-01/P-02
  (`data-model.md` común), P-03/P-04 (`contracts/api.md`), P-05 (`contracts/stock-operations.md`,
  incluido el aviso al escritor de recepciones de HU-01), P-06 (archivos de A de T013), P-07 y P-08.
  Se envía con el PR; la implementación sigue sin esperar. Hecho cuando A responde y la respuesta queda
  anotada aquí.
  - **Respuesta de A (2026-11-21), parcial**: confirma expresamente (a) que la recepción de HU-01 comprueba
    `IsInOpenCountAsync` bajo los bloqueos de todas las ubicaciones de destino, antes de `Receive`, con 409
    `CountScopeFrozen` (FR-008 de HU-01), y que liberar reservas sigue permitido (FR-015); (b) que los ajustes
    de conteo usan solo `CounterpartyNotApplicableReason`; (c) que el conteo vinculado a una salida es un
    ajuste del físico y la limitación P-07 queda visible. El resto de P-01…P-08 no tiene aprobación de A
    registrada: la tarea sigue abierta.
  - **Hecho (2026-11-21)**: A da su visto bueno expreso a P-01…P-08 sobre el corte `3e2ad7e`. Registrado
    en [plan.md § Estado de la revisión de A](plan.md#estado-de-la-revisión-de-a). Lo que A deja para
    T018 se anota en esa tarea.
- [X] T002 ⚑ Registrar en `specs/001-reparto-specs-inventario/integration-tasks.md` (coordinación A+B)
  la parte inicial de P-07: la tarea receptora de la corrección de salidas (P-07 de HU-03) queda
  asignada a HU-04 US4 (FR-018–FR-020), y la nota de P-05 para HU-01. Sin marcar INT-HU04-01/02/03
  (se marcan en T018 con evidencia). Sin prueba: solo documento.
  - **Hecho**: registradas en `integration-tasks.md` la tarea receptora de P-07 (HU-04 US4, T012) con lo
    confirmado por A el 2026-11-21 y la nota P-05 en HU-01. INT-HU04-01/02/03 sin marcar.

## Phase 2: Fundamentos (bloquean las historias)

- [X] T003 ⚑ 🔀 PR #10 [INT-HU04-01] Dominio, esquema y uso del artículo:
  - `Domain/Stock/CycleCount.cs`, `CountLocation.cs`, `CountLine.cs` con las transiciones de D-02 y los
    campos de [data-model](data-model.md): `CountCode` «`CC-` + 12 primeros hexadecimales del Id»,
    único; `State` en `Draft`, `Counting`, `AwaitingApproval`, `Confirmed`, `Cancelled`;
    `CancelReason` ≤ 500; `CountedPhysical` «nullable, ≥ 0»; `Reason` «nvarchar(500) nullable»;
    `Difference` calculada persistida; `LotCode` nvarchar(64); `CorrectsShipmentId` FK `Shipments`
    `Restrict`.
  - `Infrastructure/Persistence/Configurations/CycleCount*Configuration.cs` con
    `UX_CountLocations_Frozen` (`WHERE IsFrozen = 1`), `UX_CountLines_Bucket`, FK compuesta
    `(CountId, LocationId)` a `CountLocations` y `HasTrigger` en las tres tablas.
  - Migración `HU04_CycleCounts` sobre `20261121135335_HU03_Shipments` con `DENY DELETE` en
    `CycleCounts` y los triggers 51401–51407 de D-07 a mano, **solo `THROW`**.
  - `Features/CycleCounts/CycleCountArticleUsageContributor.cs` registrado en `AddStockCore`
    (`Features/Stock/Core/StockArticleUsage.cs`, H0, ⚑).
  - Pruebas: `CycleCounts/Hu04SchemaTests.cs` (C-01) y conjunto exacto de FK a `Article` con
    `CountLine` en `Stock/Hu05SchemaTests.cs` (HU-05, ⚑).
  - **Rojo esperado**: `Hu04SchemaTests` falla porque las tablas no existen; tras añadir la
    configuración sin contributor, `Hu05SchemaTests` falla porque `CountLine` referencia `Article` sin
    declararse.
  - **Hecho**: rojo observado 1) `Hu04SchemaTests` 5/5 en rojo, `FindEntityType(CycleCount)` null y
    migración ausente; 2) con configuración y migración, sin contributor: `EnsureCovered` falla en
    `Hu03/Hu04SchemaTests`, `Hu05SchemaTests` no tiene `CountLine` y HU-08 responde 503. Verde: suite
    296/1 omitida, `has-pending-model-changes` limpio. `Hu03SchemaTests` pasa a comprobar que HU-03
    sigue en la cadena (ya no es la última). Migración `20261121152857_HU04_CycleCounts`.
- [X] T004 [P] ⚑ [INT-HU04-02] Operación `Adjust` en `StockCore` (D-13):
  `Features/Stock/Core/StockCommands.cs` (`StockOperation.Adjust` → `MovementKind.Adjustment`) y
  `StockCore.cs` (`PrepareAdjustment` con diferencia con signo; `AdjustUp` crea lote y saldo si faltan;
  `AdjustDown` con `ReservedStockConflict` bajo el reservado e `InsufficientStock` sobre el físico;
  artículo inactivo admitido). Pruebas en `Stock/StockCoreAdjustTests.cs` (C-02). **Rojo esperado**:
  no compila / `ArgumentOutOfRangeException` porque `Adjust` no existe. Paralela a T003: solo usa las
  tablas de H0.
  - **Hecho**: rojo observado: no compila (`AdjustmentAllocation`/`PrepareAdjustment` no existen). Verde:
    `StockCoreAdjustTests` 4/4; el cuerpo normalizado lleva el signo solo en bajadas (huellas previas
    intactas); el arnés usa `Inventory.Adjust` para `Adjust`. Suite 300/1 omitida.
- [X] T005 ⚑ 🔀 PR #10 [US3] [INT-HU04-03] Lector de congelación real (D-04):
  `Features/Locations/ILocationUsage.cs` (HU-02) consulta `CountLocations` con `IsFrozen = 1`, y
  `Features/Locations/Commands/LocationCommands.cs` (HU-02) comprueba la congelación antes que
  `LocationNotEmpty` al desactivar o cambiar el tipo (H2 de analyze-1). Pruebas
  en `CycleCounts/CountScopeFrozenTests.cs` (C-12, primera mitad; la segunda la cierra T009) con una
  fila congelada sembrada directamente: confirmar una
  salida, reservar, desactivar la ubicación y cambiar su tipo, vacía y ocupada → 409 `CountScopeFrozen` sin
  cambios; liberar una reserva sigue admitido (Q1); `inOpenCount = true` en `GET /locations` y su
  detalle. **Rojo esperado**: las cuatro operaciones responden 2xx porque el lector devuelve `false`.
  Depende de T003.
  - **Hecho**: rojo observado: la confirmación de la salida responde 200 (lector constante `false`). Verde:
    `CountScopeFrozenTests` 2/2 (salida, reserva, desactivar y cambiar tipo, vacía y ocupada → 409;
    liberar admitido; un borrador no congela). La prueba ocupada fallaría con `LocationNotEmpty` sin el
    reorden H2. `inOpenCount` se comprueba en `GET /locations/{id}` y `/locations/{id}/stock`: la lista
    no lo expone («sin cambios de forma», contrato API). Suite 302/1 omitida.

## Phase 3: User Story 1 — Contar una parte del almacén y ver las diferencias (P1) 🎯 MVP

**Objetivo**: crear, iniciar y contar, con diferencias visibles y sin cambiar existencias.
**Prueba independiente**: spec US1 (A 10→8, B 5→5, diferencias −2 y 0, saldos intactos).

- [X] T006 ⚑ [US1] Borrador y lecturas: `Features/CycleCounts/CycleCountCommands.cs` (crear y editar,
  `Counts.Record`, `Idempotency-Key`, `If-Match` al editar, 1–50 ubicaciones activas sin repetir),
  `CycleCountQueries.cs` (lista paginada, detalle con `ETag` y líneas por Id, contexto sin `ETag` con
  `countVersion`, D-12), `CycleCountEndpoints.cs` y registro en `Program.cs` (común, ⚑). Pruebas en
  `CycleCountDraftTests.cs` (C-03) y `CycleCountApiSupport.cs`. **Rojo esperado**: 404 en
  `POST /api/v1/cycle-counts` porque la ruta no existe. Depende de T003.
  - **Hecho**: rojo observado: 404 en `POST /api/v1/cycle-counts`. Segundo rojo: un `PUT` con ETag
    obsoleto respondía 200 porque editar el alcance no renovaba la versión de la cabecera (D-11); se
    marca `State` como modificado, como HU-05/HU-03. DTOs en `CycleCountDtos.cs`. Verde:
    `CycleCountDraftTests` 2/2; suite 304/1 omitida.
- [X] T007 [US1] [INT-HU04-01] Iniciar (D-03, D-05): `POST /cycle-counts/{id}/start` bloquea
  `cycle-count:{id}`, **todas** las ubicaciones y luego los artículos de los saldos con físico > 0;
  comprueba ubicaciones activas y no congeladas por otro conteo (409 `CountScopeFrozen` con el
  `countCode` del otro); fija `IsFrozen = 1`, crea las líneas esperadas con su teórico y marca la
  primera referencia. Pruebas en `CycleCountStartTests.cs` (C-04, C-05, borrador sin congelar de
  US3.5). **Rojo esperado**: 404 en `/start`. Depende de T005 y T006.
  - **Hecho**: rojo observado: 404 en `/start`. El alcance se lee ya bajo `cycle-count:{id}` y antes de
    `location:{id}`; los saldos, tras bloquear las ubicaciones. Verde: `CycleCountStartTests` 3/3
    (C-04 con saldo a 0 sin línea, C-05 nombrando el `countCode`, ubicación desactivada tras el
    borrador → 422, borrador sin congelar); suite 307/1 omitida.
- [X] T008 [US1] [US5] [INT-HU04-01] Contar, hallazgos y enviar: `PUT /cycle-counts/{id}/lines`
  (`counted` y `found`, cero explícito, fracciones según el artículo, hallazgo en el alcance y con lote
  si el artículo lo gestiona, bloqueo `article:{id}` y primera referencia), `DELETE …/lines/{lineId}`
  (solo hallazgos; retira la referencia) y `POST …/submit` (todas contadas, 422 por línea). Pruebas en
  `CycleCountLinesTests.cs` (C-06, C-07, 409 `CountStateConflict`). **Rojo esperado**: 404 en
  `/lines`. Depende de T007.
  - **Hecho**: rojo observado: 404 en `/lines`. `reason` al contar es un campo desconocido (422, H6). La
    forma (negativo, hallazgo a cero) se valida antes de bloquear y la fracción, con el artículo leído
    tras el bloqueo, así que llegan en respuestas distintas. Cada escritura renueva el ETag; `DELETE`
    responde 204 con `ETag`. Verde: `CycleCountLinesTests` 2/2; suite 309/1 omitida.

## Phase 4: User Story 2 — Autorizar el ajuste (P1) 🎯 MVP

**Objetivo**: revisar motivos, confirmar el ajuste o cancelar, en un commit.
**Prueba independiente**: spec US2 (físico de A en U1 = 8, ajuste −2 trazable, U1 liberada).

- [X] T009 [US2] [INT-HU04-02] Revisar, confirmar y cancelar (D-05, D-06, D-07):
  `PUT …/review` (`Inventory.Adjust`, motivos por línea; el vínculo a salida llega en T012),
  `POST …/confirm` (bloqueos de D-03; revalidar teórico → 409 `CountTheoreticalChanged` y reservado →
  409 `ReservedStockConflict` conservando la congelación; un movimiento `Adjustment` con soporte
  `CountCode` y `CounterpartyNotApplicableReason`; `IsFrozen = 0`; aprobación y `OperationId`; **sin
  modificar líneas**) y `POST …/cancel` (motivo 1–500, sin efectos en stock). Pruebas en
  `CycleCountConfirmTests.cs`: C-08, C-09 (liberar durante el conteo y confirmar), C-10, C-11, C-14
  (invariantes 1–5 y 7 en cada confirmación) y C-16 (artículo inactivo). En `CountScopeFrozenTests.cs`,
  la segunda mitad de C-12 (US3.7, SC-002): tras confirmar y tras cancelar, confirmar una salida,
  reservar, desactivar y cambiar el tipo vuelven a admitirse. **Rojo esperado**: 404 en
  `/confirm`; después, `ReservedStockConflict` ausente si no se revalida. Depende de T004 y T008.
  - **Hecho**: rojo observado: 404 en `/review`, `/confirm` y `/cancel` (7 de 9 pruebas). Segundo rojo por
    mutación temporal sin commit (quitar la revalidación del reservado): el 409 sigue llegando como
    `ReservedStockConflict` porque `StockCore` rechaza la bajada (D-13), pero sin nombrar contado y
    reservado (contrato § Errores), y la prueba C-09 falla; restaurado. `correctsShipmentId` en la
    revisión es campo desconocido (422) hasta T012. Respuesta de confirmar: `adjustedLines` como lista
    `{ lineId, difference }`. Verde: `CycleCountConfirmTests` 6/6 (C-08 ×2, C-09, C-10, C-11, C-16, con
    invariantes 1–5 y 7 en cada confirmación) y `CountScopeFrozenTests` 3/3 (segunda mitad de C-12);
    suite 316/1 omitida.

**Checkpoint MVP**: con T003–T009 el ciclo conteo → diferencias → ajuste funciona y la congelación es
real en salidas, reservas y ubicaciones.

## Phase 5: User Story 3 y User Story 6 — Congelación bajo concurrencia y fiabilidad (P1/P3)

- [X] T010 🔀 PR #10 [US3] [INT-HU04-01] [INT-HU04-03] Carreras en `CycleCountConcurrencyTests.cs` con
  `LockBarrier` y conexiones independientes, **en ambos órdenes**: iniciar frente a confirmar una salida,
  reservar y desactivar la ubicación (C-13, 20 rondas); confirmar frente a liberar una reserva; añadir y
  retirar un hallazgo frente a la edición de definición y el borrado del artículo (C-15); tras la
  retirada, el artículo sin otras referencias admite la edición y el borrado. **Rojo esperado**: sin rojo
  natural si T005–T009 ya serializan; comprobar con una mutación temporal (quitar `LocationsAsync` del
  inicio) que la carrera falla, y anotarla. Depende de T009.
  - **Hecho**: sin rojo natural. Mutación temporal sin commit (quitar `LocationsAsync` del inicio): fallan 4
    pruebas (salida primero, desactivar en ambos órdenes y las 20 rondas sin barrera); restaurado.
    `CycleCountConcurrencyTests` 17/17: C-13 (iniciar frente a salida, reserva y desactivación; confirmar
    frente a liberar; 20 rondas) y C-15 (añadir y retirar hallazgo frente a definición y borrado, H4). Quien
    llega segundo con una vista anterior recibe 412: el ETag de ubicación incluye `inOpenCount` (HU-02) y
    la primera referencia renueva la versión del artículo; el reintento con versión actual da
    `CountScopeFrozen` / `ArticleDefinitionLocked` / `HistoryPreventsDeletion`. Tras retirar el hallazgo
    se admite el borrado; la definición sigue bloqueada por la regla común de A («la primera referencia
    bloquea la definición aunque después se retire»). Suite 333/1 omitida.
- [X] T011 [P] [US6] Fiabilidad y permisos en `CycleCountReliabilityTests.cs`: replay de las nueve
  escrituras, `IdempotencyConflict`, 412/428 (C-17), `FailAfterSaveInterceptor` en la confirmación con
  `GET /operations/{id}` (C-18) y matriz 401/403/400 por ruta (Operario sin `Inventory.Adjust`).
  **Rojo esperado**: el que salga de la matriz; si todo pasa, mutación temporal (quitar la comprobación de
  permiso del ejecutor en `/confirm`) anotada. Paralela a T010: otro archivo, misma dependencia (T009).
  - **Hecho**: sin rojo natural. Mutación temporal sin commit: `/confirm` con `Counts.Record` en la ruta y en
    el ejecutor (el permiso se comprueba en ambos; quitar solo uno no es observable por HTTP): fallan la
    fila `/confirm` de la matriz y C-08 (Operario confirma); restaurado. `CycleCountReliabilityTests`
    12/12: replay de las nueve escrituras con 428/412 e `IdempotencyConflict` (C-17), fallo tras guardar en
    la confirmación con `GET /operations/{id}` 404 → 200 y un solo ajuste (C-18), matriz 401/400/403 por
    ruta (antiforgery, `Idempotency-Key`, 403 antes de 422) y lecturas 401/403 (Administrador sin
    `Inventory.Read`); ResponsableAlmacen crea e inicia. Suite 345/1 omitida.

## Phase 6: User Story 4 — Corregir una salida confirmada errónea (P2)

**Objetivo**: vincular diferencias a una salida confirmada sin cambiar lo servido ni las reservas.
**Prueba independiente**: spec US4 (salida de 6 cuando salieron 4: físico +2, pedido intacto).

- [X] T012 🔀 PR #10 [US4] Vínculo en la revisión (D-09): `correctsShipmentId` en `PUT …/review`
  (salida `Confirmed`, con una línea en el mismo artículo, ubicación y lote, y diferencia ≠ 0; 422 por
  línea) y `GET …/lines/{lineId}/shipment-candidates` en `CycleCountQueries.cs`. Pruebas en
  `ShipmentCorrectionTests.cs` (C-19 con la invariante 6 de data-model, C-20). **Rojo esperado**: 422
  por campo desconocido `correctsShipmentId` y 404 en `shipment-candidates`. Depende de T009.
  - **Hecho**: rojo observado: 404 en `shipment-candidates` (C-19) y 422 por campo desconocido
    `correctsShipmentId` en lugar de `lines[0].correctsShipmentId` (C-20). P-07 explícita en código
    (`CountLine.SetCorrection`, `ShipmentCorrections.Notice`) y en la respuesta: `shipmentCorrectionNotice`
    en cada línea vinculada del detalle y `shipmentCorrections` con `orderChanged: false` al confirmar
    (contrato actualizado). C-19 compara salida (versión incluida), servido, estado del pedido, consumos y
    asignaciones antes y después (invariante 6). Verde: `ShipmentCorrectionTests` 2/2; suite 347/1 omitida.

## Phase 7: Frontend y E2E (US1, US2, US4)

- [X] T013 ⚑ [US1] Archivos de A (P-06), en su propio commit:
  `frontend/src/app/core/operations/pending-operation.store.ts` (`mutationPermission()` de
  `/api/v1/cycle-counts…` según la tabla P-06; `APP_ROUTE` con `^/inventarios/(nuevo|{uuid})$`) y
  `operation-status.component.ts` (enlace «Abrir conteo»), con sus specs. **Rojo esperado**: la spec del
  store devuelve `null` para `/api/v1/cycle-counts/{id}/confirm`. Sin dependencias de backend: puede
  hacerse en cualquier momento tras T001.
  - **Hecho**: rojo observado: `mutationPermission()` devuelve `null` para las nueve escrituras y el panel
    no ofrece «Abrir conteo» (6 de 8 pruebas). Los dos archivos de A van en su propio commit, solo con el
    cambio aprobado en P-06; las specs, en `features/cycle-counts/data/*-hu04.spec.ts` para no tocar las
    de A (como HU-03). Lecturas (`context`, `shipment-candidates`) y rutas mal formadas siguen en `null`.
    Verde: Vitest 195/195 (187 + 8), build correcto.
- [X] T014 [US1] Servicio, rutas, lista y nuevo: `cycle-counts-api.service.ts`, `cycle-counts.routes.ts`
  (sustituye el placeholder, H0), `count-list.page.ts` y `count-new.page.ts` con `allRows` para las
  ubicaciones, `begin(…, appRoute)` + `send()`, foco al primer error. Vitest en sus `.spec.ts`.
  **Rojo esperado**: la spec de la lista no encuentra la tabla porque sigue el placeholder. Depende de
  T006 (contrato) y T013.
  - **Hecho**: rojo observado: las specs no compilan (`count-list.page` y `count-new.page` no existen) y la
    ruta `''` sigue cargando el placeholder. Servicio con detalle + contexto releídos hasta coincidir
    (D-12), ubicaciones activas de todas las páginas de 100 (patrón `allRows`) y las nueve escrituras con
    `begin(…, appRoute)` + `send()`; modelos y `differenceText` en `data/cycle-counts.models.ts`; datos de
    prueba en `testing/cycle-counts-testing.ts`. `operation-areas.spec.ts` (H0, B) cuenta `/inventarios`
    como área desarrollada. La ruta `:id` llega con el detalle (T015). Verde: Vitest 204/204, build correcto.
- [X] T015 [US1] [US5] Detalle en borrador y en conteo: `count-detail.page.ts` (alcance, iniciar con
  aviso de congelación, contar con cero explícito, diferencias en texto, hallazgos, enviar; 412 con
  relectura sin perder lo escrito; 409 con relectura). Vitest. **Rojo esperado**: la spec no encuentra
  «Contado» ni «Enviar a aprobación». Depende de T014.
  - **Hecho**: rojo: la spec no compilaba sin la página; mutación temporal sin commit (renombrar
    «Enviar a aprobación»): falla la prueba de envío; restaurado. Ruta `:id`. Borrador: alcance editable
    (`PUT` con `If-Match`) y aviso de congelación antes de «Iniciar conteo». En conteo: «Contado» vacío,
    diferencia en texto («Faltan 2 UD», «Sin diferencia»), validación local (negativo, fracción no admitida),
    guardar solo lo escrito, hallazgos (búsqueda de artículo, lote en mayúsculas si lo gestiona) y retirada con
    `DELETE`; enviar guarda antes lo pendiente y marca la fila de `lines[i]`. Sin motivo al contar (H6). 412
    relee y conserva lo escrito; 409 de estado relee. Verde: Vitest 214/214, build correcto.
- [ ] T016 [US2] [US4] Revisión, confirmación y cancelación: `count-review-dialog.ts`,
  `shipment-link-dialog.ts` (candidatas con todas las páginas, aviso «No cambia lo servido del pedido ni
  las reservas consumidas») y estado cerrado en solo lectura con la operación visible al recuperar
  (`OperationStatusComponent`) y, en cada línea vinculada, la salida corregida (FR-022, US4.2). Aviso
  visible de la limitación P-07 en el detalle del conteo (salida de 6, real 4 → el ajuste devuelve 2 al
  saldo, pero el pedido sigue mostrando 6 servidas; el pedido no se corrige), según
  [spec § FR-020](spec.md) y el [contrato UI](contracts/cycle-counts-ui.md). Vitest,
  con una prueba de un conteo cerrado con una línea vinculada que muestra el aviso. **Rojo esperado**: la spec no encuentra «Confirmar ajuste».
  Depende de T015 y, para el vínculo, de T012.
- [ ] T017 [US1] [US2] E2E-03 en `frontend/e2e/hu04-cycle-counts.spec.ts`: operario crea, inicia y cuenta
  con una diferencia; responsable revisa con motivo y confirma; existencias ajustadas y ubicación fuera de
  conteo. Reutiliza las cuentas `operario` y `responsable` y `POST /testing/stock-receipts`; sin cambios en
  fixtures de A (si hicieran falta, preguntar). **Rojo esperado**: falla en la primera navegación a
  `/inventarios/nuevo`. Depende de T016. **M1 de analyze-1**: si el E2E falla a mitad, debe limpiar
  (cancelar) o reutilizar su conteo abierto para no dejar ubicaciones congeladas que rompan la
  siguiente ejecución.

## Phase 8: Cierre

- [ ] T018 ⚑ Evidencia y documentos: [quickstart.md](quickstart.md) § Evidencia con la matriz C y la
  suite completa; estado de [spec.md](spec.md); en `integration-tasks.md` marcar INT-HU04-01/02/03 con su
  evidencia (P-07); evidencia V-09/V-10 en el quickstart común (P-08). Sin código. Depende de T010, T011,
  T012 y, si se hicieron, T017.
  - **Acordado con A en T001 (2026-11-21)**: los cierres de INT-HU04-01/02/03 se ligan a su evidencia
    (P-07); en el quickstart común publicar la evidencia de la matriz C e incluir V-07, V-08 y V-12 del
    plan, y dejar identificada la prueba de recepción de V-09 que queda pendiente de HU-01 (P-08, P-05).

---

## Phase 9: Convergence

Vacía hasta `/speckit-analyze` y `/speckit-converge`.

## Si hay tiempo (fuera de la numeración, accesorio)

- S1 Versión por línea para que dos personas cuenten a la vez el mismo conteo sin 412 (D-11).
- S2 SC-006: carga con 25 sesiones y conteos de 50 líneas.
- S3 SC-007: validación con trabajadores representativos con el guion del quickstart (principio III).
- S4 En la pantalla de ubicaciones, enlazar «En conteo» con el conteo que la ocupa.
- S5 Prueba de recepciones sobre un alcance congelado cuando exista el escritor de HU-01 (A, P-05).
- S6 Filtro de la lista de conteos por ubicación.

## Dependencias

```text
T001 ∥ T002 (no bloquean)
T003 ∥ T004 → T005 (T003) → T006 (T003) → T007 (T005, T006) → T008 → T009 (T004, T008)
T009 → T010 ∥ T011, T009 → T012
T013 (cualquier momento) → T014 (T006) → T015 → T016 (T012) → T017
T010, T011, T012, [T017] → T018
```

**Dependencias externas**: 🔀 PR #10 (HU-03) en T003 (migración sobre `HU03_Shipments`), T005 y T010
(confirmar salidas) y T012 (lectura de salidas). Si el PR #10 cambia al fusionarse, merge en esta rama,
regenerar `HU04_CycleCounts` y volver a pasar la suite antes de seguir.

**Archivos de A**: solo T013 (`pending-operation.store.ts`, `operation-status.component.ts`). Documentos
comunes: T002 y T018 (`integration-tasks.md`, `quickstart.md` común), y los diffs P-01…P-05 se proponen
en el PR sin editarse (T001).

## Paralelismo real

- T003 ∥ T004: esquema nuevo frente a `StockCore`, sin archivos comunes.
- T010 ∥ T011: dos archivos de prueba, ambos tras T009.
- T013 en paralelo a cualquier tarea de backend.
- No hay más: T005–T009 comparten `CycleCountCommands.cs` y se encadenan.

## Estrategia

1. **MVP** (T003–T009): conteo → diferencias → ajuste y congelación real. Entregable aunque falte todo lo
   demás; las pruebas de API cubren US1–US3.
2. **Robustez** (T010–T011): carreras y fiabilidad, exigidas por el principio V para aceptar.
3. **Corrección de salidas** (T012).
4. **Interfaz** (T013–T017). Sin ella, la funcionalidad existe por API pero no se valida con trabajadores.
5. **Cierre** (T018).
