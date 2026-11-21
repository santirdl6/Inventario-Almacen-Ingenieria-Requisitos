# Tareas: HU-06 — Alertas de stock mínimo

**Entrada**: [spec](spec.md), [plan](plan.md), [modelo](data-model.md), [contratos](contracts/), [investigación](research.md) y [validación](quickstart.md). Base: [constitución](../../.specify/memory/constitution.md), [plan global](../../.specify/memory/project-plan.md) e [INT-HU06-01](../001-reparto-specs-inventario/integration-tasks.md#hu-06--mínimos-y-alertas--persona-a).

Lista compacta: cada tarea agrupa cambios relacionados; `[P]` indica archivos distintos y sin dependencia entre tareas marcadas. Las pruebas exigidas por los escenarios se agrupan por historia. La aceptación H5 se registra por separado.

## Fase 1: Preparación

- [X] T001 Revisar la cadena EF vigente, los escritores existentes y las instrucciones de ejecución aislada en `specs/009-configurar-alertas-stock-minimo/quickstart.md`; registrar dependencias reales sin crear proyectos ni infraestructura duplicados.

  Resultado: el snapshot local incluye `20261121172905_HU01CorrectionReference`; verificar la cadena integrada antes de generar la migración. `ConfirmReceipt` y `ReconcileReceiptVoucher` consumen `StockCore.ApplyAsync`; `CorrectReceipt` usa `CorrectReceiptAsync`. El gate de primera referencia comparte `ArticleUsageCoverageValidator.RequiredTypes` y el coordinador de maestros consume `IArticleStateEffects`. Se verificaron las instrucciones de pruebas aisladas y `docker compose config --quiet`.

## Fase 2: Base compartida

- [X] T002 Crear `StockAlert` y su configuración/migración aditiva en `backend/src/Inventario.Api/Domain/Stock/StockAlert.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/`: aplicar literalmente las restricciones de la tabla de `specs/009-configurar-alertas-stock-minimo/data-model.md`, incluidas PK UUID, FK restrictiva, `State IN ('Active', 'Resolved')`, cantidades `decimal(18,3)` no nulas, instantes UTC, `ResolvedAt` nulo solo en `Active`, sugerencia no negativa e índice único filtrado por artículo activo; conservar `StockPolicy` 1:1, mínimo no negativo y `rowversion`. Implementar contributor de uso y separar el gate de primer enlace como exige `contracts/integration.md`; probar borrado con historial y edición de definición sin primer uso real.
- [X] T003 Implementar el evaluador compartido en `backend/src/Inventario.Api/Features/Replenishment/StockAlertEvaluator.cs`: usar `OperationContext` y lock de artículo, consolidar todos los buckets y deltas EF pendientes exactamente una vez, comprobar rangos, abrir/actualizar/resolver episodios y conservar histórico, sin transacción ni commit propios; registrar el servicio en `backend/src/Inventario.Api/Program.cs` conforme a `contracts/integration.md`.

## Fase 3: US1 — Configurar umbral (P1, MVP)

**Objetivo**: consultar y actualizar el mínimo con permisos y concurrencia, reevaluando en el mismo commit.

**Prueba independiente**: artículo activo con mínimo cero → establecer 10 → consultar versión; rechazar negativo, fracción inválida y versión obsoleta sin cambios.

- [X] T004 [P] [US1] Escribir pruebas SQL/HTTP de política y replay en `backend/tests/Inventario.IntegrationTests/StockPolicyTests.cs`: Q06-01, permiso, antiforgery, idempotencia, auditoría, ETag, 412 y rollback de política/alerta.
- [X] T005 [US1] Implementar GET/PUT de política en `backend/src/Inventario.Api/Features/Replenishment/`, con `Inventory.Read`/`StockPolicy.Manage`, decimales exactos, validación de artículo y unidad, `If-Match`, ejecutor común, auditoría y replay antes de versión; invocar T003 bajo el lock en la misma transacción según `contracts/api.md`.
- [X] T006 [US1] Incorporar lectura y edición accesible del mínimo en `frontend/src/app/features/master-data/articles/article-form.component.ts` y su plantilla: unidad, permisos, validación, ETag, conservación de datos en 412 y recuperación de intención incierta según `contracts/ui.md`.

## Fase 4: US2 — Detección y ciclo de alerta (P1, MVP funcional)

**Objetivo**: mantener una alerta activa coherente con el stock confirmado y resolverla al recuperar el mínimo.

**Prueba independiente**: físico 12, reservado 8 y mínimo 5 abren alerta con disponible 4 y sugerencia 1; una recepción que deja disponible 7 la resuelve.

- [X] T007 [P] [US2] Escribir pruebas SQL reales en `backend/tests/Inventario.IntegrationTests/Stock/StockAlertTests.cs` para Q06-02/03/06: V-11, 1 y 20 líneas, buckets nuevos y existentes, concurrencia con barreras, unicidad, rollback y replay sin duplicados.
- [X] T008 [US2] Conectar explícitamente T003 a los efectos confirmados de los escritores existentes en `backend/src/Inventario.Api/Features/Stock/Core/StockCore.cs` y `backend/src/Inventario.Api/Features/Receiving/`, incluidas correcciones y reserva/liberación/envío disponibles, tras aplicar efectos y antes del commit; verificar cada ruta real y documentar en `specs/009-configurar-alertas-stock-minimo/contracts/integration.md` el punto que deben consumir HU-03/04/05 cuando se integren.
- [X] T009 [US2] Ejecutar las pruebas T007 y la regresión de escritores afectados con SQL aislado; registrar comandos, resultados y rutas efectivamente cubiertas en `specs/009-configurar-alertas-stock-minimo/validation/stock.md`, dejando explícitas las rutas futuras no disponibles.

## Fase 5: US3 — Consulta y propuesta de compra (P2)

**Objetivo**: consultar alertas y abrir una propuesta editable sin crear compras automáticamente.

**Prueba independiente**: con `Alerts.Read`, consultar alerta mínimo 10/disponible 3 y ver sugerencia 7; abrir compras con artículo y cantidad editables y comprobar que no se creó pedido.

- [X] T010 [P] [US3] Escribir pruebas de permisos, consulta y navegación en `backend/tests/Inventario.IntegrationTests/StockAlertQueryTests.cs` y `frontend/e2e/stock-alerts.spec.ts`: Q06-05, filtros/paginación, `Inventory.Read` sin `Alerts.Read`, artículo inactivo y ausencia de pedido/correo al navegar.
- [X] T011 [US3] Implementar lista y detalle en `backend/src/Inventario.Api/Features/Replenishment/`, con `Alerts.Read`, filtros y orden de `contracts/api.md`, lectura consistente y cantidades decimales exactas.
- [X] T012 [US3] Crear panel `/alertas` y navegación en `frontend/src/app/features/replenishment/` y `frontend/src/app/operation-areas.ts`; extender `frontend/src/app/features/purchasing/` para aceptar propuesta editable y validada con `Purchasing.Manage`, sin proveedor preseleccionado, sobrescritura de intención pendiente ni guardado automático, según `contracts/ui.md`.

## Fase 6: US4 — Ciclo de vida del artículo (P2)

**Objetivo**: resolver al desactivar y reevaluar al reactivar, en la misma transacción.

**Prueba independiente**: desactivar artículo con alerta activa y comprobar resolución; reactivarlo bajo el mínimo y comprobar un episodio nuevo.

- [X] T013 [P] [US4] Escribir pruebas SQL reales de `INT-HU06-01` en `backend/tests/Inventario.IntegrationTests/ArticleAlertLifecycleTests.cs`: Q06-04, ambos órdenes frente a movimientos y política, lock de artículo, rollback y conservación del historial.
- [X] T014 [US4] Implementar y registrar `IArticleStateEffects` en `backend/src/Inventario.Api/Features/Replenishment/StockAlertEvaluator.cs`, consumido por `backend/src/Inventario.Api/Features/MasterData/Commands/ArticleLifecycleCoordinator.cs`: resolver/suspender al desactivar y reevaluar al reactivar con T003; registrar la evidencia técnica de `INT-HU06-01` en `specs/001-reparto-specs-inventario/integration-tasks.md`.

## Fase 7: Validación transversal

- [ ] T015 Preparar o adaptar instrumentación de pruebas para medir la duración del evaluador y el generador del perfil de carga en `scripts/measure-hu06-alerts.mjs` y `backend/tests/Inventario.IntegrationTests/`; reutilizar herramientas existentes cuando sea viable. Ejecutar Q06-01–06, pruebas backend/frontend, migraciones y compilación de `specs/009-configurar-alertas-stock-minimo/quickstart.md`; medir SC-004 con ese perfil y registrar resultados o motivo de inconclusión en `specs/009-configurar-alertas-stock-minimo/validation/acceptance.md`. Mantener sin aprobar cualquier control no ejecutado.

- [ ] T016 Coordinar y registrar revisión A+B de migración, reglas y escritores; actualizar acuerdos y alcance de consumidores en `.specify/memory/project/` y `specs/009-configurar-alertas-stock-minimo/validation/shared-review.md` antes de integrar. Mantener H5 y su aceptación operativa en `specs/001-reparto-specs-inventario/integration-tasks.md`.

**Estado: 14 de 16 tareas completadas.** T015 (ensayo SC-004) y T016 (registro de revisión A+B) quedan abiertas.

## Dependencias y ejecución

- T001 → T002 → T003. US1 y US2 necesitan la base; T005 precede T006 y T008 precede T009. Para el MVP funcional completar US1 y US2: configurar el mínimo por sí solo no ofrece detección en movimientos.
- US3 necesita episodios de US2 y el formulario existente de HU-07; US4 necesita T003 y el coordinador de maestros. T016 es gate previo a integrar cambios compartidos. T008 y T009 acreditan solo escritores existentes; HU-03/04/05 deben integrar y verificar sus propios escritores al incorporarse antes de considerarlos cubiertos por FR-005 y SC-001.
- Paralelismo si hay capacidad: T004 con T002/T003; T007 con T004 o T005; T010 con T008; T013 con T011/T012. Las pruebas se escriben antes de la implementación que verifican: T004 antes de T005, T007 antes de T008, T010 antes de T011/T012 y T013 antes de T014. No ejecutar en paralelo tareas que cambien los mismos archivos.
- Ejemplos por historia: US1 T004 y T003 trabajan en pruebas y evaluador separados; US2 T007 y T005 en pruebas y endpoint separados; US3 T010 y T008 en pruebas de consulta y escritores; US4 T013 y T012 en pruebas y frontend. Con recursos limitados, seguir T001–T016 secuencialmente es válido.
- Entregar incrementalmente: primero US1, después US2 para un MVP verificable; añadir US3 y US4 y finalizar las validaciones. Las historias dependen de servicios compartidos, pero cada criterio anterior se comprueba por separado.
