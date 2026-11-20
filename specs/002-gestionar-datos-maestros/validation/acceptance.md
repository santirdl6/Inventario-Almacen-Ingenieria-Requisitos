# HU-08 — Estado de validación técnica

**Fecha de cierre**: 2026-11-19. **Estado**: HU-08 cerrada — entrega técnica/académica.
El [cierre final](#cierre-final-de-hu-08-2026-11-19) sustituye los estados intermedios
conservados debajo como historial de validación. No quedan tareas de implementación locales.
**Rama**: `a/h0-identity-article`, cambios locales sin publicar.
Fuentes: [constitución](../../../.specify/memory/constitution.md),
[plan global](../../../.specify/memory/project-plan.md), [spec](../spec.md),
[plan](../plan.md), [tareas](../tasks.md) y [quickstart](../quickstart.md).

## Comprobaciones del corte histórico, 2026-11-18

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

## Cobertura y límites del corte histórico H1

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

## Convergencia implementada, 2026-11-19

T098/T099/T104 verificadas; [evidencia de la continuación](convergence.md).

| Requisitos/criterios | Evidencia nueva | Estado |
|---------------------|-----------------|--------|
| FR-014/020; US4.4–US4.5; Q-09/Q-10; principio III | Opciones legibles paginadas, tipos en español, tabla accesible y recorrido poblado por teclado. | T098/T104 completadas para el diario real presente. |
| FR-019; Q-05/Q-07; principio V | Carrera primera referencia/edición con StockCore, ambos órdenes y espera SQL real, conservando unidad histórica y conciliación. | T099 completada; T100 conserva el caso documental retirado. |
| FR-014; principio IV | La UI distingue ausencia de información de no aplicabilidad explícita. | T097 pendiente por reserva del snapshot; no se declara remediada la persistencia de ese motivo. |
| FR-009/010/013/019; SC-003/004/007 | Se mantiene evidencia StockCore y se identifican los consumidores ausentes. | T100–T103/T106 pendientes de integraciones reales. |
| SC-006 y validación operativa | Guion humano y ensayo técnico de restauración previos conservados. | T105/T107 pendientes de participantes, documentos y responsables. |
| Revisión final de contratos/cadena y aceptación | Comandos, documentación, matriz y bloqueos actualizados. | T108 parcial; faltan revisiones A+B/de responsables. |

SQL: 97 aprobadas, 1 omitida (V-14 específica), y regresión final 16/16. Vitest: 41/41;
historial final 3/3. Build Docker de frontend/API y tipos E2E correctos; EF sin cambios
de modelo. Los 11 E2E previos y el nuevo poblado tienen evidencia aprobada en pasadas
separadas; no se declara una pasada única 12/12. SC-005 no se volvió a medir.
El snapshot y los demás archivos reservados para B permanecen intactos. Este trabajo
no amplía la aprobación académica previa a una aceptación integrada u operativa.

## Cierre final de HU-08, 2026-11-19

El usuario solicita completar el trabajo propio y cerrar esta spec, asignando al módulo
consumidor lo que necesita su implementación posterior. **HU-08 queda cerrada técnicamente
y para su entrega académica.** T097/T098/T099/T104/T108 están completadas. Las siete tareas
T100–T103/T105–T107 se han trasladado, con IDs y responsables, al
[backlog receptor](../../001-reparto-specs-inventario/integration-tasks.md). Su traslado
no es una prueba superada ni una exclusión de las reglas que deben cumplir los consumidores.

### Última corrección y migración

`20261119171332_HU08MovementCounterparty`, sobre H0_StockCore, incorpora motivo explícito
de no aplicabilidad de contraparte al diario. El núcleo valida referencia o motivo antes
de crear efectos, después del replay común. La huella solo incorpora el motivo si existe,
conservando compatibilidad de intenciones antiguas. La migración no actualiza filas:
la CHECK habilitada exige evidencia a cada inserción nueva y conserva los datos históricos
sin atribuirles un motivo inventado. El historial de inactivos proyecta el motivo persistido.

Las cuatro pruebas `StockCounterpartyTests` acreditan rechazo sin efectos parciales,
normalización/persistencia, replay/conflicto, proyección de inactivos, migración de filas
históricas sin evidencia, replay de su huella anterior, CHECK en nuevas inserciones y
DENY de modificación del diario con el rol de ejecución y 503 mientras falta aplicar
la nueva migración. Las regresiones de fallos,
concurrencia y stock conservan las garantías previas.

### Matriz final del alcance propio y sus contratos receptores

| Requisitos / criterios | Evidencia propia | Resultado / receptor |
|------------------------|------------------|----------------------|
| FR-001/005/006, US1, SC-001/002 | Familias SQL/formularios/E2E, normalización, filtros, estado y referencias de artículos. | Implementado y verificado. |
| FR-002/003/008, US2.1–US2.7 | Artículos, familia obligatoria, StockPolicy cero, edición y migración H0. | Implementado y verificado. |
| FR-004, US3, SC-001/002 | Proveedores, contacto opcional/validación, identidad y estados. | Implementado; documentos futuros en INT-HU07-02/INT-HU01-02. |
| FR-007/015/016/017/018/021, US5, SC-004 | Roles reales/CSRF, auditoría y resultado atómicos, ETag, rollback/replay y recuperación de intención. | Implementado y verificado. |
| FR-009/010/011/019, SC-003/004 | Marca monotónica, lectores/gate, borrado real con/sin stock y carreras de familia/estado/borrado/primera referencia. | Implementado con consumidores presentes. Nuevos escritores: INT-HU01/03/04/05/07-01. |
| FR-012/013, US4, SC-003/007 | Estados sin cascadas ni cambios de cantidades, historial conservado y operaciones de stock permitidas/prohibidas. | Implementado en HU-08/StockCore; documentos en las tareas INT de cada HU y evaluador en INT-HU06-01. |
| FR-014, US4.4–US4.5, SC-003/007 | Diario real de inactivos, filtros paginados legibles, unidad histórica, responsable, soporte/contraparte o motivos; T097/T098/T104. | Implementado y verificado. |
| FR-020, Q-10 | Español, etiquetas, conservación de datos, revisión de estado/borrado y recorrido por teclado en navegador. | Implementado y verificado. SC-006 humano se conserva en H5-01 por su alcance operativo. |
| SC-005 | 10.000 artículos, 25 sesiones, p95 1,677 s y 193/200 en ≤2 s, sin errores, en la medición registrada. | Verificado; el cierre no cambia las consultas de catálogo. |
| Recuperación y revisión compartidas | Ensayo técnico V-14 previo, revisión local de código/contratos y pruebas finales. | Ejercicio operativo/aceptación en H5-02/H5-03; revisión A+B antes de integrar/fusionar en COORD-01. |

### Comprobaciones finales del mismo estado productivo

| Comprobación | Resultado |
|--------------|-----------|
| SQL Server real, suite completa | **101 aprobadas**, 0 fallidas; V-14 específica omitida por requerir su entorno independiente. |
| Vitest | **41 aprobadas**, 16 archivos; cobertura de líneas 88,37 % en los archivos ejercitados. |
| Playwright funcional | **12/12 aprobadas en una sola pasada**, Chrome real, incluyendo historial poblado y conservación de saldos. |
| Build Docker | Frontend con Node 24 y publicación API Release correctos. |
| Tipos E2E | Correctos. |
| EF 10.0.13 | Modelo y snapshot sin diferencias. |

Comandos SQL/Vitest/EF en [convergencia](convergence.md); la pasada funcional final usa
`HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- --grep-invert SC-005` desde frontend,
reconstruyendo la imagen actual y aplicando la migración en InventoryHu08E2E. Cuentas
retiradas y proyecto E2E detenido al terminar. No se modifica InventoryDev.

La reserva anterior del snapshot queda sustituida por esta finalización de T097 solicitada
por el usuario. El snapshot refleja solo la migración aditiva de contraparte; los otros
archivos reservados permanecen intactos. La revisión de la otra persona y las condiciones
constitucionales de producción siguen asignadas fuera de este módulo, sin certificarlas
como realizadas por este cierre técnico.

## Publicación y base integrada de HU-02

El [PR #6](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/6)
se ha aprobado y fusionado tras resolver R1–R4. El cierre de HU-08 y la coordinación
están publicados en el [PR #7](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/7),
actualizado sobre esa base y con revisión solicitada a B para COORD-01.
La verificación conjunta pasa **127 SQL**, **108 frontend** y **15 E2E**, con build/tipos
correctos y modelo sin diferencias. [Detalle y comandos](pr-publication.md).
La revisión de B sigue siendo una condición externa de integración, no una tarea de
implementación que reabra HU-08 ni una aprobación que este registro dé por recibida.
