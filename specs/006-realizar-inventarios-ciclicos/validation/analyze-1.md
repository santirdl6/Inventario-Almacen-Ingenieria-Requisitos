# HU-04 — /speckit-analyze, iteración 1

**Fecha**: 2026-11-21. **Autora**: Persona B. **Rama**: `b/hu-04-realizar-inventarios-ciclicos`, creada
sobre `b/hu-03-registrar-salida-mercancia` (PR #10 abierto), tras `b85264e` (`/speckit-tasks`).
**Artefactos analizados**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
[research.md](../research.md), [data-model.md](../data-model.md), [contratos](../contracts/) y
[quickstart.md](../quickstart.md), frente a la [constitución v1.0.0](../../../.specify/memory/constitution.md)
y el [backlog de integración](../../001-reparto-specs-inventario/integration-tasks.md#hu-04--conteos-y-ajustes--persona-b).
Formato: el del [análisis de HU-03](../../005-registrar-salida-mercancia/validation/analyze-1.md).

Por economía no se releyeron los anexos comunes. Para comprobar tres hechos que afirman el plan y el
research se consultaron, sin modificarlos, cuatro puntos del código:

- `LocationCommands.cs:90-99`: el orden de `LocationNotEmpty` y `CountScopeFrozen`.
- `ILocationUsage.cs:41`: el lector `IsInOpenCountAsync`, que sigue siendo constante.
- `article-history.component.ts:46,67`: el historial de A, que muestra «Conteo» sin enlace.
- La migración `20261121135335_HU03_Shipments`, que está presente.

**Análisis de solo lectura**: este informe no cambia ningún artefacto. Las correcciones aplicadas
después, en commits aparte, constan en [Resolución](#resolución). El tope sigue siendo de 18 tareas.

---

## Resumen

| Gravedad | Hallazgos |
|----------|-----------|
| Crítica | 0 |
| Alta | 6 (H1–H6) |
| Media | 7 (M1–M7), sin desarrollar |
| Baja | 3 (L1–L3), sin desarrollar |

**Veredicto**: no hay infracciones de principios MUST de la constitución. H1 y H4 se corrigen
editando tareas y casos existentes, sin tareas nuevas. H2, H3, H5 y H6 implican una decisión de diseño
o de alcance y se consultan con B antes de cambiar nada. Conviene cerrarlas antes de
`/speckit-implement`, porque H2 haría fallar la prueba independiente de US3 tal como está escrita.

---

## Hallazgos críticos y altos

| ID | Categoría | Gravedad | Ubicación | Resumen | Propuesta |
|----|-----------|----------|-----------|---------|-----------|
| H1 | Cobertura US3.7 / SC-002 (C-12 sin tarea) | ALTA | quickstart.md:240 (C-12); tasks.md:91-97 (T005), 128-136 (T009) | C-12 es el único caso de la matriz que ninguna tarea cita. T005 describe su primera mitad con una fila congelada sembrada, pero no lo nombra. La segunda mitad, «tras cerrar, todo admitido» (US3.7 y la segunda parte de SC-002), necesita confirmar y cancelar (T009), y ninguna tarea la prueba. | T005 cita C-12 en su primera mitad. T009 añade la segunda: tras confirmar y tras cancelar, confirmar la salida, reservar, desactivar y cambiar el tipo se admiten, en `CountScopeFrozenTests.cs`. |
| H2 | Inconsistencia spec ↔ research ↔ código (US3.4) | ALTA | spec.md:130-133 (prueba independiente de US3), 146-148 (US3.4), 460-462 (SC-002); research.md:94-95 (D-04); `LocationCommands.cs:90-99` | La spec pide que desactivar U1 o cambiar su tipo se rechace con «ubicación en conteo» y, en la prueba independiente, U1 tiene existencias. `LocationCommands` comprueba `LocationNotEmpty` **antes** que `IsInOpenCountAsync`, y D-04 lo acepta («una no vacía ya da antes `LocationNotEmpty`»). Con U1 ocupada la respuesta es `LocationNotEmpty`, no `CountScopeFrozen`, así que la prueba independiente falla tal como está escrita. C-12 y T005 solo prueban la ubicación vacía. | **Decisión de B**: invertir las dos comprobaciones en `LocationCommands` (HU-02, archivo de B) dentro de T005, o bien ajustar la spec para que una ubicación ocupada responda `LocationNotEmpty`. |
| H3 | Cobertura FR-022 / US4.2 | ALTA | spec.md:180-182 (US4.2), 356-357 (FR-022); contracts/cycle-counts-api.md:161; `article-history.component.ts:46` | FR-022 exige que el historial permita llegar al conteo o a la salida que originó el ajuste. US4.2 exige que el ajuste muestre la referencia a la salida original. El historial de A muestra «Conteo», el Id y el `CountCode` como soporte, pero no enlaza nada. La salida corregida solo consta en `CountLine.CorrectsShipmentId`, no en el movimiento. Ninguna tarea ni prueba cubre ninguna de las dos cosas. | **Decisión de B**: enlace «Abrir conteo» en el historial (archivo de A, en P-06/T013) más la salida visible en el detalle cerrado del conteo (T016), o bien acotar FR-022/US4.2 a que el soporte `CountCode` lleve al conteo y la salida se vea en su detalle. |
| H4 | Cobertura INT-HU04-01 (retirada y carreras) | ALTA | tasks.md:143-148 (T010); quickstart.md:243 (C-15); research.md:65, 184-186 (D-03, D-08) | INT-HU04-01 exige la retirada permitida y las carreras de definición y borrado del artículo. D-03 bloquea `article:{id}` también al retirar un hallazgo, pero C-15 y T010 solo prueban la carrera al **añadirlo**. Falta la retirada (`DELETE …/lines/{lineId}`) frente a `PUT` de definición y `DELETE` del artículo, en ambos órdenes. Es el mismo defecto que H4 de HU-03. | En T010 y C-15: retirar el hallazgo frente a `PUT` de definición y `DELETE` del artículo, en ambos órdenes. Tras la retirada, el artículo sin otras referencias puede editarse o borrarse. |
| H5 | Ambigüedad: clarificaciones sin confirmar | ALTA | spec.md:41-42 (Q1, Q2) | Q1 (la liberación no se congela) y Q2 (corrección solo mediante conteo) dicen «Decisión por recomendación, pendiente de confirmar por B». Plan, research (D-04, D-09), contratos y tareas T005, T009 y T012 ya se apoyan en ellas. | **Decisión de B**: confirmar o cambiar Q1 y Q2. Si se confirman, retirar «pendiente de confirmar» de la spec. |
| H6 | Inconsistencia spec ↔ contrato ↔ UI (quién fija el motivo) | ALTA | spec.md:84-86, 101-102 (US2); research.md:40 (D-02), 193-194 (D-09); contracts/cycle-counts-api.md:128 (`reason?` en `PUT …/lines`); contracts/cycle-counts-ui.md:182 («motivo opcional» al contar); data-model.md:55 (`ReasonBy`) | En la spec y en D-09 el motivo lo fija el responsable en la revisión, con `Inventory.Adjust`. El contrato API admite `reason?` en `PUT …/lines` con `Counts.Record`, y la UI ofrece «motivo opcional» al contar. No queda definido si el motivo del operario cuenta para confirmar, ni si la revisión es obligatoria antes de confirmar. Afecta a la trazabilidad del principio IV (`ReasonBy`). | **Decisión de B**: retirar `reason` de `PUT …/lines` y de la pantalla de contar, de modo que solo la revisión fija motivos; o bien admitirlo como propuesta del operario que el responsable valida en la revisión (`ReasonBy` = quien lo fijó por última vez). |

## Medios y bajos (sin desarrollar)

- **M1** E2E-03 (T017): si una ejecución falla a mitad, la ubicación queda en un conteo abierto y la
  siguiente ejecución recibe `CountScopeFrozen`. Conviene usar una ubicación nueva en cada ejecución.
- **M2** FR-023 pide «responsables» en la lista, pero el contrato de lista solo da `createdBy`.
- **M3** La dependencia transitiva del PR #9 (HU-05) no figura junto a la del PR #10.
- **M4** D-03 bloquea las ubicaciones al crear y editar el borrador, pero T006 no lo menciona.
- **M5** FR-017/C-13: la carrera de inicio frente a cambiar el tipo de la ubicación no está en C-13,
  que solo cubre desactivar.
- **M6** FR-021 dice que «cada ajuste» registra quién contó y la salida vinculada, pero eso consta en
  `CountLine`, no en el movimiento. Se puede rastrear, pero la redacción no lo dice.
- **M7** El `CountLine.LotId` de un hallazgo con lote nuevo queda nulo tras confirmar, porque las
  líneas no se modifican. El vínculo con el lote creado es solo por `LotCode`.
- **L1** spec.md:35 sigue diciendo «el futuro plan de HU-04».
- **L2** US2.3 y US2.5 hablan de «un ajuste por cada línea»; tras D-06 es una entrada del movimiento
  por cada línea.
- **L3** El encabezado de la fase 7 incluye US4, pero E2E-03 no recorre la corrección.

---

## Coherencia spec ↔ plan ↔ tasks ↔ contratos

### Cobertura de requisitos

| Requisito | Tareas | Pruebas | Estado |
|-----------|--------|---------|--------|
| FR-001 | T006, T014 | C-03, Vitest | Cubierto |
| FR-002, FR-003 | T007 | C-04, C-05 | Cubierto |
| FR-004, FR-005, FR-007 | T008, T015 | C-06, Vitest | Cubierto |
| FR-006 | T008, T015 | C-07 | Cubierto; H4 (carreras de retirada) |
| FR-008–FR-012 | T009, T016 | C-08–C-11 | Cubierto; H6 (quién fija el motivo) |
| FR-013 | T003, T009 | C-01, C-14 | Cubierto |
| FR-014 | T005 | C-12 (mitad) | H1, H2; recepciones como desviación (HU-01) |
| FR-015 | T005, T009, T010 | C-09, C-13 | Cubierto |
| FR-016 | T005 | C-12 | Cubierto |
| FR-017 | T010 | C-13 | Cubierto; M5 |
| FR-018–FR-020 | T012, T016 | C-19, C-20 | Cubierto |
| FR-021 | T009, T012 | C-08, C-19 | Cubierto; M6 |
| FR-022 | — | — | **Sin cobertura** (H3) |
| FR-023 | T006, T014 | C-03, Vitest | Parcial (M2) |
| FR-024 | T003, T007, T008, T010 | C-01, C-07, C-15 | H4 |
| FR-025 | T004, T009 | C-02, C-16 | Cubierto |
| FR-026, FR-027 | T011 | C-17 | Cubierto (nueve escrituras) |
| FR-028 | T011, T016 | C-18, Vitest | Cubierto |
| FR-029 | T014–T016 | Vitest | Cubierto |
| SC-001, SC-004, SC-005 | T009, T011, T012 | C-14, C-17, C-19 | Cubierto |
| SC-002 | T005 | C-12 (mitad) | H1, H2 |
| SC-003 | T010 | C-13 | Cubierto |
| SC-006, SC-007 | — | — | Desviación registrada (plan, Complexity Tracking) |

### Puntos revisados sin hallazgo

- **Orden de bloqueo con `cycle-count`** (D-03): el orden ordinal `customer-order-reference:` <
  `customer-order:` < `cycle-count:` < `reservation-release:` es correcto, porque `u` (0x75) va antes
  que `y` (0x79) y `-` antes que `:`. Ningún comando de HU-04 combina `cycle-count` con otro documento.
  Iniciar, confirmar y cancelar toman todas las ubicaciones y luego los artículos. Los escritores de B
  leen la congelación después de `location:{id}` (comprobado en `LocationCommands` y, según el
  research, en salidas y reservas).
- **ETag y contexto** (D-11, D-12): el detalle lleva solo datos propios y `Version`. Cada escritura
  renueva la cabecera, también las que solo cambian líneas. El contexto va sin `ETag`, con
  `countVersion`, y lleva el reservado actual, que la liberación puede cambiar durante el conteo.
- **Contributor** (D-08): registrado en `AddStockCore` y con `CountLine` en `Hu05SchemaTests` (T003).
  La historia cuenta los conteos cerrados; la única observación es H4.
- **Triggers, DENY y EF** (D-07): confirmar y cancelar no tocan líneas. Los triggers admiten los dos
  estados de cada transición, así que no dependen del orden de EF. Solo usan `THROW`, como en M3 de
  HU-03, y C-01 comprueba que no aparece el error 3609. Hay `HasTrigger` en las tres tablas y
  `DENY DELETE` en `CycleCounts`. Los códigos 51401–51407 coinciden entre D-07, T003 y C-01.
- **`IsInOpenCountAsync` y `CountScopeFrozen`** (D-04): el lector sigue siendo constante
  (`ILocationUsage.cs:41`), como espera el rojo de T005. El inicio responde 409 nombrando el
  `countCode`. La liberación no se congela (Q1). La única observación es H2.
- **Dependencias del PR #10 y de A**: la migración base está presente. Las tareas marcadas 🔀 (T003,
  T005, T010 y T012) son las que leen `Shipments` o la confirman. Los archivos de A se limitan a T013
  (P-06). Los diffs P-01…P-05 se proponen sin editarse (T001). La única observación es M3.
- **E2E-03**: reutiliza las cuentas y `POST /testing/stock-receipts`, sin tocar fixtures de A. La
  única observación es M1.

### Constitución

Sin infracciones de principios MUST. SC-006 (rendimiento) y SC-007 (validación con trabajadores,
principio III) siguen registrados como desviación: HU-04 puede integrarse, pero no darse por aceptada.
H3 y H6 tocan el principio IV (trazabilidad), pero no lo infringen: el dato existe y lo que falta es
cómo se muestra o quién lo fija.

### Métricas

- Requisitos: 29 FR (FR-001…FR-029) y 7 SC (5 con trabajo construible).
- Tareas: 18 (T001–T018), más Convergence (vacía) y «Si hay tiempo» (S1–S6).
- Cobertura: 28/29 FR con al menos una tarea; FR-022 no tiene ninguna (H3). Con prueba completa en
  todas sus partes, 22/29 antes de corregir. Los parciales son FR-006, FR-014, FR-022, FR-023 y FR-024,
  y FR-008/FR-009, que dependen de H6.
- Ambigüedades: 2 (H5, H6). Duplicados: 0. Críticos: 0.

## Próximas acciones

1. Corregir H1 y H4 en tareas y casos, en un commit aparte.
2. Decidir con B H2, H3, H5 y H6 y aplicar lo que se decida.
3. `/speckit-implement` puede empezar tras las correcciones. No se lanza en esta fase.

## Resolución

Aplicada el 2026-11-21 en commits aparte, solo en documentación y tareas. H1 y H4 no exigían decidir
nada. H2, H3, H5 y H6 se consultaron con B, que eligió la opción recomendada en las cuatro. Siguen
siendo 18 tareas, con Convergence vacía y «Si hay tiempo», y los enlaces relativos están verificados.

| Hallazgo | Estado | Commit | Cambio |
|----------|--------|--------|--------|
| H1 | Resuelto | `695ede9` | T005 cita la primera mitad de C-12. T009 añade la segunda: tras confirmar y tras cancelar se admiten las operaciones (US3.7, SC-002). |
| H4 | Resuelto | `695ede9` | T010, C-15 y D-16: retirar el hallazgo frente a `PUT` de definición y `DELETE` del artículo, en ambos órdenes. |
| H2 | Resuelto (decisión de B: invertir el orden) | `2ee4b0d` | D-04, T005, C-12 y plan: `LocationCommands` (HU-02, B) comprueba la congelación antes que `LocationNotEmpty`, con prueba sobre una ubicación vacía y otra ocupada. |
| H3 | Resuelto (decisión de B: acotar la spec) | ídem | FR-022 y US4.2: el historial muestra el `CountCode` como soporte, y el detalle del conteo cerrado muestra la salida corregida por línea (cycle-counts-ui y T016, con Vitest). No se tocan archivos de A. |
| H5 | Resuelto (decisión de B: confirmar ambas) | ídem | Q1 y Q2 de la spec quedan confirmadas por B. |
| H6 | Resuelto (decisión de B: solo en la revisión) | ídem | Se retira `reason` de `PUT …/lines` y de la pantalla de contar. `ReasonBy` es quien fija el motivo en la revisión. |

**Sin cambiar**: M1–M7 y L1–L3, medios y bajos fuera del alcance de esta fase.
