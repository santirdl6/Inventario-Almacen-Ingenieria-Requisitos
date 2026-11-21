# SC-007 — Ensayos representativos

**Fechas**: 2026-11-20/21 · **T055: pendiente; optimización en curso, SC-007 no acreditado**.
Base: [protocolo reproducible](../quickstart.md#protocolo-reproducible-de-rendimiento-sc-007),
[spec](../spec.md), [plan](../plan.md) y [tareas](../tasks.md).

**Estado**: los ensayos cuarto y quinto alcanzan la muestra mínima y el objetivo de consulta,
pero no el de guardados. El sexto fue inconcluso por una interrupción de la ventana.
El séptimo y el ensayo de cierre alcanzan la muestra mínima, pero todavía incumplen
el objetivo de guardados. Las secciones siguientes conservan la evidencia.

## Ejecución real

Desde raíz:

```bash
docker compose build initialize
node --check scripts/measure-hu07-purchasing.mjs
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet build Inventario.slnx --configuration Release --no-restore --nologo'
HU08_E2E_BROWSER_CHANNEL=chrome node scripts/measure-hu07-purchasing.mjs
```

Imagen publicada correctamente, JavaScript válido y solución compilada con **0 errores y
0 avisos**. El proceso Node devolvió resultado no aprobado. Proyecto dedicado:
`inventario-hu07-e2e-performance-0e371d1c412c48d4ab15ef181c1241b3`.
Inicio de preparación 18:32:38 UTC; medición desde **18:42:44 UTC durante 600 segundos**,
precedida por 120 segundos de calentamiento con las mismas 25 sesiones. Finalización y
limpieza 18:54:33 UTC. No se ejecutaron suites durante la ventana del navegador.

Artefactos locales ignorados en
`frontend/test-results/hu07-performance/inventario-hu07-e2e-performance-0e371d1c412c48d4ab15ef181c1241b3/`:
report.json, samples.json y private-manifest.json (modo 0600). Este último contiene cuentas
temporales y **no debe incorporarse a Git**. Los resultados resumidos durables están aquí;
Playwright puede limpiar su directorio de resultados en una ejecución posterior.

## Datos y condiciones

- Seed 7007; generador determinista independiente por sesión. Identidades/IDs/números
  retornados y partición de pedidos conservados en el manifiesto.
- 100 proveedores, 10.000 artículos, 10.000 compras iniciales Draft/None, con 100 por
  proveedor y distribución 40/30/30 de 1/10/20 líneas. Los escritores HTTP reales generan
  maestros/compras; 5.000 comandos sintéticos auditados de 200 asignaciones por StockCore
  generan **1.000.000 de entradas**, sin DML de saldos ni recepción HU-01 simulada.
- 25 cuentas Comprador/contextos de Chrome independientes. Ciclos de cuatro consultas,
  dos altas y dos correcciones; tamaños 1/10/20 cíclicos y pedidos de edición particionados.
- Consultas pageSize=25: sin filtro, proveedor, prefijo y Draft/período; rotación de páginas
  primera/intermedia/final. Período fijo **[2026-11-20T18:32:00Z, 2026-11-20T18:41:00Z)**.
- Moneda EUR, SMTP/worker deshabilitado; cero solicitudes de envío. No se provocan conflictos.
- Reloj monotónico Node desde Buscar/cambio de página/Guardar hasta filas correspondientes
  o número del pedido persistido visibles, con dos frames. HTTP se registra aparte.

| Recuento SQL observado | Antes de calentamiento | Al finalizar |
|---|---:|---:|
| Artículos / proveedores | 10.000 / 100 | 10.000 / 100 |
| Pedidos | 10.000 | 10.593 |
| Entradas / movimientos de stock | 1.000.000 / 5.000 | 1.000.000 / 5.000 |
| Físico total | 1.000.000 | 1.000.000 |
| Resultados de operaciones | 25.175 | 26.337 |
| Pedidos fuera de Draft/None | 0 | 0 |

Calentamiento: 446 observaciones, **100 altas exitosas**, cero errores. Por tanto había
10.100 pedidos al inicio de la medición (derivado de recuento inicial + altas de calentamiento,
no de una lectura SQL separada en ese instante). Las 493 altas medidas completadas explican
los 10.593 finales; los resultados adicionales incluyen correcciones.

## Entorno

- Cliente macOS/darwin ARM64, Apple M4, 10 CPU, 16 GiB RAM; Node 25.9.0 y Chrome
  158.0.8127.36, Playwright 1.65.0. Publicación Angular con Node 24 del Dockerfile.
- Docker Desktop/Engine 29.1.3, Linux `6.12.54-linuxkit`, ARM64, 10 CPU y
  8.217.448.448 bytes de RAM asignados a la VM (~7,65 GiB).
- API .NET 10/EF 10.0.13, imagen Linux ARM64
  `sha256:c048c9c93e781a0b101d86208470ef065fceb484c65653ef230ed4db7d80b4a5`.
- SQL Server 2025 RTM-CU10 **17.0.5035.5**, Linux/Ubuntu 24.04 x64 por emulación amd64;
  imagen `sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3`.
- API/SQL sin límite individual de memoria/CPU (`Memory=0`, `NanoCpus=0`); comparten la VM.
  Dos instancias SQL preexistentes permanecieron encendidas e inactivas; no se modificaron.
  Esta máquina de desarrollo emulada no es una plataforma SQL x64 de producción soportada.

## Resultados medidos

Percentil por rango más próximo sobre éxitos; **errores conservados por separado**.

| Operación | Intentos | Éxitos | Errores | Timeouts UI | Conflictos 409/412 | p95 UI | p95 HTTP auxiliar |
|---|---:|---:|---:|---:|---:|---:|---:|
| Consulta | 1.023 | 1.018 | 5 | 0 | 0 | 4.476 ms | 3.289 ms |
| Alta | 517 | 493 | 24 | 16 | 0 | 6.647 ms | 4.765 ms |
| Corrección | 486 | 469 | 17 | 8 | 0 | 7.690 ms | 4.978 ms |

Tasas de error: consulta **0,49 %**, alta **4,64 %**, corrección **3,50 %**.

No se alcanza el mínimo de 1.000 observaciones de alta/corrección y los tres p95 observados
superan el objetivo de 2.000 ms. Hay **46 errores**: 19 HTTP 500 (incluidos cuatro en
preparación de filtro), tres HTTP 503 y 24 timeouts de preparación UI. No son muestras de
éxito ni se eliminan del informe. Los 503 no se cuentan como conflictos 409/412; una nueva
ejecución debe desglosar además su código ProblemDetails y las esperas de bloqueo.

## Incidente observado y bloqueo

El log del contenedor SQL registra a **18:51:45 UTC**:

```text
This program has encountered a fatal error and cannot continue running
Reason: 0x00000006
Message: Last Chance
Process: 11 - sqlservr
```

El proceso reinicia; a 18:52:16 vuelve a admitir conexiones y recupera la base. El log API
registra errores de transporte SQL/`Connection reset by peer` y transacciones interrumpidas.
El reinicio del motor explica errores de esa parte de la ventana, **sin demostrar la causa
raíz del fallo nativo ni explicar por sí solo toda la latencia previa**. No se atribuye sin
evidencia a OOM, a una consulta concreta ni a pérdida de integridad de la aplicación.

Consulta reproducible del incidente (contenedores conservados, detenidos):

```bash
docker logs inventario-hu07-e2e-performance-0e371d1c412c48d4ab15ef181c1241b3-sqlserver-1 --since 2026-11-20T18:49:00Z
docker logs inventario-hu07-e2e-performance-0e371d1c412c48d4ab15ef181c1241b3-app-1 --since 2026-11-20T18:49:00Z
```

El finally obtuvo recuentos finales, **retiró las 27 cuentas temporales** mediante bloqueo
y cambio de security stamp y detuvo solo los contenedores propios, sin borrar volúmenes.

**Siguiente paso de T055**: estabilizar/seleccionar el entorno SQL de carga y diagnosticar
la latencia con perfiles de consulta/recursos; después repetir el mismo protocolo completo.
No se aplicó una optimización especulativa ni se redujeron carga, muestra u objetivo para
obtener un PASS. T056/T058–T060 conservan su dependencia y siguen pendientes; T057 sí se
completó con el [guion H5](h5-handoff.md).

## Comprobación frontend adicional previa a la medición

Se lanzó `npm test && npm run build && npm run test:e2e:types` durante la preparación,
antes del calentamiento: **135 aprobadas y un timeout de 5 s** en el caso preexistente
`locations.routes.spec.ts` de navegación a artículos. La cadena se detuvo y no ejecutó
build/tipos locales. La compilación Docker sí pasó por separado. No se aumentó el timeout
ni se declaró pasada la regresión final; volver a comprobar esa suite sin carga concurrente
al reanudar el checkpoint T056.

## Segundo ensayo — Repetición solicitada por el usuario

**Fecha**: 2026-11-20 · **Resultado: INCONCLUSO; no supera T055**.

Se repitió el mismo comando, código, imágenes, carga, seed, mezcla, tamaños, número de
sesiones, calentamiento, ventana medida y criterios. No se ajustaron límites ni se ejecutaron
otras suites en paralelo. Se creó un proyecto/base/volúmenes nuevos:
`inventario-hu07-e2e-performance-af1fb816e0714e7ba4a7c121e9c8f5f4`.

```bash
HU08_E2E_BROWSER_CHANNEL=chrome node scripts/measure-hu07-purchasing.mjs
docker inspect inventario-hu07-e2e-performance-af1fb816e0714e7ba4a7c121e9c8f5f4-sqlserver-1 --format 'RestartCount={{.RestartCount}} OOMKilled={{.State.OOMKilled}} Status={{.State.Status}}'
```

Preparación desde **19:03:19 UTC**; recuentos SQL iniciales verificados y 25 sesiones
autenticadas. Medición desde **19:13:36 UTC durante 600 segundos**, precedida por 120 s
de calentamiento. Finalización/limpieza a **19:25:00 UTC**. Período fijo para consultas:
**[2026-11-20T19:03:00Z, 2026-11-20T19:12:00Z)**. Entorno y hashes de las dos imágenes,
versiones de SQL/Chrome y recursos Docker idénticos al primer ensayo.

| Operación medida | Intentos / éxitos | Errores | Timeouts / conflictos | p95 UI | p95 HTTP auxiliar |
|---|---:|---:|---:|---:|---:|
| Consulta | 1.036 / 1.036 | 0 | 0 / 0 | 4.172 ms | 2.791 ms |
| Alta | 503 / 503 | 0 | 0 / 0 | 6.718 ms | 4.641 ms |
| Corrección | 500 / 500 | 0 | 0 / 0 | 8.842 ms | 5.480 ms |

Tasa de error medida **0 % en las tres operaciones**. Calentamiento: 442 observaciones,
100 altas exitosas y **dos HTTP 503 en correcciones de 20 líneas** (sesiones 8 y 6,
iteración 6). Esas incidencias no se cuentan en el percentil medido ni se ocultan; el
runner conserva también los errores del calentamiento para impedir un PASS indebido.

| Recuento SQL observado | Antes de calentamiento | Al finalizar |
|---|---:|---:|
| Artículos / proveedores | 10.000 / 100 | 10.000 / 100 |
| Pedidos | 10.000 | 10.603 |
| Entradas / movimientos de stock | 1.000.000 / 5.000 | 1.000.000 / 5.000 |
| Físico total | 1.000.000 | 1.000.000 |
| Resultados de operaciones | 25.175 | 26.376 |
| Pedidos fuera de Draft/None | 0 | 0 |

Al inicio de la medición había 10.100 pedidos, derivados del recuento inicial más las
100 altas de calentamiento; las 503 altas medidas explican los 10.603 finales.

Inspección Docker posterior: **RestartCount=0, OOMKilled=false, Status=exited**.
El contenedor no reinició durante esta ejecución. Las 27 cuentas temporales quedaron
retiradas y se detuvieron exclusivamente los contenedores propios, conservando los volúmenes.
Artefactos locales: `frontend/test-results/hu07-performance/` seguido del proyecto anterior,
con report.json, samples.json y el manifiesto privado 0600; no incorporar credenciales a Git.

### Conclusión actualizada

La caída nativa del primer ensayo no se reprodujo. Sin embargo, tampoco sin ese reinicio
se alcanzan las 1.000 muestras de alta/corrección, y los tres p95 superan 2 s. Por tanto,
**el reinicio anterior no basta para explicar el incumplimiento de rendimiento**.
La repetición solicitada está ejecutada y documentada; T055 conserva su estado pendiente.
El siguiente trabajo es perfilar la latencia de API/SQL y del cliente bajo estas 25 sesiones,
investigar los 503 del calentamiento y optimizar lo que las mediciones identifiquen antes
de repetir el protocolo. T056/T058–T060 siguen pendientes, sin declarar cierre técnico.

## Perfil y optimizaciones posteriores — 2026-11-21

Se analizaron los logs de la API de la segunda ejecución durante su ventana medida,
sin modificar sus datos. Percentiles de duración de petición **en servidor**:
PUT compra 5.354 ms, POST compra 4.583 ms, GET listado de compras 2.427 ms y GET artículos
3.656 ms. Se observaron 40.497 comandos EF, p50 56 ms y p95 374 ms. Estas cifras no
incluyen los comandos manuales de applock ni sustituyen el percentil UI.

Query Store en la base aislada conservada identifica como consultas costosas la búsqueda
de artículos, count de compras por estado/período y páginas avanzadas de compras. El
listado con snapshots alcanza unas **10.959 lecturas lógicas medias** en la consulta
estado/período y unas 10.711 sin filtros: se recuperan datos de filas que luego se saltan.
No se atribuye toda la demora al navegador ni se cambia el significado de búsqueda.

Correcciones locales aplicadas:

1. Listado de compras pagina primero IDs/orden y luego recupera solo los campos de las
   filas de esa página. Count/filas siguen dentro del mismo SNAPSHOT.
2. Índice (State, CreatedAt, Number, Id), generado con EF en migración aditiva
   `20261120194234_HU07PurchasingQueryIndexes`. La migración de tablas y sus DENY se conservan.
3. Listado de artículos cuenta sin JOIN innecesario (FamilyId tiene FK obligatoria) y
   une familia después de paginar IDs. Conserva búsqueda por contenido, collation y permisos.
4. InventoryLocks envía la fase ordenada en un lote parametrizado y ejecuta sus applocks
   secuencialmente: mismo Transaction/public/timeout 5 s por recurso, primer negativo
   detiene el lote y el ejecutor revierte. Elimina hasta 19 viajes en una fase de 20 artículos.
5. Corrección de cantidades/precios sin reordenar líneas evita el guardado intermedio de
   posiciones temporales. Añadir/retirar/reordenar conserva ese mecanismo de protección.
6. El runner espera Guardar habilitado antes del reloj y registra por separado tiempo de
   acción, respuesta y representación; conserva códigos públicos/traceId ante errores HTTP.

Verificación previa al siguiente ensayo: prueba nueva de timeout en el recurso intermedio
**1/1 antes de la optimización**; después **59/59 pruebas afectadas** (operaciones,
StockCore, maestros, borradores, consultas y carreras propias), y **4/4 de migración/consulta**.
EF confirmó modelo sin diferencias al restaurar la herramienta en el contenedor.

Durante la regresión se reprodujo una carrera de limpieza de fixtures: DROP LOGIN podía
adelantarse al logout de una conexión del pool. Se limita ClearPool al login aleatorio de
ese fixture y se espera hasta 1 s exclusivamente ante SQL15434; los demás errores siguen
fallando. La selección de 59 pruebas pasó con limpieza incluida tras la corrección.
Un pase completo obtuvo 197 aprobadas/1 omitida V-14 y un fallo de arranque SQL al habilitar
SNAPSHOT en un fixture; ese caso específico pasó 1/1 al repetir. Ese pase **no se presenta
como regresión completa aprobada**; se comprobará de nuevo en T056.

Frontend: tras reproducir el arranque frío >5 s de la prueba de rutas HU-02 sin carga
concurrente, se incorpora el margen local de 20 s ya publicado por B en `cdb8674`, sin
cambiar aserciones ni código productivo. **136/136**, build y tipos E2E aprobados. No se
integra por ello la rama HU-05. Publicación Docker del backend optimizado correcta.
Cambios comunes documentados para revisión A+B; no se registra esa revisión como realizada.

## Tercer ensayo — Backend optimizado

Proyecto `inventario-hu07-e2e-performance-4df3419e036b4d5693f729f6543c3a45`;
ventana medida **2026-11-21 01:39:35 UTC +600 s**, tras el calentamiento de 120 s.
Datos iniciales completos verificados; mismos 25 contextos/mezcla/seed/tamaños/objetivo.
Resultado **INCONCLUSO**:

| Operación | Intentos / éxitos | Errores | p95 UI | p95 HTTP auxiliar |
|---|---:|---:|---:|---:|
| Consulta | 1.106 / 1.106 | 0 | 3.234 ms | 1.561 ms |
| Alta | 588 / 588 | 0 | 5.474 ms | 3.776 ms |
| Corrección | 555 / 555 | 0 | 6.649 ms | 4.010 ms |

Calentamiento: 494 observaciones, 100 altas y cero errores. Pedidos iniciales/finales:
10.000/10.688; resultados de operaciones 25.175/26.518. Entradas/movimientos/físico
permanecen en 1.000.000/5.000/1.000.000; todos los pedidos Draft/None. Se retiraron
27 cuentas y se detuvieron los contenedores propios. Se conservan report/samples/manifiesto
privado bajo el directorio ignorado de este proyecto. Finalización 01:50:58 UTC.

El desglose del runner detecta coste importante de preparación/orquestación: p95 de la
acción Playwright 1.905 ms en consultas y 1.887 ms en correcciones, además de la respuesta.
El log registra **2.786 llamadas a /auth/me** contando preparación y navegador: el ejecutor
recargaba toda Angular con page.goto antes de cada operación. Esto mantiene en arranque
frío una SPA que en uso normal conserva sesión/módulos entre vistas y carga innecesariamente
cliente/API durante el calentamiento y la medición.

Se corrige la preparación de rutas con `scripts/hu07-browser-profile.mjs`: navegación
history/popstate dentro del documento ya autenticado, equivalente al mecanismo del router
para atrás/adelante; solo el inicio de cada sesión carga la página completa. Los campos se
preparan fuera del reloj. Se conservan clics Buscar/Guardar reales, validación de la respuesta
correspondiente, filas/número persistidos visibles y los dos frames, 25 sesiones, mezcla,
ventanas, tamaños, filtros y mínimo de 1.000 por operación. No cambia el objetivo SC-007.

Una prueba temporal con Chrome/API/SQL reales verificó lista→alta→lista filtrada y la
conservación del mismo documento global: **1/1 aprobada, 2,4 s de recorrido**. Se retiró
el archivo de diagnóstico después de ejecutarlo; cuentas retiradas y entorno detenido.
El siguiente ensayo usa esta preparación SPA; los tres resultados anteriores se conservan.

## Cuarto ensayo — Preparación SPA

Proyecto `inventario-hu07-e2e-performance-f0b31e9b26c345eb8d21edba8719c0a9`.
Resultado **FAIL**, ya con muestra mínima en los tres tipos y cero errores medidos:

| Operación | Muestras exitosas | p95 UI | p95 HTTP auxiliar |
|---|---:|---:|---:|
| Consulta | 2.243 | 1.288 ms | 868 ms |
| Alta | 1.128 | 3.709 ms | 2.357 ms |
| Corrección | 1.112 | 3.766 ms | 2.338 ms |

Consulta supera su objetivo y la muestra mínima ya no bloquea; los dos guardados siguen
sin alcanzar ≤2 s. No se presenta como PASS ni se descartan las mediciones anteriores.

Las consultas de artículos por contenido siguen siendo una carga importante de preparación.
Se añade el filtro opcional `skuPrefix` al mismo catálogo y un modo explícito en el selector
de compras, manteniendo q por contenido como opción predeterminada. Usa el índice SKU ya
existente y no cambia conjuntos de artículos/proveedores, permisos o lógica de compra.
El ensayo selecciona ese modo para los códigos del manifiesto. Además se solicitan en
paralelo detalle/contexto, comprobando todavía igualdad de ETag/purchaseOrderVersion antes
de combinarlos. Las correcciones alternan 10/11 reconociendo también el formato decimal
11.000, para que volver al mismo pedido modifique realmente la cantidad.

Verificación selectiva: **12/12 backend** (catálogo, prefijo literal/case-insensitive,
comodines como texto, q compatible y consultas de compras) y **9/9 frontend** (formulario,
selector y detalle/contexto que pueden terminar en ambos órdenes). Imagen recompilada
correctamente. El siguiente ensayo conserva las condiciones SC-007 y prepara por prefijo
SKU desde una SPA autenticada, sin alterar su umbral ni mínimo de muestras.

## Quinto ensayo — Prefijo SKU y detalle/contexto paralelos

Proyecto `inventario-hu07-e2e-performance-459a4f63271d48db882e38ee5a980964`;
ventana medida **2026-11-21 02:34:31 UTC +600 s**, tras 120 s de calentamiento.
Resultado **FAIL**, sin errores en calentamiento ni medición:

| Operación | Muestras exitosas | p95 UI | p95 HTTP auxiliar |
|---|---:|---:|---:|
| Consulta | 2.176 | 1.557 ms | 1.034 ms |
| Alta | 1.072 | 4.080 ms | 2.860 ms |
| Corrección | 1.058 | 3.977 ms | 2.694 ms |

Calentamiento: 1.004 observaciones, 250 altas. Pedidos 10.000→11.322 (10.250 al inicio
medido, derivado); resultados 25.175→27.803. Entradas/movimientos/físico siguen en
1.000.000/5.000/1.000.000, sin pedidos fuera de Draft/None. Se retiraron 27 cuentas y
se detuvieron los contenedores propios. Artefactos conservados en el directorio ignorado
del proyecto; finalización 02:45:53 UTC.

Se inspeccionó Query Store del SQL aislado conservado: para el count por prefijo SKU y
estado, query_id 222 conserva un plan 205 con key lookup y un plan 209 con escaneo de índice
clusterizado. El log sitúa su p95 en 727 ms: el índice único SKU no cubría IsActive.
Se añade (IsActive, Sku, Id) con INCLUDE de los campos de listado, mediante la migración
aditiva `20261121025020_HU07ArticleLookupIndex`. Se conservan búsquedas, filas y precisión.
**10/10 pruebas de catálogo/migración aprobadas y EF sin diferencias**. El siguiente ensayo
completo verifica este índice; todavía no se registra SC-007 como aprobado.

## Sexto ensayo — Índice de cobertura y ventana interrumpida

Proyecto `inventario-hu07-e2e-performance-b246f9bd5bf64782887b12a4f337e37f`.
Inicio medido 2026-11-21 03:02:49 UTC, informe final a las 09:47:59 UTC. Ese salto
de reloj es incompatible con una ventana válida de 600 s. No se determinó su causa
exacta; Docker informa `RestartCount=0` y `OOMKilled=false`. El calentamiento registró
1.006 observaciones y cero errores; después hubo timeouts de red, espera y render.

| Operación | Intentos / éxitos | Errores | p95 UI diagnóstico |
|---|---:|---:|---:|
| Consulta | 64 / 62 | 2 | 1.953 ms |
| Alta | 29 / 9 | 20 | 3.869 ms |
| Corrección | 12 / 9 | 3 | 4.283 ms |

**INCONCLUSO**: no permite evaluar el índice bajo una ventana válida. Recuentos finales:
10.260 pedidos, 25.685 resultados y 1.000.000 de entradas sin cambio. Se retiraron 27
cuentas y se detuvo el proyecto. Se añade una comprobación de continuidad de reloj
al runner. En macOS la siguiente ejecución se lanza con `caffeinate -is` para
mantener el equipo despierto mientras se mide.

## Séptimo ensayo — Ventana continua e índice de cobertura

Proyecto `inventario-hu07-e2e-performance-6bbb9bf4b99d448ca270d474b46ad15a`.
`caffeinate -is env HU08_E2E_BROWSER_CHANNEL=chrome node scripts/measure-hu07-purchasing.mjs`.
Ventana medida: **2026-11-21 10:03:57 UTC +600 s**. Calentamiento de 120 s:
1.318 observaciones, 324 altas, cero errores. Mismo perfil de 25 sesiones, seed 7007,
10.000 artículos/pedidos iniciales y 1.000.000 de entradas. No se registró un salto
de reloj. Intervalo de creación: [09:57:00, 10:02:00) UTC del mismo día.

| Operación | Intentos / éxitos | Errores / timeouts / conflictos | p95 UI | p95 HTTP |
|---|---:|---:|---:|---:|
| Consulta | 2.765 / 2.765 | 0 / 0 / 0 | 1.159 ms | 851 ms |
| Alta | 1.386 / 1.386 | 0 / 0 / 0 | 3.503 ms | 2.468 ms |
| Corrección | 1.389 / 1.389 | 0 / 0 / 0 | 3.484 ms | 2.433 ms |

**FAIL**: se supera el mínimo de muestras para cada operación y la consulta cumple
≤2 s, pero no los guardados. Al terminar: 11.710 pedidos Draft/None, 28.577 resultados,
1.000.000 de entradas y 5.000 movimientos; el físico total no cambió. Se retiraron
las 27 cuentas temporales y se detuvieron los contenedores del proyecto a las 10:15:15 UTC.
El corte probado ya tenía ambos índices. El cambio posterior T061 afecta al seguimiento
de entregas pendientes, que este perfil Draft/None no ejercita.

T055 sigue pendiente. El log de ASP.NET en la ventana medida confirma p95 **en servidor**
de 2.353 ms en POST y 2.363 ms en PUT (1.392 respuestas por ruta, incluidas las que cruzan
el límite del recorte temporal del log). GET de compras: 682 ms; GET de artículos: 821 ms.
El diagnóstico restante debe separar espera de bloqueo, consultas, persistencia y commit
en POST/PUT. Conviene contrastar en un entorno SQL x64 soportado antes de atribuir toda
la latencia al código o a la emulación. Mantener el perfil y el umbral para acreditar SC-007.

## Ensayo de cierre — Preparación y repetición

El proyecto `inventario-hu07-e2e-performance-ce51570e7dd64f4cb129c39d0b49b851`
agotó los reintentos SQL al preparar el historial sintético (1205 en el guardado del
ejecutor). No empezó la ventana de medición. El fixture prepara ahora los primeros
50 grupos secuencialmente y limita a ocho el paralelismo del resto de la generación de
historial; el navegador conserva 25 sesiones y el volumen no cambia.

La repetición usa un override Compose exclusivo del ensayo para emitir comandos EF desde
Warning en vez de registrar cada comando exitoso. Primera y última página se preparan
desde el listado sin consulta auxiliar de recuento; para la intermedia se conserva ese
recuento. La consulta medida se sigue disparando desde la UI y se valida su resultado.

La repetición `inventario-hu07-e2e-performance-d760125b23df4d1d98432c165c098ebc`
completó la preparación y la ventana de 600 s desde **2026-11-21 10:37:56 UTC**.
Mismo host y versiones del séptimo ensayo, 25 sesiones Chrome, seed 7007, 10.000
artículos/pedidos iniciales y 1.000.000 de entradas. Intervalo de creación
[10:31:00, 10:36:00) UTC del mismo día. Calentamiento de 120 s con 1.228 observaciones,
302 altas y un `503 LockTimeout` en corrección, registrado aparte.

| Operación | Intentos / éxitos | Errores / timeouts / conflictos medidos | p95 UI | p95 HTTP |
|---|---:|---:|---:|---:|
| Consulta | 2.982 / 2.982 | 0 / 0 / 0 | 1.252 ms | 1.041 ms |
| Alta | 1.485 / 1.485 | 0 / 0 / 0 | 3.653 ms | 2.713 ms |
| Corrección | 1.483 / 1.483 | 0 / 0 / 0 | 3.605 ms | 2.624 ms |

**FAIL SC-007**: muestra suficiente y consulta ≤2 s; ambos guardados incumplen. La
reducción de carga auxiliar no redujo sus percentiles frente al séptimo ensayo. Resultado
reproducible en `report.json` y `samples.json` del proyecto aislado. Recuento final:
11.787 pedidos, 28.734 resultados, 1.000.000 de entradas y 5.000 movimientos, con
físico sin cambio. Las 27 cuentas quedaron retiradas y el proyecto se detuvo a las
10:49:14 UTC. El criterio de rendimiento queda abierto en T055 y referenciado en
el [backlog común](../../001-reparto-specs-inventario/integration-tasks.md).

## Ensayo adicional — Reducción de consultas del flujo de compras

Proyecto `inventario-hu07-e2e-performance-85340f16b3de4af78bc9a32f03e199b7`.
Ventana medida: **2026-11-21 13:50:50 UTC +600 s**. Se mantuvieron las 25 sesiones,
la mezcla, el seed 7007, las 10.000 compras iniciales y el millón de entradas. Calentamiento:
741 observaciones, 187 altas y cero errores. La imagen experimental agrupaba los locks
documentales del alta, omitía lecturas de referencias ya conocidas bajo los locks y
evitaba cargar catálogos de nuevo al corregir un borrador o paginar el listado.

| Operación | Muestras válidas | Errores medidos | p95 UI | p95 HTTP |
|---|---:|---:|---:|---:|
| Consulta | 1.800 | 0 | 1.972 ms | 1.506 ms |
| Alta | 899 | 0 | 4.827 ms | 3.603 ms |
| Corrección | 920 | 0 | 5.112 ms | 3.657 ms |

**INCONCLUSO:** alta y corrección no alcanzaron la muestra mínima; sus percentiles
diagnósticos siguieron por encima de 2 s. En los logs de la ventana, POST/PUT alcanzaron
3.485/3.518 ms de p95 en servidor. También aumentó la latencia de consultas sin cambios
en su lógica principal; la comparación no permite atribuir el deterioro a una sola causa.
Después del ensayo no constaban reinicios ni OOM del SQL. Se conservaron 11.086 pedidos,
el millón de entradas y el mismo saldo total; el proyecto se detuvo y retiró 27 cuentas.

Se retiró el cambio experimental y se reconstruyó la imagen del corte funcional, al no
acreditar una mejora. En la selección exploratoria, las 18 pruebas frontend pasaron;
backend tuvo 70 aprobadas y un fallo de inicialización del fixture SQL, superado al repetir
ese caso de manera aislada. La [regresión integrada](integration.md) sigue correspondiendo
al corte funcional conservado. T055 solo se cierra tras un nuevo ensayo representativo
que cumpla el protocolo; para el siguiente contraste se requiere un entorno dedicado SQL
Server x64 con recursos documentados, aislado de otras cargas. El ensayo conserva los
umbrales originales.
