# Validación: HU-03 — Registrar la salida de mercancía

**Base**: [guía común](../../.specify/memory/project/quickstart.md) (Compose, SQL de pruebas, datos
comunes §5, V-04, V-05, V-07, V-08, V-12, V-13) y la [guía de HU-05](../004-consultar-existencias/quickstart.md)
(receta Docker, matriz S, cuentas y siembra D-17). Contratos: [API](contracts/shipping-api.md) y
[UI](contracts/shipping-ui.md); reglas en [data-model.md](data-model.md). **Nada ejecutado todavía.**

## Prerrequisitos

1. Rama `b/hu-03-registrar-salida-mercancia` con HU-05 (PR #9) y la base común de A.
2. Migración `HU03_Shipments` aplicada sobre `20261120113458_HU05_CustomerOrdersReservations` y
   `dotnet ef migrations has-pending-model-changes` sin cambios.
3. Datos §5 y los de HU-05: ART-01 (lote L-001, UD sin fracciones), ART-02 sin lote, ubicaciones
   P01-E01-N01 y N02. Existencias con `StockCore` (Receive) o la siembra D-17; pedidos y reservas con
   los comandos reales de HU-05, nunca escribiendo filas.
4. Cuentas de HU-05: operario (`Shipping.Record`, `Inventory.Read`), responsable
   (`Reservations.Manage`, `Inventory.Read`), gestor de maestros (`Inventory.Read`) y administrador.

## Pruebas automatizadas backend

Misma receta Docker que HU-05, con `--filter "FullyQualifiedName~Inventario.IntegrationTests.Shipping"`
para repetir solo las de HU-03. Las previas (HU-02, HU-05, HU-08) deben seguir pasando. Las de
concurrencia se repiten tres veces.

| Caso | Preparación y acción | Resultado esperado | Spec / común |
|------|----------------------|--------------------|--------------|
| H-01 | Dominio: `Consume`, `Ship`, `DeriveFulfilment`, `Shipment` (`Draft → Confirmed`). | Rechazan cantidades no positivas, consumo > remanente, servido > pedido y confirmar dos veces. | D-04 |
| H-02 | Esquema con `inventory_runtime`: actualizar o borrar una salida confirmada y sus líneas; insertar línea en confirmada; actualizar o borrar un consumo; conjunto exacto de tipos con FK a `Article`. **Consumo tardío** (iteración 2026-11-21): tras confirmar la salida S con la operación X, insertar por SQL un consumo de una línea de S con otra operación Y. Insertar en un borrador un consumo con Y y confirmar después con X. En una transacción, confirmar a mano con X en los dos órdenes (`UPDATE` de la cabecera y luego `INSERT` del consumo con X, y al revés). **Consumo tardío con la misma operación** (Convergence C1): tras confirmar S con X, insertar por SQL otro consumo con X; confirmar a mano declarando 1 consumo e insertar 2 (en los dos órdenes). `POST /shipments/{id}/confirm` con reserva. | Triggers rechazan; `DENY` en consumos; el conjunto incluye `ShipmentLine` (`Hu05SchemaTests` ampliado). El consumo con Y tras confirmar falla en la base de datos (`THROW` del trigger) y el número de consumos no cambia. Confirmar sobre un consumo previo con Y se rechaza. Los dos órdenes con X confirman. El consumo con X tras confirmar falla en la base de datos (51305) y el número de consumos no cambia; la confirmación con más consumos de los declarados se rechaza en los dos órdenes (51305 o 51306). La confirmación por la API sigue en 200, sus consumos llevan X y la cabecera declara su número y su suma. **Rojo previo**: antes del trigger, el `INSERT` con Y se admite; antes de C1, el `INSERT` con X también. | D-06, D-08, D-09 |
| H-03 | Lookup: « ped-0042 » para PED-0042; referencia inexistente; vacía. | 200 con id y estado; 404; 422. | FR-001a, US1.11 |
| H-04 | Operario crea borrador de PED-A (ART-01×6) con 6 de N01; consultar `/stock`, pedido y diario. Repetir la clave. | 201 + `ETag`; existencias, servido y diario sin cambios; replay idéntico. | US1.1, FR-004 |
| H-05 | Borradores inválidos: cantidad 0, 1,5 en ART-01, lote ajeno, bucket sin saldo, línea de otro pedido, misma línea/bucket dos veces, Σ > pendiente, fecha futura, pedido no `Open`. | 422 por campo o línea, 409 `InsufficientStock` o `CustomerOrderNotOpen`; nada creado. | US1.6–1.7, FR-002–003, FR-005 |
| H-06 | `PUT` del borrador con `ETag` vigente, con `ETag` viejo y sin `If-Match`; `PUT` y `DELETE` de una confirmada. | 200 + `ETag` nuevo; 412; 428; 409 `DocumentAlreadyConfirmed`. | US1.8, FR-004, FR-013 |
| H-07 | **INT-HU03-01**: `DELETE` del borrador; contributor de uso antes y después; `DELETE` repetido con la misma y con otra clave. **Retirada por edición** (analyze-1 H4): `PUT` que quita la única línea del artículo X y conserva la de Y. | 204; sin filas ni movimientos; el contributor deja de ver la línea de salida (la del pedido sigue bloqueando la definición y el borrado); replay 204; 404. Tras el `PUT`, el contributor no ve X en la salida y sí Y; X sigue bloqueado por la línea de pedido. | US1.9, FR-004 |
| H-08 | Confirmar sin reservas: físico 10, PED-A ×6, salida de 6. | 200; físico 4; servido 6; `Fulfilled`; un movimiento `Shipment` con `ShipAvailable` 6, soporte `PED-A`, contraparte = cliente, sin motivo de no aplicación. | US1.3, FR-008–009, FR-015–016 |
| H-09 | Parcial y dos orígenes: PED-A ×10; salida de 6 desde N01 (4) y N02 (2); después otra de 4. | `Open` con pendiente 4; luego `Fulfilled`. | US1.4–1.5, FR-009 |
| H-10 | **V-04**: físico 10 en N01; reservar 4 para A y 3 para B (HU-05); servir 6 de A; después 4 de A y cualquier cantidad de B por encima de su posible. | Físico 4, reservado 3, disponible 1; reserva de A `Consumed`, consumos 4; B intacta; siguientes 409 `InsufficientStock` con la cantidad posible («Se pueden servir como máximo…», Convergence C2; US2.2 en `H10_us22_…`). | US2.1–2.3, FR-007–008, SC-002 |
| H-11 | **Extensión de V-04 / regla 5**: A con reserva 4 en N01; servir 4 de A desde N02. | 409 `ReservedStockConflict` con la cantidad a liberar; nada cambia. Tras liberar (HU-05), la salida confirma. | US2.4, FR-011 |
| H-12 | **Regla 4 / FR-012**: `ETag` del pedido y de la reserva antes y después de una salida con reserva y de otra sin reserva; reservar y liberar con los `ETag` viejos; confirmar con `orderVersion` viejo. | El pedido cambia en ambas; la reserva solo en la primera; 412 en reservar, liberar y confirmar. | US2.6–2.7, FR-012, SC-005 |
| H-13 | **S-18 completo**: `ICustomerOrderReader` dentro de la confirmación antes y después de servir, con dos buckets y dos reservas de la misma línea. | Pendiente y remanente por bucket coinciden con `GET /customer-orders/{id}`; consumo por Id ascendente. | Contrato HU-03 §5 |
| H-14 | Conciliación tras H-08…H-13: invariantes 1–8 de [data-model.md](data-model.md#invariantes-comprobables-quickstart-h-14). | Todas se cumplen. | FR-017, constitución IV |
| H-15 | **INT-HU03-02**: pedido y reserva de ART-01; desactivar ART-01; preparar y confirmar. | 200; contexto con `isActive = false`; historial y saldos coherentes; alta de pedido nuevo (HU-05) sigue en 409. | US3.1–3.2, FR-014 |
| H-16 | **V-05 extendido** (20 repeticiones, ambos órdenes): físico 10 sin reservas; dos salidas de 8 de pedidos distintos. | Una 200 y otra 409 `InsufficientStock`; físico 2. | US5.4, FR-022, SC-003 |
| H-17 | Carreras con `LockBarrier`, ambos órdenes: dos salidas del mismo pedido que superan el pendiente; salida/reserva y salida/liberación sobre el mismo bucket; salida/desactivación del artículo (INT-HU03-02); borrador (crear y editar)/`PUT` de definición y borrador (crear, editar y eliminar)/`DELETE` del artículo (INT-HU03-01); salida/`PUT` de definición (T011; analyze-1 H4). **Construido** (T011): además, salida de la línea X frente a reserva de la línea Y del mismo pedido, que solo serializa `customer-order` (regla 1). | Nunca servido > pedido, reservado > físico, consumido + liberado > asignado ni físico < 0; perdedores con 409, 412 o 422; artículo con borrador no se borra. | US3.3, US5.5–5.6, FR-022 |
| H-18 | **V-07**: confirmar; repetir con la misma clave; con la misma clave y otro `orderVersion`; con otra clave. | Replay; 409 `IdempotencyConflict`; 409 `DocumentAlreadyConfirmed`; un solo movimiento. | US5.2, FR-021 |
| H-19 | **V-08**: `FailAfterSaveInterceptor` en alta, edición, eliminación y confirmación; repetir con la misma clave. | Ningún efecto parcial ni `OperationResult`; el reintento aplica un solo efecto. | US5.7, FR-020 |
| H-20 | Permisos: sin sesión, sin antiforgery, gestor, comprador y administrador en cada ruta; operario sin `Reservations.Manage` confirma consumiendo reservas. | 401; 400; lectura permitida y 403 en escrituras; 403 al administrador también en lectura; el operario confirma. | US4.3, US5.1, FR-019 |
| H-21 | Lecturas: detalle con líneas por Id y `ETag` estable al renombrar el artículo; contexto refleja el nombre nuevo; `review` con propio, ajeno y posible; lista del pedido paginada. | Como el contrato; `shipmentVersion` = `ETag`. | FR-006, FR-018, D-12 |

`CountScopeFrozen` al confirmar queda **pendiente de INT-HU04-03** (`IsInOpenCountAsync` devuelve
`false` hasta HU-04), como en HU-05.

## Pruebas frontend (Vitest)

Búsqueda (encontrado, inexistente, foco); pedido con salidas y estado servido; propuesta desde
reservas (dos buckets, sin reserva, acotada al pendiente); selector con un bucket posterior al 25;
borrador (guardar, editar, 412, eliminar con confirmación); revisión (cifras de `review`, aviso de
cantidad posible, «Inactivo» sin bloquear); salida confirmada reabierta con su operación; errores con foco al
primer error; `ReservedStockConflict` con enlace al pedido; cambios de archivos de A en specs nuevas
de B (`mutationPermission`, `APP_ROUTE` y enlace «Abrir salida»).

**Construido** (T013–T016): 33 pruebas en 8 specs de `features/shipping` (`shipping.routes`,
`order-lookup.page`, `order-shipments.page`, `shipment-form.page`, `shipment-confirm.page`,
`shipment-review-dialog`, `pending-operation-hu03` y `operation-status-hu03`), con datos comunes en
`testing/shipping-testing.ts`. Vitest completo: 180 pruebas.

## E2E-02 (Playwright)

Responsable reserva 4 de 10 para PED-E2E (HU-05); operario busca « ped-e2e », prepara la salida con
la propuesta (4, del único bucket sembrado) y cambia su cantidad a 6, porque otra fila del mismo
bucket repetiría línea, ubicación y lote (FR-002). Revisa (propio 4, posible 6) y confirma; ve
«Salida registrada», el pedido servido y en `/existencias` físico 4, reservado 0.

## Fuera de alcance de esta entrega

- SC-006 (p95 ≤ 3 s con 25 sesiones): diseño con índices, sin medición.
- SC-007 / V-13: validación con operarios representativos (constitución III). Guion: buscar el
  pedido, preparar con reserva propia y sin ella, revisar y confirmar; registrar participantes por
  rol, tareas, incidencias y correcciones.

## Evidencia

Al ejecutar: fecha, entorno, commit, número de pruebas y resultado por caso H, más la verificación
de la rama (suite backend, `has-pending-model-changes`, Vitest, `tsc`, Playwright).

### Verificación de la rama (T018, 2026-11-21)

**Entorno y versión**:

- Backend: `git archive` de `8275c43` con `mcr.microsoft.com/dotnet/sdk:10.0` sobre `mssql/server`
  (Compose, red `inventario_default`), caché `inventario-nuget`.
- Vitest, `tsc` y build: locales con Node 26.
- Playwright 1.65 + Chromium en el proyecto Compose `inventario-hu08-e2e`, con la imagen construida
  del árbol de `a134668`.
- Windows 11 + Docker Desktop.

| Comprobación | Resultado | Línea base |
|--------------|-----------|------------|
| Suite backend, tres veces seguidas | 287 aprobadas, 1 omitida (V-14), 0 fallidas en las tres | 207/1/0 antes de HU-03 |
| `has-pending-model-changes` | Sin cambios | — |
| Cadena desde cero | Cada fixture crea su base y aplica la cadena completa; revertir a HU-05 y reaplicar comprobado en T004 | — |
| Vitest (`ng test --watch=false`) | 180/180 en 43 archivos | 147 antes de T013 |
| `tsc` de app, specs y E2E y `npm run build` | Sin errores | — |
| Playwright (`--grep-invert SC-005`), una ejecución | 17/17, con E2E-02 | 16/16 |

**Casos H**, todos en verde en las tres ejecuciones:

| Caso | Pruebas |
|------|---------|
| H-01 | `Hu03DomainTests` |
| H-02 | `Hu03SchemaTests`, incluido el consumo tardío y los dos órdenes de confirmación |
| H-03 | `CustomerOrderLookupTests` |
| H-04–H-07 | `ShipmentDraftTests`, con M1, la retirada por edición y las 10 carreras de INT-HU03-01 |
| H-08–H-15, H-18 | `ShipmentConfirmTests`, con las reglas 3–5 y `AssertShipmentInvariantsAsync` (H-14) |
| H-16, H-17 | `ShipmentConcurrencyTests`, 15 pruebas |
| H-19 | `ShipmentReliabilityTests` |
| H-20 | Repartido entre `ShipmentDraftTests.H20_…`, `ShipmentConfirmTests.H20_…` y `CustomerOrderLookupTests` |
| H-21 | `ShipmentQueryTests` |
| Vitest de `/salidas` | 33 pruebas en 8 specs, más 7 de los archivos de A (T013); 40 desde Convergence C2–C4 |
| E2E-02 | `frontend/e2e/shipping.spec.ts` |

**Para T002 (P-08 e INT-HU03-01/02)**:

- **INT-HU03-01**: contrato de escritores en `ShipmentDraftTests`, con crear, editar, retirar y
  borrar, y carreras con definición y borrado en ambos órdenes. Además, la confirmación frente a
  `PUT` de definición y `DELETE` en `ShipmentConcurrencyTests`, y el contributor en `AddStockCore`
  (`Hu03SchemaTests`).
- **INT-HU03-02**: `ShipmentConfirmTests.H15_…` y la carrera con la desactivación en
  `ShipmentConcurrencyTests`.
- **V-04, V-05 extendido, V-07 y V-08**: H-10, H-11, H-16, H-18 y H-19, sobre `8275c43`.

Sigue fuera: SC-006, SC-007 y `CountScopeFrozen` (INT-HU04-03).

### Verificación de la convergencia (C1 y /speckit-converge, 2026-11-21)

Sobre `a6ec4b2`, con el mismo entorno que T018. El detalle está en
[validation/converge-1.md](validation/converge-1.md).

| Comprobación | Resultado | Línea base |
|--------------|-----------|------------|
| Suite backend | 290 aprobadas, 1 omitida (V-14), 0 fallidas | 287/1/0 en T018 |
| `has-pending-model-changes` | Sin cambios | — |
| Vitest | 180/180 en 43 archivos | 180 en T018 |
| Playwright (`--grep-invert SC-005`), una ejecución | 17/17 | 17/17 en T018 |

**Tras Convergence C2–C4**: backend 291/1/0, `has-pending-model-changes` sin cambios, Vitest
187/187 y Playwright 17/17 ([converge-1](validation/converge-1.md#tras-convergence-c2c4-2026-11-21)).

**Nota de proceso**: todas las tareas de HU-03 hechas en esta entrega (T003–T018 y C1–C4) se
ejecutaron con la skill `/speckit-implement` cargada. T001 y T002, de coordinación con A, siguen
pendientes. Las desviaciones y los incumplimientos (G1, G2 y G4 resueltos en C2–C4; G3 aceptado) están en
[converge-1](validation/converge-1.md).
