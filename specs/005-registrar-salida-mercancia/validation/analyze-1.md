# HU-03 — /speckit-analyze, iteración 1

**Fecha**: 2026-11-21. **Autora**: Persona B. **Rama**: `b/hu-03-registrar-salida-mercancia`, tras
el merge `ca9f6db` de `origin/b/hu-05-consultar-existencias`, que trae C3, C4 y T001/T002 de HU-05.
**Artefactos analizados**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
[research.md](../research.md), [data-model.md](../data-model.md), [contratos](../contracts/) y
[quickstart.md](../quickstart.md), frente a la [constitución v1.0.0](../../../.specify/memory/constitution.md),
el [plan global](../../../.specify/memory/project-plan.md), sus anexos y el
[backlog de integración](../../001-reparto-specs-inventario/integration-tasks.md).

Para no repetir lo ya conocido se leyeron también el
[análisis de HU-05](../../004-consultar-existencias/validation/analyze-1.md) y su
[convergencia](../../004-consultar-existencias/validation/converge-1.md). Sus hallazgos C1, C2, H1–H4
y M1–M4 ya están incorporados al diseño de HU-03: transporte común con `appRoute`, contraparte
explícita, contrato de escritores, IDs INT, migración regenerable, detalle propio con `ETag` y
contexto sin `ETag`. Aquí solo se cita lo que HU-03 repite. G2 y G3 de HU-05 sí se repiten, como
H2 y H3.

**Análisis de solo lectura**: este informe no cambia ningún artefacto. Las correcciones aplicadas
después, en commits aparte, constan en [Resolución](#resolución). El tope sigue siendo de 18 tareas.

---

## Resumen

| Gravedad | Hallazgos |
|----------|-----------|
| Crítica | 0 |
| Alta | 6 (H1–H6) |
| Media | 5 (M1–M5), sin desarrollar |
| Baja | 3 (L1–L3), sin desarrollar |

**Veredicto**: no hay infracciones de principios MUST de la constitución. Las seis altas se corrigen
editando documentos y tareas existentes, sin tareas nuevas ni decisiones de diseño. Conviene
resolverlas antes de `/speckit-implement`, porque H1 haría fallar E2E-02 y H2/H3 repiten dos defectos
que A ya rechazó en el PR #9.

---

## Hallazgos críticos y altos

| ID | Categoría | Gravedad | Ubicación | Resumen | Propuesta |
|----|-----------|----------|-----------|---------|-----------|
| H1 | Inconsistencia spec ↔ quickstart ↔ tasks (E2E-02) | ALTA | quickstart.md:62-64; tasks.md:419-421 (T017) | E2E-02 siembra 10 unidades en **un** bucket. El operario prepara la propuesta de 4 desde la reserva y después «añade 2 desde disponible». Añadir otra fila del mismo bucket repite línea de pedido, ubicación y lote, lo que prohíben FR-002, `UX_ShipmentLines_Shipment_Bucket` y la validación de forma (422 `errors.lines`). Tal como está escrito, E2E-02 falla o la UI no permite el paso. | Paso 3: «cambia la cantidad propuesta de 4 a 6». Ejercita FR-006a, que dice que el operario puede cambiar la cantidad. La revisión sigue con propio 4 y posible 6, y el resultado no cambia (físico 4, reservado 0). |
| H2 | Cobertura FR-023 / US1.3 / US4.2 (repite G2 de HU-05) | ALTA | contracts/shipping-ui.md:64; tasks.md:394-397 (T016); contracts/shipping-api.md:110-112 | El identificador de la operación solo se muestra en el aviso tras el 200. Al volver a abrir una salida confirmada (otra sesión, «Abrir salida» o URL), la vista de solo lectura no lo muestra, aunque `GET /shipments/{id}` ya devuelve `operationId`. A condicionó el PR #9 a cerrar este mismo defecto en el pedido (C3 de HU-05). | En shipping-ui y T016, la salida confirmada muestra «Salida confirmada con la operación {operationId}» desde el detalle. Prueba Vitest: abrir `:id` confirmada sin aviso previo. No hace falta cambiar la API. |
| H3 | Cobertura FR-002 / FR-006a (repite G3 de HU-05) | ALTA | contracts/shipping-ui.md:40-42; tasks.md:380-381 (T015), 458-460 (X4) | El selector de buckets de T015 usa `GET /stock?articleId=` sin indicar páginas, y X4 aplaza «todas las páginas» a «Si hay tiempo». En un artículo con más de 25 combinaciones no se podría elegir un origen posterior. A exigió cerrar ese límite en el diálogo de reservar de HU-05 (C4). Tras el merge `ca9f6db`, la rama ya tiene `StockApiService.allRows`. | T015 usa `StockApiService.allRows(articleId, false)`, con prueba de más de 25 combinaciones. X4 queda retirada sin renumerar. shipping-ui dice «todas las combinaciones con existencias». |
| H4 | Cobertura INT-HU03-01 (contrato de escritores) | ALTA | tasks.md:231-236 (T008); quickstart.md:33, 43 (H-07, H-17); integration-tasks.md:18-27 | El contrato exige crear y **retirar** la referencia de borrador y las carreras referencia/edición y referencia/borrado en ambos órdenes. T008 prueba la retirada solo con `DELETE` del borrador y las carreras solo al crear y eliminar. Falta el `PUT` que **quita** la línea de un artículo (retirada por edición) y sus carreras con `PUT` de definición y `DELETE` del artículo. Además, H-17 no recoge la carrera confirmar / `PUT` de definición que la iteración de tareas añadió a T011. | En T008 y H-07: `PUT` que quita la única línea del artículo X. El contributor deja de verla y X sigue bloqueado por la línea de pedido. Carreras editar borrador / `PUT` de definición y editar borrador / `DELETE` del artículo, en ambos órdenes. En H-17, añadir esas carreras y la de confirmar / `PUT` de definición. |
| H5 | Cobertura FR-006 / US3.2 | ALTA | spec.md:163-164, 296-299; contracts/shipping-ui.md:52-55; tasks.md:390-401 (T016) | US3.2 exige indicar en la revisión, en texto, que el artículo está inactivo, sin impedir confirmar. La API lo cubre (`articles[].isActive` del contexto, T012/H-15), pero el diálogo de revisión de shipping-ui no lo menciona y T016 no lo prueba. Es un escenario de aceptación sin prueba en la interfaz. | shipping-ui § Revisar: «Inactivo» en texto junto al artículo. T016: prueba Vitest con `isActive = false`, que muestra «Inactivo» y deja «Confirmar salida» habilitado. |
| H6 | Dependencias del PR #9 y de A desactualizadas | ALTA | tasks.md:85-87 (T002), 487-495; plan.md:202 | Tras la aprobación de A del PR #9 (`2894dad`) y el merge `ca9f6db`: T002 dice que P-03 y P-06 de HU-05 «aún esperan su propia aprobación», pero ya están aprobados y aplicados en los documentos comunes. El aviso ⚠ dice «rebasar primero», pero la rama se integra con merge. El riesgo del plan («PR #9 sin fusionar») no refleja que C3, C4 y T001/T002 ya están en la rama. | Actualizar T002, el aviso ⚠ y la fila de riesgo. T002 se aplica sobre los documentos comunes ya actualizados. La dependencia restante es la fusión del PR #9 en la rama base, con integración por merge desde `origin/b/hu-05-consultar-existencias`. |

## Medios y bajos (sin desarrollar)

- **M1** FR-021: no hay prueba de `IdempotencyConflict` en crear, editar ni eliminar el borrador;
  H-18 solo la cubre al confirmar. Es el mismo caso que M3 de HU-05.
- **M2** FR-025: `ShipmentSummary.totalQuantity` y la columna «total» de la tabla de salidas
  (shipping-ui:33) suman cantidades de artículos con unidades distintas.
- **M3** Son los primeros triggers del repositorio. `ROLLBACK` + `THROW` dentro del trigger termina
  la transacción del ejecutor (error 3609). Solo ocurre en caminos ilegítimos, pero conviene
  decidir con A si basta con `THROW` (decisión de diseño, no se cambia aquí).
- **M4** El árbol de pruebas del plan (plan.md:149-158) atribuye H-20 y H-21 a
  `ShipmentConfirmTests.cs`, mientras que tasks reparte H-20 entre T008 y T010 y pone H-21 en
  `ShipmentQueryTests.cs` (T012). Faltan en el plan `ShipmentQueryTests.cs` y `shipping.routes.spec.ts`.
- **M5** `appRoute` de eliminar es `/salidas/{shipmentId}`. Tras recuperar un `DELETE` confirmado,
  «Abrir salida» lleva a un 404.
- **L1** plan.md:126 sigue diciendo «tasks.md (no creado todavía)».
- **L2** T006 depende de T005 sin necesidad: solo toca archivos de HU-05.
- **L3** Constitution Check, fila de gobernanza: cita «P-01…P-07», pero existe P-08.

---

## Coherencia spec ↔ plan ↔ tasks ↔ contratos

### Cobertura de requisitos

| Requisito | Tareas | Pruebas | Estado |
|-----------|--------|---------|--------|
| FR-001, FR-001a | T006, T007, T014 | H-03, H-04, Vitest | Cubierto |
| FR-002, FR-003 | T003, T004, T007 | H-05 | Cubierto; H1 y H3 en la UI y el E2E |
| FR-004 | T007, T008 | H-04, H-06, H-07 | Cubierto; H4 (retirada por edición) |
| FR-005 | T003, T007 | H-05 (fecha futura) | Cubierto |
| FR-006, FR-006a | T012, T015, T016 | H-21, Vitest | H3, H5 |
| FR-007–FR-012 | T009, T010, T011 | H-08–H-13, H-17 | Cubierto (reglas 3 y 4, S-18, V-04) |
| FR-013 | T004, T008 | H-02, H-06, H-18 | Cubierto (triggers y consumos ligados a la operación) |
| FR-014 | T010, T011 | H-15, H-17 | Cubierto en la API; H5 en la UI |
| FR-015–FR-017 | T004, T009, T010 | H-02, H-08, H-14 | Cubierto |
| FR-018 | T012, T014 | H-21, Vitest | Cubierto |
| FR-019 | T008, T010, T014 | H-20, Vitest | Cubierto |
| FR-020 | T009, T011 | H-19 | Cubierto |
| FR-021 | T008, T010 | H-04, H-07, H-18 | Parcial (M1) |
| FR-022 | T011 | H-16, H-17 | Cubierto; H4 (carreras de edición) |
| FR-023 | T013, T016 | Vitest | H2 |
| FR-024, FR-025 | T014–T016 | Vitest, E2E-02 | Cubierto; M2 |
| SC-001–SC-005 | T010, T011 | H-08–H-19 | Cubierto |
| SC-006, SC-007 | — | — | Desviación registrada (plan, Complexity Tracking) |

### Puntos revisados sin hallazgo

- **Órdenes de bloqueo** (D-03): coinciden con `stock-operations.md` tras la P-06 de HU-05 aplicada.
  El orden es documentos (`customer-order` < `reservation` < `shipment`, en una llamada), luego
  ubicaciones y luego artículos. El borrador no bloquea el pedido. La renovación de `orderVersion`
  cubre las reservas leídas antes del bloqueo.
- **ETag y contexto** (D-05, D-12): el detalle lleva solo datos propios y `Shipment.Version`. El
  contexto va sin `ETag`, con `shipmentVersion` y `orderVersion`. La confirmación renueva el pedido
  y las reservas consumidas.
- **Contributor** (D-09): registrado en `AddStockCore` dentro de T004, sin `DependencyUnavailable`.
- **Triggers, DENY y EF** (D-06, iteración del 2026-11-21): `HasTrigger` en las tres tablas. Los
  consumos se ligan a la operación que confirma, sin depender del orden de EF. El `DENY UPDATE,
  DELETE` cubre los consumos. La única observación es M3.
- **Archivos de A** (T013 y T017): solo los permitidos, cada uno en un commit mínimo y con prueba.

### Constitución

Sin infracciones de principios MUST. Los principios III (validación con operarios, SC-007) y el
rendimiento (SC-006) siguen registrados como desviación: HU-03 puede integrarse, pero no darse por
aceptada.

### Métricas

- Requisitos: 27 FR (FR-001…FR-025, FR-001a y FR-006a) y 7 SC (5 con trabajo construible).
- Tareas: 18 (T001–T018), más Convergence (vacía) y «Si hay tiempo» (X1–X4).
- Cobertura: 27/27 FR con al menos una tarea. Con prueba completa en todas sus partes, 18/27 antes
  de corregir. Los parciales son FR-002, FR-003, FR-004, FR-006, FR-006a, FR-014, FR-021, FR-022 y
  FR-023 (H1–H5, M1).
- Ambigüedades: 0. Duplicados: 0. Críticos: 0.

## Próximas acciones

1. Corregir H1–H6 en documentos y tareas, en commits aparte (ver Resolución).
2. Decidir M3 con A antes de implementar T004.
3. `/speckit-implement` puede empezar tras las correcciones. No se lanza en esta fase.

## Resolución

Pendiente: se completa al aplicar las correcciones de H1–H6.
