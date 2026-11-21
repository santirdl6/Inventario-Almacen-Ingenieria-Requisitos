# Implementation Plan: HU-03 — Registrar la salida de mercancía

**Branch**: `b/hu-03-registrar-salida-mercancia` (directorio de feature `specs/005-registrar-salida-mercancia`,
creada sobre `b/hu-05-consultar-existencias`, PR #9 abierto) | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: especificación clarificada de [spec.md](spec.md) (sesión 2026-11-20, cuatro decisiones).
**Responsable**: Persona B · **Hito**: H3 ([plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación)).

**Base normativa y acuerdos consumidos** (se referencian, no se copian):
[constitución v1.0.0](../../.specify/memory/constitution.md) ·
[plan global](../../.specify/memory/project-plan.md) ·
[research común](../../.specify/memory/project/research.md) (R-03, R-04, R-05, R-06, R-10) ·
[modelo común](../../.specify/memory/project/data-model.md#compromisos-y-salidas--persona-b--hu-03-hu-05) ·
[contrato HTTP](../../.specify/memory/project/contracts/api.md#hu-03-y-hu-05--b-pedidos-de-cliente-reservas-y-salidas) ·
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md#cálculo-de-salida) ·
[contrato UI](../../.specify/memory/project/contracts/ui.md) ·
[guía común](../../.specify/memory/project/quickstart.md) ·
[backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#hu-03--salidas--persona-b) (INT-HU03-01/02).
De HU-05: [plan](../004-consultar-existencias/plan.md), [research](../004-consultar-existencias/research.md),
[modelo](../004-consultar-existencias/data-model.md),
[contrato de integración](../004-consultar-existencias/contracts/hu03-integration.md) y
[convergencia](../004-consultar-existencias/validation/converge-1.md).

Artefactos de esta feature: [research.md](research.md) (D-01…D-15) · [data-model.md](data-model.md) ·
[contracts/shipping-api.md](contracts/shipping-api.md) · [contracts/shipping-ui.md](contracts/shipping-ui.md) ·
[quickstart.md](quickstart.md).

## Summary

HU-03 registra salidas de mercancía contra los pedidos de cliente de HU-05: localizar el pedido por
su referencia externa, preparar un borrador con orígenes propuestos desde las reservas propias,
editarlo o eliminarlo, revisarlo y confirmarlo. La confirmación descuenta el físico, consume primero
la reserva propia en cada origen, nunca las ajenas, actualiza lo servido y el estado del pedido y
renueva las versiones del pedido y de las reservas consumidas, en un solo commit.

**Backend**: feature vertical `Features/Shipping` sobre los componentes comunes, sin crear otros:

- `InventoryOperationExecutor` (permiso, idempotencia, replay, transacción y `OperationResult`).
- `InventoryLocks` con el tipo nuevo `shipment` (D-03).
- `StockCore.PrepareShipment` y `ApplyAsync` (operación `Ship`, ya construida en H0).
- `ICustomerOrderReader` de HU-05 y sus entidades, cargadas tras los bloqueos (contrato HU-03).
- Una migración aditiva `HU03_Shipments` con tres tablas, tres triggers y un `DENY` (D-06, D-08, D-10). Los consumos se ligan a la operación que confirma, así que no se pueden insertar tras confirmar (iteración 2026-11-21).

**Frontend**: sustituye el placeholder de `/salidas` (D-14) con el transporte de intenciones común y
`appRoute`.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (minimal APIs); TypeScript 6.0 / Angular 22.2, según el
[plan global](../../.specify/memory/project-plan.md#technical-context).

**Primary Dependencies**: EF Core 10 + SqlServer, ASP.NET Core Identity y PrimeNG 22.1. Sin
dependencias nuevas.

**Storage**: SQL Server 2025, `InventoryDbContext` único.

- Tablas nuevas: `Shipments`, `ShipmentLines` y `ShipmentReservationConsumptions`.
- Se modifican filas (no esquema) de `CustomerOrders`, `CustomerOrderLines`, `Reservations` y
  `ReservationAllocations` de HU-05, y se escriben `StockMovements`, `StockEntries` y
  `StockBalances` mediante `StockCore`.
- La cadena parte de `20261120113458_HU05_CustomerOrdersReservations` (D-10). Se regenera si cambia
  la integración (PR #9 o migraciones de A).

**Testing**: xUnit + `IdentityFixture` contra SQL Server real, con `StockTestHarness`,
`FailAfterSaveInterceptor`, `SaveBarrier` y `LockBarrier`, mediante la receta Docker (no hay SDK
local). Vitest/TestBed. Playwright para E2E-02.

**Target Platform**: la del plan global.

**Project Type**: aplicación web (monolito modular `backend/` + `frontend/`).

**Performance Goals**: SC-006 (p95 ≤ 3 s al confirmar con 25 sesiones) queda fuera de esta entrega
(Complexity Tracking). El diseño no lo impide: bloqueos por recurso, `IX_Shipments_CustomerOrder` y
los índices de bucket de HU-05.

**Constraints**:

- Migración aditiva; triggers y `DENY` a mano.
- Permiso en cada endpoint y otra vez en el ejecutor.
- `Idempotency-Key` en las cuatro escrituras; `If-Match` en editar, eliminar y confirmar;
  `orderVersion` en confirmar.
- Sin `SaveChanges` dentro de `change` al confirmar (FK del diario); sí al crear, editar y eliminar.
- Sin reversión de salidas confirmadas (Q3) ni liberaciones implícitas (regla 5).

**Scale/Scope**: 8 rutas HTTP (una en `customer-orders`), 4 pantallas, casos H-01…H-21 y un E2E.
Presupuesto de `/speckit-tasks`: ≤ 20 tareas más una fase «Convergence» aparte (lección 8). Sin
incógnitas abiertas: ver [research.md](research.md).

## Constitution Check

*GATE previo a Phase 0 y repetido tras Phase 1. PASS = conformidad del diseño, no evidencia de
implementación. La evidencia se recogerá con los casos del [quickstart](quickstart.md).*

| Principio | Compromiso de diseño en HU-03 | Evidencia prevista | Antes | Después |
|-----------|-------------------------------|--------------------|-------|---------|
| I. Correspondencia | El físico solo baja al confirmar, mediante `StockCore` (`ShipReserved`/`ShipAvailable`); el borrador no cambia nada; `OccurredAt` conserva el instante real del hecho. | H-04, H-08, H-14 | PASS | PASS |
| II. Ubicación precisa | Cada línea sale de un bucket concreto con FK a `Locations` y FK compuesta a `Lots`; un bucket por salida. | H-05, H-09 | PASS | PASS |
| III. Uso intuitivo | Búsqueda por referencia, propuesta editable, revisión con propio/ajeno/posible, errores por línea con foco, estados en texto. La validación con operarios (SC-007, V-13) queda fuera de esta entrega. | Vitest, E2E-02 | PASS* | PASS* |
| IV. Trazabilidad | Un movimiento `Shipment` con actor, instantes, motivo, soporte (`ExternalReference`) y contraparte (`CustomerReference`); consumos por asignación append-only (D-08); salida confirmada inmutable por trigger (D-06); sin reversión en HU-03, la corrección pasa a HU-04 (Q3). | H-02, H-08, H-14 | PASS | PASS |
| V. Integridad | Ejecutor común; bloqueos documentos → ubicaciones → artículos antes de leer; `If-Match` de la salida y `orderVersion` del pedido; renovación de versiones (regla 4); idempotencia y unicidad del diario; atomicidad; carreras con conexiones reales. | H-10…H-12, H-16…H-19 | PASS | PASS |
| Revisión y gobernanza | Cambios al modelo común y a archivos de A como diff (P-01…P-07), sin aplicarlos. Desviación anotada: `OwnReservationConsumed` deja de almacenarse y pasa a derivarse de la tabla de consumos (P-01, P-02). | Revisión de A en el PR | PASS | PASS |

\* Desviación registrada en Complexity Tracking: sin validación con operarios ni medición de
rendimiento, HU-03 puede integrarse pero no darse por **aceptada**.

**Revaluación tras Phase 1**: el diseño no introduce otro ejecutor, otro `OperationResult`, otro
bloqueo, otro cálculo de disponible ni otra lectura del pendiente. Los componentes nuevos son el
documento de salida, su tabla de consumos, un lector de uso del artículo exigido por HU-08 y una
consulta exacta del pedido. Sin violaciones nuevas.

## Project Structure

### Documentation (this feature)

```text
specs/005-registrar-salida-mercancia/
├── spec.md
├── plan.md                  # este documento
├── research.md              # D-01…D-15
├── data-model.md            # 3 tablas, cambios en entidades de HU-05, invariantes, migración
├── quickstart.md            # H-01…H-21, Vitest, E2E-02, fuera de alcance
├── contracts/
│   ├── shipping-api.md      # 8 rutas HTTP
│   └── shipping-ui.md       # /salidas
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks (no creado todavía)
```

### Source Code (repository root)

```text
backend/src/Inventario.Api/
├── Domain/Shipping/Shipment.cs                       # Shipment + ShipmentLine + ShipmentReservationConsumption
├── Domain/Stock/{CustomerOrder,Reservation}.cs       # (B, HU-05) + Ship, DeriveFulfilment, Consume (D-04)
├── Features/Shipping/
│   ├── ShipmentEndpoints.cs                          # AddShipping(), MapShippingEndpoints()
│   ├── ShipmentCommands.cs                           # crear, editar, eliminar (D-02) y confirmar (D-04)
│   ├── ShipmentQueries.cs                            # detalle, contexto con review y lista del pedido (D-12)
│   ├── ShipmentDtos.cs
│   ├── ShippingDocuments.cs                          # tipo canónico "shipment" (D-03)
│   └── ShipmentArticleUsage.cs                       # contributor de uso + ShipmentSchema (D-09)
├── Features/Stock/CustomerOrders/CustomerOrderQueries.cs   # (B, HU-05) + LookupAsync (D-11)
├── Features/Stock/StockEndpoints.cs                  # (B, HU-05) + GET /customer-orders/lookup
├── Features/Stock/Core/StockArticleUsage.cs          # (B, H0) AddStockCore registra el contributor de HU-03
├── Infrastructure/Persistence/Configurations/{Shipment,ShipmentLine,ShipmentReservationConsumption}Configuration.cs
├── Infrastructure/Persistence/Migrations/<timestamp>_HU03_Shipments.cs
└── Program.cs                                        # (común) AddShipping() y api.MapShippingEndpoints()

backend/tests/Inventario.IntegrationTests/
├── Shipping/
│   ├── Hu03DomainTests.cs, Hu03SchemaTests.cs        # H-01, H-02
│   ├── ShippingApiSupport.cs                         # sesión, pedidos, reservas y salidas reales
│   ├── CustomerOrderLookupTests.cs                   # H-03
│   ├── ShipmentDraftTests.cs                         # H-04…H-07, carreras de INT-HU03-01
│   ├── ShipmentConfirmTests.cs                       # H-08…H-15, H-18, H-20, H-21
│   ├── ShipmentConcurrencyTests.cs                   # H-16, H-17 con LockBarrier, ambos órdenes
│   └── ShipmentReliabilityTests.cs                   # H-19 (V-08)
└── Stock/Hu05SchemaTests.cs                          # (B, HU-05) conjunto exacto + ShipmentLine

frontend/src/app/
├── operation-areas.ts                                # (B, H0) salidas → Inventory.Read
├── core/operations/pending-operation.store.ts        # (A) mutationPermission + APP_ROUTE (P-06)
├── core/operations/operation-status.component.ts     # (A) texto del enlace (P-06)
└── features/shipping/
    ├── shipping.routes.ts                            # '', pedidos/:orderId, pedidos/:orderId/nueva, :id
    ├── data/{shipping.models,shipments-api.service}.ts
    ├── data/{pending-operation-hu03,operation-status-hu03}.spec.ts   # pruebas de los cambios en archivos de A
    ├── order-lookup.page.ts
    ├── order-shipments.page.ts
    ├── shipment-form.page.ts                         # nueva y borrador (propuesta D-13)
    └── shipment-review-dialog.ts
frontend/e2e/shipping.spec.ts                         # E2E-02
```

**Structure Decision**: estructura del plan global, con `Features/Shipping` como dice su tabla de
áreas. Archivos de otros dueños: los de A, en P-06 (frontend) y como posibles ajustes de sus
fixtures si E2E-02 lo requiere (se avisará antes); los comunes, `Program.cs`. Archivos de B fuera de
la feature: dominio, consultas y endpoints de HU-05, `StockArticleUsage.cs` (H0), `Hu05SchemaTests`
y `operation-areas.ts` (H0). No se tocan `StockCore`, `InventoryOperationExecutor`,
`InventoryLocks`, `OperationResult`, `EntityTags` ni `ICustomerOrderReader`.

## Diseño por capas

1. **Dominio**: `Shipment` aplica reglas locales (cantidades, un bucket por línea de pedido,
   `Draft → Confirmed`, inmutable al confirmar). Los métodos nuevos de HU-05 (`Consume`, `Ship`,
   `DeriveFulfilment`) aplican solo reglas locales; saldos, pendiente y reservas ajenas se deciden en
   el comando tras los bloqueos.
2. **Comandos**: `OperationRequest` con método, recurso canónico, `Shipping.Record` y el DTO HTTP
   normalizado (precedente HU-05 D-05). Bloqueos según D-03. Confirmar según D-04.
3. **Consultas**: `AsNoTracking` en `SNAPSHOT`; detalle con datos propios y `ETag`; contexto sin
   `ETag` con `shipmentVersion` y `orderVersion`; lista paginada con `Paging` (D-12).
4. **HTTP**: lecturas con `Inventory.Read`; escrituras con `Shipping.Record`; antiforgery del grupo
   `/api/v1` y `LocationEndpoints.Key()` para la clave.
5. **Frontend**: servicios tipados; `apiProblem()`; `MasterDataMutationsService` /
   `PendingOperationStore` con `appRoute`; `OperationStatusComponent` para el estado 7; foco al
   primer error (D-14).

## Dependencias y riesgos

| Dependencia o riesgo | Efecto | Mitigación |
|----------------------|--------|------------|
| PR #9 (HU-05) sin fusionar | HU-03 se apoya en su código y su migración; si la revisión de A cambia algo, cambia la base. | Rama creada sobre la de HU-05; migración `HU03_Shipments` local y regenerable (D-10). |
| Revisión de A de P-01…P-07 | Bloquea la fusión, no la implementación. | Cambios aditivos; si A rechaza la tabla de consumos (P-02), queda la invariante agregada (D-08, alternativa). |
| Triggers en tablas gestionadas por EF (D-06) | EF 10 exige declarar `HasTrigger` en la tabla para no usar `OUTPUT` sin `INTO`; si se omite, las escrituras fallan. | Declarar `HasTrigger` en las tres configuraciones (salida, líneas y consumos); H-02 y todas las pruebas de escritura lo cubren. |
| Orden de reservas a bloquear (D-03) | Una reserva creada entre la lectura previa y el bloqueo quedaría sin `reservation:{id}`. | `orderVersion` bajo bloqueo: cualquier reserva nueva renueva el pedido y da 412 antes de decidir; H-17 lo prueba. |
| HU-04 conteos | `CountScopeFrozen` al confirmar y corrección de salidas erróneas (Q3). | `IsInOpenCountAsync` devuelve `false` hasta INT-HU04-03; la corrección se traslada a HU-04 (P-07). |
| HU-06 alertas | La regla común reevalúa el mínimo tras cada salida. | Sin alertas integradas no hay nada que evaluar; se engancha bajo `article:{id}` cuando exista. |
| E2E-02 necesita reservas | Cuenta `responsable` ya aprovisionada por HU-05 (`ef296cc`). | Reutilizarla; sin cambios en fixtures de A previstos. |

## Iteración del plan (2026-11-21): consumos tras confirmar

A, que revisa P-01 y P-02, pidió que el plan explique cómo se impide insertar **nuevas** filas de
consumo tras confirmar la salida. La revisión de tasks.md detectó que un trigger de `INSERT` que
rechazara con la cabecera `Confirmed` rompería la propia confirmación, porque EF no garantiza el
orden entre el `UPDATE` de la cabecera y los `INSERT` de consumos.

Mecanismo fijado ([research D-06](research.md#d-06-protección-sql-de-la-salida-confirmada-triggers-decisión-de-b)):

- **Cabecera**: ya guarda la operación que la confirma en `Shipments.OperationId`, inmutable por
  el trigger de D-06. No se añade columna.
- **Consumos**: cada fila de consumo lleva `OperationId`, el de la operación que la crea.
- **Trigger nuevo**: `TR_ShipmentReservationConsumptions_ConfirmingOperation` (`AFTER INSERT`)
  rechaza la fila si la cabecera ya está `Confirmed` con otro `OperationId`.
- **Complemento**: `TR_Shipments_ConfirmedImmutable` rechaza confirmar si ya hay consumos de otra
  operación.
- **EF**: `HasTrigger` en `ShipmentReservationConsumptionConfiguration`.

Dentro de la confirmación funciona en cualquier orden. Invariante 7 en [data-model](data-model.md#invariantes-comprobables-quickstart-h-14);
prueba en [quickstart H-02](quickstart.md#pruebas-automatizadas-backend). Es parte de P-02.

## Cambios al modelo común pendientes de A

Ningún documento ni archivo común o de A se edita en esta rama sin aviso y revisión de A en el PR
(AGENTS.md; lección 9).

| ID | Documento o archivo (dueño) | Cambio propuesto | Motivo |
|----|-----------------------------|------------------|--------|
| P-01 | `data-model.md` § Shipment y ShipmentLine (común) | Borrador eliminable (Q1); confirmada inmutable, sin reversión en HU-03 (Q3); `OperationId` obligatorio solo en `Confirmed`; único `(ShipmentId, CustomerOrderLineId, LocationId, LotId)`; `OwnReservationConsumed` derivado de los consumos, no almacenado. | Spec FR-002, FR-004, FR-013; research D-06, D-08. |
| P-02 | `data-model.md`, nueva entidad (común) | `ShipmentReservationConsumption(Id, ShipmentLineId, ReservationAllocationId, Quantity > 0, OperationId)`, append-only. Ningún consumo nuevo tras confirmar: `OperationId` de la fila = `Shipments.OperationId`, impuesto por trigger en cualquier orden de EF (iteración 2026-11-21, a petición de A). | Constitución IV: qué reserva se consumió. Decisión de B (D-08). |
| P-03 | `contracts/api.md`, tabla «HU-03 y HU-05» (común) | Añadir `DELETE /shipments/{id}`; `GET /shipments/{id}` con solo datos propios y `ETag`; `GET /shipments/{id}/context` sin `ETag`; `GET /shipments?customerOrderId=`; cuerpo `orderVersion` y respuesta sin `ETag` en `/confirm`. | Q1, FR-012, FR-018; lección 1; D-05, D-12. |
| P-04 | `contracts/api.md`, nueva fila (común) | `GET /customer-orders/lookup?externalReference=` (Inventory.Read), exacta y sin lista. | FR-001a; D-11. Sustituye para HU-03 a P-04 de HU-05 (búsqueda por prefijo), que sigue sin implementar. |
| P-05 | `contracts/stock-operations.md` § Alcance y disciplina de escritura (común) | Tipo `shipment` y orden `customer-order-reference < customer-order < reservation-release < reservation < shipment`; reservas a bloquear leídas antes y `orderVersion` revalidado bajo bloqueo; motivo, soporte y contraparte de la salida. | D-03, D-05, D-07. Amplía P-06 de HU-05. |
| P-06 | `frontend/src/app/core/operations/pending-operation.store.ts` y `operation-status.component.ts` (A) | `mutationPermission()`: `/api/v1/shipments`, `/api/v1/shipments/{id}` y `/api/v1/shipments/{id}/confirm` → `Shipping.Record`. `APP_ROUTE`: añadir `^/salidas/(pedidos/{uuid}|{uuid})$`. Enlace «Abrir salida» o «Abrir pedido» según la ruta. | Lección 6; D-14. Mismo patrón que `e491a28`, `f867b61` y `013d429` de HU-05, cada cambio en su commit. |
| P-07 | `specs/001-reparto-specs-inventario/integration-tasks.md` (coordinación A+B) | Nueva tarea receptora de HU-04: corregir una salida errónea mediante ajuste trazable (Q3). Marcar INT-HU03-01/02 solo con implementación y evidencia. | Spec «Alcance y dependencias»; AGENTS.md. |
| P-08 | `quickstart.md` común | Evidencia de HU-03 (V-04, V-05 extendido, V-07, V-08) al ejecutar la matriz H. | Como P-08 de HU-05. |

### Archivos de B fuera de la feature que se tocan

- `Domain/Stock/CustomerOrder.cs` y `Reservation.cs` (HU-05): métodos `Ship`, `DeriveFulfilment` y
  `Consume` (D-04). El contrato `hu03-integration.md` no cambia.
- `Features/Stock/CustomerOrders/CustomerOrderQueries.cs` y `Features/Stock/StockEndpoints.cs`
  (HU-05): búsqueda exacta (D-11).
- `Features/Stock/Core/StockArticleUsage.cs` (H0): registro del contributor de HU-03 en
  `AddStockCore` (D-09).
- `backend/tests/.../Stock/Hu05SchemaTests.cs` (HU-05): conjunto exacto de tipos con FK a `Article`.
- `frontend/src/app/operation-areas.ts` (H0): área `salidas` con `Inventory.Read`.
- `frontend/src/app/features/shipping/shipping.routes.ts` (H0): sustituye el placeholder.
- `Program.cs` (común): `AddShipping()` y `MapShippingEndpoints()`, como `1d02e97` de HU-05.

## Complexity Tracking

| Desviación | Por qué se acepta para esta entrega | Alternativa más simple descartada | Resolución y responsable |
|------------|-------------------------------------|-----------------------------------|--------------------------|
| SC-007 sin validación con operarios (constitución III, V-13). | Requiere sesiones con operarios. HU-03 se integra, pero queda **sin aceptar**. | Validar con el equipo, que no cumple el principio III. | Persona B, antes de la aceptación, con el guion del [quickstart](quickstart.md#fuera-de-alcance-de-esta-entrega). |
| SC-006 sin medición de rendimiento. | Necesita carga y datos sintéticos. | Medir con datos mínimos, no representativos. | Persona B, antes de la aceptación. |
| Triggers de inmutabilidad en lugar de `DENY DELETE` (D-06). | Q1 exige borrar borradores; un `DENY` no distingue estados. | Defensa solo en la aplicación, más débil; tablas de borrador separadas, que cambian el modelo común. | Decisión de B (2026-11-20); P-01. |
| `OwnReservationConsumed` derivado, no almacenado (D-06, D-08). | Evita modificar líneas al confirmar y depender del orden de sentencias de EF bajo el trigger. | Almacenarlo y confiar en el orden de EF, frágil. | P-01, revisión de A. |
| Consumos ligados a la operación que confirma: tercer trigger y columna `OperationId` en consumos (D-06, iteración 2026-11-21). | A pide que la base de datos impida insertar consumos tras confirmar; el `DENY` solo cubre `UPDATE`/`DELETE`. | Trigger de `INSERT` contra la cabecera `Confirmed`, que rompe la confirmación según el orden de EF. | Decisión de B (2026-11-21); P-02, revisión de A. |
| Congelación por conteo al confirmar sin prueba. | `IsInOpenCountAsync` devuelve `false` hasta HU-04. | — | INT-HU04-03. |
| Corrección de salidas confirmadas fuera de HU-03 (Q3). | Clarificación de B. | Reversión vinculada en HU-03, que amplía el alcance. | HU-04, con la tarea receptora de P-07. |
