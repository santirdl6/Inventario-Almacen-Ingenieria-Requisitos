# HU-04 — /speckit-converge, iteración 2

**Fecha**: 2026-11-21. **Rama**: `b/hu-04-realizar-inventarios-ciclicos`, sobre `9c8974a`. El último
commit de código es `7608a84` (C8).

**Alcance**: comprobar, contra el código y las pruebas, las brechas G1–G8 de
[converge-1](converge-1.md) tras la Phase 10 (C1–C8) y buscar brechas nuevas. Mismo marco y mismas
fuentes de intención que converge-1. spec.md y plan.md no se tocan. Las decisiones D-04, D-06 y D-07
no cambian.

## Desviación respecto a la skill

Como en converge-1:

- las tareas correctoras se numeran `C…`;
- van en una fase nueva al final de tasks.md (`Phase 11: Convergence`), sin tocar las anteriores;
- no se implementan en esta iteración.

## Estado de G1–G8

| ID | Estado | Tarea y commit | Evidencia comprobada |
|----|--------|----------------|----------------------|
| G1 | **Cerrada** | C1, `00f5b8c` | `ILocationUsage.FindOpenCountAsync` → `OpenCountScope.Problem()`: «La ubicación U está en el conteo abierto CC-…». La usan `ShipmentCommands.cs`, `ReservationCommands.cs` y `LocationCommands.cs`. `CountScopeFrozenTests.Writers_are_rejected_…` comprueba la ubicación y el `countCode` en salida, reserva, desactivar y cambiar el tipo, vacía y ocupada. El rojo previo está anotado en tasks.md. |
| G2 | **Cerrada** | C2, `e9d3909` | `CycleCountSummary` con `locationCodes`, `createdByName`, `startedByName`, `closedBy` y `closedByName`. `count-list.page.ts` muestra «Creó / Inició / Cerró» y los códigos. Pruebas: `ShipmentCorrectionTests.C19_…` (la primera prueba backend de `GET /cycle-counts`) y `count-list.page.spec.ts`. |
| G3 | **Cerrada** (decisión de B: opción 3) | C3, `32d51d5` | `GET /cycle-counts?correctsShipmentId=`: solo lectura, `Inventory.Read`, sin migración porque usa `IX_CountLines_CorrectsShipmentId`, y 422 con un Id no válido. `/salidas/{id}` confirmada avisa con el código del conteo, enlazado, de que el pedido no se corrige. `ShipmentCorrectionTests.C19_…` y `shipment-confirm.page.spec.ts` lo prueban con 6 registradas y 4 reales: la línea sigue en 6.000 UD y el aviso dice «siguen contando 6.000 UD servidas». |
| G4 | **Cerrada por documentación** | C4, `e11d031` | El [contrato UI](../contracts/cycle-counts-ui.md) § Resultados dice que el enlace va a `/existencias`. Ni el 409 ni el contexto traen el pedido. El código no cambia. |
| G5 | **Cerrada** | C5, `1ff639f` | `data-testid="difference-count"` en todos los estados desde el inicio, fuera de la revisión, que conserva su frase. `count-detail.page.spec.ts` lo prueba en `Counting`, `AwaitingApproval` sin `Inventory.Adjust` y `Confirmed`; en el borrador no aparece. |
| G6 | **Cerrada** | C6, `d9b48e7` | `StockArticleOption.allowsFractional` (opcional), tomado de `GET /articles`. `addFound` valida con él. `count-detail.page.spec.ts` comprueba el rechazo de «2,5» con `false` y que se admite con `true`. |
| G7 | **Cerrada por documentación** | C7, `60024cd` | Se mantiene la decisión H3 de [analyze-1](analyze-1.md): el historial muestra el `countCode` como soporte, sin enlace obligatorio. El recorrido está documentado en el [quickstart](../quickstart.md#desviaciones-registradas-t010-t013t017). No se tocan archivos de A. |
| G8 | **Cerrada** | C8, `7608a84` | T034 de HU-02 marcada con la evidencia de HU-04 y quickstart de HU-02 actualizado. `location-detail.page.spec.ts` lo prueba con `inOpenCount: true` y `false`. Sin rojo natural: el aviso existía y faltaba cobertura. No se mutó `location-detail.page.ts`, que tiene un commit de A. |

La nota documental de converge-1 sobre el enunciado de T010 también está cerrada (`9c8974a`). El texto
original se conserva tachado, como desviación visible.

## Hallazgos nuevos

| ID | Tipo | Severidad | Fuente | Evidencia | Qué falta | Tarea |
|----|------|-----------|--------|-----------|-----------|-------|
| G9 | partial | BAJA | Plan § Evidencia (T018); P-08 | El [quickstart de HU-04](../quickstart.md#evidencia) y el [quickstart común § 11](../../../.specify/memory/project/quickstart.md#11-hu-04--evidencia-de-integración-del-2026-11-21) aún citan Vitest 223/223 y `GET /cycle-counts` sin filtros. Son anteriores a C1–C8, que añadieron 7 pruebas Vitest y el filtro `correctsShipmentId`. | Publicar los resultados de esta iteración en ambos quickstarts (Vitest 230/230; backend 347/1/0, mismo número porque C1–C3 ampliaron pruebas existentes; Playwright 18/18) y, en el común, el aviso de la salida corregida. | C9 |

Nota sin tarea: los datos simulados de `location-form.edit.spec.ts:76` (HU-02) y de
`shipment-confirm.page.spec.ts:118` (HU-03) usan los textos antiguos de `CountScopeFrozen`. Las
pruebas siguen siendo correctas, porque la pantalla muestra el `detail` que recibe, sea cual sea.

**Recuento por tipo**:

- trabajo ausente: 0;
- cumplimiento parcial: 1 (G9);
- comportamiento contradictorio: 0;
- código sin justificación: 0.

El filtro `correctsShipmentId` y el aviso en `/salidas/{id}` no estaban en la spec de forma explícita.
Están justificados por US4.3 y por la decisión de B sobre C3.

No hay infracciones de los principios MUST de la constitución.

## Pendientes justificados (sin cambios desde converge-1)

| Pendiente | Justificación | Responsable |
|-----------|---------------|-------------|
| Recepción sobre un alcance congelado (FR-014, SC-002 y la parte de recepción de V-09) | HU-01 no tiene escritor todavía. P-05 queda confirmada por A. | A, HU-01; S5 |
| SC-006 y SC-007 | Necesitan carga y trabajadores representativos. HU-04 es integrable, pero no está aceptada. | B; S2, S3 |
| «Si hay tiempo» S1–S6 | Accesorias, fuera de la numeración. | B (S5, A) |
| Reevaluación del mínimo tras el ajuste (HU-06) | Sin alertas integradas. | A, HU-06 |
| Casos de conteo de T034 en `LocationsUpdateTests`/`LocationStockTests` | Están en `CountScopeFrozenTests` de HU-04, porque necesitan un conteo real. No se duplican. | — |

## Verificación

| Comprobación | Resultado | Sobre |
|--------------|-----------|-------|
| Suite backend completa (receta Docker; tras C1, C2 y C3) | 347 aprobadas, 1 omitida (V-14), 0 fallidas en las tres ejecuciones | `00f5b8c`, `e9d3909`, `32d51d5` |
| `has-pending-model-changes` | No se ha ejecutado: no hay cambios de modelo ni de migración | — |
| Vitest | 230/230 en 50 archivos | `7608a84` |
| `tsc` de app, specs y E2E y `npm run build` | Sin errores | `7608a84` |
| Playwright (`--grep-invert SC-005`) | 18/18 en 5,4 min, con E2E-02 y E2E-03 | árbol de `9c8974a` (código de `7608a84`) |

Docker limpio al terminar: `inventario-sqlserver-1` parado y el proyecto `inventario-hu08-e2e` parado
por su fixture.

## Tareas correctoras añadidas

C9 en la `Phase 11: Convergence` de [tasks.md](../tasks.md), sin implementar: es solo documentación.
