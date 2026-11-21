# Guía de ejecución y validación HU-07

**Fecha**: 2026-11-20 · US1–US5 implementadas y verificadas localmente; regresión final
y carga continúan en sus tareas. Evidencias en validation/; no implica aceptación operativa.
Base: [spec](spec.md), [plan](plan.md), [API](contracts/api.md),
[integración](contracts/integration.md), [UI](contracts/ui.md),
[constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md) y
[guía común](../../.specify/memory/project/quickstart.md).

## Preparación y arranque

Docker Desktop activo (SQL Server amd64 en Mac ARM). Para pruebas frontend locales,
Node 24 ≥24.15.0 y npm; backend mediante SDK 10 o perfil Docker. Desde la raíz:

```bash
docker compose config --quiet
docker compose up --build -d --wait --wait-timeout 300
```

Esperado: initialize termina 0, app en http://localhost:8081, health 200 y Mailpit en
http://localhost:8025. No borrar volúmenes para probar reinicio. Configuración local
smtp:1025/None y remitente de Compose; confirmar Currency=EUR en los datos de prueba.
Tras implementar, worker activo solo en ejecución ordinaria; no desde inicializador.
Los nombres/opciones nuevas están en el contrato de integración; no contienen secretos
en documentos. En producción TLS/configuración válida y migraciones explícitas.

Opciones efectivas de Compose en README/.env.example: SMTP_HOST/PORT/FROM/SECURITY,
SMTP_USERNAME/PASSWORD opcionales, PURCHASING_CURRENCY y PURCHASE_DELIVERY_ENABLED/
MAX_ATTEMPTS/POLL_SECONDS/TIMEOUT_SECONDS/LEASE_SECONDS. La API usa las claves estándar
Smtp:*, Purchasing:Currency y PurchaseDelivery:*. Para pausar sin perder datos:

```bash
PURCHASE_DELIVERY_ENABLED=false docker compose up -d app
PURCHASE_DELIVERY_ENABLED=true docker compose up -d app
```

La prueba de pausa/recreación de host conserva Pending y edición bloqueada y reanuda una
sola entrega al habilitar. No equivale a restaurar backup: H5-02 exige esa conciliación real.

Cuentas controladas: comprador, gestor de maestros, solo consulta y administrador sin
permisos implícitos. Usar fixtures aislados para asignarlas; no añadir API pública de
roles. Preparar por HTTP/handlers reales familia GENERAL, proveedor PROV-07 con buzón
de prueba, ART-07-UD entero y ART-07-KG fraccionable. Capturar UUID/ETag retornados.
Sin stock inicial; no escribir saldos/recibidos manualmente para acreditar compras.

## Pruebas automatizadas

Desde raíz, suite backend sobre bases SQL aleatorias del fixture existente:

```bash
docker compose --profile test run --build --rm tests
```

Desde `backend/`, con SDK y conexión explícita de pruebas cuando no se usa Docker:

```bash
dotnet tool restore
dotnet test Inventario.slnx --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api
```

El modo local requiere Testing__SqlConnection configurado como en la guía común. La
comprobación EF usa la conexión de diseño de desarrollo. Esperado: pruebas de Purchasing,
worker y regresiones existentes aprobadas, modelo/snapshot sin diferencias. Probar migración
en base vacía y sobre esquema previo, sin reescribir historial de migraciones.

Desde `frontend/`:

```bash
npm ci
npm run build
npm test
npm run test:e2e:types
npm run test:e2e
```

El fixture HU-07 está implementado con perfil explícito Comprador y proyecto/base/Mailpit
aislados, conservando los perfiles HU-08/HU-02. Desde frontend:

```bash
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- 'purchasing-.*\.spec\.ts' --output=test-results/hu07-regression
```

La suite prepara y retira sus cuentas y detiene solo sus contenedores. Tras compilar la
imagen actual se puede usar `HU08_E2E_SKIP_BUILD=1` para evitar reconstrucciones repetidas.

Para fallos SMTP, usar transporte controlado en integración y Mailpit para aceptación
real/MIME. Las pruebas de timeout tras DATA, leases y commit incierto necesitan puntos de
fallo internos de tests; no exponerlos como endpoints/configuración pública de producción.

### Entrada del MVP desde el menú

/compras muestra el listado definitivo de US3 con Inventory.Read, operaciones pendientes
del actor y enlace Nuevo pedido solo con Purchasing.Manage. La pantalla provisional de
US1 se retiró. Las pruebas de rutas y E2E verifican entrada desde el menú, filtros,
paginación y consulta sin conceder gestión/envío a una cuenta de solo lectura.

### Fallo SMTP y recuperación E2E

T003/T046 usan el proceso API y Mailpit del **proyecto Compose aislado de HU-07**, con
sesión/antiforgery reales. El transporte sustituido de las pruebas SQL/HTTP no controla
el SMTP de ese contenedor. El fixture debe pasar solo a esa API estas opciones:

| Opción de entorno | Valor del E2E de recuperación |
|-------------------|-----------------------------|
| PurchaseDelivery__Enabled | true |
| PurchaseDelivery__MaxAttempts | 1 |
| PurchaseDelivery__PollSeconds | 1 |
| PurchaseDelivery__TimeoutSeconds | 5 |
| PurchaseDelivery__LeaseSeconds | 40 |

40 > 5 + 30 satisface el margen del contrato. Los valores reducidos permiten observar
Failed sin esperar los cinco intentos/backoff ordinarios dentro del timeout del navegador;
no cambian los defaults del servicio ni los casos de integración con presupuesto completo.
Las opciones se activan cuando el worker T027 esté disponible.

Guion de T046:

1. Con SMTP disponible, guardar pedido y abrir la revisión de envío; capturar su identidad.
2. Detener exclusivamente `smtp` del proyecto E2E identificado explícitamente. Solicitar
   envío y esperar mediante consultas/estado visible a Draft/Failed tras el único intento.
   Comprobar solicitud persistida, incidente comprensible y edición bloqueada.
3. Arrancar el mismo servicio y esperar disponibilidad real de SMTP y buzón con un límite
   acotado; la mera existencia del contenedor no acredita que pueda aceptar correo.
4. Reintentar desde la UI con una nueva operación. Esperar Accepted y comprobar la misma
   Delivery, MessageId, destinatario y documento, y una sola compra.
5. Restaurar SMTP en `finally`, incluso si falla una aserción. No detener servicios de
   InventoryDev ni introducir endpoints de fallo en la aplicación.

No usar sleeps como prueba de progreso ni ampliar globalmente el timeout de Playwright
para esperar el backoff ordinario. Rechazo permanente, timeout tras DATA, incertidumbre
externa, lease/fencing y cinco intentos se prueban con transporte controlado en T021/T041.
Este E2E acredita específicamente indisponibilidad y recuperación del flujo real.

## Recorridos de comprobación

| ID | Preparación y acciones | Resultado esperado / trazabilidad |
|----|------------------------|------------------------------------|
| Q07-01 | Crear 10 UD, precio 0, fecha general; dos líneas del mismo artículo con IDs distintas, una fecha propia. Editar/retirar/añadir. | Número único, Draft/None, recibido 0, pendientes exactos; conserva identidades/snapshots. Stock/diario/reservas sin cambios. US1; FR-001–006/022; SC-001. |
| Q07-02 | Cantidad 0/negativa, fracción de UD, >3 decimales significativos, precio negativo/>2, fecha inválida, vacío, ID repetida/ajena; proveedor sin correo. | 422 por campo/línea y valores válidos conservados; sin parcial. Sin correo sí permite borrador; primera solicitud rechazada. US1/US2; SC-002. |
| Q07-03 | Abrir /enviar, revisar por teclado; cambiar correo en maestros y confirmar revisión anterior. Volver a revisar y enviar. | Conflicto sin Delivery para revisión vieja; nueva confirmación devuelve 202 Draft/Pending y bloquea edición. Mailpit recibe mismo documento/destinatario; luego Sent/Accepted. US2; FR-009–014; SC-003. |
| Q07-04 | Rechazo permanente y fallo transitorio controlados; agotar presupuesto, reintentar explícitamente. | Draft/Failed al terminar ciclo; retry nueva operación y misma Delivery/MessageId/MIME/contacto. Intentos y evidencias conservados. US4.1/2; FR-015/017. |
| Q07-05 | Perder respuestas de alta/PUT/send/retry después del commit; recargar, comprobar resultado y repetir clave original. Cerrar sesión/cambiar actor. | Replay histórico exacto sin duplicados; ETag/Location reproducibles; referencia sin payload tras logout, otra identidad no reutiliza intención. US4.3; US5.3; FR-025/027; SC-004. |
| Q07-06 | Dos PUT, PUT frente a send, dos initial, dos retry sobre Failed; conexiones/barreras independientes. | Solo transición compatible con orden; ninguna sobreescritura/entrega alternativa. UI bloquea grupo por pedido durante incertidumbre. US4.6/US5.2; FR-017/026. |
| Q07-07 | Primer borrador real; editar definición/borrar artículo concurrentemente en ambos órdenes. Retirar última línea; repetir guardado. | Marca monotónica, FK/lector reales; definición vigente o rechazo incompatible, sin huérfanos ni evidencia duplicada. INT-HU07-01; SC-005. |
| Q07-08 | Editar/desactivar/reactivar proveedor y artículo con documentos; alta/primera solicitud concurrentes contra desactivación en ambos órdenes. | Documentos/snapshots intactos, nuevas asignaciones/envío rechazados tras desactivación, reactivación permite nuevas compras. INT-HU07-02; SC-005. |
| Q07-09 | Dos workers; caída tras claim/antes SMTP, aceptación con acuse local perdido, lease vencido y propietario antiguo que vuelve. | No pierde intención; fence impide finalización obsoleta; incertidumbre/copia identificada. Repetir finish tras fallo SQL no repite SMTP. FR-016/028; V-08/V-13. |
| Q07-10 | Fallo antes de commit de alta/edición/solicitud/finish; mutar sin sesión/permiso/antiforgery; ejecutar UPDATE/DELETE de evidencia con runtime. | Rollback completo y cero correo de trabajo no persistido; 401/403/400; DENY real en Delivery/auditoría/eventos/resultados. US5; FR-023/024/028. |
| Q07-11 | Búsqueda/filtros/páginas; página fuera de rango; fallo de consulta; cambiar maestros después de pedir. | Lista/count coherentes, error distinto de vacío, navegación recuperable y documentos históricos íntegros. US3.1/2/5/6; FR-018/019/030. |
| Q07-12 | Cambiar línea sin editar cabecera; aceptar correo desde worker; consultar context/delivery en revisión distinta. | ETag del detalle avanza por cada variación propia; no atribuye el mismo ETag a cuerpos distintos. UI refresca al detectar purchaseOrderVersion distinto. FR-019/026. |

Q07-07/08 requieren evidencia de espera/solapamiento SQL, no solo Task.WhenAll. Conservar
ambos órdenes de confirmación, IDs, entorno, fecha y aserciones de invariantes. Q07-09
incluye envío aceptado seguido de Disconnect fallido: sigue siendo aceptación conocida.
Agregar caso de comando interno sin autorización persistida válida: cero cambio/envío.

## Integración posterior y rendimiento

- **HU-01, V-02/V-06, SC-006**: pedido enviado de 10, confirmar 6 y 4; recibido/pendiente
  0/10 → 6/4 → 10/0 y estados correctos en primera consulta posterior. Dos recepciones
  reales de 6 no aceptan 12; borrador de recepción no cambia acumulados. HU-01 implementa
  y acepta estos escritores junto a stock. Preparaciones técnicas solo prueban lectura.
- **SC-007**: ejecutar T055 según el [protocolo reproducible](#protocolo-reproducible-de-rendimiento-sc-007),
  con resultados visibles, muestra mínima y datos/carga fijos; no sustituirlo por tiempos
  de tests pequeños ni por latencia HTTP aislada.
- **H5-02/V-14**: restaurar con PurchaseDelivery:Enabled=false; conciliar correos posiblemente
  emitidos antes de reactivar. Mantener RPO/RTO aprobados y evidencia independiente.
- **H5-03/SC-008**: ≥3 compradores completan preparar/corregir/revisar/consultar incidencia
  sin asistencia; documentar incidencias y correcciones. No sustituirlo por Playwright.

### Protocolo reproducible de rendimiento SC-007

Perfil de T055 para compras, manteniendo el objetivo del plan global. El protocolo no
presupone un resultado aprobado. Usar un proyecto/base de carga aislados, sin mezclar ejecución
de suites SQL/E2E o cambios de datos ajenos durante la ventana medida.

| Parámetro | Condición fijada |
|-----------|------------------|
| Artículos | 10.000 activos, con unidades/fracciones válidas. |
| Proveedores / pedidos iniciales | 100 proveedores activos y 10.000 pedidos Draft/None: 100 por proveedor. |
| Distribución de líneas | Por proveedor: 40 pedidos de 1 línea, 30 de 10 y 30 de 20; total 4.000/3.000/3.000 pedidos respectivamente. |
| Historial | 1.000.000 de entradas según la guía común, generado por StockCore y sus operaciones auditadas; no DML de saldos. |
| Identidades / concurrencia | 25 cuentas Comprador y 25 contextos autenticados independientes de navegador, cada uno con una operación activa como máximo. |
| Semilla | 7007 para datos, selección de pedidos/artículos y secuencia de filtros; conservar manifiesto de IDs y versiones. |
| Calentamiento | 2 minutos con la misma carga, excluidos de percentiles/muestra medida. |
| Ventana medida | 10 minutos; esperar las operaciones iniciadas en la ventana y registrar también errores/timeouts. |
| Mezcla por sesión | Ciclos de 8 operaciones: 4 consultas paginadas, 2 altas, 2 correcciones; 50 % / 25 % / 25 %. |
| Muestra mínima | 1.000 observaciones medidas de consulta, 1.000 de alta y 1.000 de corrección. |
| Objetivo | p95 ≤2 s por cada uno de los tres tipos; no solo un percentil combinado. |

La preparación usa handlers/comandos auditados. Mantener los pedidos en Draft/None;
este ensayo no solicita envío, no necesita recepción HU-01 ni crea stock al comprar.
Las altas/correcciones recorren tamaños de 1/10/20 líneas en orden cíclico y respetan
precisión/unidad. Particionar pedidos editables por sesión para evitar conflictos deliberados;
conservar los conflictos que aparezcan como incidencias, nunca como éxitos descartados.
Registrar recuentos de pedidos/entradas al inicio y al final, incluidas altas del calentamiento.

Alternar consultas con pageSize=25 por este orden: sin filtros, proveedor del manifiesto,
prefijo de número del manifiesto y state=Draft con período de creación que cubra la
preparación. Rotar proveedores/prefijos de forma determinista y recorrer páginas válidas
primera/intermedia/final; fijar y registrar el intervalo UTC de creación usado como filtro.
El conjunto de filtros es el mismo en todas las repeticiones a partir del manifiesto.

**Frontera de medición**: el ejecutor Node invoca Playwright existente. Para consulta,
iniciar el reloj monotónico en la acción Buscar/cambio de página que dispara la petición;
terminar cuando la tabla o el vacío confirmado correspondiente se represente. Para alta/
corrección, preparar antes los campos y medir desde Guardar hasta el resultado persistido
visible asociado a esa operación/pedido. No incluir tiempo de escritura humana o preparación
del formulario; sí petición, servidor y representación UI. Medir HTTP por separado como
diagnóstico: su p95 no reemplaza el tiempo de resultado visible exigido por SC-007.

Inicializar cada sesión una vez y preparar las rutas mediante navegación SPA, conservando
sesión y módulos como en el recorrido normal. Para los códigos del manifiesto, el selector
permite elegir explícitamente «Prefijo del código SKU»; el q general por contenido sigue
disponible. Estas preparaciones quedan fuera del reloj; Buscar/Guardar y resultado visible
mantienen la frontera, carga, ventanas y muestra mínima de este protocolo.

Calcular percentiles por rango más próximo (posición ceil(0,95×N)) sobre tiempos de
operaciones exitosas, y publicar además total de intentos, éxitos, errores, timeouts y
conflictos por tipo; no computar un error como éxito rápido ni ocultarlo. Registrar aparte
las esperas por conflicto y SMTP según el contrato común. No declarar aprobado con errores
funcionales inesperados, con algún p95 >2 s o con omisiones del perfil de carga. Si no se
alcanza alguna muestra mínima en 10 minutos, declarar **inconcluso**, no aprobado, y conservar
igualmente todos los resultados del ensayo.

Guardar en validation/performance.md comandos, seed/manifiesto, recuentos iniciales/finales,
filtros, mediciones UI/HTTP, tasas de error, tamaños de documento, versiones y CPU/RAM/OS/
arquitectura del cliente/API/SQL (incluida emulación), límites Docker y configuración usada.
Repetir con el mismo perfil tras una optimización; no cambiar datos/carga para obtener un PASS.

Ejecutor desde raíz, con la imagen del código actual compilada:

```bash
docker compose build initialize
HU08_E2E_BROWSER_CHANNEL=chrome node scripts/measure-hu07-purchasing.mjs
```

Cada ejecución crea un proyecto `inventario-hu07-e2e-performance-<uuid>` nuevo, base
InventoryHu07Performance y puertos 8086/8028. La preparación (plazo máximo 60 minutos)
crea maestros/pedidos por HTTP y un millón de entradas con ejecutor/StockCore runtime;
no representa una recepción HU-01. Verifica los recuentos SQL antes del navegador.
Conserva report.json/samples.json y manifiesto privado 0600 en el directorio ignorado
`frontend/test-results/hu07-performance/<proyecto>/`. El manifiesto contiene credenciales
temporales: no incorporarlo a Git. El finally bloquea las cuentas de carga, renueva sus
security stamps y detiene únicamente ese proyecto, conservando los volúmenes de evidencia.

Para evitar suspensiones en macOS, ejecutar el comando Node mediante `caffeinate -is env`.
El runner invalida ventanas con interrupciones del reloj. Su override Compose limita a
Warning el log de comandos EF, conservando errores y el registro de peticiones HTTP.
Para primera/última página prepara la vista directamente; para la intermedia consulta el
total antes de elegir la página anterior. Las tres operaciones se disparan desde la UI.

Para contrastar el resultado del Mac, repetir en un host de pruebas x86-64 Linux con
Docker, dependencias de `frontend/` instaladas y un navegador disponible para Playwright.
Ejecutar allí la compilación y el runner (el navegador accede a Compose por localhost).
El runner crea una base nueva del proyecto de carga y conserva los informes; no reutilizar
InventoryDev ni mezclar otras suites durante la medición. Comparar primero los tres p95
visibles del corte conservado con la [evidencia local](validation/performance.md), antes
de atribuir la diferencia al entorno o a una fase concreta de la escritura.

## Evidencia y criterio de cierre técnico

Al implementar, guardar en validation/ resultados por Q/FR/SC/INT, comandos, entorno,
fechas y limitaciones observadas, incluida la posibilidad de copia SMTP. Acreditar
INT-HU07-01/02 antes de completar sus checkboxes en el backlog receptor y ejecutar
regresión del ejecutor/transporte de HU-08/HU-02/StockCore afectados. Registrar revisión
A+B real de cambios compartidos antes de integrar. HU-01/H5 conservan su aceptación
propia; no mantener HU-07 técnicamente abierta por ausencia de esos módulos futuros.
