# HU-03 — /speckit-converge, iteración 1

**Fecha**: 2026-11-21. **Rama**: `b/hu-03-registrar-salida-mercancia`, desde `a6ec4b2` (Convergence C1),
con el PR #9 de HU-05 ya fusionado en la integración (`af099ab`, traído con el merge `04dcc80`).

**Alcance**: reconciliar [spec](../spec.md), [plan](../plan.md), [research](../research.md),
[data-model](../data-model.md), [contratos](../contracts/shipping-api.md) y [quickstart](../quickstart.md)
con lo construido en T003–T018 y C1. El marco es la [constitución](../../../.specify/memory/constitution.md).
Sin cambios de código y sin tareas nuevas: [tasks.md](../tasks.md) conserva sus 18 tareas y la fase
Convergence (C1).

## Desviación respecto a la skill

`/speckit-converge` solo añade una fase `## Phase N: Convergence` a `tasks.md`, con una tarea por
hueco, y no toca `spec.md` ni `plan.md`. Por decisión de B, como en la
[convergencia de HU-05](../../004-consultar-existencias/validation/converge-1.md), esta iteración hace lo
contrario:

- reconcilia los documentos con lo construido;
- no añade tareas, y los incumplimientos se recogen aquí para decidirlos con B.

## Nota de proceso

Todas las tareas de HU-03 ejecutadas en esta entrega (T003–T018 y C1) se hicieron con la skill
`/speckit-implement` cargada. También se hicieron con su skill:

- `/speckit-analyze`: `f0ca359` y sus correcciones.
- Las iteraciones de `/speckit-plan`: `786abc9` y `01f7c64`.
- La iteración de `/speckit-tasks`: `e5e85ef`.

El método fue el mismo en todas: pruebas primero, rojo anotado con su razón y un commit por tarea.
Cuando no hubo rojo natural (T008, T010, T011), se comprobó con mutaciones temporales sin commit,
anotadas en tasks.md.

T001 y T002 (coordinación con A) no se han ejecutado. Esta sesión no puede acreditar cómo se
hicieron `/speckit-specify`, `/speckit-clarify`, el primer `/speckit-plan` y el primer
`/speckit-tasks`, anteriores a ella. Sus commits los citan (`302a2e5`, `c16a635`, `fae22d8`,
`0984bed`, `c42cfeb`).

## Cambios por documento

| Documento | Cambio | Verificable en |
|-----------|--------|----------------|
| spec.md | Estado «Implementada», integrable y no aceptada. Apartado «Estado de la entrega» con lo que queda fuera y los incumplimientos G1–G4. Los requisitos no cambian. | este informe; tasks.md |
| plan.md | Árbol de documentación y de código **construido**: migración `20261121135335_HU03_Shipments`; `ShipmentQueryTests`, `shipping.routes.spec`, `shipment-confirm.page.spec` y `testing/shipping-testing.ts` (analyze-1 M4, L1). Gobernanza con P-01…P-08 (L3). P-02 con los totales declarados. Complexity Tracking con C1, el total con «—» y «guardar antes de revisar». | `a6ec4b2`, `3708acb`, `fab4cbe`, `d09371b` |
| research.md | D-10: migración construida y regenerada en C1. D-12: paginación sin `Paging` común. D-14: frontend construido (`allRows` en el selector, guard de «nueva», «—», guardar antes de revisar, aviso en `history.state`, solo lectura con la operación al reabrir, «Inactivo» y «Abrir salida»). D-15: carreras construidas, con la carrera extra de T011 sobre `customer-order` y la barrera por tabla. D-06 ya recogía M3 (solo `THROW`) y C1 (`01f7c64`). | `4634b05`, `55ce526`, `8487830`, `fab4cbe`, `a6ec4b2` |
| data-model.md | Migración con su nombre real, los códigos de error de cada trigger y la nota de que todos solo hacen `THROW`. Las columnas e invariante 8 de C1 ya estaban (`01f7c64`). | `20261121135335_HU03_Shipments.cs` |
| contracts/shipping-api.md | Orden real de las validaciones del borrador (T007). En `/confirm`: 422 sin `orderVersion`, `detail` real de `InsufficientStock` y totales declarados (C1). | `ShipmentCommands.cs`, `c09ae87`, `488bb5a` |
| contracts/shipping-ui.md | Total con «—» si las unidades difieren; eliminar con `alertdialog` en la página y aviso en `history.state`; «Revisar y confirmar» solo con el borrador guardado; lo construido frente a lo contratado en los 409 (G1, G3, G4). | `order-shipments.page.ts`, `shipment-form.page.ts` |
| quickstart.md | H-17 con la carrera extra de T011; recuento de Vitest construido; evidencia final de la convergencia. La de T018 ya estaba (`4aba547`). | pruebas citadas en cada fila |
| validation/converge-1.md | Este informe. | — |

## Incumplimientos encontrados (sin corregir, a decidir con B)

| ID | Severidad | Fuente | Evidencia | Qué falta |
|----|-----------|--------|-----------|-----------|
| G1 | ALTA, **resuelto en Convergence C2** | US2.2 («explicando la cantidad posible (7)»); FR-010 y FR-024 («la cantidad posible actual»); shipping-api § confirm | El `detail` de `InsufficientStock` lo redacta `StockCore.Effects` y dice «físico F, disponible D» (`StockCore.cs:161`), sin la cantidad posible: mín(F, propio + D). La revisión sí la muestra antes de confirmar (`review.possible`). | Explicar la cantidad posible al rechazar. Opción: que `ShipmentCommands` la calcule antes de `ApplyAsync` y la añada al mensaje, sin tocar `StockCore` (archivo de B, H0). |
| G2 | MEDIA | US4.1 («por línea, artículo, unidad, ubicación, lote, cantidad y reserva propia consumida» y «fecha y hora del hecho y del registro»); FR-018 | En `shipment-form.page.ts`, `readonly-line` muestra ubicación, lote, cantidad con unidad y reserva consumida, pero no el SKU ni el nombre del artículo. La cabecera no muestra la fecha de registro. Las filas del borrador (`shipment-line`) tampoco identifican el artículo; en un pedido con varios artículos no se distinguen. | Mostrar «SKU · nombre» por línea y la fecha de registro. |
| G3 | BAJA | shipping-ui § Resultados («junto a la línea») | El aviso de `InsufficientStock` va arriba de la página (`role="alert"`), con foco. | Ubicar el aviso en la línea afectada, o aceptar la ubicación actual. |
| G4 | MEDIA | shipping-ui § Resultados (409 de estado: «recarga del estado actual») | `ShipmentFormPage.fail()` solo relee en el 412. En `CustomerOrderNotOpen`, `CountScopeFrozen` y `DocumentAlreadyConfirmed` muestra el motivo, pero no vuelve a leer la salida ni el pedido. | Releer tras esos 409, o aceptar el límite. |

No se ha visto ninguna infracción de los principios MUST de la constitución. El principio III
(validación con operarios, SC-007) y el rendimiento (SC-006) siguen sin medirse, como dice el plan.

## Requisitos y evidencia

| Req. | Evidencia | Estado |
|------|-----------|--------|
| FR-001 | `ShipmentDraftTests.H04_…`, `H05_…` (pedido inexistente); `order-shipments.page.spec.ts` («Nueva salida» solo con pedido abierto) | Cumplido |
| FR-001a | `CustomerOrderLookupTests` (H-03); `order-lookup.page.spec.ts`; E2E-02 | Cumplido |
| FR-002 | `Hu03DomainTests` (una línea por bucket); `Hu03SchemaTests` (`UX_ShipmentLines_Shipment_Bucket`); `ShipmentDraftTests.H05_…` | Cumplido |
| FR-003 | `ShipmentDraftTests.H05_…` (forma, línea ajena, lote ajeno, fracción, Σ > pendiente); `ShipmentConfirmTests` (422 por pendiente bajo bloqueo) | Cumplido |
| FR-004 | `ShipmentDraftTests` (H-04, H-06, H-07, retirada por edición, carreras); `ShipmentConfirmTests.H18_…` (409 a editar o eliminar una confirmada) | Cumplido |
| FR-005 | `ShipmentDraftTests.H05_…` (fecha futura); `OccurredAt` y `RecordedAt` en `Shipment` | Cumplido |
| FR-006 | `ShipmentQueryTests` (H-21, `review`); `shipment-review-dialog.spec.ts`; E2E-02 | Cumplido |
| FR-006a | `shipment-form.page.spec.ts` (propuesta por Id, acotada, sin reserva no propone; selector con 30); E2E-02 | Cumplido |
| FR-007 | `ShipmentConfirmTests.H10_…` (V-04), `Rule3_…`; `StockCore` (`ShipReserved`/`ShipAvailable`) | Cumplido |
| FR-008 | `ShipmentConfirmTests.H08_…`, `H10_…`, `Rule3_…`; `AssertShipmentInvariantsAsync` | Cumplido |
| FR-009 | `ShipmentConfirmTests.H08_…`, `H09_…` | Cumplido |
| FR-010 | `ShipmentConfirmTests.H10_…` (409 `InsufficientStock`), `H15_…` (no `Open`) | **Parcial**: G1 (sin cantidad posible); conteo abierto pendiente de INT-HU04-03 |
| FR-011 | `ShipmentConfirmTests.H11_…` | Cumplido |
| FR-012 | `ShipmentConfirmTests.H12_…`; `ShipmentConcurrencyTests.H17_…` (412 al perdedor) | Cumplido |
| FR-013 | `Hu03SchemaTests` (triggers, `DENY`, consumo tardío también con la misma operación, C1); `ShipmentConfirmTests.H18_…` | Cumplido |
| FR-014 | `ShipmentConfirmTests.H15_…`; carrera con la desactivación en `ShipmentConcurrencyTests` | Cumplido |
| FR-015 | `ShipmentConfirmTests.H08_…` (motivo, soporte = referencia, actor, entradas) | Cumplido |
| FR-016 | `ShipmentConfirmTests.H08_…` (`CounterpartyReference` = cliente, sin motivo de no aplicación) | Cumplido |
| FR-017 | Diario de solo adición de H0; `AssertShipmentInvariantsAsync` (diario = saldo) | Cumplido |
| FR-018 | `ShipmentQueryTests` (detalle, lista); `order-shipments.page.spec.ts`; `shipment-confirm.page.spec.ts` | **Parcial**: G2 (falta el artículo por línea en la vista de la salida) |
| FR-019 | `ShipmentDraftTests.H20_…`, `ShipmentConfirmTests.H20_…`, `CustomerOrderLookupTests`, `ShipmentQueryTests`; `shipping.routes.spec.ts` y `shipment-confirm.page.spec.ts` (sin `Shipping.Record`) | Cumplido |
| FR-020 | `ShipmentReliabilityTests.H19_…` (V-08) | Cumplido |
| FR-021 | `ShipmentDraftTests` (replay e `IdempotencyConflict`, M1); `ShipmentConfirmTests.H18_…` | Cumplido |
| FR-022 | `ShipmentConcurrencyTests` (H-16 en 20 rondas; H-17, 14 carreras); `ShipmentDraftTests` (10 carreras) | Cumplido (los conteos llegan con HU-04) |
| FR-023 | `shipment-confirm.page.spec.ts` (operación tras confirmar y al reabrir; recuperación con «Abrir salida»); `operation-status-hu03.spec.ts`; E2E-02 | Cumplido |
| FR-024 | `shipment-form.page.spec.ts` (422 con foco), `shipment-confirm.page.spec.ts` (409 con foco al aviso, `ReservedStockConflict` con enlace) | **Parcial**: G1; G3 y G4, de UI |
| FR-025 | Textos en español y unidad junto a cada cantidad (specs de `/salidas`); diálogo con teclado; estados en texto; total con «—» si las unidades difieren; E2E-02 por roles y etiquetas | Cumplido |
| SC-001 | `ShipmentConfirmTests` (H-08–H-10) y `AssertShipmentInvariantsAsync` | Cumplido |
| SC-002 | `ShipmentDraftTests.H05_…`, `ShipmentConfirmTests.H10_…`, `H11_…`, `H15_…` | Cumplido |
| SC-003 | `ShipmentConcurrencyTests.H16_…` (20 rondas) y `H17_…`, repetidas tres veces en T011 y T018 | Cumplido |
| SC-004 | Permisos, replays, `IdempotencyConflict`, 412 y V-08 (`ShipmentReliabilityTests`) | Cumplido |
| SC-005 | `ShipmentConfirmTests.H12_…`; `AssertShipmentInvariantsAsync` (Σ remanente = reservado) | Cumplido |
| SC-006 | — | Fuera de alcance: requiere carga y datos sintéticos (plan, Complexity Tracking) |
| SC-007 | — | Fuera de alcance: validación con operarios (principio III, V-13); guion en el [quickstart](../quickstart.md#fuera-de-alcance-de-esta-entrega) |

**Fuera de alcance, con justificación**:

- **`CountScopeFrozen` al confirmar**: `IsInOpenCountAsync` devuelve `false` hasta INT-HU04-03.
- **Corrección de salidas confirmadas erróneas**: pasa a HU-04 (clarificación Q3; tarea receptora de
  P-07).

## Verificación final de la rama

Sobre `a6ec4b2`, el último commit de código. Esta iteración solo cambia documentos.

| Comprobación | Resultado | Línea base |
|--------------|-----------|------------|
| Suite backend (receta de specs/003, `git archive HEAD`) | 290 aprobadas, 1 omitida (V-14), 0 fallidas | 207/1/0 antes de HU-03; 287/1/0 antes de C1 |
| `dotnet ef migrations has-pending-model-changes` | Sin cambios | — |
| Vitest (`ng test --watch=false`) | 180/180 en 43 archivos | 147 antes de T013 |
| Playwright (`--grep-invert SC-005`, proyecto `inventario-hu08-e2e`) | 17/17 en 3,7 min | 16/16 antes de T017 |
