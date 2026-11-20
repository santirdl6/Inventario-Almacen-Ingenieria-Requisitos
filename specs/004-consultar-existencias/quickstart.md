# Validación: HU-05 — Consultar existencias y reservas

**Base**: [guía común](../../.specify/memory/project/quickstart.md): arranque Compose, SQL de pruebas,
datos comunes §5 y escenarios V-04, V-05 y V-08. Esta guía solo añade los pasos de HU-05.
Contratos: [API](contracts/stock-api.md), [UI](contracts/stock-ui.md) e
[integración HU-03](contracts/hu03-integration.md); reglas en [data-model.md](data-model.md).
**Nada de lo descrito aquí está ejecutado todavía.**

## Prerrequisitos

1. Rama `b/hu-05-consultar-existencias`, con HU-02 y la base común de A integradas.
2. Migración `HU05_CustomerOrdersReservations` aplicada sobre `HU08MovementCounterparty` (PR #7) y
   `dotnet ef migrations has-pending-model-changes` sin cambios.
3. Datos comunes §5 (ART-01 con lote L-001 y unidad UD sin fracciones; ubicaciones P01-E01-N01 y
   P01-E01-N02), más ART-02 sin seguimiento de lote. Mientras no exista HU-01, las existencias se
   preparan con `StockCore` (Receive) desde `StockTestHarness`, nunca escribiendo saldos.
4. Cuentas: operario (`Shipping.Record`, `Inventory.Read`), responsable (`Reservations.Manage`,
   `Inventory.Read`), gestor de maestros (solo `Inventory.Read`) y administrador (sin permisos de
   inventario).

## Pruebas automatizadas backend

No hay SDK .NET local. Se usa el contenedor del SDK sobre la red de Compose, como en la guía común:

```bash
docker compose up -d sqlserver
docker run --rm --network inventario_default -v "$PWD/backend:/src" -w /src \
  -e Testing__SqlHost=sqlserver -e 'Testing__SqlAdminPassword=LocalSqlAdmin_2026!' \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'dotnet tool restore && dotnet test Inventario.slnx --nologo'
```

Para repetir solo las de HU-05, añadir `--filter "FullyQualifiedName~Inventario.IntegrationTests.Stock"`.
Resultado esperado: las pruebas previas siguen pasando, incluidas las de HU-02 tras delegar en
`StockBalanceReader`. Las de concurrencia (S-12, S-13) se repiten tres veces.

| Caso | Preparación y acción | Resultado esperado | Spec / común |
|------|----------------------|--------------------|--------------|
| S-01 | Recibir 2 y 4 de ART-01/L-001 en N01/N02; `GET /stock?articleId=` sin filtro, con `locationId=N02`, con `lotId` y con `pageSize=1`. | Dos filas; totales 6/0/6 en todas las páginas; con filtro 4/0/4. | US1.1–1.3, FR-002–004, SC-001; V-03 |
| S-02 | Reservar 4 de 6 en N02; consultar; `GET /articles/{id}/locations` (HU-02). | Fila N02 con 4/4/0 y totales 6/4/2; HU-02 devuelve las mismas cantidades. | FR-003, D-02 |
| S-03 | Sin `articleId`; artículo inexistente; `locationId` ajeno; `page=0`. | 422 `errors.articleId`; 404; 200 vacío con totales 0; 422 `errors.page`. | US1.4, US1.7, FR-001 |
| S-04 | Movimiento confirmado y otro documento sin confirmar (fallo inducido); consultar. | Solo cuenta el confirmado. | US1.5, FR-005, SC-002 |
| S-05 | Dejar N01 a físico 0 con una salida del núcleo (`Ship`, sin reserva); consultar con y sin `includeEmpty`. | N01 oculta por defecto y visible con 0/0/0 al pedirla; totales iguales en ambos casos. | US1.9, FR-002 |
| S-06 | Desactivar ART-01 con existencias; consultar y buscar. | `article.isActive = false`; cantidades visibles. | US1.6, FR-007 |
| S-07 | Tras S-08…S-11: comparar Σ remanente de `/stock/reservations` con `totals.reserved` de `/stock`, y diario con saldos (`AssertJournalMatchesBalanceAsync`). | Coinciden en cada bucket. | FR-019–020; invariantes del modelo |
| S-08 | Operario crea PED-1001 con ART-01×7 y ART-02×3; repetir la misma clave; otra clave con « ped-1001 ». | 201 `Open`, servido 0 y `IsDefinitionLocked` true en ambos artículos; replay idéntico; 409 `DuplicateCode`. | US2.1–2.2, FR-009–012 |
| S-09 | Altas con cantidad 0, ART-01 repetido, 1,5 en artículo entero, artículo inexistente o inactivo. | 422 o 409 por línea; ningún pedido ni marca de artículo creada. | US2.3, FR-011 |
| S-10 | Responsable reserva 7 en N01 para PED-1001 (físico 10); otra reserva de 1 en la misma línea; reserva sin `If-Match`, con ETag viejo, en ubicación vacía, con lote ajeno y para un pedido no `Open`. | 201 con 10/7/3; 422 por superar el pendiente; 428; 412; 409 `InsufficientStock`; 422; 409 `CustomerOrderNotOpen`. | US3.1–3.4, US3.7, FR-015–017 |
| S-11 | Reserva con dos asignaciones, una con disponible insuficiente; reserva con el mismo bucket dos veces. | Ninguna asignación ni movimiento; 422. | US3.5, FR-022 |
| S-12 | **V-05**: físico 10 sin reservas; dos conexiones reservan 7 para pedidos distintos (20 repeticiones). | Exactamente una 201 y otra 409 `InsufficientStock`; reservado 7, nunca 14. | US3.6, FR-024, SC-004 |
| S-13 | Dos reservas simultáneas de la misma línea que juntas superan el pendiente. | Una 201 y otra 412 o 422; remanente ≤ pendiente. | FR-024 |
| S-14 | **Extensión de V-04**: liberar 2 con R1 y motivo; liberar 2 con R2; repetir R1 con su clave; R1 con otra clave; liberar 5 de los 3 restantes; sin motivo; ETag viejo. | 200; 200; replay; 409 `DocumentAlreadyConfirmed`; 409 `ReservedStockConflict`; 422 `errors.reason`; 412. Reservado final 3, físico sin cambios, dos movimientos `Release` con motivo y actor. | US4.1–4.5, FR-018–019 |
| S-15 | Liberar con ART-01 desactivado; liberar todo el remanente. | 200; estado `Released`. | US4.6, R-05 |
| S-16 | **V-08**: `FailAfterSaveInterceptor` en alta, reserva y liberación; consultar operación, saldos e historial; repetir con la misma clave. | Ningún efecto parcial ni `OperationResult`; el reintento confirma un único efecto. | US5.5, FR-022–023, SC-005 |
| S-17 | Sin sesión, sin antiforgery, gestor (lectura) y administrador intentan cada ruta; operario intenta reservar. | 401; 400; 403 en mutaciones y lectura permitida al gestor; 403 al administrador también en lectura; 403 al operario. | US5.1, FR-014, FR-021 |
| S-18 | Contrato HU-03: `ICustomerOrderReader` dentro del ejecutor tras bloquear el pedido. | Pendiente y remanente por bucket iguales a `GET /customer-orders/{id}`. | [hu03-integration.md](contracts/hu03-integration.md) |
| S-19 | Uso del artículo: con una línea de pedido, `GET /articles/{id}/context` y DELETE del artículo. | Definición bloqueada; borrado rechazado por referencias; sin `DependencyUnavailable`. | D-13 |

## Pruebas frontend

Directorio `frontend/`, con la configuración de pruebas de A:

```bash
npm test
npm run build
```

Las unitarias (Vitest/TestBed) cubren:

- Consulta: «Elige un artículo», totales separados de la página, `includeEmpty`, «sin lote»,
  «Inactivo» y `asOf`.
- Alta: errores por línea conservando datos y artículo repetido no seleccionable.
- Diálogos de reservar y liberar: motivo obligatorio, `releaseId` estable entre reintentos y
  visibilidad según `Reservations.Manage`.
- Estado «Resultado pendiente de comprobar».

## E2E (solo el recorrido del vídeo)

`frontend/e2e/stock.spec.ts`, con el fixture de login de A y Compose activo:

| Caso | Recorrido |
|------|-----------|
| E2E-01 | Preparación: el fixture recibe 10 de ART-01/L-001 en N01 mediante el endpoint de preparación de research D-17, activo solo si se arranca con `TESTING_ENABLE_STOCK_SEEDING=true docker compose up`. El responsable abre `/existencias`, busca ART-01 y ve totales y filas. El operario crea PED-1001 en `/existencias/pedidos/nuevo`. El responsable abre el pedido, reserva 7 en N01 y comprueba disponible 3 en `/existencias`. Libera 2 con motivo y ve reservado 5 y disponible 5. |

No se automatiza en E2E lo que ya cubren las pruebas de API (errores, concurrencia, fallos).

## Fuera de alcance de esta entrega

- **SC-006**: p95 ≤ 2 s con 10.000 artículos, 1.000.000 de entradas y 25 sesiones. Requiere datos
  sintéticos y carga concurrente.
- **SC-007**: validación con trabajadores representativos (constitución III). Guion previsto:
  localizar el disponible de ART-01 en N02, reservar 3 para un pedido y liberar 1 con motivo, sin
  ayuda, registrando participantes por rol, resultado e incidencias.

Ambos quedan registrados como desviación en el [plan](plan.md#complexity-tracking). HU-05 puede
integrarse, pero no darse por aceptada sin ellos.

## Evidencia

Al ejecutar, registrar aquí la fecha, el entorno (imagen del SDK y de SQL Server), el número de
pruebas, el resultado por caso y las incidencias. Después, llevar un resumen al quickstart común
(diff P-08 del plan).

| Fecha | Entorno | Qué se ejecutó | Resultado |
|-------|---------|----------------|-----------|
| 2026-11-20 | `mcr.microsoft.com/dotnet/sdk:10.0` sobre `mssql/server:2025-latest` (Compose, red `inventario_default`), Windows 11 + Docker Desktop. Copia limpia de `cdb8674` (`git archive`). | **Línea base de T003**: `dotnet test Inventario.slnx` antes de añadir código de HU-05. | 121 aprobadas, 1 omitida (V-14, requiere dos instancias), 0 fallidas. |
| 2026-11-20 | Igual; copia del árbol de trabajo de `b/hu-05-consultar-existencias` sobre `de9ff61` con las pruebas de T014 (sin `bin/obj`). | **Cierre del backend de HU-05 (T014)**: `dotnet test Inventario.slnx` tres veces seguidas y `has-pending-model-changes` tras cada una. Incluye S-01, S-03, S-05, S-08–S-12, S-14–S-16, S-18 y las carreras de INT-HU05-01/02 (`CustomerOrderTests`, `ReservationTests`, `ReleaseTests`, `StockReliabilityTests`). | 199 aprobadas, 1 omitida (V-14), 0 fallidas en las tres ejecuciones; sin cambios de modelo. Las 14 carreras con `LockBarrier` (6 de reserva y 8 de liberación, más las 6 de alta con `SaveBarrier`) pasaron en las tres. |
