# HU-04 — /speckit-converge, iteración 1

**Fecha**: 2026-11-21. **Rama**: `b/hu-04-realizar-inventarios-ciclicos`, desde `fa2a389` (T018). El
último commit de código es `ef9b596` (T017).

**Alcance**: contrastar [spec](../spec.md), [plan](../plan.md) y [tasks](../tasks.md) con el código y
las pruebas reales de T003–T017. También se usan [research](../research.md),
[data-model](../data-model.md), los [contratos](../contracts/cycle-counts-api.md) y el
[quickstart](../quickstart.md). El marco es la [constitución](../../../.specify/memory/constitution.md).
Una tarea marcada [X] no basta como prueba: cada fila de abajo cita el código o la prueba que se ha
leído.

## Desviación respecto a la skill

`/speckit-converge` añade una fase `## Phase N: Convergence` con tareas `T0xx`. Esta iteración
sigue la convención de HU-03 y HU-05:

- las tareas correctoras se numeran `C1…`, fuera del presupuesto T001–T018;
- van en una fase nueva al **final** de tasks.md (`Phase 10: Convergence`). La `Phase 9: Convergence`
  existente queda intacta, porque la skill solo permite añadir;
- **no se implementan**: B las revisa con A antes de pasar `/speckit-implement`;
- spec.md y plan.md no se tocan.

## Nota de proceso

Todas las tareas de HU-04 se hicieron con la skill `/speckit-implement` cargada: T001–T018, con
T018 en `055a8bb` y `fa2a389`. También `/speckit-analyze` (`d9933ff` y sus correcciones). El método
fue el mismo en todas: pruebas primero, rojo anotado con su razón y un commit por tarea.

Cuando no hubo rojo natural, se comprobó con mutaciones temporales sin commit, anotadas en tasks.md:

- T009, segundo rojo;
- T010;
- T011;
- T015, renombrado.

T017 no tuvo rojo observable; se explica en el [quickstart](../quickstart.md#desviaciones-registradas-t010-t013t017).

## Hallazgos por tipo

| Tipo | Número | IDs |
|------|--------|-----|
| Trabajo ausente (`missing`) | 0 corregibles en HU-04 | Lo ausente está justificado (ver «Pendientes justificados») |
| Cumplimiento parcial (`partial`) | 7 | G1, G2, G3, G5, G6, G7, G8 |
| Comportamiento contradictorio (`contradicts`) | 1 | G4 |
| Código sin justificación (`unrequested`) | 0 | — |

Por severidad: 0 críticos, 0 altos, 3 medios (G1–G3) y 5 bajos (G4–G8).

No se ha visto ninguna infracción de un principio MUST de la constitución. El principio III (SC-007)
y el rendimiento (SC-006) siguen sin medirse, como dice el plan en Complexity Tracking. Por eso HU-04
es integrable, pero no está aceptada.

**Código sin justificación**: no se ha encontrado. Lo que va más allá del texto de la spec está
recogido en un contrato o en una tarea:

- `shipmentCorrectionNotice` y `shipmentCorrections`, con `orderChanged: false`: T012, contrato API
  actualizado;
- `GET …/context`, con usuarios, reservado y salidas: D-12, contrato API;
- la línea de `operation-areas.spec.ts`: T014, registrada como desviación;
- el `finally` de limpieza del E2E-03: T017, M1 de analyze-1.

## Incumplimientos encontrados (sin corregir, a decidir con B)

| ID | Tipo | Severidad | Fuente | Evidencia | Qué falta | Tarea |
|----|------|-----------|--------|-----------|-----------|-------|
| G1 | partial | MEDIA | US3.1 («se rechaza indicando la ubicación y el conteo»); FR-014 | `ShipmentCommands.cs:178`: «Una ubicación de origen está en un conteo abierto», sin ubicación ni conteo. `ReservationCommands.cs:97` nombra la ubicación, pero no el conteo. `LocationCommands.cs:95` no nombra ninguno de los dos. `CountScopeFrozenTests` solo comprueba el código 409, no el `detail`. Al iniciar sí se nombran los dos (`CycleCountCommands.RequireNotFrozenAsync`). | Que los tres escritores nombren la ubicación y el `countCode` que la ocupa, y una prueba del `detail`. | C1 |
| G2 | partial | MEDIA | FR-023 («lista de conteos con su estado, alcance y responsables») | `CycleCountSummary` solo trae `createdBy`, y `count-list.page.ts` no muestra ningún responsable, solo el número de ubicaciones. El [contrato UI](../contracts/cycle-counts-ui.md) § Rutas ya omitía los responsables. | Mostrar en la lista quién creó y quién aprobó o canceló, y los códigos del alcance; o reformular FR-023. | C2 |
| G3 | partial | MEDIA | US4.3 («se consulta el pedido de cliente … la pantalla lo indica expresamente») | El aviso P-07 está en el detalle del conteo (`data-testid="p07-notice"`, `shipmentCorrectionNotice`), conforme a FR-019 y a P-07. El detalle del pedido de cliente (`features/stock`) y el de la salida (`features/shipping`) no indican que una salida suya se corrigió por conteo. | Indicarlo en el pedido o en la salida corregida, o acotar US4.3 al detalle del conteo con `/speckit-clarify`. | C3 |
| G4 | contradicts | BAJA | [contrato UI](../contracts/cycle-counts-ui.md) § Resultados («en `ReservedStockConflict`, enlace al pedido para liberar la reserva») | `count-detail.page.ts`, `releaseNeeded`: el enlace va a `/existencias`, no al pedido. El 409 no trae el pedido, y un bucket puede tener reservas de varios pedidos. Ya consta como desviación de T016. | Enlazar el pedido o los pedidos que reservan en el bucket, o corregir el contrato UI a `/existencias`. | C4 |
| G5 | partial | BAJA | FR-005 («y por conteo el número de líneas con diferencia») | `differenceCount()` solo se muestra en la revisión (`reviewMode()`), con `Inventory.Adjust` y en `AwaitingApproval`. No aparece al contar, en el estado cerrado ni para quien solo tiene `Counts.Record`. | Mostrar el total en todos los estados posteriores al inicio. | C5 |
| G6 | partial | BAJA | FR-004 y FR-029 («sin fracciones si el artículo no las admite»; «señalar el campo a corregir») | `count-detail.page.ts:503`: la fila de hallazgo no comprueba en local si el artículo admite fracciones, porque `StockArticleOption` no lo trae. El servidor lo rechaza (`CycleCountCommands.cs:172`, `found[i].countedPhysical`). Las filas esperadas sí se validan en local. Ya consta como desviación de T015. | Validar la fracción del hallazgo en local, con el artículo elegido. | C6 |
| G7 | partial | BAJA | Prueba independiente de US2 («el historial muestra un ajuste de −2 con motivo, quien contó y quien autorizó»); US4.2 | En el historial del artículo (`article-history.component.ts`, archivo de A) el ajuste lleva el motivo genérico «Ajuste por conteo cíclico» (`CycleCountCommands.cs:427`), al autorizador como actor, «Conteo» y el `countCode` como soporte. El motivo de cada diferencia y quién contó solo están en el detalle del conteo, y el historial no enlaza con él. Es coherente con FR-021 («el de cada diferencia se conserva en su línea del conteo») y con P-02, aprobada por A. | Decidir: aceptar el recorrido historial → `countCode` → detalle y documentarlo en el quickstart, o pedir a A un enlace del soporte `cycle-count` a `/inventarios/{id}`. | C7 |
| G8 | partial | BAJA | FR-016; HU-02 T034 («⛔ BLOQUEADA — depende de HU-04») | El lector es real (`ILocationUsage.cs:44`, `IsFrozen`) y está probado en `CountScopeFrozenTests` (HU-04). Pero T034 sigue sin marcar en `specs/003-gestionar-ubicaciones/tasks.md`. Faltan sus casos en `LocationsUpdateTests`/`LocationStockTests` y un Vitest de `location-detail.page` con `inOpenCount: true`: el aviso «En conteo» existe en la línea 55, pero todas las specs usan `false`. | Cerrar T034 de HU-02 con la evidencia de HU-04 y añadir el Vitest con `inOpenCount: true`. | C8 |

Nota documental, sin tarea: el enunciado de T010 en tasks.md («admite la edición y el borrado»)
sigue diciendo lo que el código no hace. tasks.md no se reescribe; su nota «Hecho» y el quickstart
(C-15, corregido en T018) dicen lo correcto: tras la retirada se admite el borrado, pero la
definición sigue bloqueada (`ArticleCommands.cs:68`, `ArticleDefinitionLocked`).

## Pendientes justificados (no generan tarea)

| Pendiente | Justificación | Responsable |
|-----------|---------------|-------------|
| Recepción sobre alcance congelado: FR-014, SC-002 («recibir») y la parte de recepción de V-09 | HU-01 (A) no tiene escritor. A confirmó que comprobará `IsInOpenCountAsync` bajo los bloqueos de todas las ubicaciones de destino antes de `Receive` (P-05). La prueba de recepción está identificada en el [quickstart común § 11](../../../.specify/memory/project/quickstart.md#11-hu-04--evidencia-de-integración-del-2026-11-21). | A, HU-01; S5 |
| SC-006 (25 sesiones, 50 líneas, p95 ≤ 3 s) | Necesita carga y datos sintéticos (plan, Complexity Tracking). | B, antes de la aceptación; S2 |
| SC-007 (validación con trabajadores, principio III) | Necesita sesiones con trabajadores; el guion está en el quickstart. | B, antes de la aceptación; S3 |
| «Si hay tiempo» S1–S6 | S1: versión por línea (D-11, límite aceptado del 412 a un segundo contador). S2/S3: arriba. S4: enlace «En conteo» → conteo. S5: arriba. S6: filtro por ubicación. Fuera de la numeración y accesorias. | B (S5, A) |
| Reevaluar el mínimo tras cada ajuste (HU-06) | Sin alertas integradas todavía; se enganchará bajo `article:{id}` (plan, Dependencias y riesgos). | A, HU-06 |
| T033 de HU-02 | No aplica: es `PUT`/`DELETE /locations/{id}` (405) y está hecha. La tarea de HU-02 que depende de HU-04 es T034 (G8, C8). | — |

## Requisitos y evidencia

| Req. | Evidencia leída | Estado |
|------|-----------------|--------|
| FR-001 | `CycleCountDraftTests` (2), 1–50 ubicaciones activas sin repetir, 403; `count-new.page.spec.ts` | Cumplido |
| FR-002 | `CycleCountStartTests.Starting_freezes_…`, `A_location_deactivated_…`; bloqueos de todas las ubicaciones y luego los artículos (T007) | Cumplido |
| FR-003 | `UX_CountLocations_Frozen` (`Hu04SchemaTests`); `CycleCountStartTests.A_location_in_another_open_count_…` | Cumplido |
| FR-004 | `CycleCountLinesTests` (cero explícito, negativo, fracción, `countedBy`/`countedAt`) | Cumplido; la validación local del hallazgo es parcial (G6) |
| FR-005 | `CountLineView.Difference`; `differenceText` en `count-detail.page.ts`; «N líneas con diferencia» en la revisión | Parcial (G5) |
| FR-006 | `CycleCountLinesTests` (hallazgo en el alcance, con lote, retirada; esperada no retirable, 422/51403) | Cumplido |
| FR-007 | `CycleCountLinesTests` (`submit` con 422 por línea; 409 `CountStateConflict` al contar fuera de `Counting`); triggers de D-07 (`Hu04SchemaTests`) | Cumplido |
| FR-008 | `CycleCountConfirmTests` (aprobación con autor e instante; Operario 403); `CycleCountReliabilityTests` (matriz de permisos) | Cumplido |
| FR-009 | `CycleCountConfirmTests.Review_and_confirm_…` (422 sin motivo) | Cumplido |
| FR-010 | `CycleCountConfirmTests` (un movimiento `Adjustment`, entradas ±, físico = contado, alcance liberado; sin diferencias no hay movimiento) | Cumplido |
| FR-011 | `CycleCountConfirmTests.Confirming_below_the_reserved_…`, `A_theoretical_changed_…`; mensajes con el bucket y las cantidades (`CycleCountCommands.cs:361`, `:370`) | Cumplido |
| FR-012 | `CycleCountConfirmTests.Cancelling_from_each_open_state_…` | Cumplido |
| FR-013 | `Hu04SchemaTests` (`DENY DELETE`, triggers 51401–51407, solo `THROW`); diario de solo adición de H0 | Cumplido |
| FR-014 | `CountScopeFrozenTests` (salida, reserva, desactivar y cambiar el tipo, vacía y ocupada → 409) | Cumplido salvo la recepción (HU-01, justificado). El motivo no nombra la ubicación ni el conteo (G1) |
| FR-015 | `CountScopeFrozenTests` (liberar admitido); `CycleCountConcurrencyTests.C13_confirming_and_releasing_…` | Cumplido |
| FR-016 | `ILocationUsage.IsInOpenCountAsync` sobre `IsFrozen`; `inOpenCount` en `GET /locations/{id}` y en `/stock` (`CountScopeFrozenTests`); aviso en `location-detail.page.ts:55` | Cumplido; la tarea de HU-02 queda abierta (G8) |
| FR-017 | `CycleCountConcurrencyTests.C13_…` (ambos órdenes, 20 rondas); mutación de T010 | Cumplido |
| FR-018 | `ShipmentCorrectionTests.C19_…`; `correctsShipmentId` en `PUT …/review`; `shipment-link-dialog.ts` | Cumplido |
| FR-019 | `ShipmentCorrectionTests.C19_…` (salida, servido, estado, consumos y asignaciones intactos; `orderChanged: false`); aviso P-07 en el detalle | Cumplido en el detalle del conteo; en la pantalla del pedido, parcial (G3) |
| FR-020 | `ShipmentCorrectionTests.C20_…` | Cumplido |
| FR-021 | Movimiento con `SupportReference = countCode` y `CounterpartyNotApplicableReason` (`CycleCountCommands.cs:380-382`); motivo, `countedBy`, `reasonBy` y `correctsShipmentId` en la línea (D-06, P-02) | Cumplido según su redacción |
| FR-022 | Historial de A con el tipo «Conteo» y el `countCode` como soporte; detalle cerrado con «Corrige la salida del pedido …» (`count-review.page.spec.ts`) | Cumplido; el recorrido de la prueba independiente de US2 es parcial (G7) |
| FR-023 | `CycleCountQueries.ListAsync`, `count-list.page.ts` | Parcial: sin responsables (G2) |
| FR-024 | `CycleCountArticleUsageContributor`; `Hu05SchemaTests` (FK exactas); `CycleCountConcurrencyTests.C15_…` | Cumplido (borrado admitido tras la retirada, definición bloqueada por la regla de A) |
| FR-025 | `CycleCountConfirmTests.An_inactive_article_…`; `StockCoreAdjustTests.An_inactive_article_…` | Cumplido |
| FR-026 | `CycleCountReliabilityTests.C17_…` (412/428); 412 con relectura sin perder lo escrito (`count-detail.page.spec.ts`) | Cumplido |
| FR-027 | `CycleCountReliabilityTests.C17_…` (replay de las nueve escrituras, `IdempotencyConflict`) | Cumplido |
| FR-028 | `CycleCountReliabilityTests.C18_…`; `OperationStatusComponent` con «Comprobar resultado» y «Abrir conteo» (`operation-status-hu04.spec.ts`, `count-review.page.spec.ts`) | Cumplido |
| FR-029 | Textos en español, unidad junto a cada cantidad, diferencia en texto, foco al primer error y lo escrito conservado (specs de `/inventarios`) | Cumplido salvo la fracción del hallazgo (G6) |
| US1.1–US1.6 | `CycleCountDraftTests`, `CycleCountStartTests`, `CycleCountLinesTests`; E2E-03 | Cumplido |
| US2.1–US2.7 | `CycleCountConfirmTests`; `count-review.page.spec.ts` (reservado actual, quién contó y cuándo, total de diferencias); E2E-03 | Cumplido; la prueba independiente del historial es parcial (G7) |
| US3.1–US3.7 | `CountScopeFrozenTests` (3), `CycleCountConcurrencyTests` (C-13) | Cumplido; US3.1 no nombra la ubicación ni el conteo (G1) |
| US4.1–US4.4 | `ShipmentCorrectionTests` (2); `count-review.page.spec.ts` (vínculo, aviso y cerrado) | Cumplido salvo US4.3 en la pantalla del pedido (G3) |
| US5.1–US5.4 | `CycleCountLinesTests`, `CycleCountConfirmTests.An_inactive_article_…` | Cumplido |
| US6.1–US6.4 | `CycleCountReliabilityTests` (12) | Cumplido |
| SC-001 | Invariantes 1–5 y 7 en cada confirmación (`CycleCountConfirmTests`), 6 en `ShipmentCorrectionTests` | Cumplido |
| SC-002 | `CountScopeFrozenTests` (rechazo y readmisión tras confirmar y cancelar) | Cumplido salvo «recibir» (HU-01, justificado) |
| SC-003 | `CycleCountConcurrencyTests.C13_…_in_20_rounds` | Cumplido |
| SC-004 | `CycleCountConfirmTests`, `CycleCountReliabilityTests` | Cumplido |
| SC-005 | `ShipmentCorrectionTests.C19_…` | Cumplido |
| SC-006 | — | Fuera de alcance (justificado) |
| SC-007 | — | Fuera de alcance (justificado) |

**Decisiones del plan comprobadas en el código**:

- D-02: transiciones en `CycleCount.cs`.
- D-03: orden de bloqueos al iniciar y confirmar.
- D-04: lector real; congelación antes de `LocationNotEmpty`.
- D-05: revalidación del teórico y del reservado.
- D-06: un movimiento por conteo.
- D-07: triggers solo `THROW`.
- D-09: vínculo en la revisión y candidatas.
- D-12: detalle con `ETag` y contexto sin él.
- D-13: `Adjust` en `StockCore`.
- D-15: `allRows`, `begin(…, appRoute)`, foco al primer error.
- P-06: `mutationPermission()` y «Abrir conteo» en los archivos de A, en `17bc62a`.

Sin desviaciones aparte de G1–G8.

## Verificación

Esta iteración no cambia código ni vuelve a ejecutar pruebas: no hay cambios de código desde
`ef9b596`. Resultados de las tareas:

| Comprobación | Resultado | Commit |
|--------------|-----------|--------|
| Suite backend | 347 aprobadas, 1 omitida (V-14), 0 fallidas | `2215155` |
| `has-pending-model-changes` | Sin cambios | `2215155` |
| Vitest | 223/223 | `a084bfa` |
| `tsc` y `npm run build` | Sin errores | `a084bfa`, `ef9b596` |
| Playwright (`--grep-invert SC-005`) | 18/18, con E2E-03 | `ef9b596` |

## Tareas correctoras añadidas

En la `Phase 10: Convergence` de [tasks.md](../tasks.md): C1–C8, una por hallazgo G1–G8, **sin
implementar** y pendientes de revisión conjunta. C1 toca archivos de B de HU-03, HU-05 y HU-02 (⚑).
C3 y C7 pueden acabar en una aclaración de la spec (`/speckit-clarify`) en lugar de código. C7 puede
requerir un cambio en un archivo de A.
