# Validación: HU-05 — Consultar existencias y reservas

**Base**: [guía común](../../.specify/memory/project/quickstart.md): arranque Compose, SQL de pruebas,
datos comunes §5 y escenarios V-04, V-05 y V-08. Esta guía solo añade los pasos de HU-05.
Contratos: [API](contracts/stock-api.md), [UI](contracts/stock-ui.md) e
[integración HU-03](contracts/hu03-integration.md); reglas en [data-model.md](data-model.md).
Ejecutado en la rama `b/hu-05-consultar-existencias` el 2026-11-20. El resultado por caso está en
«Estado de la matriz S» y en «Evidencia».

## Prerrequisitos

1. Rama `b/hu-05-consultar-existencias`, con HU-02 y la base común de A integradas.
2. Migración `20261120113458_HU05_CustomerOrdersReservations` aplicada sobre
   `20261120102651_HU08ExclusiveMovementCounterparty` (PR #8) y
   `dotnet ef migrations has-pending-model-changes` sin cambios.
3. Datos comunes §5 (ART-01 con lote L-001 y unidad UD sin fracciones; ubicaciones P01-E01-N01 y
   P01-E01-N02), más ART-02 sin seguimiento de lote. Mientras no exista HU-01, las existencias se
   preparan con `StockCore` (Receive) desde `StockTestHarness`, nunca escribiendo saldos.
4. Cuentas: operario (`Shipping.Record`, `Inventory.Read`), responsable (`Reservations.Manage`,
   `Inventory.Read`), gestor de maestros (solo `Inventory.Read`) y administrador (sin permisos de
   inventario). En E2E, el fixture de A aprovisiona las tres cuentas comunes más `responsable`
   (`ResponsableAlmacen`, T018).

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

### Estado de la matriz S

Reconciliado con `/speckit-converge` el 2026-11-20.

| Caso | Estado | Prueba |
|------|--------|--------|
| S-01, S-03, S-05 | En verde | `StockQueryTests`, `StockBalanceReaderTests` |
| S-02 | En verde (T020) | `StockQueryTests.S02_…` |
| S-04 | **No ejecutado**: aún no hay documentos en borrador (HU-01); «Si hay tiempo» S4 | — |
| S-06 | Parcial: `article.isActive` en la API (S-01) y «Inactivo» en la UI (Vitest); la consulta con el artículo desactivado queda en S4 | `StockQueryTests`, `stock-query.page.spec.ts` |
| S-07 | En verde, como conciliación de las asignaciones con `StockBalances.Reserved` y del diario con los saldos | `StockApiSupport.AssertCommitmentsMatchBalancesAsync` en `ReservationTests`, `ReleaseTests` y `StockReliabilityTests` |
| S-08, S-09, S-19 | En verde | `CustomerOrderTests` |
| S-10, S-11, S-12 (V-05, 20 rondas), S-18 | En verde | `ReservationTests`, `CustomerOrderTests` |
| S-13 | **No ejecutado**; «Si hay tiempo» S4 | — |
| S-14, S-15 | En verde | `ReleaseTests` |
| S-16 (V-08) | En verde | `StockReliabilityTests` |
| S-17 | En verde, repartido: sin sesión 401, sin antiforgery 400, gestor y administrador 403, operario 403 al reservar y liberar | `StockQueryTests`, `CustomerOrderTests`, `ReservationTests`, `ReleaseTests` |

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

Construido: `stock-query.page.spec.ts` (7), `order-form.page.spec.ts` (7), `order-detail.page.spec.ts`
(10) y las specs de los cambios en archivos de A (`pending-operation-hu05.spec.ts`, 5;
`operation-status-hu05.spec.ts`, 4). Incluyen el foco al primer error tras un rechazo (C1, FR-027
y FR-028). El caso de 412 en los diálogos no tiene prueba (validation/converge-1.md, G4).

## E2E (solo el recorrido del vídeo)

`frontend/e2e/stock.spec.ts`, con el fixture de login de A y Compose activo:

| Caso | Recorrido |
|------|-----------|
| E2E-01 | Preparación: el fixture recibe 10 de ART-01/L-001 en N01 mediante el endpoint de preparación de research D-17, activo solo si se arranca con `TESTING_ENABLE_STOCK_SEEDING=true docker compose up`. El responsable abre `/existencias`, busca ART-01 y ve totales y filas. El operario crea PED-1001 en `/existencias/pedidos/nuevo`. El responsable abre el pedido, reserva 7 en N01 y comprueba disponible 3 en `/existencias`. Libera 2 con motivo y ve reservado 5 y disponible 5. |

No se automatiza en E2E lo que ya cubren las pruebas de API (errores, concurrencia, fallos).
Construido con SKU, referencia y código de ubicación únicos por ejecución (`E2E05-…`, `PED-…`,
`N01-…`) en lugar de ART-01/PED-1001, para no chocar con ejecuciones anteriores en la misma base E2E.
Comando: desde `frontend/`, `npx --no-install playwright test --grep-invert SC-005`.

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
| 2026-11-20 | Igual; copia del árbol de trabajo de `b/hu-05-consultar-existencias` sobre `de9ff61` con las pruebas de T014 (sin `bin/obj`). | **Cierre del backend de HU-05 (T014)**: `dotnet test Inventario.slnx` tres veces seguidas y `has-pending-model-changes` tras cada una. Incluye S-01, S-03, S-05, S-08–S-12, S-14–S-16, S-18 y las carreras de INT-HU05-01/02 (`CustomerOrderTests`, `ReservationTests`, `ReleaseTests`, `StockReliabilityTests`). | 199 aprobadas, 1 omitida (V-14), 0 fallidas en las tres ejecuciones; sin cambios de modelo. Las 16 carreras con `LockBarrier` (8 de reserva y 8 de liberación), más las 6 de alta con `SaveBarrier`, pasaron en las tres (corregido en `/speckit-converge`: decía 14, 6 de reserva). |
| 2026-11-20 | Proyecto Compose `inventario-hu08-e2e` (http://localhost:8082, base InventoryHu08E2E), Playwright 1.65 + Chromium, Windows 11 + Docker Desktop; imagen construida desde `110df1a` más `e2e/stock.spec.ts`. | **E2E-01 (T019)** y suite Playwright completa con `--grep-invert SC-005`, con `TESTING_ENABLE_STOCK_SEEDING=true` solo en ese proyecto (fixture de T018). | E2E-01 en verde; suite 16/16 (línea base 15/15), sin intermitencias. |
| 2026-11-20 | Igual (backend con `git archive` de `e220381`; Vitest y `tsc` locales con Node 26; Playwright en el proyecto `inventario-hu08-e2e`). | **Verificación final tras `/speckit-converge`**: suite backend completa, `has-pending-model-changes`, Vitest completo, `tsc` de app, specs y E2E, y Playwright con `--grep-invert SC-005`. | Backend 206 aprobadas, 1 omitida, 0 fallidas; modelo sin cambios; Vitest 136/136 (35 archivos); tipos sin errores; Playwright 16/16. Detalle FR/SC en [validation/converge-1.md](validation/converge-1.md). |

**Nota de proceso (honesta)**: T003 a T007 se ejecutaron siguiendo a mano el `SKILL.md` de
`/speckit-implement`. La sesión se había abierto en la carpeta padre del repositorio y la skill no se
registraba. De T008 en adelante se ejecutaron con la skill cargada. El método (pruebas primero, rojo
anotado, commit por tarea) fue el mismo; la diferencia es solo de herramienta.
