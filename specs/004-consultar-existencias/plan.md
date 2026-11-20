# Implementation Plan: HU-05 — Consultar existencias y reservas

**Branch**: `b/hu-05-consultar-existencias` (directorio de feature `specs/004-consultar-existencias`)
| **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)

**Input**: especificación clarificada de [spec.md](spec.md) (sesión 2026-11-19).
**Responsable**: Persona B · **Hito**: H2 ([plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación)).

**Base normativa y acuerdos consumidos** (se referencian, no se copian):
[constitución v1.0.0](../../.specify/memory/constitution.md) ·
[plan global](../../.specify/memory/project-plan.md) ·
[research común](../../.specify/memory/project/research.md) (R-02, R-03, R-04, R-05, R-06, R-08, R-10, R-11) ·
[modelo común](../../.specify/memory/project/data-model.md#compromisos-y-salidas--persona-b--hu-03-hu-05) ·
[contrato HTTP](../../.specify/memory/project/contracts/api.md#hu-03-y-hu-05--b-pedidos-de-cliente-reservas-y-salidas) ·
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md) ·
[contrato UI](../../.specify/memory/project/contracts/ui.md) ·
[guía común](../../.specify/memory/project/quickstart.md).

Artefactos de esta feature: [research.md](research.md) (D-01…D-17) · [data-model.md](data-model.md) ·
[contracts/stock-api.md](contracts/stock-api.md) · [contracts/stock-ui.md](contracts/stock-ui.md) ·
[contracts/hu03-integration.md](contracts/hu03-integration.md) · [quickstart.md](quickstart.md).

## Summary

HU-05 añade la consulta de existencias de un artículo (físico, reservado y disponible por
ubicación y lote, con totales del filtro completo), el pedido de cliente mínimo (alta y consulta)
y los comandos de reservar y liberar.

**Backend**: es una feature vertical en `Features/Stock`.

- Los comandos se ejecutan con el `InventoryOperationExecutor` de A (permiso, idempotencia, replay,
  transacción única y `OperationResult`).
- Declaran sus bloqueos con `InventoryLocks` en el orden documentos → ubicaciones → artículos.
- Delegan los efectos de stock en `StockCore` (`Reserve` y `Release`), sin otro cálculo de
  disponible.
- Los errores siguen el patrón `InventoryProblemException` y las precondiciones el de `EntityTags`.
- Una migración aditiva `HU05_CustomerOrdersReservations` crea las cuatro tablas que ya define el
  modelo común.
- Las consultas comparten con HU-02 un único lector del agregado por artículo.

**Frontend**: sustituye el placeholder de `/existencias` sobre el layout, `SessionService`,
`permissionGuard` y `apiProblem()` de A.

**HU-03**: dispone de un contrato de integración para leer el pedido, el pendiente por línea y el
remanente reservado por bucket.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (minimal APIs); TypeScript 6.0 / Angular 22.2, según el
[plan global](../../.specify/memory/project-plan.md#technical-context).

**Primary Dependencies**: EF Core 10 + SqlServer, ASP.NET Core Identity y PrimeNG 22.1. Sin
dependencias nuevas. El UUID v5 de D-04 se calcula con `System.Security.Cryptography`, sin paquete.

**Storage**: SQL Server 2025 con un único `InventoryDbContext`.

- Tablas nuevas: `CustomerOrders`, `CustomerOrderLines`, `Reservations` y `ReservationAllocations`.
- Se consumen sin cambios: `Articles`, `Locations`, `Lots`, `StockBalances`, `StockMovements`,
  `StockEntries` y `OperationResults`.
- La cadena parte de `20261119163347_HU02_LocationCharacters`, la última integrada.

**Testing**: xUnit + `IdentityFixture` contra SQL Server real, reutilizando `StockTestHarness` y
`FailAfterSaveInterceptor`, con la receta Docker del [quickstart](quickstart.md#pruebas-automatizadas-backend)
porque no hay SDK local. Vitest/TestBed. Playwright solo para E2E-01.

**Target Platform**: la del plan global.

**Project Type**: aplicación web (monolito modular `backend/` + `frontend/`).

**Performance Goals**: SC-006 (p95 ≤ 2 s) queda fuera de esta entrega (Complexity Tracking). Para
no impedir la medición posterior, las consultas son paginadas y usan los índices
`StockBalances (ArticleId, …)` e `IX_ReservationAllocations_Bucket`.

**Constraints**:

- Migración solo aditiva.
- Permiso en cada endpoint y otra vez en el ejecutor.
- Existencias leídas solo de `StockBalances`, en SNAPSHOT.
- If-Match e Idempotency-Key en reservar y liberar; Idempotency-Key en el alta.
- Sin edición ni cancelación de pedidos.
- Sin `SaveChanges` dentro de `change` en comandos que escriben diario (FK de
  `StockMovements.OperationId`).

**Scale/Scope**: 8 endpoints, 4 pantallas, casos S-01…S-19 y un E2E. Sin incógnitas abiertas: ver
[research.md](research.md).

## Constitution Check

*GATE previo a Phase 0 y repetido tras Phase 1. PASS = conformidad del diseño, no evidencia de
implementación. La evidencia se recogerá con los casos del [quickstart](quickstart.md).*

| Principio | Compromiso de diseño en HU-05 | Evidencia prevista | Antes | Después |
|-----------|-------------------------------|--------------------|-------|---------|
| I. Correspondencia | Las consultas leen solo `StockBalances` confirmados en SNAPSHOT (D-02). Reservar y liberar no tocan el físico: lo garantiza `StockCore` (`Reserve` y `Release` solo mueven el reservado). | S-01, S-04, S-10, S-14 | PASS | PASS |
| II. Ubicación precisa | Cifras por ubicación y lote; cada asignación apunta a un bucket concreto con FK a `Locations` y FK compuesta a `Lots` (data-model). Sin buckets duplicados (P-02). | S-01, S-02, S-11 | PASS | PASS |
| III. Uso intuitivo | Español, unidades visibles, «sin lote», «Inactivo» en texto, revisión antes de confirmar y errores por campo conservando datos (contrato UI). La validación con trabajadores (SC-007) queda fuera de esta entrega. | Unitarias frontend, E2E-01 | PASS* | PASS* |
| IV. Trazabilidad | Cada reserva y liberación es un movimiento del diario append-only con actor de sesión, instantes, motivo y documento (D-05, D-06). `ReleasedQuantity` solo crece. El alta queda en `OperationResult`. | S-07, S-14 | PASS | PASS |
| V. Integridad | Ejecutor común; bloqueos documento → ubicación → artículo antes de leer; If-Match del pedido y de la reserva con versión tocada (D-07); idempotencia y unicidad del diario; `DENY DELETE`; atomicidad ante fallo; concurrencia en SQL real. | S-10…S-16 | PASS | PASS |
| Revisión y gobernanza | Cambios al modelo común y a archivos de A identificados como diff, sin aplicarlos (sección siguiente). **Desviación anotada**: el estado `Cancelled` del modelo común y el «comando de modificación del pedido» de `stock-operations.md` no se implementan en HU-05 (clarificación 3, spec «Alcance y dependencias»). No se redefinen ni se eliminan. | Revisión de A en el PR | PASS | PASS |

\* Desviación registrada en Complexity Tracking: la entrega no aporta la validación con
trabajadores ni la medición de rendimiento. HU-05 puede integrarse, pero no darse por **aceptada**
hasta aportarlas.

**Revaluación tras Phase 1**: el diseño no introduce otro ejecutor, otro `OperationResult`, otro
bloqueo ni otro cálculo de disponible. Los dos componentes nuevos (`StockBalanceReader` y
`CustomerOrderArticleUsageContributor`) son una consulta y un lector de uso exigidos por los
controles existentes. Sin violaciones nuevas.

## Project Structure

### Documentation (this feature)

```text
specs/004-consultar-existencias/
├── spec.md
├── plan.md                     # este documento
├── research.md                 # D-01…D-17
├── data-model.md               # 4 tablas, invariantes, hechos del diario, migración
├── quickstart.md               # S-01…S-19, E2E-01, fuera de alcance, evidencia
├── contracts/
│   ├── stock-api.md            # 8 rutas HTTP
│   ├── stock-ui.md             # /existencias y pedidos
│   └── hu03-integration.md     # lectura del pedido para HU-03
├── checklists/requirements.md
└── tasks.md                    # /speckit-tasks (no generado aquí)
```

### Source Code (repository root)

```text
backend/src/Inventario.Api/
├── Domain/Stock/
│   ├── CustomerOrder.cs                        # CustomerOrder + CustomerOrderLine (Create, Touch)
│   └── Reservation.cs                          # Reservation + ReservationAllocation (Release, DeriveState, Touch)
├── Features/Stock/
│   ├── StockEndpoints.cs                       # AddStock(), MapStockEndpoints(), Key()
│   ├── StockDocuments.cs                       # tipos canónicos y UUID v5 de la referencia (D-04)
│   ├── Queries/
│   │   ├── StockBalanceReader.cs               # agregado único por artículo (D-02)
│   │   ├── StockQueries.cs                     # GET /stock, GET /stock/reservations
│   │   └── StockDtos.cs
│   ├── CustomerOrders/
│   │   ├── CustomerOrderCommands.cs            # alta (D-10)
│   │   ├── CustomerOrderQueries.cs             # detalle y búsqueda (D-11)
│   │   ├── ICustomerOrderReader.cs             # contrato HU-03
│   │   ├── CustomerOrderArticleUsage.cs        # contributor de uso del artículo (D-13)
│   │   └── CustomerOrderDtos.cs
│   ├── Reservations/
│   │   ├── ReservationCommands.cs              # reservar (D-05, D-09) y liberar (D-06)
│   │   ├── ReservationQueries.cs
│   │   └── ReservationDtos.cs
│   └── Testing/StockSeedingEndpoints.cs        # solo con Testing:EnableStockSeeding (D-17)
├── Features/Locations/Queries/LocationQueries.cs   # (B, HU-02) ArticleLocationsAsync delega en StockBalanceReader
├── Infrastructure/
│   ├── Http/MasterDataProblemMapper.cs             # (A) + UX_CustomerOrders_ExternalReference → DuplicateCode
│   ├── Persistence/Configurations/{CustomerOrder,CustomerOrderLine,Reservation,ReservationAllocation}Configuration.cs
│   └── Persistence/Migrations/<ts>_HU05_CustomerOrdersReservations.cs
└── Program.cs                                      # (común) AddStock(); api.MapStockEndpoints()

backend/tests/Inventario.IntegrationTests/Stock/
├── StockQueryTests.cs              # S-01…S-07
├── CustomerOrderTests.cs           # S-08, S-09, S-19
├── ReservationTests.cs             # S-10, S-11
├── ReleaseTests.cs                 # S-14, S-15
├── ReservationConcurrencyTests.cs  # S-12, S-13
├── StockReliabilityTests.cs        # S-16, S-17
└── CustomerOrderReaderTests.cs     # S-18

frontend/src/app/
├── core/operations/pending-operation.store.ts           # (A) mutationPermission() + rutas de HU-05 (D-15)
├── core/operations/operation-status.component.ts        # (A) route() hacia /existencias/pedidos (D-15)
└── features/stock/
    ├── stock.routes.ts                     # sustituye el placeholder
    ├── data/{stock.models,stock-api.service,customer-orders-api.service}.ts
    ├── query/stock-query.page.ts           # existencias y pestaña de reservas
    ├── orders/{order-list.page,order-form.page,order-detail.page}.ts
    └── orders/{reserve-dialog,release-dialog}.ts
frontend/e2e/stock.spec.ts                  # E2E-01
docker-compose.yml                          # (común) pasa TESTING_ENABLE_STOCK_SEEDING, false por defecto
```

La preparación E2E de D-17 indica `SupportNotApplicableReason` y `CounterpartyNotApplicableReason`
(constitución IV; regla de contraparte del PR #7).

**Structure Decision**: estructura del plan global, con `Features/Stock` como dice su tabla de
áreas. Archivos de otros dueños que se tocan, todos con cambios aditivos señalados para revisión
en el PR:

- De A:
  - `MasterDataProblemMapper.cs` (una rama);
  - en frontend, `mutationPermission()` de `pending-operation.store.ts` y `route()` de
    `operation-status.component.ts`, ampliados con los recursos de HU-05 (D-15).
- Comunes: `Program.cs` (registro) y `docker-compose.yml` (variable con valor `false`).
- De B: `LocationQueries.cs` de HU-02 (delegación sin cambio de contrato). No se trasladan archivos
  de frontend de HU-02: la intención usa el transporte común (D-15).

No se tocan `StockCore`, `InventoryOperationExecutor`, `InventoryLocks`, `OperationResult` ni
`EntityTags`.

## Diseño por capas

1. **Dominio**: `CustomerOrder` y `Reservation` aplican solo reglas locales (cantidades,
   remanente ≥ 0, estado derivado y `Touch()`). Las reglas que dependen de saldos, del pendiente o
   de otras reservas se evalúan en el comando tras los bloqueos.
2. **Comandos**: cada uno construye un `OperationRequest` con método, recurso canónico, permiso y
   DTO normalizado (D-05) y llama a `executor.ExecuteAsync`. Dentro de `change`:

   | Comando | Documentos (una llamada) | Ubicaciones | Artículos | Después de bloquear |
   |---------|--------------------------|-------------|-----------|---------------------|
   | Alta de pedido | `customer-order-reference:{uuid5}`, `customer-order:{nuevo}` | — | los de las líneas | duplicado → artículos activos → `ArticleReferenceTracker.MarkAsync` → `Add` → `SaveChanges` → 201 + `ETag` |
   | Reservar | `customer-order:{o}`, `reservation:{nuevo}` | las de las asignaciones | el de la línea | `RequireMatch(order)` → `Open` → lotes → conteo → pendiente → `StockCore.Prepare(Reserve)` + `ApplyAsync` → `Add` reserva → `order.Touch()` → 201 |
   | Liberar | `customer-order:{o}`, `reservation-release:{rel}`, `reservation:{r}` | las de las asignaciones | el de la reserva | `RequireMatch(reservation)` → remanente por asignación → `StockCore.Prepare(Release)` + `ApplyAsync` → `ReleasedQuantity` y estado → `Touch()` reserva y pedido → 200 |

   Las relaciones inmutables (línea → pedido, reserva → línea, asignación → bucket, lote →
   artículo) se leen antes de bloquear solo para saber qué recursos pedir. Todo lo decisorio se
   relee después.
3. **Consultas**: `StockBalanceReader` y las demás proyecciones usan AsNoTracking en una transacción
   SNAPSHOT (página, totales y `asOf`), con la paginación de `Paging` (422 fuera de rango), como
   HU-02.
4. **HTTP**: `GET` con `Inventory.Read`; `POST /customer-orders` con `Shipping.Record`; reservar y
   liberar con `Reservations.Manage`. Antiforgery del grupo `/api/v1` y `Key()` como en HU-02.
5. **Frontend**: servicios tipados; `apiProblem()`; `permissionGuard` en `pedidos/nuevo`;
   `SessionService.has('Reservations.Manage')` para los diálogos. La intención idempotente usa el
   transporte común `MasterDataMutationsService` / `PendingOperationStore`, con las correcciones
   R1–R4 del PR #6, y `OperationStatusComponent` para el estado 7 (D-15).

## Dependencias y riesgos

| Dependencia | Efecto | Mitigación |
|-------------|--------|------------|
| Revisión de A de los diffs P-01…P-08 y de los cambios en sus archivos | Bloquea la fusión, no la implementación. | Cambios aditivos; si A rechaza P-04 (búsqueda), la UI navega solo desde el alta y los enlaces de «Reservas». |
| HU-01 recepciones por HTTP | Sin existencias reales para el vídeo. | Endpoint de preparación desactivado por defecto (D-17); se retira cuando exista HU-01. |
| HU-04 conteos | `CountScopeFrozen` al reservar. | `ILocationUsage.IsInOpenCountAsync` devuelve `false` hasta HU-04. Liberar nunca lo comprueba. |
| HU-03 salidas | Consumo de reservas y servido. | Contrato [hu03-integration.md](contracts/hu03-integration.md) y prueba S-18. |
| HU-06 alertas | La regla común reevalúa el mínimo al reservar o liberar. | Sin alertas integradas no hay nada que evaluar. Cuando HU-06 exista, se engancha en el mismo commit bajo `article:{id}`, como prevé el protocolo. |
| Otra migración integrada antes | Snapshot obsoleto. | Regenerar `HU05_CustomerOrdersReservations` local (plan global §5). |

## Cambios al modelo común pendientes de A

Ningún documento común se edita en esta rama hasta la revisión de A en el PR, según AGENTS.md.

### P-01 · `data-model.md` — `### CustomerOrder y CustomerOrderLine`

```diff
 Referencia mínima local del pedido: Id, ExternalReference única, CustomerReference,
 State (Open/Fulfilled/Cancelled), Version; líneas con Id, CustomerOrderId, ArticleId,
 OrderedQuantity > 0 y ShippedQuantity entre cero y lo pedido.
+Una línea por artículo: (CustomerOrderId, ArticleId) es único (HU-05, clarificación 1).
+ExternalReference se compara sin distinguir mayúsculas ni espacios extremos.
+HU-05 crea y consulta el pedido; no lo edita ni lo cancela: Cancelled y el comando de
+modificación quedan definidos para una historia posterior, sin uso en esta entrega.
+Cada cambio de reservas o servido toca la cabecera para renovar su rowversion (ETag).
 No incluye precios comerciales, facturación ni integración ERP automática.
```

### P-02 · `data-model.md` — `### Reservation y ReservationAllocation`

```diff
 Asignación: Id, ReservationId, ArticleId, LocationId, LotId, AllocatedQuantity,
 ConsumedQuantity, ReleasedQuantity. Remaining = Allocated − Consumed − Released ≥ 0.
+Una asignación por bucket y reserva: (ReservationId, ArticleId, LocationId, LotId) único;
+LotId con FK compuesta (ArticleId, LotId) como StockBalance. Remaining es columna calculada.
+Pedidos, líneas, reservas y asignaciones no se borran (DENY DELETE a inventory_runtime).
+Cada liberación toca la cabecera de la reserva (ETag) y la del pedido.
```

### P-03 · `contracts/api.md` — tabla «HU-03 y HU-05» y «HU-05 — B: consultas»

```diff
-| POST /reservations/{id}/release | allocations con id y quantity; motivo; Reservations.Manage. No libera más del remanente. |
+| POST /reservations/{id}/release | releaseId, reason (texto libre obligatorio), allocations con allocationId y quantity; If-Match de la reserva; Reservations.Manage. No libera más del remanente. 200 sin ETag. |
```

```diff
-GET /stock?articleId=...&locationId=...&lotId=...&page=1&pageSize=25: exige articleId;
+GET /stock?articleId=...&locationId=...&lotId=...&includeEmpty=false&page=1&pageSize=25: exige articleId;
 los demás filtros son opcionales. Inventory.Read. Cada fila identifica artículo, ubicación
 y lote y muestra physical, reserved, available. Totales consistentes del filtro completo.
+Por defecto solo filas con physical > 0; includeEmpty=true muestra también las de cero.
+GET /articles/{id}/locations (HU-02) usa el mismo agregado.
```

Además: `POST /reservations` responde 201 sin `ETag`, porque el diario impide guardar antes del
resultado común (research D-08).

### P-04 · `contracts/api.md` — nueva fila

```diff
+| GET /customer-orders | search (prefijo de externalReference), state (Open/Fulfilled/all), paginado; Inventory.Read. |
```

### P-05 · `contracts/api.md` — fila 409 de errores

```diff
-| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict. |
+| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict, CustomerOrderNotOpen. |
```

(Si la fila ya incluye los códigos de HU-02 cuando se integre, se añade a continuación.)

### P-06 · `contracts/stock-operations.md` — `## Alcance y disciplina de escritura`

```diff
 - Un comando de modificación del pedido de cliente declara primero las reservas que va
   a liberar y sus ubicaciones, conservando la jerarquía documento → ubicación → artículo.
+- Tipos de documento de HU-05: customer-order-reference (UUID v5 de la referencia
+  normalizada), customer-order, reservation-release y reservation; orden ordinal
+  customer-order-reference < customer-order < reservation-release < reservation, pedidos
+  en una sola llamada. Todo escritor de reservas o servido de un pedido (HU-05, HU-03)
+  bloquea customer-order:{id}.
+- Reservar usa reservation/{reservationId} como documento; cada liberación usa
+  reservation-release/{releaseId}, con SupportReference reservation:{reservationId}.
+- Cuando el StockCommand depende de lecturas bajo bloqueo, la OperationRequest lleva el
+  DTO HTTP normalizado y el StockCommand se prepara dentro de change (HU-05 D-05).
```

Alternativa abierta a A: una tabla `ReservationRelease(Id, ReservationId)` en lugar de
`SupportReference` (research D-06). Esta rama no la aplica.

### P-07 · Frontera HU-03/HU-05 — `research.md` R-10 y `specs/001-reparto-specs-inventario/plan.md`

```diff
-completas se concretan en sus historias. CustomerOrder mínimo queda a cargo de B en HU-03.
+completas se concretan en sus historias. CustomerOrder mínimo queda a cargo de B en HU-05
+(alta y consulta); HU-03 lo consume según specs/004-consultar-existencias/contracts/hu03-integration.md.
```

Y el mismo cambio en la línea 114 del plan de coordinación (decisión de B ya recogida en la spec).

### P-08 · `quickstart.md` común

Añadir la evidencia de HU-05 (fecha, entorno, número de pruebas y resultado) cuando se ejecute
la [matriz S](quickstart.md#pruebas-automatizadas-backend).

### Cambios en archivos de A y comunes, incluidos en el PR para su revisión

- `MasterDataProblemMapper`: 2601/2627 de `UX_CustomerOrders_ExternalReference` → 409
  `DuplicateCode` con `errors.externalReference` (defensa; el caso normal lo resuelve el bloqueo
  de la referencia).
- `Program.cs`: `AddStock()` y `api.MapStockEndpoints()`. El endpoint de D-17 solo se mapea con
  `Testing:EnableStockSeeding`.
- `docker-compose.yml`: `Testing__EnableStockSeeding: ${TESTING_ENABLE_STOCK_SEEDING:-false}`
  en el servicio `app`.
- `frontend/src/app/core/operations/pending-operation.store.ts` (A): en `mutationPermission()`,
  `/api/v1/customer-orders` → `Shipping.Record`, y `/api/v1/reservations` y
  `/api/v1/reservations/{id}/release` → `Reservations.Manage`.
- `frontend/src/app/core/operations/operation-status.component.ts` (A): `route()` lleva esos
  recursos a `/existencias/pedidos/…`.

### Estado de la revisión de A

**PENDIENTE** (2026-11-19). No bloquea la implementación de HU-05 ni las pruebas S. Bloquea la
fusión.

## Complexity Tracking

| Desviación | Por qué se acepta para esta entrega | Alternativa más simple descartada | Resolución y responsable |
|------------|-------------------------------------|-----------------------------------|--------------------------|
| SC-007 sin validación con trabajadores (constitución III y «Revisión y criterios de aceptación»). | Requiere sesiones con operarios que no caben en esta entrega. HU-05 se integra, pero queda **sin aceptar**. | Validar con el equipo, que no cumple el principio III. | Persona B, antes de la aceptación, con el guion del [quickstart](quickstart.md#fuera-de-alcance-de-esta-entrega). |
| SC-006 sin medición de rendimiento (p95 ≤ 2 s con 10.000 artículos, 1.000.000 de entradas y 25 sesiones). | Necesita datos sintéticos y carga. El diseño paginado e indexado no lo impide. | Medir con datos mínimos, que no son representativos. | Persona B, antes de la aceptación. |
| `Cancelled` y el comando de modificación del pedido del modelo común, sin implementar. | Clarificación 3: alta y consulta bastan; liberar reservas resuelve compromisos innecesarios. | Implementar la cancelación, que amplía el alcance y las pruebas sin necesidad para HU-03. | Anotado en P-01; una historia posterior lo implementa con el protocolo común. |
| Endpoint de preparación de existencias solo para pruebas (D-17). | El vídeo necesita existencias y HU-01 aún no ofrece recepciones por HTTP. | Escribir saldos directamente, prohibido por la constitución IV y la guía común. | Persona B; se retira cuando HU-01 integre recepciones. |
