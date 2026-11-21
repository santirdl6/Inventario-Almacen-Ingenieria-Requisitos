# Guía de ejecución y validación conjunta

**Estado**: H0 incluye frontend de estado, API de sesión Identity, policies por rol,
migraciones y bootstrap de identidad, pruebas de integración SQL y arranque contenedorizado.
Los recorridos de negocio y su interfaz se completarán con las historias del
[plan global](../project-plan.md). Los hitos se describen en el
[plan de coordinación](../../../specs/001-reparto-specs-inventario/plan.md).
Los escenarios de negocio definen resultados esperados pendientes de sus funcionalidades.

## 0. Arranque recomendado con contenedores

Con Docker iniciado, desde la raíz y sin SDKs locales ni preparación manual:

```bash
docker compose up
```

Aplicación en http://localhost:8081, API de estado en /api/v1/health y buzón de correo
en http://localhost:8025. La API sirve Angular; la base no expone puerto al host.
Compose espera SQL, ejecuta la inicialización una vez por arranque y solo inicia la app
tras su éxito. Repetir no borra datos. Variables y operaciones en el [README](../../../README.md).

Validación V-15 de infraestructura:

```bash
docker compose config --quiet
sh scripts/verify-containers.sh
```

Resultado esperado: health HTTP 200 con conexión SQL, ruta SPA accesible, migración aplicada
y dato técnico conservado después de recrear contenedores. La prueba deja el entorno
arrancado y elimina su tabla de comprobación. Se espera `initialize` en Exited (0).

Los volúmenes conservan datos SQL, claves y mensajes. Detener con `docker compose down`
no los elimina; `docker compose down --volumes` sí borra expresamente los datos locales.
En ARM, SQL necesita emulación amd64 de Docker; para producción se utiliza x64 soportado.

Verificación de infraestructura realizada el 2026-10-25 en Docker Desktop ARM con SQL
amd64: build frontend y backend correctos, modelo EF sin cambios pendientes, arranque sin
imagen app previa, inicializador Exited (0), health SQL 200, dato conservado al recrear
contenedores, puerto publicado cambiado mediante entorno y API desconocida con 404.
El usuario de ejecución se comprobó sin sysadmin ni permiso CREATE TABLE.

### H0 de Persona A: sesión y permisos

Tras cambiar el backend, actualizar la imagen y esperar a la inicialización:

```bash
docker compose up --build -d --wait --wait-timeout 300
docker compose --profile test run --build --rm tests
```

El inicializador añade `H0Identity` sobre el historial existente sin borrar la migración
inicial. Crea cinco roles y una cuenta Administrador; repetir no duplica usuarios ni
restablece su contraseña. La conexión administrativa solo se usa en el inicializador.
Cuenta local y variables `INITIAL_ADMIN_EMAIL` / `INITIAL_ADMIN_PASSWORD` en el README.

El borrador generado se consulta en http://localhost:8081/openapi/v1.json durante Development.
Las operaciones de sesión son csrf → login → me → logout. GET csrf devuelve requestToken,
una cookie interna HttpOnly y XSRF-TOKEN legible; el cliente conserva cookies y envía el
token legible en X-XSRF-TOKEN. Login devuelve un token nuevo ligado al usuario y logout
lo requiere. Sin sesión, me/logout devuelven 401, sin redirección a una página HTML.

Pruebas del proyecto `Inventario.IntegrationTests`: anonimato, antiforgery ausente/ajeno,
rotación de token, validación de campos, bloqueo tras cinco intentos fallidos, combinación
de permisos sin duplicados, Administrador sin privilegios implícitos de stock, seed repetible,
usuario SQL sin DDL, sesión recuperable por otra instancia con las mismas claves,
cookies Secure de producción y OpenAPI limitado a Development.

Se crea una base `InventoryH0Tests_...` y un login aleatorios por ejecución; el fixture
solo puede eliminar los nombres que generó, nunca InventoryDev. El perfil Compose usa
Testing__SqlHost y Testing__SqlAdminPassword; fuera de Docker se exige una conexión
Testing__SqlConnection explícita con sa de un servidor de desarrollo/pruebas.

Verificación de esta entrega H0-A: 8 pruebas de integración aprobadas, migración y modelo
consistentes y `initialize` terminado con código 0 sobre el entorno Compose existente.
Se comprobó además por HTTP real que login/me/logout funcionan, que la cuenta no cambia
al repetir la inicialización y que la cookie sigue siendo válida tras recrear la app
con el mismo volumen de claves. El proceso API no recibe las contraseñas administrativas
de bootstrap o de SQL. El trabajo del núcleo de stock de B y la revisión conjunta de H0
se completan según el plan de coordinación.

### Desbloqueo A → B: Article e ICurrentActor

La cadena añade `20261112105657_H0ArticleReference` después de `H0Identity`. El inicializador
aplica la tabla `Articles`; no crea artículos de negocio. La familia y el mantenimiento
de artículos/proveedores siguen en HU-08. B consume la referencia existente y el servicio
scoped `ICurrentActor`; detalles en el
[handoff H0](../../../specs/002-gestionar-datos-maestros/h0-handoff.md).

Verificación realizada el 2026-11-12 con SQL Server real en Docker: **18 pruebas aprobadas**
(8 de Identity y 10 casos nuevos de referencia/actor). Incluye unicidad SQL de SKU sin
distinguir mayúsculas, metadata y rowversion, edición obsoleta, rollback de alta técnica,
actor ausente/anónimo/malformado/vacío/inexistente, sesión real y rechazo tras borrar al
usuario. Modelo y snapshot consistentes según `dotnet ef migrations has-pending-model-changes`.
Los fixtures migran bases aleatorias y prueban también reinicialización sin resetear cuentas.
No se acredita con estas pruebas la idempotencia ni la concurrencia de stock: corresponden
al núcleo H0 de B y necesitan sus tablas y comandos.

La compilación y pruebas se ejecutaron sobre el código actual montado en la imagen SDK
de pruebas ya disponible. El intento de reconstruir la imagen con `--build` agotó el tiempo
de descarga de una nueva imagen base; no fue un fallo de compilación. El comando utilizado
desde la raíz para las 18 pruebas fue:

```bash
docker compose --profile test run --no-deps --rm \
  --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c \
  'dotnet restore Inventario.slnx --force && dotnet test Inventario.slnx --configuration Release --no-restore --nologo'
```

Este comando de verificación necesita `sqlserver` saludable e imagen `inventario-h0-tests:local`
existente; el comando ordinario con `--build` sigue siendo el arranque reproducible del README.
La verificación aplica las migraciones solo en bases aisladas de pruebas, no en InventoryDev.

### Núcleo de stock H0 (B) sobre la base común de A

La cadena añade `20261118194940_H0_StockCore` después de `HU08Suppliers`, sin modificar
migraciones previas: Locations (referencia mínima de HU-02), Lots, StockBalances,
StockMovements y StockEntries, con las FK, CHECK e índices del [modelo](data-model.md).
La migración deniega a `inventory_runtime` UPDATE/DELETE sobre StockMovements y
StockEntries y DELETE sobre StockBalances, como hace A con OperationResults y la auditoría.
`StockCore` se ejecuta dentro de `InventoryOperationExecutor` y usa `InventoryLocks`
y el único `OperationResult` de A según el [protocolo](contracts/stock-operations.md).

Verificación realizada el 2026-11-18 con SQL Server real de Compose: **88 pruebas aprobadas**,
compilación con 0 avisos y `has-pending-model-changes` sin cambios. Son 76 de A (una
actualizada: el modelo ya tiene consumidores de Article) y 12 de B. Las 8 de
`Stock/StockCoreTests` son las de H0 adaptadas al ejecutor: ejemplo de salida mixta del
contrato, replay con 6 y 6.000, clave reutilizada con otro cuerpo o actor, otro OperationId
sobre el mismo hecho, ocho reintentos simultáneos, fallo inducido tras escribir y antes del
commit, reservas y salidas solapadas, reglas de lote/unidades/actividad, permiso 403,
DENY 229 y CHECK 547. Las 4 de `Stock/StockArticleIntegrationTests` cubren cobertura de FK,
borrado con saldo rechazado (HistoryPreventsDeletion), borrado sin referencias correcto con
DENY DELETE en StockBalances y bloqueo de definición tras el primer movimiento.
Las pruebas de stock y las de A afectadas se repitieron tres veces (20/20 cada vez).
La cadena completa se aplicó en una base vacía y `H0_StockCore` se revirtió y reaplicó.

Sin SDK local, desde la raíz:

```bash
docker compose up -d sqlserver
docker run --rm --network inventario_default -v "$PWD/backend:/src" -w /src \
  -e Testing__SqlHost=sqlserver -e 'Testing__SqlAdminPassword=LocalSqlAdmin_2026!' \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'dotnet tool restore && dotnet test Inventario.slnx --nologo'
```

## 1. Requisitos para desarrollo y pruebas fuera de contenedores

- Node 24 LTS compatible con Angular 22 y .NET SDK 10 estable.
- SQL Server 2025 accesible, con bases separadas InventoryDev e InventoryTest.
  En Mac ARM, servidor remoto/VM x64 o emulación amd64; no hay contenedor SQL nativo ARM.
- Usuario SQL con permisos de migración solo para preparar el esquema; usuario de ejecución
  restringido sin UPDATE/DELETE sobre el diario. Cadena de conexión mediante secreto local.
- Servidor SMTP de pruebas que capture mensajes sin enviarlos a proveedores reales.
- Navegador compatible; Playwright instala sus navegadores al preparar E2E.
- Ya existen manifiesto dotnet-tools con dotnet-ef 10, solución backend y lockfile npm.
  El proxy Angular apunta a la API Compose en http://localhost:8081; si se ejecuta la API
  directamente en https://localhost:7043, ajustar ese destino para la sesión de desarrollo.

Comprobar herramientas, desde cualquier directorio:

```bash
node --version
npm --version
dotnet --info
```

Resultado esperado: Node 24 ≥ 24.15.0 y SDK .NET 10; versiones frontend compatibles con R-01.

## 2. Preparación del backend

Directorio de ejecución: `backend/`. Usar Development para el entorno local y la URL 7043.
Definir previamente INVENTARIO_SQL_MIGRATION_CONNECTION para migrar e
INVENTARIO_SQL_CONNECTION para ejecutar con permisos restringidos; ambas apuntan a InventoryDev,
con SMTP de prueba configurado mediante secretos. No incluir credenciales en Git ni
en documentación.

```bash
export ASPNETCORE_ENVIRONMENT=Development
dotnet dev-certs https --trust
dotnet tool restore
dotnet restore Inventario.slnx
dotnet build Inventario.slnx --configuration Release --no-restore
export ConnectionStrings__Inventory="${INVENTARIO_SQL_MIGRATION_CONNECTION:?Definir conexión de migración}"
dotnet ef database update --project src/Inventario.Api --startup-project src/Inventario.Api
dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api
export ConnectionStrings__Inventory="${INVENTARIO_SQL_CONNECTION:?Definir conexión de ejecución}"
dotnet run --project src/Inventario.Api -- --urls https://localhost:7043
```

Resultado esperado: migraciones aplicadas sin diferencias pendientes; API responde a
GET https://localhost:7043/api/v1/health con 200 si SQL está accesible. Para aplicar
migraciones se usa la credencial de migración; para iniciar la API, cambiar a la credencial
de ejecución. En producción las migraciones se aplican explícitamente, no al arrancar.
Los datos de ejemplo se cargan mediante comandos auditados o un fixture de pruebas,
nunca escribiendo saldos manualmente. La API no envía correo real en Development.

## 3. Preparación del frontend

Directorio de ejecución: `frontend/`, en otra terminal. El proxy existente apunta a Compose.

```bash
npm ci
npx --no-install ng version
npx --no-install ng serve --port 4200 --proxy-config proxy.conf.json
```

Abrir http://localhost:4200; la pantalla actual muestra el estado del entorno. La interfaz
de login y el menú por permisos se incorporan en el trabajo de frontend. La API de sesión
ya permite comprobar /api/v1/auth/me y los tokens desde un cliente HTTP conservando cookies.
El navegador usa /api del mismo origen mediante proxy; producción sirve todo por HTTPS.

## 4. Pruebas automatizadas

Directorio `backend/`; preparar INVENTARIO_TEST_SQL_CONNECTION con acceso sa a SQL Server
de desarrollo/pruebas. El fixture crea una base y un login con nombres aleatorios propios,
aplica migraciones y los elimina al finalizar. No reutiliza InventoryDev ni acepta una
conexión implícita. No ejecutar esta suite contra un servidor de producción.

```bash
export Testing__SqlConnection="${INVENTARIO_TEST_SQL_CONNECTION:?Definir conexión SQL aislada de pruebas}"
dotnet test Inventario.slnx --configuration Release --no-build
```

Resultado esperado actual: pruebas HTTP/SQL de Identity correctas. Las futuras pruebas
de carrera del stock utilizarán conexiones independientes y solapamiento controlado.

Directorio `frontend/`:

```bash
npx --no-install ng build --configuration production
npx --no-install ng test --watch=false --coverage
npx --no-install playwright install chromium
npx --no-install playwright test
```

Playwright requerirá API/UI activas o configuración webServer equivalente creada en H0.
Debe iniciar sesión por el flujo real y comprobar permisos/antiforgery. No se declara
aprobado ningún escenario porque únicamente esté escrito aquí.

## 5. Datos comunes de validación

Crear por comandos: familia GENERAL, proveedor PROV-01 con buzón SMTP de prueba,
artículo ART-01 (unidad UD, fracciones no permitidas, seguimiento por lote), lote L-001
y ubicaciones REC-01 (recepción), P01-E01-N01 y P01-E01-N02. Usar existencias iniciales
cero; cada escenario prepara su stock por recepciones confirmadas y sus propias reservas.

Cuentas: operario; responsable con permiso de reservas y ajustes; comprador; gestor de
maestros. No reutilizar contraseñas reales. Los UUID retornados por la API se utilizan
en pasos posteriores; no depender de IDs internos generados al azar por otra ejecución.

Los contratos de peticiones, estados y errores están en [api.md](contracts/api.md);
los efectos e invariantes, en [stock-operations.md](contracts/stock-operations.md).

## 6. Escenarios de aceptación integrados

| ID | Preparación y acciones | Resultado esperado | Historia / principio |
|----|------------------------|--------------------|----------------------|
| V-01 | A crea ART-01/proveedor; B crea dos ubicaciones e intenta repetir un código. | Maestros consultables por ambas áreas; código duplicado rechazado; unidad/fracciones validadas. | HU-08, HU-02 / II, V |
| V-02 | Compra de 10 UD, envío al SMTP de pruebas; recibir 6 y después 4. | Envío primero pendiente, después Sent; tras recibir 6: físico 6, pendiente 4 y PartiallyReceived; al completar: físico 10, pendiente 0 y Received. | HU-07, HU-01 / I, IV, V |
| V-03 | Recibir 6 distribuyendo 2 y 4 entre ubicaciones, con L-001; intentar aceptar 7 con pendiente 4. | Filtros localizan 2/4, total 6; exceso rechazado, diferencia documentable, sin aumentar saldos ni acumulados. | HU-01, HU-02, HU-05 / I, II |
| V-04 | Físico 10; reservar 4 para pedido A y 3 para B. Servir 6 de A; intentar después consumir reservas de B desde otro pedido. | Primera salida deja físico 4, reservado 3 y disponible 1; la segunda no puede consumir las 3 ajenas. | HU-03, HU-05 / IV, V |
| V-05 | Físico 10 y sin reservas; dos conexiones reservan 7 simultáneamente para pedidos distintos. | Solo una reserva se confirma; la otra recibe InsufficientStock; reservado nunca 14. | HU-03, HU-05 / V |
| V-06 | Dos parciales distintas de 6 contra una compra pendiente de 10; confirmar en conexiones distintas. También probar dos recepciones válidas creando el primer bucket. | No se aceptan 12 contra 10; creación concurrente produce un único bucket y suma exacta de recepciones válidas. | HU-01, HU-07 / V |
| V-07 | Confirmar recepción; perder/simular pérdida de respuesta; repetir misma clave. Repetir con otro cuerpo y con otra clave sobre el documento confirmado. | Un solo efecto; replay devuelve resultado; cuerpo distinto da IdempotencyConflict; otra clave no duplica la confirmación. | HU-01, HU-03 / I, V |
| V-08 | Inyectar un fallo de prueba entre cambios del documento y antes del commit; consultar operación, saldos e historial. | Ningún cambio parcial confirmado; repetir misma clave completa una sola operación; ante commit incierto se recupera el resultado. | Todas con escritura / V |
| V-09 | Iniciar conteo con físico 10; intentar salida/recepción; contar 8, indicar motivo y autorizar ajuste. | Operaciones sobre alcance congelado rechazadas; falta permiso/motivo rechazada; ajuste −2 trazable y físico final 8; después se libera alcance. | HU-04, HU-05 / I, IV, V |
| V-10 | Conteo físico 2 con reservado 3; registrar hallazgo nuevo en otro bucket; cerrar y cancelar en ejecuciones separadas. | No confirmar por debajo del reservado; liberar explícitamente reservas permite cierre; hallazgo requiere ubicación/lote; cancelar conserva evidencia y no ajusta. | HU-04 / II, IV, V |
| V-11 | Físico 12, reservado 8, mínimo 5; cambiar reservado/mínimo y restaurar disponible ≥ mínimo. | Alerta activa con disponible 4, mínimo 5, sugerido 1; solo un episodio activo; al recuperar mínimo se resuelve sin pedido automático. | HU-06, HU-05 / I, V |
| V-12 | Desactivar artículo con movimientos; intentar eliminarlo y crear una recepción/reserva nueva. Consultar historia y corregir con autorización. | Borrado y nuevas operaciones prohibidas; historial intacto; corrección compensatoria permitida solo respetando invariantes; diario no admite UPDATE/DELETE con usuario de ejecución. | HU-08, transversal / IV, V |
| V-13 | Worker cae antes/después de SMTP; sesión sin permisos intenta ajuste; mutación sin antiforgery; operarios completan recepción y salida. | Intención de envío no se pierde, posible duplicado SMTP identificado; 403/400 apropiados; usuarios completan sin ayuda técnica y sin errores no detectados, con evidencia. | HU-07 y flujos / III, V |
| V-14 | Restaurar copia/log en instancia separada, comprobar diario/saldos/reservas/documentos; conciliar comprobante de contingencia repetido y hechos físicos posteriores. | RPO ≤ 15 min y RTO ≤ 4 h medidos; CHECKDB sin errores; invariantes verificadas; comprobante no duplica; almacén no reabre el alcance hasta conciliar. | Todas / I, IV, V |

Extensiones de V-04: servir desde otro bucket no puede dejar reservas propias superiores
al nuevo pendiente; se exige liberación explícita o reasignación. Probar dos liberaciones
parciales legítimas con releaseId diferentes y después repetir cada una: se permiten ambas,
pero ninguna se aplica dos veces.

Extensión de V-05: con físico 10 sin reservas, dos pedidos distintos confirman salidas
de 8 simultáneamente. Solo una confirma; la otra recibe insuficiencia y el físico final
es 2, nunca negativo. Forzar solapamiento también entre una reserva y una salida.

## 7. Rendimiento y evidencia de aceptación

Con datos sintéticos de 10.000 artículos y 1.000.000 de entradas, 25 sesiones concurrentes
y documentos de hasta 20 líneas, medir p95 de consulta paginada y confirmación: objetivo
≤ 2 s, separado de esperas por conflictos y SMTP. Registrar también esas esperas, sin
ocultar errores como muestras descartadas. Consultar no modifica datos y conserva coherencia
de totales/buckets en el mismo snapshot.

Cada historia conserva los escenarios que le afectan, resultados, fecha, entorno y evidencia
de usabilidad, concurrencia o restauración. Responsable de proyecto y de almacén revisan
las desviaciones antes de aceptar la entrega. Las ocho futuras specs referencian estos
escenarios y añaden los propios; no sustituyen la validación con operarios por tests unitarios.

## 8. Restauración y apertura operativa

Ejecutar backup completo diario y log cada 15 min, retención 30 días, copia fuera del servidor.
En un ensayo cronometrado: restaurar full y cadena de logs hasta el punto elegido, ejecutar
DBCC CHECKDB, consultar las conciliaciones del modelo y demostrar los objetivos propuestos.
RESTORE VERIFYONLY por sí solo no da por completado V-14.

Tras restaurar, bloquear operativa ordinaria hasta contrastar comprobantes, mercancía que
se movió después del punto recuperado y correos posiblemente enviados. La API no reenvía
a ciegas intenciones restauradas sin esta revisión. Conservar fuera del servidor SQL
claves necesarias para backups y Data Protection, con acceso restringido.

Antes de producción deben quedar aprobados RPO/RTO, responsables de registro/ajuste/revisión,
calendario de conteos y procedimiento de contingencia. Esta guía define cómo demostrarlo;
no declara aprobaciones ni ensayos ya realizados.

## 9. HU-02 — Revisión de integración del 2026-11-19

Ubicaciones incorpora unicidad física, caracteres ASCII, auditoría y bloqueo de última
Receiving. La revisión de A del PR #6 corrige restauración de intenciones, separación por
actor, ETag fuerte de la representación completa y recuperación de páginas desaparecidas.
Resultado del head corregido: 121 pruebas SQL aprobadas y V-14 específica omitida,
107 frontend, 14 E2E funcionales, build/tipos correctos y EF sin diferencias. El entorno
E2E usa `HU08_E2E_PROJECT=inventario-pr6-e2e`, puerto 8083 y base aislada, con retirada
de cuentas y parada de contenedores al terminar. La evidencia y comandos particulares
están en [la revisión HU-02](../../../specs/003-gestionar-ubicaciones/validation/pr-review-2026-11-19.md).
Conteos de HU-04 y las desviaciones operativas SC-005/SC-006 mantienen su alcance declarado.

## 10. HU-05 — Evidencia de integración del 2026-11-21

Consulta de existencias, pedido de cliente mínimo, reservas y liberaciones (HU-05, PR #9, aprobado
por A el 2026-11-21 con la Convergence C1–C4). Ejecutado sobre `363de56`:

- **Backend**: 207 pruebas SQL aprobadas, 1 omitida (V-14) y 0 fallidas, con `git archive` y
  `mcr.microsoft.com/dotnet/sdk:10.0` sobre la red de Compose. EF sin cambios de modelo.
- **Escenarios comunes cubiertos**:
  - V-05 (20 rondas);
  - V-04 en su parte de reserva, la de salida queda para HU-03;
  - V-08 (`FailAfterSaveInterceptor`);
  - el contrato de escritores de INT-HU05-01/02, con carreras con `LockBarrier` en ambos órdenes
    y la conciliación de asignaciones, diario y saldo.
- **Frontend**:
  - Vitest 147/147;
  - `tsc` de app, specs y E2E sin errores;
  - build correcto;
  - Playwright 16/16 con `--grep-invert SC-005`, en el proyecto `inventario-hu08-e2e` y con
    E2E-01 incluido.
- **Pendiente**:
  - `CountScopeFrozen` al reservar (INT-HU04-03);
  - la búsqueda de pedidos por prefijo (P-04 de HU-05, S1);
  - SC-006 y SC-007, que impiden dar HU-05 por aceptada.

Detalle por caso en el [quickstart de HU-05](../../../specs/004-consultar-existencias/quickstart.md#evidencia).
