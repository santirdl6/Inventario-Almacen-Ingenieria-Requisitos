# Implementation Plan: HU-04 — Realizar inventarios cíclicos y ajustar diferencias

**Branch**: `b/hu-04-realizar-inventarios-ciclicos` (directorio de feature `specs/006-realizar-inventarios-ciclicos`,
creada sobre `b/hu-03-registrar-salida-mercancia`, PR #10 abierto) | **Date**: 2026-11-21 | **Spec**: [spec.md](spec.md)

**Input**: especificación clarificada de [spec.md](spec.md) (sesión 2026-11-21, dos decisiones).
**Responsable**: Persona B · **Hito**: H4 ([plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación)).

**Base normativa y acuerdos consumidos** (se referencian, no se copian):
[constitución v1.0.0](../../.specify/memory/constitution.md) ·
[plan global](../../.specify/memory/project-plan.md) ·
[research común](../../.specify/memory/project/research.md) (R-05, bloqueos) ·
[modelo común § Conteos](../../.specify/memory/project/data-model.md#conteos--persona-b--hu-04) ·
[contrato HTTP § HU-04](../../.specify/memory/project/contracts/api.md#hu-04--b-conteos-y-ajustes) ·
[protocolo de stock § Conteo](../../.specify/memory/project/contracts/stock-operations.md#conteo-sin-transacción-humana-abierta) ·
[contrato UI](../../.specify/memory/project/contracts/ui.md) ·
[guía común](../../.specify/memory/project/quickstart.md) (V-07…V-10, V-12) ·
[backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#hu-04--conteos-y-ajustes--persona-b) (INT-HU04-01/02/03).
De HU-03: [plan](../005-registrar-salida-mercancia/plan.md), [research D-03 y D-06](../005-registrar-salida-mercancia/research.md)
y [convergencia](../005-registrar-salida-mercancia/validation/converge-1.md). De HU-05:
[contrato HU-05 → HU-03](../004-consultar-existencias/contracts/hu03-integration.md).

Artefactos de esta feature: [research.md](research.md) (D-01…D-16) · [data-model.md](data-model.md) ·
[contracts/cycle-counts-api.md](contracts/cycle-counts-api.md) · [contracts/cycle-counts-ui.md](contracts/cycle-counts-ui.md) ·
[quickstart.md](quickstart.md).

## Summary

HU-04 cuenta ubicaciones completas sin parar el almacén: crear un borrador con el alcance, iniciarlo
(congela las ubicaciones y captura el teórico), registrar el contado y los hallazgos, enviarlo, revisar
motivos y, si procede, vincular las diferencias a una salida confirmada errónea, y confirmar el ajuste o
cancelar el conteo. La confirmación revalida teórico y reservado, añade un movimiento de ajuste con una
entrada por diferencia y libera el alcance, en un solo commit.

**Backend**: feature vertical `Features/CycleCounts` sobre los componentes comunes, sin crear otros:

- `InventoryOperationExecutor` (permiso, idempotencia, replay, transacción y `OperationResult`).
- `InventoryLocks` con el tipo nuevo `cycle-count` (D-03).
- `StockCore` con la operación `Adjust` nueva (D-13), única vía al diario.
- `ILocationUsage.IsInOpenCountAsync` real (D-04): activa el `CountScopeFrozen` que salidas, reservas y
  ubicaciones ya consultan tras bloquear la ubicación (INT-HU04-03).
- Una migración aditiva `HU04_CycleCounts` con tres tablas, tres triggers y un `DENY` (D-07, D-14).

**Frontend**: sustituye el placeholder de `/inventarios` (D-15) con el transporte de intenciones común y
`appRoute`.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (minimal APIs); TypeScript 6.0 / Angular 22.2, según el
[plan global](../../.specify/memory/project-plan.md#technical-context).

**Primary Dependencies**: EF Core 10 + SqlServer, ASP.NET Core Identity y PrimeNG 22.1. Sin dependencias
nuevas.

**Storage**: SQL Server 2025, `InventoryDbContext` único.

- Tablas nuevas: `CycleCounts`, `CountLocations` y `CountLines`.
- Se escriben `StockMovements`, `StockEntries`, `StockBalances` y `Lots` solo mediante `StockCore`.
- Se leen sin modificar `Shipments`/`ShipmentLines` (HU-03) y `StockBalances`.
- La cadena parte de `20261121135335_HU03_Shipments` (D-14); se regenera si cambia la base.

**Testing**: xUnit + `IdentityFixture` contra SQL Server real, con `StockTestHarness`,
`FailAfterSaveInterceptor` y `LockBarrier`, mediante la receta Docker. Vitest/TestBed. Playwright
para E2E-03.

**Target Platform**: la del plan global.

**Project Type**: aplicación web (monolito modular `backend/` + `frontend/`).

**Performance Goals**: SC-006 (p95 ≤ 3 s al confirmar 50 líneas con 25 sesiones) queda fuera de esta
entrega (Complexity Tracking). El diseño no lo impide: bloqueos por recurso, índice por ubicación en
`CountLocations` y un solo movimiento por conteo.

**Constraints**:

- Migración aditiva; triggers y `DENY` a mano, solo `THROW`, con `HasTrigger` en EF.
- Permiso en cada endpoint y otra vez en el ejecutor.
- `Idempotency-Key` en las nueve escrituras; `If-Match` en las que actúan sobre un conteo existente.
- Confirmar y cancelar no modifican líneas (D-07).
- Sin ajuste directo sin conteo (clarificación Q2); la liberación de reservas no se congela (Q1).

**Scale/Scope**: 13 rutas HTTP, 3 pantallas, casos C-01…C-20 y un E2E. Presupuesto de
`/speckit-tasks`: ≤ 20 tareas más una fase «Convergence» aparte y una lista «Si hay tiempo». Sin
incógnitas abiertas: ver [research.md](research.md).

## Lecciones de HU-05 y HU-03 aplicadas desde el diseño

| # | Lección | Aplicación en HU-04 |
|---|---------|---------------------|
| 1 | `ETag` fuerte = representación entera | Detalle solo con datos propios y líneas por Id; contexto sin `ETag` con `countVersion` (D-12). |
| 2 | Orden de bloqueos con tipos nuevos definido frente a los existentes | `cycle-count` entre `customer-order` y `reservation-release` (D-03). |
| 3 | Contraparte con exactamente un campo | `CounterpartyNotApplicableReason` en todo ajuste (D-06). |
| 4 | Contributor de uso para toda FK a `Articles` | `CycleCountArticleUsageContributor` en `AddStockCore` (D-08). |
| 5 | Migración sobre la última presente; `DENY` y triggers a mano, solo `THROW`, `HasTrigger` | D-07, D-14; sin depender del orden de EF porque confirmar y cancelar no tocan líneas. |
| 6 | Frontend con `PendingOperationStore`, `appRoute`, `begin` + `send`, foco, ID de operación, todas las páginas | D-15; P-06 para los archivos de A. |
| 7 | Carreras con conexiones independientes en ambos órdenes | C-13 y C-15 con `LockBarrier` (D-16). |
| 8 | ≤ 20 tareas + Convergence + «Si hay tiempo»; cada commit en verde | tasks.md. |
| 9 | Cambios comunes o de A en una tabla P-xx, sin editarlos sin avisar | Sección siguiente. |

## Constitution Check

*GATE previo a Phase 0 y repetido tras Phase 1. PASS = conformidad del diseño, no evidencia de
implementación. La evidencia se recogerá con los casos del [quickstart](quickstart.md).*

| Principio | Compromiso de diseño en HU-04 | Evidencia prevista | Antes | Después |
|-----------|-------------------------------|--------------------|-------|---------|
| I. Correspondencia | Las diferencias se resuelven con un movimiento de ajuste de `StockCore`; nunca se sobrescribe un saldo; el teórico se revalida al confirmar y la congelación impide movimientos intermedios. | C-08, C-10, C-12, C-14 | PASS | PASS |
| II. Ubicación precisa | Conteo por ubicaciones completas; cada línea con ubicación del alcance (FK compuesta) y lote; hallazgos con lote obligatorio si el artículo lo gestiona. | C-04, C-07 | PASS | PASS |
| III. Uso intuitivo | Diferencias en texto, cero explícito, revisión antes de confirmar, errores por campo con foco, aviso de que la corrección no cambia lo servido. La validación con trabajadores (SC-007) queda fuera. | Vitest, E2E-03 | PASS* | PASS* |
| IV. Trazabilidad | Movimiento con actor que aprueba, motivo, soporte (`CountCode`) y contraparte no aplicable; motivo, contador y vínculo a salida por línea; conteo cerrado inmutable por trigger; diario de solo adición. | C-01, C-08, C-19 | PASS | PASS |
| V. Integridad | Ejecutor común; bloqueos documento → ubicaciones → artículos antes de leer; congelación persistente con índice filtrado; `If-Match`, idempotencia, atomicidad; no confirmar por debajo del reservado; carreras con conexiones reales. | C-09, C-13, C-15, C-17, C-18 | PASS | PASS |
| Controles operativos | Aprobación con `Inventory.Adjust` registrada aunque la misma persona cuente; comparación físico/registrado por artículo y ubicación; evidencia y resolución conservadas. El calendario de conteos lo documenta el responsable fuera del sistema (spec, fuera de alcance). | C-08, C-11 | PASS | PASS |
| Revisión y gobernanza | Cambios a documentos comunes y archivos de A como diff (P-01…P-08), sin aplicarlos sin revisión. | Revisión de A en el PR | PASS | PASS |

\* Desviación registrada en Complexity Tracking: sin validación con trabajadores ni medición de
rendimiento, HU-04 puede integrarse pero no darse por **aceptada**.

**Revaluación tras Phase 1**: el diseño no introduce otro ejecutor, otro `OperationResult`, otro
bloqueo, otro cálculo de disponible ni otra vía al diario. Los componentes nuevos son el documento de
conteo, la operación `Adjust` de `StockCore` y el lector real de congelación. Sin violaciones nuevas.

## Project Structure

### Documentation (this feature)

```text
specs/006-realizar-inventarios-ciclicos/
├── spec.md
├── plan.md              # este archivo
├── research.md          # D-01…D-16
├── data-model.md
├── quickstart.md        # matriz C-01…C-20
├── contracts/
│   ├── cycle-counts-api.md
│   └── cycle-counts-ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
backend/src/Inventario.Api/
├── Domain/Stock/CycleCount.cs, CountLocation.cs, CountLine.cs          # nuevo
├── Features/CycleCounts/                                               # nuevo
│   ├── CycleCountCommands.cs      # crear, editar, iniciar, contar, hallazgos, enviar, revisar, confirmar, cancelar
│   ├── CycleCountQueries.cs       # lista, detalle, contexto, salidas candidatas
│   ├── CycleCountEndpoints.cs     # AddCycleCounts / MapCycleCountEndpoints
│   └── CycleCountArticleUsageContributor.cs
├── Features/Stock/Core/StockCore.cs, StockCommands.cs, StockArticleUsage.cs   # B (H0): Adjust y registro del contributor
├── Features/Locations/ILocationUsage.cs                                       # B (HU-02): IsInOpenCountAsync real
├── Infrastructure/Persistence/Configurations/CycleCount*Configuration.cs      # nuevo, con HasTrigger
├── Infrastructure/Persistence/Migrations/<ts>_HU04_CycleCounts.cs              # nuevo
└── Program.cs                                                                 # común: AddCycleCounts y Map
backend/tests/Inventario.IntegrationTests/CycleCounts/                         # nuevo
├── Hu04SchemaTests.cs, CycleCountDraftTests.cs, CycleCountStartTests.cs, CycleCountLinesTests.cs,
├── CycleCountConfirmTests.cs, CountScopeFrozenTests.cs, CycleCountConcurrencyTests.cs,
├── CycleCountReliabilityTests.cs, ShipmentCorrectionTests.cs, CycleCountApiSupport.cs
backend/tests/.../Stock/Hu05SchemaTests.cs, StockCoreAdjustTests.cs           # B: FK exactas y Adjust
frontend/src/app/features/cycle-counts/                                        # sustituye el placeholder
├── cycle-counts.routes.ts, cycle-counts-api.service.ts,
├── count-list.page.ts, count-new.page.ts, count-detail.page.ts (+ .spec.ts),
├── count-review-dialog.ts, shipment-link-dialog.ts (+ .spec.ts)
frontend/src/app/core/operations/pending-operation.store.ts, operation-status.component.ts   # A (P-06)
frontend/e2e/hu04-cycle-counts.spec.ts                                         # E2E-03
```

## Diseño por capas

1. **Dominio**: `CycleCount` aplica las transiciones de D-02 y las reglas locales (contado ≥ 0, cero
   explícito, hallazgo retirable, motivo solo con diferencia). Saldos, reservado y congelación ajena se
   deciden en el comando tras los bloqueos.
2. **Comandos**: `OperationRequest` con método, recurso canónico, permiso de la fila y el DTO normalizado.
   Bloqueos según D-03. Confirmar: revalidar (D-05), `StockCore.PrepareAdjustment` + `ApplyAsync` con
   las líneas con diferencia (D-06, D-13), `IsFrozen = 0` y aprobación.
3. **Consultas**: `AsNoTracking` en `SNAPSHOT`; detalle con `ETag`; contexto sin `ETag` con
   `countVersion`; lista y candidatas paginadas (D-12, D-09).
4. **HTTP**: lecturas con `Inventory.Read`; escrituras con `Counts.Record` o `Inventory.Adjust`;
   antiforgery del grupo `/api/v1` y `LocationEndpoints.Key()` para la clave.
5. **Frontend**: servicio tipado; `apiProblem()`; `MasterDataMutationsService` /
   `PendingOperationStore` con `appRoute`; `OperationStatusComponent`; foco al primer error (D-15).

## Dependencias y riesgos

| Dependencia o riesgo | Efecto | Mitigación |
|----------------------|--------|------------|
| PR #10 (HU-03) abierto | La migración parte de `HU03_Shipments`; la corrección (US4) lee `Shipments`; C-12 prueba la confirmación de salidas. | Rama creada sobre la de HU-03; si la fusión cambia algo, merge y regenerar `HU04_CycleCounts` (D-14). Las tareas que dependen del PR #10 están marcadas en tasks.md. |
| Revisión de A de P-01…P-08 | Bloquea la fusión, no la implementación. | Cambios aditivos; P-06 (archivos de A) en su propio commit. |
| Triggers y orden de EF (lección de HU-03) | Un trigger que vigile filas modificadas en la misma `SaveChanges` que la cabecera sería frágil. | Confirmar y cancelar no tocan líneas; los triggers admiten los dos estados de la transición de inicio (D-07). |
| Recepciones (HU-01, A) | Sin escritor todavía; deberá respetar la congelación. | P-05: consultar `IsInOpenCountAsync` tras `location:{id}`. |
| Dos contadores en el mismo conteo | 412 al segundo (D-11). | Límite aceptado; versión por línea en «Si hay tiempo». |
| HU-06 alertas | La regla común reevalúa el mínimo tras cada ajuste. | Sin alertas integradas; se engancha bajo `article:{id}` cuando exista. |
| E2E-03 necesita existencias y dos roles | Cuentas `operario` y `responsable` ya aprovisionadas (HU-05 `ef296cc`) y `POST /testing/stock-receipts`. | Reutilizarlas; sin cambios en fixtures de A previstos. |

## Cambios al modelo común pendientes de A

Ningún documento ni archivo común o de A se edita en esta rama sin aviso y revisión de A en el PR
(AGENTS.md; lección 9).

| ID | Documento o archivo (dueño) | Cambio propuesto | Motivo |
|----|-----------------------------|------------------|--------|
| P-01 | `data-model.md` § Conteos (común) | Campos concretos de `CycleCount` (`CountCode`, responsables por paso, `CancelReason`, `OperationId`, `AdjustmentMovementId`); `CountLine` con `LotCode`, `IsFound`, `ReasonBy`, `CorrectsShipmentId`; un movimiento `Adjustment` por conteo con una entrada por diferencia; triggers de conteo cerrado y `DENY DELETE`. | D-06, D-07, D-09, D-10. |
| P-02 | `data-model.md` § Diario (común) | Precisar que la unicidad «conteo/Confirm» es un movimiento por conteo; motivo por diferencia en la línea del conteo. | D-06. |
| P-03 | `contracts/api.md` § HU-04 (común) | Añadir `PUT /cycle-counts/{id}`, `DELETE /cycle-counts/{id}/lines/{lineId}`, `PUT /cycle-counts/{id}/review`, `GET /cycle-counts`, `GET /cycle-counts/{id}/context` y `GET …/lines/{lineId}/shipment-candidates`; `PUT …/lines` con `lotCode` en hallazgos en lugar de `lotId`; detalle con `ETag` y contexto sin él. | D-02, D-09, D-12. |
| P-04 | `contracts/api.md` § Errores (común) | Nuevos 409 `CountStateConflict` y `CountTheoreticalChanged`. | D-05; contrato API de la feature. |
| P-05 | `contracts/stock-operations.md` § Conteo y § Alcance (común) | Tipo `cycle-count` y orden `customer-order < cycle-count < reservation-release`; operación `Adjust` en `StockCore`; la liberación no se congela (Q1); el escritor de recepciones de HU-01 consulta `IsInOpenCountAsync` tras `location:{id}`. | D-03, D-04, D-13. |
| P-06 | `frontend/src/app/core/operations/pending-operation.store.ts` y `operation-status.component.ts` (A) | `mutationPermission()`: `/api/v1/cycle-counts`, `/{id}`, `/{id}/start`, `/{id}/lines(/{lineId})`, `/{id}/submit` → `Counts.Record`; `/{id}/review`, `/{id}/confirm`, `/{id}/cancel` → `Inventory.Adjust`. `APP_ROUTE`: `^/inventarios/(nuevo|{uuid})$`. Enlace «Abrir conteo». | D-15; mismo patrón que P-06 de HU-03, en su propio commit. |
| P-07 | `specs/001-reparto-specs-inventario/integration-tasks.md` (coordinación A+B) | Registrar la tarea receptora de la corrección de salidas (P-07 de HU-03) como cubierta por US4; marcar INT-HU04-01/02/03 solo con implementación y evidencia; nota del pendiente de HU-01 (P-05). | Spec «Integraciones recibidas»; AGENTS.md. |
| P-08 | `quickstart.md` común | Evidencia de HU-04 (V-09, V-10, V-07, V-08, V-12) al ejecutar la matriz C. | Como P-08 de HU-03. |

### Archivos de B fuera de la feature que se tocan

- `Features/Stock/Core/StockCore.cs` y `StockCommands.cs` (H0): operación `Adjust` (D-13).
- `Features/Stock/Core/StockArticleUsage.cs` (H0): registro del contributor de HU-04 en `AddStockCore` (D-08).
- `Features/Locations/ILocationUsage.cs` (HU-02): `IsInOpenCountAsync` real (D-04).
- `backend/tests/.../Stock/Hu05SchemaTests.cs` (HU-05): conjunto exacto de tipos con FK a `Article`.
- `frontend/src/app/features/cycle-counts/cycle-counts.routes.ts` (H0): sustituye el placeholder.
- `Program.cs` (común): `AddCycleCounts()` y `MapCycleCountEndpoints()`, como HU-05 y HU-03.

La spec se ajusta en este commit (FR-010, FR-021): una entrada del movimiento del conteo por cada
diferencia, en lugar de un movimiento por línea (D-06). No cambia qué se registra.

## Complexity Tracking

| Desviación | Por qué se acepta para esta entrega | Alternativa más simple descartada | Resolución y responsable |
|------------|-------------------------------------|-----------------------------------|--------------------------|
| SC-007 sin validación con trabajadores (constitución III). | Requiere sesiones con trabajadores. HU-04 se integra, pero queda **sin aceptar**. | Validar con el equipo, que no cumple el principio III. | Persona B, antes de la aceptación, con el guion del [quickstart](quickstart.md#fuera-de-alcance-de-esta-entrega). |
| SC-006 sin medición de rendimiento. | Necesita carga y datos sintéticos. | Medir con datos mínimos, no representativos. | Persona B, antes de la aceptación. |
| Revisión como paso explícito antes de confirmar (D-07, D-09). | Evita que una `SaveChanges` cambie cabecera y líneas bajo triggers. | Motivos en el `POST /confirm` con tabla de resoluciones y totales declarados. | Decisión de B (2026-11-21); P-01, P-03. |
| Un movimiento por conteo, no por línea (D-06). | Conserva la unicidad común por documento y la etiqueta del historial de A. | Movimiento por línea con un tipo de documento nuevo. | Decisión de B; P-02. |
| Versión única del conteo (D-11). | Simplicidad; el 412 conserva lo escrito. | Versión por línea. | «Si hay tiempo». |
| Recepciones congeladas sin prueba. | HU-01 (A) no tiene escritor todavía. | — | P-05, A al implementar HU-01. |
