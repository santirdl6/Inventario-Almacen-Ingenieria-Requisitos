# HU-08 — Estado de validación técnica

**Fecha**: 2026-11-18. **Estado**: corte H1; aceptación integrada pendiente.
**Rama**: `a/h0-identity-article`, cambios locales sin publicar.
Fuentes: [constitución](../../../.specify/memory/constitution.md),
[plan global](../../../.specify/memory/project-plan.md), [spec](../spec.md),
[plan](../plan.md), [tareas](../tasks.md) y [quickstart](../quickstart.md).

## Comprobaciones ejecutadas

| Comprobación | Resultado |
|--------------|-----------|
| Publicación API Release y suite de integración SQL | 76 aprobadas, 0 fallidas, 0 omitidas. |
| `dotnet ef migrations has-pending-model-changes` | Sin cambios pendientes, EF 10.0.13. |
| `npm test` | 36 aprobadas, 15 archivos; cobertura de líneas 88,99 % en archivos ejercitados. |
| `npm run build` | Producción correcta, también dentro de Docker con Node 24. |
| `npm run test:e2e:types` | Correcto. |
| E2E funcional | 11 aprobadas: 3 HTTP y 8 de navegador, sin fallos. |
| SC-005 | Final sin trazas: p95 1,677 s, 193/200 en ≤2 s, 0 errores. Superado; primer fallo conservado en [detalle](performance.md). |
| `git diff --check` | Correcto. |

Backend y SQL Server 2025 se ejecutan en Docker Desktop/macOS ARM, SQL amd64 emulado,
con bases/logins de prueba aleatorios. Los cambios de esta continuación posteriores a la
suite SQL afectan únicamente al cliente, sus pruebas y documentación. Build frontend y
las 36 unitarias se ejecutaron después de corregir recuperación.
El host tiene Node 25.9.0; la imagen de aplicación compila con Node 24 según el proyecto.
Playwright es 1.65.0. El proyecto E2E dedicado usa InventoryHu08E2E y no InventoryDev.

Comandos desde la raíz, salvo npm desde `frontend/`:

```bash
docker compose --profile test run --build --rm tests
docker compose --profile test run --no-deps --rm --entrypoint sh tests -c 'dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api --configuration Release --no-build'
npm test
npm run build
npm run test:e2e:types
HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- --grep-invert SC-005
HU08_E2E_SKIP_BUILD=1 npm run test:e2e -- e2e/master-data-performance.spec.ts
```

El primer intento E2E pasó tres casos HTTP y no pudo lanzar ocho casos de navegador por
ausencia del ejecutable Chromium. La descarga completa tuvo timeouts; el reintento con
`PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT=120000 npx --no-install playwright install chromium --only-shell`
terminó correctamente. Se añadió la opción de usar Chrome instalado con perfiles temporales,
sin alterar el navegador predeterminado de Playwright. La ejecución funcional usa Chrome
158.0.8127.36. Este fallo de preparación no se cuenta como aprobación de pruebas.

## Cobertura y límites

| Requisitos/criterios | Evidencia | Estado |
|---------------------|-----------|--------|
| FR-001/005/006 | [US1](us1.md), regresión SQL/frontend. | Familias H1 verificadas. |
| FR-002/003/008 | [US2](us2.md), regresión SQL/frontend. | Catálogo H1 verificado. |
| FR-004 | [US3](us3.md). | Proveedores verificados; instantáneas de pedidos esperan HU-07. |
| FR-007/015–018/021; Q-01/03/04/06 | [US5](us5.md), SQL y pruebas de intención/versión/sesión. | Escenarios técnicos ejecutados; navegador se registra arriba. |
| FR-009/010/013/019; Q-05/07/09 | [US2](us2.md), [US4](us4.md), gate de consumidores. | Parte H1; integración pendiente. |
| FR-011/012; Q-08 | [US4](us4.md). | Borrado sin referencias, evidencia conservada y estado H1 comprobados. |
| FR-014 | Contrato, pantalla y rechazo explícito por diario ausente. | Lector real e historial pendientes, T069. |
| FR-020; Q-10; SC-006 | Pruebas frontend, recorridos automáticos. | Falta validación humana con tres gestores, T091. |
| Q-11 | [US2](us2.md), regresión de migraciones. | Transición H0 técnica comprobada; revisión A+B pendiente. |
| SC-005 | [Rendimiento](performance.md), 10.000 artículos/25 sesiones/200 muestras. | Superado en la ejecución final sin trazas, T090. |
| SC-007 y principio V | Auditoría/resultados atómicos en SQL; restauración común. | Recuperación V-14/RPO/RTO pendientes, T092. |

No se declara completa HU-08. Permanecen T035 integrada, T064/T066/T069/T070/T075/T076/T077/T078
en sus partes dependientes de documentos/stock, la aceptación humana, recuperación y revisión
A+B/T095/T096. No se crearon sustitutos de las historias de B ni aprobaciones humanas.

Principios I/II: altas y cambios de maestros no crean existencias ni ubicaciones; stock
integrado aún no acreditado. IV/V: auditoría append-only, resultados, rollback, permisos,
concurrencia y reintentos comprobados en SQL. III: mensajes, borrador preservado y revisión
automatizada; aceptación con trabajadores pendiente. La constitución exige conservar esos
límites hasta tener evidencia real.

## Corte local con el stock H0, 2026-11-19

FR-014 queda implementado y verificado sobre el diario de la PR #3 en una rama
local de integración. El recorrido Chromium comprueba desactivación, consulta
del historial vacío, reactivación y borrado. La build de producción pasó con
Node 24 en Docker, Vitest pasó 40/40 y la suite SQL pasó 95/95. EF 10.0.13
confirmó que el modelo coincide con el snapshot. Los resultados y el alcance de US4 se
detallan en [su validación](us4.md#validación-con-el-stock-h0-2026-11-19).

La PR #4 está fusionada en `a/h0-identity-article`. El cierre de HU-08 requiere
integrar el stock aprobado, verificar los consumidores todavía pendientes y
registrar la usabilidad con gestores, la restauración V-14 y las aprobaciones
de coordinación y aceptación.

## Validación adicional, 2026-11-19

La [aprobación de la PR #3](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/3#pullrequestreview-5974358200)
queda registrada sobre `25d6df6`. La PR seguía abierta durante el ensayo.
La [validación conjunta HU-02 y HU-08](us4.md#prueba-cruzada-de-consumidores-2026-11-19)
pasó 119/119 pruebas SQL e incluyó existencias e historial del mismo artículo
tras desactivarlo.
El [ensayo técnico V-14](recovery.md) recuperó maestros, auditoría, resultados y
diario a un punto en el tiempo en una segunda instancia. La
[prueba con tres gestores](usability.md) está preparada para convocar.

## Cierre de la entrega académica, 2026-11-19

El usuario aprobó expresamente la entrega de clase de HU-08. El alcance verificado
incluye los catálogos, el historial compartido, las pruebas con el núcleo de stock
y HU-02, y el ensayo técnico de restauración. Las PR [#3](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/3)
y [#4](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/4)
quedaron fusionadas en `a/h0-identity-article`.

Las verificaciones pendientes con consumidores documentales se cierran en esta
entrega por traslado a HU-01, HU-03, HU-04, HU-05, HU-06 y HU-07. Cada módulo
comprobará sus referencias y escritores reales al integrarse. SC-006 y T091
quedan fuera de la entrega de clase porque no hay tres gestores disponibles.

La aceptación operativa y sus criterios siguen regidos por la
[constitución](../../../.specify/memory/constitution.md). El ensayo V-14 de clase
sirve de evidencia técnica para esa revisión posterior. Los estados de las tareas
constan en el [cierre académico](../tasks.md#cierre-de-la-entrega-académica-2026-11-19).
