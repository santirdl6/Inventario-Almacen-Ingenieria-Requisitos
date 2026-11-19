# Guía de validación de HU-08

**Estado actualizado 2026-11-18**: proveedores, retirada H1 y recuperación implementados.
Pasan 76 pruebas backend SQL, 36 unitarias frontend, build de producción, tipado E2E y
comprobación de modelo EF. Pasan 11 E2E funcionales; SC-005 final sin trazas alcanza
p95 1,677 s, 193/200 búsquedas en ≤2 s y cero errores, con la primera medición fallida
conservada en [rendimiento](validation/performance.md). Evidencia por historia en [US3](validation/us3.md),
[US4](validation/us4.md), [US5](validation/us5.md); ejecución de navegador y límites
consolidados en [aceptación técnica](validation/acceptance.md). El diario, referencias
reales, stock/documentos, validación humana y revisión A+B siguen pendientes.
Antecedentes: [foundation](validation/foundation.md), [US1](validation/us1.md),
[US2](validation/us2.md) y [baseline](validation/baseline.md).

**Referencias**: [plan](plan.md), [modelo](data-model.md), [HTTP](contracts/api.md),
[UI](contracts/ui.md), [integración](contracts/integration.md) y
[guía común](../../.specify/memory/project/quickstart.md).

## 1. Preparación y estado esperado

Desde la raíz del repositorio, con Docker iniciado y memoria/emulación según README:

```bash
docker compose config --quiet
docker compose up --build -d --wait --wait-timeout 300
docker compose ps -a
curl --fail --silent http://localhost:8081/api/v1/health
```

Resultado de infraestructura: initialize Exited(0), app saludable y JSON healthy/ready.
Los catálogos están en /maestros/articulos, /maestros/familias y /maestros/proveedores después de iniciar sesión
con una cuenta gestora o lectora preparada controladamente. /maestros redirige a artículos.
OpenAPI en Development describe sesión, resultados, los tres catálogos, contexto y borrado
de artículos y consulta de movimientos. Sin lector real de diario, esta última devuelve
503 DependencyUnavailable; no documenta un historial vacío como integración terminada.
SQL no publica puerto al host y la aplicación conserva la conexión sin DDL.

**Si hay artículos H0 existentes**: realizar primero la transición en mantenimiento descrita
en data-model, con respaldo y mapa explícito aprobado. No ejecutar a ciegas el arranque que
aplica todas las migraciones esperando que invente familias. La migración de cierre debe
rechazar filas sin clasificar; la prueba Q-11 acredita esa conducta y la transición válida.
En una base nueva vacía no se necesitan asociaciones ni datos de negocio automáticos.

Procedimiento implementado para una base H0 con artículos:

1. Respaldar y entrar en mantenimiento; preparar antes las familias destino y una cuenta
   gestora autorizada. Aplicar solo la expansión con la herramienta de migración externa.
2. Iniciar el modo `Maintenance__Hu08FamilyPreparation=true` sobre la conexión habitual
   sin DDL. Verificar que no se habilitan endpoints ordinarios de maestros/escritores de
   negocio y que las rutas de sesión siguen disponibles. Health debe responder 503; este
   proceso no se anuncia listo para la operación ordinaria, incluso con esquema final.
3. Autenticar a la persona responsable por csrf→login→me. Enviar el mapa aprobado en lotes
   de hasta 100 entradas a POST /api/v1/maintenance/hu08/family-map con cookie, token y clave
   estable por lote. No enviar ActorId ni credenciales SQL administrativas a la API.
4. Comprobar asociaciones, políticas, marcas y evidencia con el actor de la sesión; repetir
   una intención debe recuperar su resultado sin duplicar. Un lote inválido no cambia nada.
5. Detener ese proceso, aplicar el cierre externamente y arrancar sin el modo de mantenimiento.
   Verificar familia obligatoria, readiness normal y 404 de la ruta de mantenimiento.

Q-11 ejecutado cubre además 401/403, intento de proporcionar actor, rollback por auditoría,
replay y funcionamiento de la proyección nullable. SQL 51002 impide cerrar si falta familia
o política. Los nombres de migraciones son `20261117095524_HU08MasterDataExpand` y
`20261117095527_HU08RequireArticleFamily`; las anteriores H0 permanecen intactas.

Con SDK .NET 10, desde `backend/`, la herramienta externa selecciona explícitamente cada
objetivo. `ConnectionStrings__Inventory` debe proceder del entorno privado de migración,
con conexión administrativa a la copia objetivo; esa conexión no se entrega al proceso API:

```bash
dotnet tool restore
dotnet ef database update 20261117095524_HU08MasterDataExpand --project src/Inventario.Api --startup-project src/Inventario.Api
# Ejecutar el mapa autenticado, comprobarlo y detener mantenimiento antes del cierre.
dotnet ef database update 20261117095527_HU08RequireArticleFamily --project src/Inventario.Api --startup-project src/Inventario.Api
```

No ejecutar ambos objetivos consecutivamente sobre filas H0 sin preparar. La prueba SQL
automatizada aplica estos mismos objetivos mediante IMigrator, sobre bases aisladas,
con una FK técnica externa para comprobar conservación de UUID referenciado. No acredita
documentos/stock todavía ausentes. Una base nueva vacía aplica toda la cadena sin seed.

Preparar controladamente cuentas de GestorMaestros y consulta de inventario con los permisos
del contrato, sin crear un API de usuarios para esta historia. El administrador local inicial
no sustituye a esas cuentas. Los fixtures SQL existentes permiten crear usuarios por rol;
los fixtures E2E preparan las cuentas de su entorno aislado por el mismo mecanismo.
No reutilizar contraseñas reales ni escribir datos de stock fuera de los comandos del núcleo.

## 2. Compilación y suites

### Backend con contenedores

Desde la raíz, comando ya soportado; al integrar HU-08 debe incluir sus nuevas pruebas:

```bash
docker compose --profile test run --build --rm tests
```

Variante ejecutada sobre la imagen SDK existente y código actual montado, con sqlserver
saludable; evita reconstruir esa imagen sin reutilizar una compilación antigua del código:

```bash
docker compose --profile test run --no-deps --rm \
  --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c \
  'dotnet test Inventario.slnx --configuration Release --no-restore --nologo'
```

Si aún no se restauraron dependencias en ese árbol, ejecutar antes `dotnet restore
Inventario.slnx` en el mismo contenedor. La comprobación EF se puede añadir tras las pruebas
con `dotnet tool restore` y el comando has-pending-model-changes de la sección siguiente.

El fixture crea y elimina bases/logins aleatorios de pruebas. Nunca reutiliza InventoryDev.
No aceptar EF InMemory/SQLite como evidencia de bloqueos, restricciones o rowversion de SQL.
Los tests deben comprobar las mutaciones con el usuario de ejecución, incluidos sus DENY.

### Backend con SDK local y SQL de pruebas accesible

Desde `backend/`; SQL remoto o VM accesible, porque Compose no publica su puerto al host:

```bash
dotnet tool restore
dotnet restore Inventario.slnx
dotnet build Inventario.slnx --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api --configuration Release --no-build
export Testing__SqlConnection="${INVENTARIO_TEST_SQL_CONNECTION:?Definir SQL de pruebas}"
dotnet test Inventario.slnx --configuration Release --no-build
```

Resultado tras implementar: modelo/snapshot consistentes, suite H0 y maestros aprobada.
Para aislar las nuevas clases propuestas, usar después de crearlas:

```bash
dotnet test Inventario.slnx --configuration Release --filter FullyQualifiedName~MasterData
```

### Frontend

Desde `frontend/`, Node 24 LTS ≥24.15.0. Runners, sesión, los tres catálogos, retirada y recuperación están disponibles.
Para E2E de navegador instalar Chromium una vez. Comandos actuales:

```bash
npm ci
npm run build
npm test
npm run test:e2e:types
npx --no-install playwright install chromium
npm run test:e2e
```

El target `@angular/build:unit-test` usa Vitest 5.1.0, jsdom 31.0.0 y cobertura V8 5.1.0.
La cobertura actual corresponde a los archivos ejercitados de H0/fundamentos/familias/artículos,
no a HU-08 completa. Playwright 1.65.0
usa aplicación real en un proyecto Compose dedicado `inventario-hu08-e2e`, con
DB_NAME=InventoryHu08E2E, APP_PORT=8082 y MAILPIT_PORT=8026; baseURL configurable mediante
HU08_E2E_BASE_URL, por defecto `http://localhost:8082`; solo admite una URL HTTP de loopback
con puerto distinto de 8081. Los ejemplos manuales posteriores usan el entorno de validación
de 8081. El perfil Compose test no ejecuta estas suites frontend. El servidor de desarrollo
opcional usa `npm start -- --port 4200` con el proxy existente.

### Provisionamiento y limpieza E2E implementados

El fixture compila/arranca el proyecto dedicado y ejecuta `scripts/provision-e2e-users.cs`
con el SDK del servicio tests, montando el código actual. Ese programa file-based de .NET 10
reutiliza `Fixtures/MasterDataTestData.cs` del proyecto de integración y UserManager/Identity
reales; no añade otro proyecto productivo ni una ruta de administración a la API.

La base objetivo es **InventoryHu08E2E**, la misma que usa la aplicación E2E, no la base
aleatoria del fixture xUnit. El helper exige opt-in explícito y host sqlserver dedicado,
esquema actualizado y tres cuentas únicas de la misma ejecución en el dominio hu08.e2e.test.
Las credenciales temporales se pasan por entorno al contenedor, no se guardan en archivos
del repositorio ni se imprimen. Los roles son GestorMaestros, Operario para consulta sin
gestión de maestros y Administrador sin permisos implícitos de inventario.

Cada cliente obtiene csrf, inicia sesión, usa su token renovado y consulta me. Al acabar
cierra sesión; el helper bloquea las cuentas y renueva sus security stamps sin borrar actores
ni romper futuras referencias de auditoría. Se comprueba que el gestor retirado ya no puede
iniciar sesión. Finalmente se detienen solo los contenedores dedicados, con 60 s de gracia;
no se borran volúmenes ni se reinicia InventoryDev. No ejecutar dos suites sobre el mismo
proyecto dedicado simultáneamente.

Opciones del fixture:

- `HU08_E2E_SKIP_BUILD=1`: reutilizar imágenes ya construidas del mismo código; el helper
  sigue rechazando un esquema con migraciones pendientes. El arranque predeterminado compila.
- `HU08_E2E_KEEP_RUNNING=1`: conservar los contenedores para diagnóstico; las cuentas de
  la ejecución se retiran igualmente y sus credenciales no sirven para entrar después.
- `HU08_E2E_BASE_URL`: cambiar el puerto local; el fixture configura APP_PORT acorde a él.
- `HU08_E2E_BROWSER_CHANNEL=chrome`: usar Chrome instalado en perfiles temporales de
  Playwright. Por defecto se utiliza el Chromium de Playwright; basta instalar su shell con
  `npx --no-install playwright install chromium --only-shell` para ejecución headless.

Los tres casos de environment.spec.ts son HTTP reales sin navegador. Las suites de
familias, artículos, proveedores, retirada y fiabilidad abren Chromium e interactúan con
login, formularios, filtros, estados, permisos, comparación de versiones y recuperación.
SC-005 añade preparación auditada de 10.000 artículos y 25 contextos de navegador.
Para omitir únicamente esa medición: `npm run test:e2e -- --grep-invert SC-005`. No confundir sus resultados con la aceptación de las otras
historias ni con la validación humana SC-006. No ejecutar compilaciones SDK con el mismo
árbol montado simultáneamente: el auxiliar E2E y los tests backend comparten bin/obj.

## 3. Recorrido HTTP reproducible tras implementar

Requisitos adicionales: curl, jq, uuidgen y cuenta gestora del entorno de validación.
Definir HU08_EMAIL/HU08_PASSWORD en el entorno privado; nunca guardarlas en el repositorio.
Usar códigos libres por ejecución para no confundir duplicados legítimos con fallos.

Desde la raíz:

```bash
BASE=http://localhost:8081/api/v1
JAR=$(mktemp)
TOKEN=$(curl --fail --silent -c "$JAR" "$BASE/auth/csrf" | jq -r .requestToken)
LOGIN=$(jq -n --arg email "$HU08_EMAIL" --arg password "$HU08_PASSWORD" '{email:$email,password:$password}')
TOKEN=$(curl --fail --silent -b "$JAR" -c "$JAR" \
  -H 'Content-Type: application/json' -H "X-XSRF-TOKEN: $TOKEN" \
  -d "$LOGIN" "$BASE/auth/login" | jq -r .requestToken)
curl --fail --silent -b "$JAR" "$BASE/auth/me"
KEY=$(uuidgen)
CODE="F-$(uuidgen)"
BODY=$(jq -n --arg code "$CODE" '{code:$code,name:"Familia de validación",isActive:true}')
FAMILY=$(curl --fail --silent -b "$JAR" -c "$JAR" \
  -H 'Content-Type: application/json' -H "X-XSRF-TOKEN: $TOKEN" \
  -H "Idempotency-Key: $KEY" -d "$BODY" "$BASE/families")
FAMILY_ID=$(printf '%s' "$FAMILY" | jq -r .resource.id)
curl --fail --silent -D - -b "$JAR" "$BASE/families/$FAMILY_ID"
```

Esperado: login/me exitosos; alta 201 con Location/ETag y MutationResult; GET detalle 200
con el mismo UUID. Repetir el último POST con la misma KEY/BODY produce la misma respuesta
sin otro maestro ni auditoría. Cambiar el cuerpo con esa KEY devuelve 409 IdempotencyConflict.

Continuar con los cuerpos definidos en contracts/api: crear Article con FAMILY_ID y Supplier
con correo opcional. Cada intención nueva usa otro UUID; conservar clave y cuerpo cuando
se pruebe un replay. No copiar el token HttpOnly interno a X-XSRF-TOKEN.

Para editar, copiar íntegro el ETag del GET —comillas incluidas—, enviar If-Match, nueva clave
idempotente y PUT completo. Guardar una edición y luego intentar otra basada en el ETag
anterior debe dar 412; repetir la intención de la primera edición debe recuperar su 200.
Sin If-Match en una intención nueva: 428.

Para probar borrado, crear un artículo sin referencias: DELETE con If-Match y clave devuelve
204; repetirlo devuelve 204; GET del artículo devuelve 404 y GET /operations/{clave} conserva
el outcome 204. Comprobar en la suite SQL que la auditoría permanece. Cerrar sesión con el
token autenticado y eliminar el fichero temporal de cookies al terminar.

## 4. Matriz de aceptación verificable

| ID | Preparación/acción | Resultado esperado y requisitos |
|----|--------------------|---------------------------------|
| Q-01 | Sesión real: anónimo, lector, gestor y administrador sin rol de maestros; mutación sin CSRF. | 401/403/400 correctos; lector consulta sin modificar; gestor autorizado; FR-007/020. |
| Q-02 | Altas/consultas/modificaciones de los tres catálogos; códigos repetidos con espacios/case e inactivos; familia inválida; correo ausente/inválido. | Persistencia correcta, duplicados rechazados, campos conservados; Article+StockPolicy cero sin saldo/movimiento; FR-001–008, SC-001/002. |
| Q-03 | Dos sesiones cargan la misma versión y guardan cambios incompatibles. | Solo la primera confirma; segunda 412 sin sobrescritura; ETag ausente 428; FR-017. |
| Q-04 | Repetir alta/PUT/DELETE con misma clave; payload distinto; replay tras cambios posteriores y tras borrar. | Resultado y ETag/Location originales, sin duplicados; DELETE recuperable aun con recurso 404; FR-011/015/018/021. |
| Q-05 | Editar definición nunca usada y después de primera referencia, incluso retirada; verificar consumidor sin enlace y H1 sin consumidores. | Nunca usado y gate completo permite; primer uso bloquea permanentemente; consumidor sin enlace da 503; ausencia de consumidores acreditada permite H1; FR-009/019. |
| Q-06 | Fallo inducido entre escrituras de maestro/política/auditoría/resultado y antes del commit; simular respuesta perdida después del commit. | Rollback completo o resultado recuperable, nunca un éxito parcial ni otro efecto al reintentar; FR-016/018. |
| Q-07 | Conexiones SQL independientes, barreras de solapamiento: primera referencia/edición, referencia/borrado, alta/asignación/desactivación de familia, operación nueva/desactivación de artículo. | Ambos órdenes coherentes, sin huérfanos ni uso de definición/estado obsoletos; FR-003/010/019. |
| Q-08 | Revisar autor y snapshots; borrar artículo legítimo; intentar UPDATE/DELETE de auditoría y OperationResult con usuario API. | Evidencia y resultado conservados e inalterables; actor de sesión, no del cuerpo; FR-011/015/016, SC-007. |
| Q-09 | Desactivar/reactivar maestros; historial real de inactivo; reservas/pedidos/recepciones nuevos y compromisos existentes; proveedor en pedidos previos. | Conserva enlaces, snapshots y stock; restricciones/acciones permitidas comunes; historial filtrable; FR-010/012–014, V-12 integrado. |
| Q-10 | Formularios, búsqueda/filtros/páginas, errores, confirmaciones, teclado, permisos, timeout y recarga. | Estados claros, campos válidos conservados, intención estable y sin falso éxito; FR-006/020/021, SC-006. |
| Q-11 | Actualizar copia H0 con UUID referenciados: expansión, preparación HTTP autenticada, cierre; probar mapa inválido, actor en cuerpo, permisos, rollback y replay. | Conserva UUID/stock, actor real de sesión, escritura atómica sin duplicados, familia obligatoria y ruta de mantenimiento ausente fuera del modo; falla sin clasificar; revisión A+B registrada. |

Q-05/Q-07/Q-09 se completan con las tablas/comandos reales de las features consumidoras.
Los dobles sirven para pruebas unitarias, no para certificar integración. Si no existe el
proveedor de diario, comprobar 503 DependencyUnavailable, no un falso historial vacío.

## 5. Rendimiento y validación con trabajadores

Preparar 10.000 artículos mediante comandos/fixtures auditados y 25 sesiones concurrentes;
medir al menos 200 búsquedas paginadas por código/nombre/familia/estado y registrar p95,
errores, entorno y volumen. Objetivo SC-005: al menos 95 % en ≤2 s. No se acredita por
medir solo SQL ni por eliminar muestras fallidas. La búsqueda no modifica datos.

En H5-01 del [backlog receptor](../001-reparto-specs-inventario/integration-tasks.md),
tres gestores representativos realizan cada uno las tareas de SC-006: alta de familia,
artículo y proveedor, corrección de dato inválido y desactivación, sin asistencia técnica
ni errores no detectados. Registrar tiempos, resultados, incidencias y correcciones.
Playwright no sustituye este ejercicio. Antes de producción se aplica V-14 y la aprobación
de recuperación/contingencia compartidas; no se introduce otro objetivo RPO/RTO local.

## 6. Evidencia y cierre

Conservar fecha, commit, entorno, resultados por Q/FR/SC, comandos y reportes de suites,
concurrencia, migración y validación humana. Separar evidencia disponible de H1 y aceptación
integrada pendiente; ningún escenario escrito equivale a prueba aprobada.
Los responsables definidos por la constitución revisan los resultados antes de aceptar
la entrega. Las decisiones compartidas se revisan A+B antes de fusionar migraciones/contratos.

## 7. Convergencia: historial poblado y filtros legibles

Desde `frontend/`, con Docker iniciado, ejecutar:

```bash
HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- e2e/master-data-lifecycle.spec.ts
```

Se puede omitir `HU08_E2E_BROWSER_CHANNEL` para usar Chromium instalado por Playwright.
El fixture crea un artículo auditado por HTTP y prepara dos entradas de recepción y una
salida mediante `scripts/prepare-hu08-history.cs`, el ejecutor común y StockCore, con
la identidad controlada Operario. El auxiliar usa la conexión de ejecución, solo admite
InventoryHu08E2E y no instala endpoints de prueba ni altera InventoryDev. Compara diario
y saldos antes/después de desactivar/reactivar; comprueba filtros y selección por teclado.
Las cuentas se retiran aunque falle una prueba y los actores históricos se conservan.

T099 forma parte de `MasterDataLifecycleConcurrencyTests`: usa conexiones independientes
y espera observable de applock, no llamadas secuenciales. Los resultados actuales, fallos
corregidos y dependencias pendientes están en [convergencia](validation/convergence.md).

## 8. Cierre técnico y actualización aditiva del diario

HU-08 queda cerrada según [aceptación](validation/acceptance.md#cierre-final-de-hu-08-2026-11-19).
No hay tareas de implementación locales pendientes. Los módulos futuros y H5 reciben
sus comprobaciones con IDs en el backlog receptor; no se reabre el catálogo por su ausencia.

La migración final `20261119171332_HU08MovementCounterparty` añade motivo de no aplicabilidad
de contraparte al diario. No actualiza movimientos confirmados: la CHECK se habilita sin
validar retroactivamente las filas antiguas y se exige a cada inserción nueva. En producción
se aplica externamente antes de usar el nuevo lector; hasta disponer de esa migración, el
historial devuelve 503 DependencyUnavailable. El Compose de pruebas/E2E la aplica en sus
bases aisladas. Referencia o motivo es obligatorio en hechos nuevos; el replay conserva las
huellas históricas. Las pruebas `StockCounterpartyTests` cubren actualización de esquema,
rechazos, permisos, proyección e idempotencia.
