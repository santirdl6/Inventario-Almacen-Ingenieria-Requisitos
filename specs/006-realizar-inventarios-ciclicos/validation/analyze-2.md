# HU-04 — /speckit-analyze, iteración 2

**Fecha**: 2026-11-21. **Autora**: Persona B. **Rama**: `b/hu-04-realizar-inventarios-ciclicos`, sobre
`8548d79`. Corresponde al estado tras las correcciones H1–H6 de analyze-1, las tareas T001–T018 y la
Convergence C1–C9.

**Artefactos analizados**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md) y los
contratos ([API](../contracts/cycle-counts-api.md) y [UI](../contracts/cycle-counts-ui.md)), frente a la
[constitución](../../../.specify/memory/constitution.md). Para no repetir lo ya comprobado contra el
código se usan los informes [converge-1](converge-1.md) y [converge-2](converge-2.md), y como punto de
partida [analyze-1](analyze-1.md). No se han releído research, data-model ni los quickstarts.

**Análisis de solo lectura**: este informe no cambia ningún artefacto y no corrige nada. Los cambios que
se decidan irán en commits aparte.

---

## Resumen

- **Críticos**: 0. **Altos**: 0. No hay contradicciones críticas ni altas entre spec, plan, tasks y
  contratos.
- **Hallazgos de analyze-1**: todos los altos (H1–H6) están cerrados en documentos y en código. De los
  medios y bajos, M1, M2 y M4 están cerrados y M3 está superado. Siguen abiertos M5, M6, M7 y L1–L3, de
  impacto bajo.
- **Cobertura**: los 29 FR y los criterios SC-001…SC-005 del alcance comprometido tienen tarea y prueba
  prevista, y casi todos la tienen ya ejecutada. SC-006 y SC-007 están fuera del alcance comprometido,
  justificados por escrito.
- **Nuevos**: 2 medios y 3 bajos, todos documentales.

## Hallazgos de analyze-1: estado

| Hallazgo | Estado | Commit | Evidencia |
|----------|--------|--------|-----------|
| H1 (C-12 sin tarea) | Cerrado | `695ede9` en documentos; `19f1dab`, `4790aa8` en código | T005 y T009 cubren C-12 completo (`CountScopeFrozenTests`, 3 pruebas). |
| H2 (congelación antes que `LocationNotEmpty`) | Cerrado | `2ee4b0d`; `19f1dab` | D-04 y T005. La prueba con la ubicación ocupada fallaría sin el nuevo orden. |
| H3 (historial → conteo) | Cerrado | `2ee4b0d`; `60024cd` (C7) | Acotado a `countCode` como soporte. La salida corregida se ve en el detalle cerrado (T016). Recorrido documentado en el quickstart. |
| H4 (retirada y carreras INT-HU04-01) | Cerrado | `695ede9`; `2d41a8f`; `9c8974a` | C-15 en `CycleCountConcurrencyTests`. El enunciado de T010 está corregido: el artículo se puede borrar tras la retirada, pero su definición sigue bloqueada. |
| H5 (Q1 y Q2 sin confirmar) | Cerrado | `2ee4b0d` | Confirmadas por B en spec § Clarifications. |
| H6 (quién fija el motivo) | Cerrado | `2ee4b0d`; `89b22da`, `4790aa8` | El motivo solo se fija en la revisión. `reason` en `PUT …/lines` responde 422 por campo desconocido. |
| M1 (E2E-03 deja conteos abiertos) | Cerrado | `ef9b596` (T017) | Datos únicos por ejecución y `finally` que cancela el conteo. |
| M2 (responsables en la lista) | Cerrado | `e9d3909` (C2) | Contrato de lista ampliado y pruebas backend y Vitest. |
| M3 (dependencia del PR #9) | Superado | `af099ab` (merge del PR #9 en la base) | Queda la dependencia del PR #10 (🔀), recogida en tasks.md y en el plan. |
| M4 (bloqueo de ubicaciones en el borrador) | Cerrado en código | — | `CycleCountCommands.cs:35-36, 55-56`: `LocationsAsync` al crear y al editar. T006 no lo nombra (ver M5 nuevo). |
| M5 (C-13 sin cambio de tipo) | Abierto, bajo | — | `StartRival` = salida, reserva, desactivar. El cambio de tipo pasa por la misma rama y el mismo bloqueo `location:{id}` de `LocationCommands`, así que el riesgo es bajo. |
| M6 (FR-021 «cada ajuste» frente a `CountLine`) | Abierto, bajo | — | converge-1 lo da por cumplido según la redacción de FR-021. La frase sigue sin decir que el dato está en la línea. |
| M7 (`LotId` nulo en un hallazgo de lote nuevo) | Abierto, bajo | — | Por diseño: las líneas no se modifican al confirmar (D-07). El vínculo con el lote creado es solo por `LotCode`. |
| L1–L3 | Abiertos | — | spec.md:35 («futuro plan»); spec.md:104 («un ajuste por cada línea»); E2E-03 no recorre US4, que cubren `ShipmentCorrectionTests` y Vitest. |

## Hallazgos críticos y altos

Ninguno.

## Medios y bajos (sin desarrollar)

- **M8 (nuevo)**: plan § «Archivos de B fuera de la feature que se tocan» no recoge los que añadió la
  Convergence:
  - `ShipmentCommands.cs`, `shipment-form.page.ts`, `shipment-confirm.page.spec.ts` y
    `shipping-testing.ts` (HU-03);
  - `ReservationCommands.cs`, `stock.models.ts` y `stock-api.service.ts` (HU-05);
  - `location-detail.page.spec.ts`, `tasks.md` y `quickstart.md` (HU-02).

  Tampoco recoge `FindOpenCountAsync` en `ILocationUsage.cs`. Constan en tasks.md (C1, C3, C6, C8) y en
  converge-2, pero el plan no se ha actualizado.
- **M9 (nuevo)**: plan § Project Structure no recoge `count-review.page.spec.ts`,
  `data/*-hu04.spec.ts` ni `testing/cycle-counts-testing.ts`. Tampoco recoge que `count-review-dialog.ts`
  y `shipment-link-dialog.ts` se prueban desde `count-review.page.spec.ts` y no con un `.spec.ts`
  propio. Es una deriva entre el árbol previsto y el construido, como M4 de HU-03.
- **L4 (nuevo)**: el estado de spec.md dice «Implementada (T003–T018)» sin mencionar la Convergence
  C1–C9 ni enlazar converge-2.
- **L5 (nuevo)**: la `Phase 9: Convergence` de tasks.md sigue diciendo «Vacía hasta `/speckit-analyze`
  y `/speckit-converge`», aunque las tareas están en las fases 10 y 11. Converge solo puede añadir, así
  que la incoherencia viene de esa regla.
- **L6 (nuevo)**: D-04 (research) describe solo `IsInOpenCountAsync`. La variante
  `FindOpenCountAsync` de C1 da el mismo resultado y añade los nombres. La decisión no cambia, pero su
  texto no la menciona.

## Coherencia spec ↔ plan ↔ tasks ↔ contratos

### Cobertura de requisitos

| Requisito | Tareas | Prueba | Nota |
|-----------|--------|--------|------|
| FR-001–FR-003 | T006, T007 | `CycleCountDraftTests`, `CycleCountStartTests` | — |
| FR-004–FR-007 | T008, T015, C5, C6 | `CycleCountLinesTests`; `count-detail.page.spec.ts` | — |
| FR-008–FR-013 | T003, T009, T016 | `CycleCountConfirmTests`, `Hu04SchemaTests`; `count-review.page.spec.ts` | — |
| FR-014 | T005, T009, C1 | `CountScopeFrozenTests` | La parte de recepción, justificada (abajo) |
| FR-015–FR-017 | T005, T010 | `CountScopeFrozenTests`, `CycleCountConcurrencyTests` | M5 |
| FR-018–FR-020 | T012, T016, C3 | `ShipmentCorrectionTests`; `count-review.page.spec.ts`, `shipment-confirm.page.spec.ts` | Limitación P-07 aprobada por A |
| FR-021, FR-022 | T009, T016, C7 | `CycleCountConfirmTests`; `count-review.page.spec.ts` | Acotado por H3; M6 |
| FR-023 | T006, T014, C2 | `ShipmentCorrectionTests.C19_…`; `count-list.page.spec.ts` | — |
| FR-024, FR-025 | T003, T004, T008, T009, T010 | `Hu05SchemaTests`, `StockCoreAdjustTests`, C-15, C-16 | — |
| FR-026–FR-028 | T011, T013, T016 | `CycleCountReliabilityTests`; `operation-status-hu04.spec.ts` | — |
| FR-029 | T014–T016, C6 | specs de `/inventarios` | — |
| US1–US6 | T006–T017, C1–C8 | Matriz C-01…C-20, E2E-03 | — |
| SC-001–SC-005 | T009, T005, T010, T011, T012 | C-14, C-12, C-13, C-17, C-19 | — |
| SC-006, SC-007 | S2, S3 | — | Fuera del alcance comprometido, justificado |

Cobertura: 29/29 FR y 5/5 SC del alcance comprometido con tarea y prueba. No hay tareas sin requisito:
T001 y T002 son de coordinación y C9 es de evidencia.

### Desviaciones registradas y su justificación escrita

| Desviación | Justificación por escrito |
|------------|---------------------------|
| S1–S6 («Si hay tiempo») | tasks.md § Si hay tiempo, fuera de la numeración. S1 también en plan, Dependencias y riesgos (D-11). converge-1 y converge-2 § Pendientes. |
| SC-006 y SC-007 | plan § Complexity Tracking (B, antes de la aceptación); spec § Estado; quickstart § Fuera de alcance, con el guion de SC-007. |
| Recepción sobre alcance congelado (V-09, HU-01) | plan § Complexity Tracking y P-05, confirmada expresamente por A; spec § Edge Cases («Recepciones durante el conteo»); quickstart común § 12 con la prueba identificada; S5. |
| Reevaluar el mínimo tras el ajuste (HU-06) | plan § Dependencias y riesgos; spec § Alcance y dependencias. |

Todas están justificadas por escrito. Ninguna afecta a un principio MUST.

### Constitución

No hay conflictos con los principios MUST:

- I (correspondencia): ajustes, sin sobrescribir saldos.
- II (ubicación): hallazgos con ubicación y lote.
- IV (trazabilidad): diario de solo adición, motivo y aprobación. H3 lo acota sin romperlo.
- V (integridad): congelación, idempotencia, versiones y carreras.

III (uso por trabajadores) queda pendiente de SC-007, con su desviación registrada en Complexity
Tracking. Por eso HU-04 es integrable, pero no está aceptada.

### Métricas

- Requisitos: 29 FR y 7 SC (5 del alcance comprometido).
- Tareas: 18 (T001–T018) más 9 de Convergence (C1–C9), todas [X]; «Si hay tiempo»: 6.
- Cobertura: 100 % de los FR y de los SC comprometidos.
- Ambigüedades: 0.
- Duplicados: 0.
- Críticos: 0.
- Altos: 0.
- Medios: 5 (M5–M9; M5 y M6 ya venían de analyze-1).
- Bajos: 7 (M7, L1–L6).

## Próximas acciones

No hay nada que bloquee la revisión del PR. Si B quiere dejar los documentos alineados antes de la
fusión, las opciones son:

1. Una iteración de `/speckit-plan` que actualice el árbol y los archivos de B tocados (M8, M9) y el
   texto de D-04 (L6).
2. Ajustes de redacción en la spec con `/speckit-clarify` o una edición manual revisada (M6, L1, L2, L4).
3. M5, si se quiere cubrir: una carrera de inicio frente a cambiar el tipo en
   `CycleCountConcurrencyTests`. Pasaría por `/speckit-converge` y `/speckit-implement`.

Ninguna se aplica en esta iteración.
