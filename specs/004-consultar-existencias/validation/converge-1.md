# HU-05 — /speckit-converge, iteración 1

**Fecha**: 2026-11-20. **Rama**: `b/hu-05-consultar-existencias`, desde `e220381` (T019).
**Alcance**: reconciliar [spec](../spec.md), [plan](../plan.md), [research](../research.md),
[data-model](../data-model.md), [contratos](../contracts/stock-api.md) y [quickstart](../quickstart.md)
con lo construido en T003–T020 (historial desde `92686c5`). Con la [constitución](../../../.specify/memory/constitution.md)
como marco. Sin cambios de código y sin tareas nuevas: [tasks.md](../tasks.md) conserva sus 20 tareas.

**Revisión de A del PR #9 (2026-11-21)**: A aprueba los diffs de los documentos comunes (salvo
P-04, pendiente con S1), aplicados en T002. Aprueba también:

- los cambios de B en archivos de A (tabla de commits del plan);
- la cuarta cuenta E2E `ResponsableAlmacen` (`ef296cc`, `110df1a`);
- que HU-05 llame a `pending.begin(…, appRoute)` + `send()` sin modificar `mutate()`.

La aprobación quedaba condicionada a cerrar G2 y G3: resueltos con C3 (`8009807`) y C4 (`363de56`).

## Desviación respecto a la skill

`/speckit-converge` solo añade una fase `## Phase N: Convergence` a `tasks.md` con una tarea por
hueco, y no toca `spec.md` ni `plan.md`. Por decisión de B, esta iteración hace lo contrario:

- reconcilia los documentos con lo construido;
- no añade tareas, y los incumplimientos se recogen aquí para decidirlos juntos.

## Nota de proceso

T003 a T007 se ejecutaron siguiendo a mano el `SKILL.md` de `/speckit-implement`. La sesión se había
abierto en la carpeta padre del repositorio y la skill no se registraba. De T008 en adelante, con la
skill cargada. El método fue el mismo: pruebas primero, rojo anotado y un commit por tarea.

## Cambios por documento

| Documento | Cambio | Verificable en |
|-----------|--------|----------------|
| spec.md | Estado «Implementada»; apartado «Estado de la entrega» con lo que no se cubre (FR-020 en UI, búsqueda de pedidos, conteo de FR-016, SC-006/007, S-04). Los requisitos no cambian. | tasks.md («Si hay tiempo», «Desviaciones registradas») |
| plan.md | Migración real; árbol de archivos **construido** con las diferencias (sin `Touch()`, `GET /stock/reservations` en `ReservationQueries`, sin `order-list.page` ni pestaña «Reservas», nombres reales de las pruebas, siembra en `MapStockEndpoints`); archivos de A añadidos al implementar y su commit; estado de la revisión de A; cuatro filas en Complexity Tracking. | `939a88f`, `ad64a17`, `3b24957`, `ef296cc` y los commits de la tabla de A |
| research.md | D-02 (`activeLocationsOnly`), D-07 (prueba de la ubicación por desactivación y **412 antes de 409** cuando el alta gana a un escritor del artículo; 412/409 con la ubicación), D-14 (migración regenerada), D-15 (sin pestaña ni búsqueda; `mutationPermission()` en el store; `begin(…, appRoute)` + `send()` sin tocar `mutate()`), D-16 (`LockBarrier`), D-17 (exclusión en Production, cuerpo, motivos, compose, fixture y cuarta cuenta E2E). | `fd1b246`, `188b32f`, `0d847e3`, `113ab4d`, `f867b61`, `013d429`, `3b24957`, `4659302`, `110df1a`, `ef296cc` |
| data-model.md | Migración real; la cabecera se renueva desde el comando (el dominio no tiene `Touch()`). | `939a88f`, `CustomerOrderCommands.cs`, `ReservationCommands.cs` |
| contracts/stock-api.md | `GET /customer-orders` sin implementar (S1); detalles construidos del alta, reservar y liberar (claves de `errors`, 403 antes de 422, `InsufficientStock` sin `SIN-LOTE`); nueva sección `POST /testing/stock-receipts` (D-17). | `7378daf`, `ad64a17`, `de9ff61`, `3b24957` |
| contracts/stock-ui.md | Sin pestaña «Reservas» (S2) ni `/existencias/pedidos` (S1); opciones de filtro y diálogo de reservar limitados a la primera página; `appRoute` construido; foco tras rechazo **pendiente** (G1). | `f9c6588`, `3fab535`, `f867b61`, `013d429` |
| contracts/hu03-integration.md | Records fijados en `ICustomerOrderReader.cs`, lectura sin seguimiento, orden por Id y pruebas S-18. | `7378daf`, `0d847e3` |
| quickstart.md | Ya no dice «nada ejecutado»; migración y cuentas reales; «Estado de la matriz S»; specs frontend construidas; E2E con datos únicos; corrección de la evidencia de T014 (16 carreras con `LockBarrier`, no 14); nota de proceso; verificación final. | pruebas citadas en cada fila |
| validation/converge-1.md | Este informe. | — |

## Incumplimientos encontrados (sin corregir, a decidir con B)

| ID | Severidad | Fuente | Evidencia | Qué falta |
|----|-----------|--------|-----------|-----------|
| G1 | MEDIA | stock-ui § Accesibilidad («El foco va al primer error tras rechazo»); FR-027, FR-028 | `order-form.page.ts`, `reserve-dialog.ts` y `release-dialog.ts` no mueven el foco. Solo los diálogos usan `cdkTrapFocus` y cierran con Escape. | **Resuelto** con la tarea C1 de la fase «Convergence» de tasks.md: foco al primer `aria-invalid` o al aviso, con 5 pruebas Vitest. |
| G2 | BAJA | FR-026, US2.1 («se muestra su identificador como resultado registrado») | `order-form.page.ts` pone «Pedido creado. Operación …» y navega en el acto al detalle, que no muestra ese identificador. | **Resuelto** con C3 (2026-11-21), a petición de A: el contexto del pedido da `creationOperationId` y el detalle lo muestra. |
| G3 | BAJA | FR-004 (filtros) y FR-015 (asignación explícita) en artículos con más de 25 combinaciones | Las opciones de filtro (`stock-query.page.ts`) y las filas del diálogo de reservar (`reserve-dialog.ts`) salen de la página 1 de `GET /stock` (`pageSize` 25). | **Resuelto** con C4 (2026-11-21), a petición de A: se leen todas las páginas del artículo. |
| G4 | BAJA (prueba) | FR-025 en la interfaz (412: «recargar sin perder las cantidades escritas») | El camino 412 de los diálogos existe (evento `stale`, relectura) pero no tiene prueba Vitest. | **Resuelto** con la tarea C2: 2 pruebas en `order-detail.page.spec.ts`, comprobadas con una mutación temporal. |

### Resolución tras `/speckit-implement` Convergence (2026-11-20)

- **G1, resuelto** en `b87852b` (C1): foco al primer `aria-invalid` o al aviso tras un rechazo, en el
  alta y en los dos diálogos, con 5 pruebas. Reutiliza el patrón de HU-02 sin tocar archivos de A.
- **G4, resuelto** en el commit «/speckit-implement Convergence C2»: 2 pruebas del 412.
- ~~G2, límite conocido aceptado.~~ **Reabierto y resuelto** (2026-11-21): ver la sección
  siguiente.
- ~~G3, límite conocido aceptado.~~ **Reabierto y resuelto** (2026-11-21): ver la sección
  siguiente.

### Revisión de A del PR #9 y resolución de G2 y G3 (2026-11-21)

A condicionó la aprobación del PR #9 a cerrar G2 y G3, que dejan de ser límites aceptados.

- **G2, resuelto** en `8009807` (C3). El identificador sale del
  servidor: `GET /customer-orders/{id}/context` añade `creationOperationId`, leído del
  `OperationResult` del alta. Así se ve tras crear, tras cerrar sesión, por «Abrir pedido» y por
  URL. Sin cambio de esquema ni de migración y sin ruta nueva. Límite anotado: la lectura no tiene
  índice por `SourceDocumentId` (SC-006).
- **G3, resuelto** en `363de56` (C4). `StockApiService.allRows`
  recorre `GET /stock` en páginas de 100 hasta `totalCount`. El diálogo de reservar lista todas las
  combinaciones con existencias, y las opciones de filtro se completan con todas cuando la consulta
  sin filtros tiene más de una página. Probado con 130 y 30 combinaciones. Sin cambios de backend.

Ya documentados y no son huecos nuevos: el 409 `CountScopeFrozen` de FR-016, que depende de
INT-HU04-03; S-04, S-13 y la consulta de S-06, en «Si hay tiempo» S4; y FR-020 en la interfaz, que es
S2. No se ha visto ninguna infracción de los principios MUST de la constitución. Los principios III
(validación con trabajadores) y el rendimiento siguen sin medirse, como dice el plan.

## Requisitos y evidencia

| Req. | Evidencia | Estado |
|------|-----------|--------|
| FR-001 | `StockQueryTests` (S-03: 422 sin artículo), `stock-query.page.spec.ts` («Elige un artículo», selector con `state=all`) | Cumplido |
| FR-002 | `StockQueryTests` (S-01, S-05), `StockBalanceReaderTests`, `stock-query.page.spec.ts` (`includeEmpty`, «sin lote») | Cumplido |
| FR-003 | `StockQueryTests` (12/8/4, S-02 frente a HU-02), `StockBalanceReader` sin otro contador | Cumplido |
| FR-004 | `StockQueryTests` (filtros y páginas con totales del filtro), `stock-query.page.spec.ts` (opciones de todas las combinaciones, C4) | Cumplido |
| FR-005 | SNAPSHOT sobre `StockBalances`, que solo cambian en commits (`StockBalanceReader.cs`); S-04 no ejecutado | Parcial (sin borradores que probar) |
| FR-006 | `asOf` y «Actualizar» (`stock-query.page.spec.ts`), SNAPSHOT | Cumplido |
| FR-007 | `article.isActive` (API), «Inactivo» en texto (`stock-query.page.spec.ts`, `order-detail.page.spec.ts`) | Cumplido |
| FR-008 | «No hay existencias para esta selección» (`stock-query.page.spec.ts`) | Cumplido |
| FR-009 | `CustomerOrderTests` (S-08, 403 sin `Shipping.Record`), `order-form.page.spec.ts`, E2E-01 | Cumplido |
| FR-010 | `CustomerOrderTests` (« ped-… » → 409; índice → `DuplicateCode`, T009) | Cumplido |
| FR-011 | `CustomerOrderTests` (S-09), `order-form.page.spec.ts` (422 por línea, artículo repetido) | Cumplido |
| FR-012 | `CustomerOrderTests` (S-08: `Open`, servido 0, sin reservas) | Cumplido |
| FR-013 | `CustomerOrderTests` (detalle), `order-detail.page.spec.ts` (detalle + contexto) | Cumplido |
| FR-014 | `ReservationTests`/`ReleaseTests` (Operario 403), `order-detail.page.spec.ts` (sin botones) | Cumplido |
| FR-015 | `ReservationTests` (asignaciones explícitas, lote ajeno, bucket repetido), diálogo de reservar con todas las combinaciones (`order-detail.page.spec.ts`, C4) | Cumplido |
| FR-016 | `ReservationTests` (pendiente, `InsufficientStock`, `CustomerOrderNotOpen`, `ArticleInactive`); conteo abierto pendiente de HU-04 | Parcial (dependencia registrada) |
| FR-017 | `ReservationTests` (S-10: físico 10, reservado 7), `ReleaseTests` (físico sin cambios) | Cumplido |
| FR-018 | `ReleaseTests` (S-14, S-15), diálogo de liberar (motivo obligatorio, `releaseId`) | Cumplido |
| FR-019 | `ReservationTests` y `ReleaseTests` (movimientos con actor, motivo, soporte y contraparte), `DENY DELETE` (`Hu05SchemaTests`) | Cumplido |
| FR-020 | `GET /stock/reservations` (`ReservationTests` S-10); compromisos por pedido en el detalle | Parcial (sin pestaña, S2) |
| FR-021 | 401 sin sesión en `StockQueryTests` y `CustomerOrderTests`; 403 por permiso en esas dos y en `ReservationTests` y `ReleaseTests`; 400 sin antiforgery en `ReleaseTests` | Cumplido |
| FR-022 | `StockReliabilityTests` (V-08), `ReservationTests` (S-11) | Cumplido |
| FR-023 | Replays e `IdempotencyConflict` en `CustomerOrderTests`, `ReservationTests` y `ReleaseTests`; `DocumentAlreadyConfirmed` | Cumplido |
| FR-024 | V-05 en `ReservationTests` (20 rondas); carreras con `LockBarrier` y `SaveBarrier`; conciliación | Cumplido |
| FR-025 | 412/428 en `ReservationTests` y `ReleaseTests`; relectura en `order-detail.page.spec.ts` (`orderVersion` ≠ `ETag` y 412 en los dos diálogos, C2) | Cumplido |
| FR-026 | «Resultado pendiente de comprobar», restauración y «Abrir pedido» (`order-detail.page.spec.ts`, `operation-status-hu05.spec.ts`); operación del alta en el detalle (`CustomerOrderTests.C3_…`, C3) | Cumplido |
| FR-027 | Revisión antes de confirmar en alta, reservar y liberar; 409/422 conservando datos y con el foco en el primer error (`order-form.page.spec.ts`, `order-detail.page.spec.ts`; C1) | Cumplido |
| FR-028 | Español, unidad junto a cada cantidad, estados en texto, diálogos con teclado y foco tras rechazo (E2E-01 por roles y etiquetas; C1) | Cumplido |
| SC-001 | `StockQueryTests` (S-01, totales en todas las páginas), S-02 | Cumplido |
| SC-002 | Reflejo inmediato tras cada confirmación (`ReservationTests`, `ReleaseTests`, E2E-01); sin borradores (S-04) | Parcial |
| SC-003 | `ReservationTests` (S-10, S-11) sin cambios | Cumplido |
| SC-004 | V-05 (S-12), 20 rondas, en cada ejecución de la suite | Cumplido |
| SC-005 | Permisos, replays, 412 y V-08 (`StockReliabilityTests`) | Cumplido |
| SC-006 | — | Fuera de alcance: requiere datos sintéticos y carga (plan, Complexity Tracking) |
| SC-007 | — | Fuera de alcance: validación con trabajadores, guion en el quickstart (plan, Complexity Tracking) |

## Verificación final de la rama

Sobre `e220381`, el último commit de código; esta iteración solo cambia documentos.

| Comprobación | Resultado | Línea base previa |
|--------------|-----------|-------------------|
| Suite backend (receta de specs/003, `git archive HEAD`) | 206 aprobadas, 1 omitida (V-14), 0 fallidas | 121/1/0 antes de HU-05 (T003) |
| `dotnet ef migrations has-pending-model-changes` | Sin cambios | — |
| Vitest (`ng test --watch=false`) | 136/136 en 35 archivos; **143/143 tras C1 y C2** | 108 antes de T015 |
| `tsc` de app, specs y E2E | Sin errores | — |
| Playwright (`--grep-invert SC-005`) | 16/16 | 15/15 antes de T019 |
