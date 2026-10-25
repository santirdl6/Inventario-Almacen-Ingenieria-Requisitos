# Guía de ejecución y validación conjunta

**Estado**: la base técnica H0 tiene frontend/API de estado, migración de infraestructura
y arranque contenedorizado. Los comandos y recorridos de autenticación, pruebas y negocio
de las secciones posteriores se completarán con sus correspondientes historias del
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

## 1. Requisitos para desarrollo y pruebas fuera de contenedores

- Node 24 LTS compatible con Angular 22 y .NET SDK 10 estable.
- SQL Server 2025 accesible, con bases separadas InventoryDev e InventoryTest.
  En Mac ARM, servidor remoto/VM x64 o emulación amd64; no hay contenedor SQL nativo ARM.
- Usuario SQL con permisos de migración solo para preparar el esquema; usuario de ejecución
  restringido sin UPDATE/DELETE sobre el diario. Cadena de conexión mediante secreto local.
- Servidor SMTP de pruebas que capture mensajes sin enviarlos a proveedores reales.
- Navegador compatible; Playwright instala sus navegadores al preparar E2E.
- H0 deberá aportar manifiesto dotnet-tools con dotnet-ef 10, solución backend, lockfile npm,
  configuración de proxy /api hacia https://localhost:7043 y usuarios de prueba controlados.

Comprobar herramientas, desde cualquier directorio:

```bash
node --version
npm --version
dotnet --info
```

Resultado esperado: Node 24 ≥ 24.15.0 y SDK .NET 10; versiones frontend compatibles con R-01.

## 2. Preparación del backend

Directorio de ejecución: `backend/`. H0 fijará la configuración Development y la URL 7043.
Definir previamente INVENTARIO_SQL_MIGRATION_CONNECTION para migrar e
INVENTARIO_SQL_CONNECTION para ejecutar con permisos restringidos; ambas apuntan a InventoryDev,
con SMTP de prueba configurado mediante secretos. No incluir credenciales en Git ni
en documentación.

```bash
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

Directorio de ejecución: `frontend/`, en otra terminal. El proxy se creará en H0.

```bash
npm ci
npx --no-install ng version
npx --no-install ng serve --port 4200 --proxy-config proxy.conf.json
```

Abrir http://localhost:4200, iniciar sesión con cuenta de prueba y comprobar /api/v1/auth/me.
El navegador usa /api del mismo origen mediante proxy; producción sirve todo por HTTPS.
Verificar token antiforgery, menú por permisos y mensajes en español.

## 4. Pruebas automatizadas

Directorio `backend/`; preparar INVENTARIO_TEST_SQL_CONNECTION para una base desechable,
distinta de producción. El fixture deberá crear/limpiar datos de su ejecución y aplicar
migraciones sin tocar otras bases. En H0 se añadirá una comprobación que rechace usar
una base de producción o una conexión no declarada expresamente como entorno Test.

```bash
export Testing__SqlConnection="${INVENTARIO_TEST_SQL_CONNECTION:?Definir conexión SQL aislada de pruebas}"
dotnet test Inventario.slnx --configuration Release --no-build
```

Resultado esperado: reglas unitarias y pruebas HTTP/SQL correctas. Los tests de carrera
usan conexiones independientes y barreras para forzar solapamiento; no solo peticiones secuenciales.

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
