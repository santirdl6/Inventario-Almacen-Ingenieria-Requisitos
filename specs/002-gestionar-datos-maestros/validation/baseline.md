# Baseline de preparación HU-08

**Fecha**: 2026-11-16. **Tareas**: T002–T005.
**Código**: HEAD `e006513` más los cambios locales de preparación; no representa un commit nuevo.
**Fuentes**: [tareas](../tasks.md), [quickstart](../quickstart.md) y
[coordinación](coordination.md).

## Entorno y versiones

- macOS/Apple Silicon, Docker Engine 29.1.3; SQL Server 2025 real con emulación amd64.
- Node 24.15.0 para las comprobaciones finales. El Node por defecto del host es 25.9.0;
  se utilizó un Node 24 temporal fuera del repositorio. Un build exploratorio con Node 25
  no se cuenta como validación de la versión acordada.
- Angular/CLI/CDK 22.2.3, TypeScript 6.0.3, PrimeNG 22.1.4.
- SDK .NET 10.0.404 en la imagen de pruebas; EF Core/Identity/MVC Testing/dotnet-ef 10.0.13.
- Vitest/coverage-v8 5.1.0, jsdom 31.0.0, Playwright Test 1.65.0, @types/node 24.19.2;
  versiones exactas en package.json y lockfile.

## Comandos y resultados finales

| Directorio | Comando | Resultado |
|------------|---------|-----------|
| frontend | `npm run build` | Build de producción aprobado con Node 24.15.0. |
| frontend | `npm test` | 2 pruebas H0 aprobadas, sin fallos. |
| frontend | `npm run test:e2e:types` | Tipos de configuración/fixtures E2E correctos. |
| frontend | `npm run test:e2e -- e2e/environment.spec.ts` | 3 pruebas HTTP E2E aprobadas; imagen app construida, inicializador correcto y SQL real. |
| frontend | `HU08_E2E_SKIP_BUILD=1 npm run test:e2e -- e2e/environment.spec.ts` | 3 pruebas aprobadas con las imágenes recién construidas; segunda ejecución motivada por ampliar a 60 s la parada del fixture. |
| raíz | `docker compose --profile test run --build --rm tests` | Imagen SDK de pruebas construida; 22 pruebas SQL aprobadas, 0 fallidas, 0 omitidas. |
| raíz | `docker compose config --quiet` | Configuración Compose válida. |
| raíz | `docker compose --profile test run --no-deps --rm --entrypoint sh tests -c 'dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api --configuration Release --no-build'` | Modelo y snapshot sin diferencias. |

## Qué acredita cada suite

**SQL (22)**: las 18 pruebas H0 previas más 4 casos de MasterDataFixtureTests. Se comprueban
contraseñas/roles reales sin concesiones implícitas al administrador, conexiones independientes
con principal sin sysadmin/DDL, visibilidad de datos después de commit y rechazo de destinos
de provisionamiento fuera de InventoryHu08E2E/sqlserver. Las bases/logins xUnit son aleatorios
y se eliminan al finalizar; no se usan saldos artificiales.

**Unitarios frontend (2)**: disponibilidad solo después de recibir respuesta y mensaje de
fallo/reintento de la pantalla H0. AppComponent se extrajo de main.ts sin cambiar ese flujo.
El 100 % de cobertura informado corresponde exclusivamente a ese componente, no al inventario
ni a funcionalidades HU-08 aún pendientes.

**Smoke E2E (3)**: health SQL y frontend servidos, API desconocida devuelve JSON 404, sesiones
reales de gestor/lector/administrador con permisos distintos, y rechazo de login sin CSRF.
El provisionamiento usa UserManager en la base de la propia aplicación E2E. La limpieza
bloquea cuentas y confirma 401 al intentar autenticarse después, sin borrarlas ni eliminar
volúmenes. Son pruebas HTTP; no abren Chromium ni certifican UI de maestros.

## Interrupción y límites observados

La primera ejecución quedó interrumpida sin resultados. Docker Desktop terminó sin motor
disponible —EOF— y el reinicio inicial agotó su espera al cerrar procesos. Tras cerrarse
y arrancar de nuevo, el motor respondió y las ejecuciones anteriores se completaron.

Los contenedores E2E quedan detenidos. En este entorno emulado, SQL terminó con exit 137
tras la parada del fixture incluso con 60 s de gracia; Docker informa OOMKilled=false.
No se acredita una parada SQL elegante. La siguiente ejecución recuperó la misma base y
superó health/provisionamiento/sesión, sin borrado o recreación de sus volúmenes.

T006/T007 siguen bloqueadas por ausencia del resultado/ejecutor común de B. No se ha
implementado otro protocolo, auditoría de negocio, endpoints de maestros ni login visual.
Estas comprobaciones no acreditan idempotencia/concurrencia de stock ni aceptación de HU-08.
La revisión A+B, los recorridos humanos y las condiciones de producción permanecen pendientes.
