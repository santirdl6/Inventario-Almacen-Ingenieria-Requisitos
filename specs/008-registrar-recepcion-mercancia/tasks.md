---
description: "Tareas de implementación de HU-01 — Registrar recepciones parciales de mercancía"
---

# Tasks: HU-01 — Registrar recepciones parciales de mercancía

**Input**: [spec](spec.md), [plan](plan.md), [investigación](research.md), [modelo](data-model.md), [HTTP](contracts/api.md), [UI](contracts/ui.md), [integración](contracts/integration.md) y [validación](quickstart.md).

**Prerequisites**: [constitución](../../.specify/memory/constitution.md), [plan global](../../.specify/memory/project-plan.md), [entrega HU-07](../007-gestionar-pedidos-proveedores/validation/hu01-handoff.md) y [backlog receptor](../001-reparto-specs-inventario/integration-tasks.md#hu-01--recepciones--persona-a). Aplicar una sola cadena EF desde el corte integrado real.

**Tests**: la spec exige pruebas de aceptación, concurrencia, recuperación y referencias reales. Escribir primero las pruebas correspondientes, observar el fallo esperado y repetirlas tras implementar. Los casos de SQL usan bases aisladas y barreras reproducibles; los de stock usan comandos reales. Una aprobación documental no cuenta como prueba de la aplicación.

**Organization**: US1–US3 P1, US4 P2. El MVP incorpora permisos, trazabilidad, atomicidad y recuperación básica desde su primera confirmación; US3 amplía la evidencia y el recorrido de recuperación.

## Format: `[ID] [P?] [Story] Description`

`[P]` identifica tareas de la misma oleada con archivos distintos, una vez cumplidas sus dependencias. Solo las tareas de historia llevan etiqueta `[USn]`. No marcar tareas, gates ni resultados externos sin evidencia.

## Path Conventions

Rutas relativas a la raíz. Los archivos nuevos siguen `backend/src/Inventario.Api/Features/Receiving/`, `backend/tests/Inventario.IntegrationTests/Receiving/` y `frontend/src/app/features/receiving/`; las pruebas de navegador viven en `frontend/e2e/`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: fijar el corte y preparar pruebas aisladas sin cambiar datos de desarrollo.

- [X] T001 Registrar en `specs/008-registrar-recepcion-mercancia/validation/baseline.md` rama, migraciones integradas, estado de HU-07/HU-08/HU-02 y consumidores presentes de B, versiones y línea base de build/pruebas/EF. Documentar omisiones reales y preservar los cambios locales.
- [X] T002 Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingFixture.cs` con bases aisladas, operario/lector/ajuste/contingencia, compras enviadas obtenidas por el flujo HU-07, artículos y ubicaciones reales, y barreras de confirmación. Reutilizar los fixtures existentes; stock y reservas solo mediante comandos autorizados.
- [X] T003 Crear `frontend/e2e/fixtures/receiving.fixture.ts` reutilizando sesión, aislamiento Compose y correo de captura; aprovisionar cuentas y preparar compras por el flujo real, conservar perfil HU-07 y retirar cuentas/recursos propios al terminar.

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: preparar el documento compartido sin habilitar todavía rutas de escritura.

- [X] T004 [P] Crear `backend/src/Inventario.Api/Domain/Receiving/Receipt.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptConfiguration.cs` con «UUID, `PurchaseOrderId` FK fija, `State` Draft/Confirmed, `OccurredAt` con zona normalizada a UTC, `SupplierDocumentReference` o `SupportNotApplicableReason`, `Reason`, `CreatedBy/At`, `RecordedAt`, `ActorId` y `OperationId` de confirmación (nulos en Draft), `RevisionStamp` interno y `Version` rowversion. Congelar datos documentales al confirmar.» Aplicar «`Draft → Confirmed`, una sola vez»; tocar revisión en cambios de hijos, sin alterar evidencia histórica.
- [X] T005 [P] Crear `backend/src/Inventario.Api/Domain/Receiving/ReceiptLine.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptLineConfiguration.cs` con «UUID estable, `ReceiptId`, `PurchaseOrderLineId` FKs restrictivas; única por recepción/línea de compra. `ReportedQuantity`, `AcceptedQuantity` decimal(18,3); `DiscrepancyReason` obligatorio si reportado supera aceptado. En confirmación conservar cantidades pedida/recibido previo/pendiente resultante por línea y referencia histórica de artículo desde la compra.» Aplicar CHECK «`0 ≤ aceptado ≤ reportado`».
- [X] T006 [P] Crear `backend/src/Inventario.Api/Domain/Receiving/ReceiptAllocation.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptAllocationConfiguration.cs` con «UUID estable, `ReceiptLineId`, `LocationId` restrictiva, código de lote propuesto en borrador, `LotId` del núcleo en confirmación (también para SIN-LOTE), `Quantity` decimal(18,3), instantánea de código y descripción de destino al confirmar.» Aplicar «cantidades positivas en asignaciones/correcciones y precisión» y referencias restrictivas; preparar claves sin FK redundante al artículo.
- [X] T007 [P] Crear `backend/src/Inventario.Api/Domain/Receiving/ReceiptIncident.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptIncidentConfiguration.cs` con «UUID, `ReceiptId`, descripción observada, tipo de incidencia, cantidad positiva y unidad observada cuando se conoce, motivo; `ReceiptLineId` opcional si se vincula a una línea de la misma recepción. Un artículo ajeno se identifica por texto, sin aceptar unidades ni crear referencia nueva al catálogo.»
- [X] T008 Generar migración EF `HU01Receiving` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y su `InventoryDbContextModelSnapshot.cs` desde el corte actual, con CHECK de documento/hijos, FK restrictivas, índices de consulta por compra/fecha/id, actor/fecha y relaciones por línea/asignación, unicidad de confirmación conforme al diario común y orden de persistencia sin ciclos. Completar los índices de corrección y comprobante en US4.
- [X] T009 Ejecutar y registrar en `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingMigrationTests.cs` pruebas de migración desde cero y sobre el esquema anterior con datos preservados, CHECK/FK y ausencia de diferencias EF. Completar T004–T008 antes del escritor.

**Checkpoint**: modelo de recepción listo con protección heredada de la línea de compra y migración verificada.

## Phase 3: User Story 1 — Registrar y confirmar una entrega parcial (Priority: P1) · MVP

**Goal**: guardar y reabrir un borrador, revisar destinos/lotes y confirmar con efectos íntegros en pedido y stock.

**Independent Test**: con un pedido enviado de 10, guardar 6 sin efectos; confirmar 6 distribuidas en 2+4, luego 4; comprobar estados, reservas y ETag de compra. Rechazar envíos no aceptados y ubicaciones/lotes inválidos.

### Tests for User Story 1

- [ ] T010 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingContractTests.cs`: altas/PUT/lecturas/contexto/confirmación, permisos y antiforgery, forma de importes y errores por línea, separación de versiones y ETag de hijos, protección en borrador y confirmada (Q01-01/02/05).
- [ ] T011 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingConfirmationTests.cs`: confirmar por escritor real 6+4, reparto 2+4, lote obligatorio/SIN-LOTE, artículos repetidos por línea y agregación por bucket. Verificar V-02/V-03, SC-006 de HU-07, un solo efecto al repetir, 6+6 concurrentes y dos confirmaciones válidas en el primer bucket (V-06); fallo antes de commit, rechazo por permiso, exceso, inactividad y precondición.
- [ ] T012 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingMasterDataIntegrationTests.cs` para INT-HU01-01/02: alta y retirada de referencias reales desde borrador, protección heredada de la compra y carreras en ambos órdenes frente a edición, borrado y desactivación, con barreras y conexiones independientes. Verificar soporte, contraparte y que no hay huérfanos.
- [ ] T013 [P] [US1] Crear `frontend/src/app/features/receiving/pages/receipt-form.page.spec.ts`: localizar compra elegible y línea exacta, unidad y destinos, recibido/pendiente, revisión previa, errores y conservación de valores, guardado sin falso éxito y relectura tras confirmación.

### Implementation for User Story 1

- [X] T014 [US1] En `backend/src/Inventario.Api/Features/Receiving/Commands/SaveReceiptDraft.cs` y `backend/src/Inventario.Api/Features/Receiving/ReceiptWrite.cs`, implementar alta/edición bajo ejecutor común; FK de compra fija, validación de pertenencia, identidad de hijos, cadenas decimales y UTC, `If-Match`, actor y actualización de revisión. Adquirir locks de compra/recepción/ubicaciones/artículos según contrato, sin efectos de stock. Dejar guardar distribuciones aún incompletas, sin aceptar datos inválidos.
- [X] T015 [US1] Implementar `backend/src/Inventario.Api/Features/Receiving/Commands/ConfirmReceipt.cs`: adquirir locks ordenados, revalidar revisión, envío Accepted, pendiente vivo por línea y destino/lote; aplicar «una confirmación exige al menos una aceptación positiva y suma exacta por línea (cero aceptado implica ninguna asignación)». Agrupar solo entrada a `StockCore.Receive`, conservar lote efectivo y datos de cada asignación, incrementar recibido por `PurchaseOrderLine` y llamar `DeriveReceiptState` en el mismo commit que recepción, diario, saldo y resultado. Garantizar replay y no cambiar reservas. Coordinar gate de conteo real cuando exista, sin habilitar un escritor de conteos con lector constante.
- [X] T016 [US1] Implementar `backend/src/Inventario.Api/Features/Receiving/Queries/ReceiptQueries.cs` y `backend/src/Inventario.Api/Features/Receiving/ReceivingEndpoints.cs`: GET lista/detalle/contexto, POST, PUT y confirmación, DTO y versiones separadas, políticas y errores del contrato; registrar rutas en `backend/src/Inventario.Api/Program.cs`. Usar SNAPSHOT para lecturas compuestas y no mezclar maestros vivos bajo el ETag documental.
- [X] T017 [US1] Implementar `frontend/src/app/features/receiving/data/receipts-api.service.ts` y ampliar `frontend/src/app/core/operations/pending-operation.store.ts` para rutas y permisos de recepción; reutilizar transporte común con la misma intención, precondición y autoría, y conservar comprobación básica del resultado al recargar.
- [ ] T018 [US1] Implementar `frontend/src/app/features/receiving/pages/receipt-list.page.ts`, `frontend/src/app/features/receiving/pages/receipt-form.page.ts`, `frontend/src/app/features/receiving/pages/receipt-detail.page.ts` y `frontend/src/app/features/receiving/receiving.routes.ts`, con navegación en `frontend/src/app/operation-areas.ts`: selección, borrador, revisión y confirmación accesibles; presentar resultado persistido y vínculo a compra sin confundir previsto y confirmado.
- [ ] T019 [US1] Crear y ejecutar `frontend/e2e/receiving-partial.spec.ts` para el recorrido MVP y repetir las pruebas T010–T013; registrar en `specs/008-registrar-recepcion-mercancia/validation/us1.md` V-02/V-03/V-06, SC-006 de HU-07 e INT-HU01-01/02 con el escritor real.

**Checkpoint MVP**: recibir 6 y luego 4 localiza 10 y cierra el pendiente; los rechazos, reintentos y carreras conservan las garantías desde el primer corte.

## Phase 4: User Story 2 — Registrar diferencias sin inflar las existencias (Priority: P1)

**Goal**: conservar diferencias auditables sin contabilizar lo no aceptado.

**Independent Test**: con 4 pendientes, reportar 7, rechazar aceptar 7 y aceptar expresamente 4 con motivo; conservar la sustitución como incidencia y guardar un borrador sin aceptación positiva.

### Tests for User Story 2

- [ ] T020 [P] [US2] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingDiscrepancyTests.cs` para Q01-03: exceso, faltante normal y daños, sustitución ajena sin FK ni asignación, línea repetida de artículo y borrador solo de incidencias que no confirma. Probar guardado y reapertura con todas las cantidades aceptadas a cero y rechazo íntegro de una confirmación que mezcle asignaciones válidas e inválidas.
- [ ] T021 [P] [US2] Crear `frontend/src/app/features/receiving/pages/receipt-discrepancies.component.spec.ts` con diferencias reportado/aceptado/pendiente, obligatoriedad de motivo, reapertura y confirmación inhabilitada sin aceptación positiva.

### Implementation for User Story 2

- [ ] T022 [US2] Completar captura, reemplazo y proyección de incidencias en `backend/src/Inventario.Api/Features/Receiving/ReceiptWrite.cs`, `backend/src/Inventario.Api/Features/Receiving/Commands/SaveReceiptDraft.cs` y `backend/src/Inventario.Api/Features/Receiving/Queries/ReceiptQueries.cs`; comprobar pertenencia de línea opcional, motivo y cantidad positiva si informada. En `backend/src/Inventario.Api/Features/Receiving/Commands/ConfirmReceipt.cs`, rechazar toda la confirmación si alguna asignación es inválida y conservar el borrador; si todas son válidas, aplicar los efectos en una sola transacción. Conservar las incidencias con aceptación cero sin generar stock.
- [ ] T023 [US2] Implementar `frontend/src/app/features/receiving/pages/receipt-discrepancies.component.ts` e integrarlo en `frontend/src/app/features/receiving/pages/receipt-form.page.ts` y `frontend/src/app/features/receiving/pages/receipt-detail.page.ts`; captura y lectura de incidencias, cantidades observadas y aceptadas separadas, foco y motivos visibles.
- [ ] T024 [US2] Crear y ejecutar `frontend/e2e/receiving-discrepancies.spec.ts` y repetir T020/T021; registrar los acumulados sin efectos indebidos y la evidencia Q01-03/SC-002 en `specs/008-registrar-recepcion-mercancia/validation/us2.md`.

## Phase 5: User Story 3 — Confirmar con seguridad y recuperar el resultado (Priority: P1)

**Goal**: comprobar de forma explícita la confirmación ante pérdida de respuesta, conflictos o cambio de sesión.

**Independent Test**: confirmar simultáneamente 6+6 contra 10; perder y recuperar la respuesta del ganador. Una consulta sin resultado no provoca una segunda entrada.

### Tests for User Story 3

- [ ] T025 [P] [US3] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingRecoveryTests.cs` para V-07/V-08: respuesta perdida después de commit, recuperación inmutable de cuerpo/Location/ETag, clave reutilizada con otro contenido, nueva clave sobre confirmada, autorización original, estado obsoleto y fallos antes de commit. Comparar recepción, pedido, saldo, reservas y diario tras cada caso; reutilizar la carrera de pendiente T011.
- [ ] T026 [P] [US3] Crear `frontend/src/app/features/receiving/data/receiving-recovery.spec.ts` para recarga, 404 no definitivo, problema 503/`Retry-After`, logout y distinta cuenta; comprobar que una respuesta histórica refresca estado vivo y no crea otra intención.

### Implementation for User Story 3

- [ ] T027 [US3] Consolidar comprobación y reintento de la misma intención en `frontend/src/app/features/receiving/data/receipts-api.service.ts` y `frontend/src/app/core/operations/operation-status.component.ts`: referencia/clave/cuerpo/ETag ligados al actor, sin arrastrar datos al cerrar sesión; recuperar resultado, refrescar compra y mostrar conflictos accionables sin introducir otro ejecutor.
- [ ] T028 [US3] Crear y ejecutar `frontend/e2e/receiving-recovery.spec.ts` con respuesta perdida/controlada y dos sesiones; repetir T025/T026 y dejar evidencia de Q01-04/05, SC-003/004 e INT-HU01-02 en `specs/008-registrar-recepcion-mercancia/validation/us3.md`. Ejecutar carrera de conteo si HU-04 está integrado; si no, registrar la frontera sin simularla.

## Phase 6: User Story 4 — Consultar la evidencia y corregir un registro confirmado (Priority: P2)

**Goal**: reconstruir recepciones e incorporar compensaciones y comprobantes con evidencia y autorización.

**Independent Test**: confirmar 6 de 10, compensar 2 sobre asignación original y ver 4 recibidas/6 pendientes con historial intacto. Incorporar dos veces un comprobante y constatar un solo efecto antes de autorizar la reapertura.

### Tests for User Story 4

- [ ] T029 [P] [US4] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingCorrectionTests.cs`: lectura histórica, compensación parcial y total, corrección tras desactivación, revisión de origen obsoleta, límite por asignación/bucket, saldo reservado y disminución física posterior; probar dos correcciones concurrentes, mismo `CorrectionId` con dos claves, rollback e inmutabilidad.
- [ ] T030 [P] [US4] Crear `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingContingencyTests.cs`: comprobante original y repetido con claves iguales/distintas, cambio de contenido, dos importaciones concurrentes, permisos por separado y juntos en escritura/replay/GET de operación, fallo antes de commit y actor distinto del responsable declarado. Probar rechazo de reapertura con comprobante pendiente o ajeno, evidencia incompleta, diferencia abierta, revisión obsoleta o falta de permiso, y autorización válida con actor, fecha y evidencia; la importación nunca cambia el estado de reapertura.
- [ ] T031 [P] [US4] Crear `frontend/src/app/features/receiving/pages/receipt-evidence.page.spec.ts`: filtros por compra/artículo/período/actor, evidencia inmutable, compensación con selección de asignación y motivo, captura de comprobante y autorización de reapertura separada. Mostrar el rechazo del servidor si falta conciliación y no mostrar autorización antes de confirmación.

### Implementation for User Story 4

- [X] T032 [P] [US4] Crear `backend/src/Inventario.Api/Domain/Receiving/ReceiptCorrection.cs`, `backend/src/Inventario.Api/Domain/Receiving/ReceiptCorrectionLine.cs` y mappings `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptCorrectionConfiguration.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptCorrectionLineConfiguration.cs` con «`CorrectionId` UUID único, recepción, movimiento original y resultante, actor, operación, fecha y motivo; cantidades positivas enlazadas por `ReceiptAllocationId` original. Efecto real negativo, reserva sin cambio. Evidencia append-only.»
- [X] T033 [P] [US4] Crear `backend/src/Inventario.Api/Domain/Stock/ContingencyReference.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ContingencyReferenceConfiguration.cs` con «identificador externo normalizado y único, tipo, fecha del hecho, responsable declarado, datos normalizados, enlace a recepción y operación conciliada, usuario importador e instante. Difiere de la referencia del albarán y de la clave HTTP.» Crear `backend/src/Inventario.Api/Domain/Receiving/ReceiptContingencyScope.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/ReceiptContingencyScopeConfiguration.cs` con «UUID, compras y ubicaciones afectadas, lista no vacía de IDs de comprobante esperados, estado `Suspended`/`ReopeningAuthorized`, revisión de agregado, referencias de evidencia para diario, existencias y pedido, diferencias identificadas y estado/resolución de cada una. Conservar al autorizar actor, instante y evidencia revisada; esa autorización no se sobrescribe.»
- [ ] T034 [US4] Generar migración aditiva `HU01ReceivingEvidence` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/`, con snapshot actualizado, unicidad de corrección/comprobante, FK restrictivas, CHECK de cantidades positivas y estados del alcance, y protección de evidencia histórica para el rol de ejecución. Ampliar `backend/tests/Inventario.IntegrationTests/Receiving/ReceivingMigrationTests.cs` con actualización de datos existentes e integridad de ejecución.
- [ ] T035 [US4] Añadir operación tipada de compensación al núcleo existente en `backend/src/Inventario.Api/Features/Stock/Core/StockCommands.cs` y `backend/src/Inventario.Api/Features/Stock/Core/StockCore.cs`; `Correction` ligado al movimiento original, efecto `AdjustDown`, `ReservedDelta=0`. Validar asignación original, remanente tras correcciones, disponible por bucket y dependencia posterior conservadora de R01-05 bajo los locks; no usar `Ship` ni saldo directo.
- [ ] T036 [US4] Implementar `backend/src/Inventario.Api/Features/Receiving/Commands/CorrectReceipt.cs` y `backend/src/Inventario.Api/Features/Receiving/Queries/ReceiptQueries.cs`: `If-Match` de recepción, `CorrectionId` único, evidencia y efecto común junto a recibido neto y nuevo estado, tocar ambas revisiones sin cambiar documento original. Leer compensaciones persistidas, no reescribir evidencia con maestros vivos. Registrar ruta de corrección en `backend/src/Inventario.Api/Features/Receiving/ReceivingEndpoints.cs`.
- [X] T037 [US4] Registrar en `backend/src/Inventario.Api/Infrastructure/Identity/IdentityConfiguration.cs`, `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/InventoryOperationExecutor.cs` y `backend/src/Inventario.Api/Infrastructure/Http/OperationEndpoints.cs` la política compuesta exigida tanto en mutación/replay como en consulta del resultado de contingencia. Reusar permisos existentes; no conceder nuevos grants a los roles ni relajar otros resultados. El lector existente ya reautoriza la política guardada; [pruebas](validation/us4.md).
- [ ] T038 [US4] Implementar `backend/src/Inventario.Api/Features/Receiving/Commands/ReconcileReceiptVoucher.cs` y registrar `/contingencies/reconcile` en `backend/src/Inventario.Api/Features/Receiving/ReceivingEndpoints.cs`: normalizar comprobante, declarar su lock junto a compra/recepción antes de ubicaciones/artículos, deduplicar duraderamente incluso con otra clave y confirmar por el mismo flujo de T015 en un solo intento. Conservar persona declarada y actor real separados. Implementar declaración, revisión y autorización de reapertura en `backend/src/Inventario.Api/Features/Receiving/Commands/ReceiptContingencyScopeCommands.cs` y sus rutas en `ReceivingEndpoints.cs`; verificar `Inventory.Adjust`, `If-Match` y el alcance bajo lock, rechazar comprobantes pendientes o ajenos, evidencia incompleta y diferencias abiertas, y registrar autorización inmutable con actor, fecha y evidencia.
- [ ] T039 [US4] Implementar `frontend/src/app/features/receiving/pages/receipt-evidence.page.ts` y `frontend/src/app/features/receiving/pages/receipt-contingency.page.ts`, conectarlas desde `frontend/src/app/features/receiving/receiving.routes.ts` y ampliar `frontend/src/app/core/operations/pending-operation.store.ts`; ofrecer compensación, importación y revisión de reapertura como acciones separadas. Mostrar comprobantes, diferencias y evidencia, y comunicar el rechazo de la reapertura con conciliación incompleta; solo mostrar autorización tras confirmación del servidor. Verificar con T031.
- [ ] T040 [US4] Crear y ejecutar `frontend/e2e/receiving-evidence.spec.ts` y repetir T029–T031; registrar en `specs/008-registrar-recepcion-mercancia/validation/us4.md` Q01-06 y SC-005, consulta con fechas/actor/artículo, compensación y contingencia sin duplicado. Diferenciar prueba técnica de aceptación operativa H5-02.

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: evidencias finales, regresión y coordinación de los cambios comunes.

- [ ] T041 [P] Implementar el perfil Q01-08 en `scripts/measure-hu01-receiving.mjs` y documentar su preparación aislada, semilla 7001, muestra por operación y medición visible en `specs/008-registrar-recepcion-mercancia/validation/performance.md`. Ejecutar con entorno representativo; distinguir PASS, FAIL e inconcluso sin rebajar volumen, tiempos ni muestra exigida.
- [X] T042 [P] Preparar guion y acta SC-008 en `specs/008-registrar-recepcion-mercancia/validation/usability.md`; cuando participen tres operarios reales, documentar observaciones, incidencias y repetición tras subsanarlas. Enlazar H5-02/H5-03; registrar lo pendiente si no hay evidencia humana u operativa.
- [ ] T043 [P] Preparar `specs/008-registrar-recepcion-mercancia/validation/shared-review.md` para revisión A+B de compensación, política compuesta, recepciones/incidencias, lote de migraciones y gate de conteo; enlazar extensiones de HU-04/HU-06 y exigir verificación de consumidores antes de habilitar sus escritores. Registrar revisión real antes de integrar y sincronizar los contratos comunes afectados cuando esté coordinada.
- [ ] T044 Ejecutar la regresión de SQL/EF/Compose, frontend y E2E de HU-01 más HU-02/HU-07/HU-08/StockCore y consumidores presentes; verificar pruebas de actualización/creación de base y restricciones de runtime. Consolidar Q01-01–09, FR-001–017, SC-001–008, V-02/V-03/V-06/V-07/V-08 y SC-006 de HU-07 en `specs/008-registrar-recepcion-mercancia/validation/acceptance.md`. Actualizar INT-HU01-01/02 en `specs/001-reparto-specs-inventario/integration-tasks.md` solo con evidencias propias y enlazar la aceptación integrada desde `specs/007-gestionar-pedidos-proveedores/validation/hu01-handoff.md` si se demuestra. Mantener estados reales de H5, revisión y rendimiento.

## Dependencies & Execution Order

```text
Setup T001 → T002 → T003
  → Foundational {T004–T007} → T008 → T009
  → US1 {T010–T013} → T014 → T015 → T016 → T017 → T018 → T019 (MVP)
  → US2 {T020,T021} → T022 → T023 → T024
  → US3 {T025,T026} → T027 → T028
  → US4 {T029–T031} → {T032,T033} → T034 → T035 → T036 → T037 → T038 → T039 → T040
  → Polish {T041–T043} → T044
```

Los conjuntos entre llaves son oleadas de archivos distintos. US2 extiende el borrador y la confirmación de US1; US3 extiende la recuperación básica del MVP y no la aplaza; US4 usa una recepción ya confirmable. Cada historia tiene su propio ejercicio con datos aislados. Repetir el checkpoint afectado si una integración posterior cambia la cadena EF, el ejecutor o las rutas compartidas. No ejecutar el perfil de rendimiento a la vez que suites de regresión.

### Parallel Examples

| Historia | Trabajo paralelo después del checkpoint previo |
|---|---|
| US1 | T010 contrato HTTP, T011 confirmación, T012 maestros, T013 formulario; cada suite en su archivo y con soporte T002/T003. |
| US2 | T020 casos SQL de diferencia y T021 componente; integrar después de verificar el contrato. |
| US3 | T025 recuperación de servidor y T026 de navegador unitario; coordinar cambios sobre el servicio común al pasar a T027. |
| US4 | T029 compensación, T030 comprobante y T031 evidencia UI; después T032/T033 en entidades y mappings distintos. Generar el snapshot solo en T034. |

## Implementation Strategy

1. **MVP (US1)**: modelo → borrador → confirmación con V-02/V-03/V-06 y protección INT acreditada. Demostrar 6+4, más el rechazo de 6+6, sin abrir una confirmación que pueda duplicar efectos.
2. **Incrementos**: US2 distingue incidencias y cantidades; US3 completa recuperación de resultados entre sesiones; US4 incorpora consulta histórica, compensación y conciliación de comprobantes.
3. **Entrega**: verificar las regresiones comunes y revisión A+B; publicar evidencia de carga y de usabilidad con sus estados reales. El cierre operativo requiere la aceptación de los responsables, no solo las suites automatizadas.
