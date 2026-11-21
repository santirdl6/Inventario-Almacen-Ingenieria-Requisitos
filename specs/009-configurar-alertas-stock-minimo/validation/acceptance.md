# Evidencia técnica local — HU-06

| Control | Resultado |
|---|---|
| Política, ETag, permisos, antiforgery, replay, auditoría y rollback | SQL real: `StockPolicyTests`. |
| V-11, unicidad, lotes y ubicaciones, rollback y replay | [Evidencia de stock](stock.md). |
| Desactivación, reactivación, orden de operaciones y rollback | SQL real: `ArticleAlertLifecycleTests`; barreras de recepción y ciclo de vida en `MasterDataLifecycleConcurrencyTests`. |
| Consulta, permisos, filtros y propuesta de compra | `StockAlertQueryTests` y Chromium `stock-alerts.spec.ts` (1/1); abrir la propuesta no cambia el total de pedidos ni el contenido de Mailpit. |
| Regresión | Backend: 233 aprobadas, 0 fallos, 2 omitidas por configuración (V-14 y benchmark optativo). Frontend: 145 aprobadas, compilación Docker y verificación de tipos E2E correctas. |
| Migración | `20261121200510_HU06StockAlerts` aplicada en SQL aislado; EF no detectó cambios pendientes del modelo. |
| SC-004 | **Diferido por decisión del usuario**. El ensayo `smoke` sobre SQL Server amd64 emulado en Mac ARM produjo p95 de 6,194 ms (52 muestras, 1 línea) y 9,901 ms (21 muestras, 20 líneas). Es una comprobación del instrumento y la carga reducida; no acredita el perfil completo ni la latencia visible. |

```sh
docker compose --profile test run --build --rm --entrypoint dotnet tests test Inventario.slnx -- xUnit.MaxParallelThreads=4
node scripts/measure-hu06-alerts.mjs smoke
```

Chromium se ejecutó desde `frontend/` con `INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-hu06 HU08_E2E_BASE_URL=http://localhost:8083 npx playwright test stock-alerts.spec.ts`. La [revisión A+B](shared-review.md) se registró como confirmada por el usuario. La aceptación H5 sigue pendiente.

## Ensayo completo en el servidor local

El 2026-11-21 se inició el perfil completo en Docker, sobre el SQL Server local `amd64` emulado en ARM y con una base temporal del fixture. La ejecución se detuvo a petición del usuario antes de obtener una medición completa. Se retiró T015 del cierre funcional.

```sh
docker compose --profile test run --detach --name hu06-benchmark-local --env HU06_BENCHMARK=full --env Logging__LogLevel__Default=Warning --entrypoint dotnet tests test Inventario.slnx --filter 'FullyQualifiedName~StockAlertPerformanceTests' --logger 'console;verbosity=detailed'
docker logs -f hu06-benchmark-local
```
