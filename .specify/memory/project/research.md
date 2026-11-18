# Investigación y decisiones comunes

**Fecha**: 2026-10-25 · **Estado**: decisiones de diseño para las ocho futuras specs.
**Base normativa**: [constitución v1.0.0](../constitution.md).
**Plan global**: [project-plan.md](../project-plan.md).

## R-01. Versiones compatibles

**Decisión**: Angular 22.2.3, CLI 22.2.3, PrimeNG 22.1.4, CDK 22 compatible,
TypeScript 6.0.3, RxJS 7.8.x y Node 24 LTS. Backend en .NET/ASP.NET Core 10 LTS,
EF Core 10 y proveedor Microsoft SQL Server de la misma versión de parche.
SQL Server 2025, edición Developer para desarrollo y pruebas.
Se fijarán versiones exactas y lockfiles al crear los proyectos, sin paquetes preview.

**Motivo**: versiones publicadas, con Angular/PrimeNG de la misma generación;
el paquete PrimeNG elegido declara Angular y CDK `^22.1.0`. Angular exige TypeScript
6.0 y admite Node 24 desde 24.15.0. .NET 10 ofrece una base LTS vigente.
No se ha realizado una compilación: la compatibilidad investigada procede de documentación
y metadatos de los paquetes. La primera entrega debe comprobarla con build y pruebas.

**Alternativas**: versiones preliminares descartadas; .NET 8/9 no se eligen para
un proyecto nuevo por su próximo fin de soporte. No se necesita adoptar otro motor SQL.

**Fuentes**:
- https://angular.dev/reference/versions
- https://registry.npmjs.org/primeng/22.1.4
- https://registry.npmjs.org/@angular/compiler-cli/22.2.3
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://learn.microsoft.com/en-us/ef/core/providers/sql-server/

## R-02. CQRS ligero y estructura

**Decisión**: una SPA, una API y una base. Un proyecto productivo ASP.NET Core,
organizado por funcionalidades, con Commands y Queries separados. Endpoints HTTP delgados
—Minimal APIs en la base H0—
llaman handlers directamente mediante inyección de dependencias. Un InventoryDbContext
por comando y consultas EF Core proyectadas a DTO con AsNoTracking.

**Motivo**: dos desarrolladores pueden trabajar verticalmente sin introducir distribución
ni consistencia eventual en las consultas de stock. Las entidades EF no se exponen por HTTP.

**Alternativas**: MediatR no es requisito; no se incorporan repositorios genéricos,
event sourcing, microservicios, broker ni base de lectura separada.

**Fuente**: https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs

## R-03. Integridad, concurrencia y agregación de stock

**Decisión**: confirmaciones con transacción SQL explícita, READ COMMITTED y bloqueos
`sp_getapplock`, exclusivos y con propietario Transaction. Orden común:
operación idempotente → documentos ordenados → ubicaciones ordenadas → artículos ordenados.
El bloqueo por artículo abarca todos sus lotes y ubicaciones, y permite evaluar su mínimo
global sin carreras entre movimientos en ubicaciones distintas. El bloqueo de ubicación
coordina el inicio de conteos con cualquier entrada, salida o reserva.

Todos los escritores participan, incluidos reservas, configuración de mínimos y desactivación
de artículos. Se lee el estado decisorio después de los bloqueos y se valida de nuevo.
El servicio de stock comparte contexto y transacción del handler; no hace commit autónomo.
RCSI reduce bloqueo de consultas; un resultado de existencias se obtiene en una sola sentencia
o en una transacción SNAPSHOT de lectura si necesita varias, para no mezclar instantes.

**Motivo**: protege también el primer saldo de una combinación inexistente y las reglas
que abarcan varias filas. La granularidad por artículo sacrifica paralelismo del mismo SKU
a cambio de un protocolo sencillo adecuado a la escala inicial.

**Alternativas**: rowversion se utiliza para edición obsoleta de documentos, pero por sí
solo no protege SUM de reservas ni inserciones concurrentes. SERIALIZABLE con bloqueos
de rango es válido, pero exige más atención a predicados e índices. Mutex en memoria no
coordina procesos distintos. No se promete que applock proteja escrituras que lo omitan.

**Fuentes**:
- https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17
- https://learn.microsoft.com/en-us/ef/core/saving/transactions
- https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver17

## R-04. Idempotencia y fallos

**Decisión**: clave UUID estable, huella normalizada de la petición y resultado confirmado
guardados en la misma transacción. Confirmar un documento tiene además unicidad de efecto
independiente de la clave. Tras un resultado incierto, consultar la operación o repetir
la misma clave, nunca generar otra para intentar resolver la incertidumbre.

**Motivo**: un doble clic o una pérdida de respuesta no duplica movimientos. La estrategia
de resiliencia EF envuelve la transacción completa y crea un contexto limpio por intento.
Máximo tres intentos ante errores transitorios; insuficiencia de stock no se reintenta.

**Alternativas**: deshabilitar el botón ayuda a la UX, pero no garantiza unicidad.
Un reintento ciego de SaveChanges no vuelve a comprobar la decisión de negocio.

**Fuente**: https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency

## R-05. Reglas comunes de negocio para no bloquear las specs

**Decisión**: adoptar los siguientes valores iniciales explícitos. Cada spec puede
detallarlos; cambiarlos requiere actualizar los contratos y las specs afectadas.

| Tema | Regla inicial |
|------|---------------|
| Cantidades | Unidad base por artículo; decimal(18,3), sin float; un artículo de unidades enteras rechaza fracciones. |
| Edición de la definición del artículo | Unidad base, seguimiento por lote y admisión de fracciones solo cambian sin documentos, reservas ni movimientos asociados. La primera referencia, incluso en un documento en borrador, bloquea los tres campos. |
| Reservas | Asignación explícita por pedido, artículo, ubicación y lote; creación y liberación mínimas pertenecen a HU-03/HU-05, sin módulo comercial completo. |
| Salidas | Consumen primero la reserva del propio pedido en el saldo indicado; el resto solo consume disponible. Nunca toman reservas ajenas. |
| Lotes | Artículo con seguimiento exige lote en recepción y movimientos; sin seguimiento usa un bucket no nulo SIN-LOTE propio del artículo. No se introduce gestión por serie o caducidad en esta entrega. |
| Recepción | Rechaza cantidad aceptada superior al pendiente y artículos ajenos al pedido. Conserva como incidencia las diferencias observadas sin incorporarlas al stock. |
| Desactivación | Impide nuevos pedidos, reservas y recepciones del artículo. No borra ni cierra documentos; permite consultar historial, liberar reservas, servir compromisos existentes y ajustar diferencias con autorización. |
| Conteo | Congelación persistente de ubicaciones durante el conteo físico; no se mantiene una transacción abierta durante la intervención humana. |
| Mínimo | Se evalúa disponible global del artículo, con comparación estricta disponible < mínimo. |
| Sugerencia | max(0, mínimo − disponible), en unidad base; no descuenta compras pendientes ni crea pedidos automáticamente. |
| Aviso | Bandeja dentro de la aplicación; un episodio activo por artículo, actualizado hasta recuperar el mínimo. Sin correo ni SMS en la base inicial. |
| Envío de compra | Correo al proveedor con documento del pedido; entrega mediante outbox persistente. En desarrollo se usa un servidor SMTP de pruebas. |

**Aclaración HU-08, 2026-11-12**: A elige bloquear los tres campos desde la primera
referencia, incluidos borradores, en lugar de esperar al primer movimiento. La regla
afecta al mantenimiento de HU-08 y a las referencias creadas por HU-01/HU-03/HU-04/HU-05/
HU-07; debe revisarse con B antes de integrar el cambio compartido. No modifica los
efectos ni los estados de las operaciones de stock.

**Motivo**: son defaults conservadores suficientes para definir los intercambios y evitar
que cada persona implemente reglas distintas. Las incidencias rechazadas en recepción
siguen quedando documentadas, sin afirmar que entraron al almacén como stock disponible.

**Alternativas**: FEFO, reservas automáticas, compra hasta un objetivo superior al mínimo,
EDI e integración ERP se aplazan al no estar solicitados. No se necesita investigación
adicional para la base técnica; estas decisiones son revisables con el responsable del almacén.

## R-06. Autenticación y experiencia de uso

**Decisión**: cuentas locales ASP.NET Core Identity, cookies HttpOnly/Secure, mismo origen
en producción y proxy /api en desarrollo. Antiforgery en mutaciones: cookie XSRF-TOKEN
y cabecera X-XSRF-TOKEN, emitidas/validadas por el servidor. Roles y políticas en la API;
usuario de auditoría derivado de la sesión, nunca confiado al cuerpo enviado por el cliente.

Concreción H0: `Inventario.Session` es HttpOnly y SameSite=Lax; el ticket dura 8 horas
con renovación deslizante. Cookie interna `Inventario.Antiforgery` HttpOnly distinta
de `XSRF-TOKEN`, que contiene el token de petición legible y se envía en X-XSRF-TOKEN.
Login renueva ese token ya vinculado a la identidad autenticada. Cookies Secure fuera
de Development; HTTP local de Compose usa SameAsRequest. Los rechazos por credenciales,
bloqueo o cuenta no confirmada comparten el error público InvalidCredentials.

Angular usa componentes standalone, formularios reactivos tipados, rutas lazy por función,
servicios HTTP y estado local con signals. PrimeNG aporta tablas, formularios, diálogos y
mensajes; etiquetas, navegación por teclado y estados no dependientes solo del color.
No hay registro público de cuentas; el entorno inicial recibe usuarios de prueba controlados.

**Motivo**: aplicación interna de navegador, con autorización identificable y sin tokens
de autenticación persistidos en localStorage. No se supone la existencia de SSO corporativo.

**Alternativas**: JWT para integraciones externas y SSO solo si una futura necesidad lo exige;
NgRx y SSR no son necesarios para el alcance inicial.

**Fuentes**:
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0
- https://angular.dev/best-practices/security

## R-07. Mensajes de compra y alertas

**Decisión**: la alerta interna se calcula y persiste dentro de la misma transacción
del saldo o mínimo, bajo el bloqueo de artículo. El envío de compra usa OutboxMessage y
BackgroundService, con reintentos y una identidad estable de mensaje; sin transacción
abierta durante SMTP. El pedido permanece en borrador con envío pendiente hasta que se
confirma aceptación por SMTP, momento en que pasa a enviado.

**Motivo**: ni el reinicio de la API pierde la intención de envío ni un cambio de existencias
deja una alerta silenciosamente desactualizada. SMTP tiene entrega al menos una vez:
aceptación por el servidor no prueba lectura ni entrega final al destinatario.

**Alternativas**: enviar correo dentro de la transacción alarga bloqueos y deja efectos
externos irreversibles. Un broker externo sería innecesario para el volumen previsto.

**Fuentes**:
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos

## R-08. Verificación y recuperación

**Decisión**: xUnit para reglas; WebApplicationFactory y SQL Server real para integración;
Vitest/TestBed para Angular; Playwright para recorridos críticos. Los tests de concurrencia
usan conexiones distintas y solapamiento controlado, no el proveedor EF InMemory.

Recuperación FULL, backup completo diario y log cada 15 minutos. Objetivos propuestos:
RPO ≤ 15 minutos y RTO ≤ 4 horas. Antes de producción deben aprobarlos los responsables
del proyecto y almacén y demostrarlos restaurando en una instancia separada, con CHECKDB,
conciliación de saldos/historial y revisión de movimientos físicos posteriores al punto
restaurado. Copias fuera del servidor, acceso restringido y retención inicial de 30 días.

**Motivo**: un backup generado no demuestra que los datos se recuperen. En Mac ARM se
utiliza SQL Server remoto, una VM x64 o emulación amd64 para Compose local;
no se presupone imagen SQL nativa ARM.

**Alternativas**: SQLite/InMemory no verifican los bloqueos ni restricciones SQL Server;
RESTORE VERIFYONLY no sustituye restaurar y comprobar el resultado.

**Fuentes**:
- https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy
- https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0
- https://angular.dev/guide/testing
- https://playwright.dev/docs/best-practices
- https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/back-up-and-restore-of-sql-server-databases?view=sql-server-ver17

## Cierre de investigación

Las incógnitas de versiones, arquitectura, concurrencia, reservas, lotes, mínimos,
envío y autenticación tienen una decisión inicial. El hosting definitivo y la edición
de producción no afectan a los contratos: se requiere ASP.NET Core 10 con ejecución
continua del worker, HTTPS y SQL Server 2025 x64 accesible. Su contratación y la aprobación
operativa de RPO/RTO son condiciones de puesta en servicio, no evidencia ya obtenida.

## R-09. Contenerización y preparación automática de desarrollo

**Decisión**: Dockerfile multi-stage Node → SDK .NET → runtime ASP.NET, con una única
imagen app que sirve Angular en el mismo origen. Compose levanta SQL Server, un
inicializador de un solo uso, la app y Mailpit. `depends_on` espera SQL saludable y la
finalización correcta del inicializador. Volúmenes para SQL, Data Protection y correo.

**Motivo**: permitir `docker compose up` sin SDKs ni comandos SQL manuales. El inicializador
aplica migraciones con administración y valida después el usuario de ejecución, sin
dar permisos de creación de bases o esquema al proceso HTTP. Los defaults locales
permiten arrancar sin `.env`; las variables sobrescriben puertos y credenciales.

**Alternativas**: separar Nginx y API añade otro servicio sin necesidad para la base inicial;
aplicar migraciones desde cada proceso HTTP mezcla permisos y crea carreras de arranque.
La automatización pertenece al entorno local; las migraciones de producción siguen siendo explícitas.

**Impacto**: transversal H0 de las ocho specs, sin cambiar entidades ni efectos de stock.
SQL se ejecuta como amd64; la emulación ARM es una opción de desarrollo. La persistencia
se comprueba recreando contenedores, no solo reiniciando procesos.

**Fuentes**:
- https://docs.docker.com/build/building/multi-stage/
- https://docs.docker.com/compose/how-tos/startup-order/
- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying

## R-10. Desbloqueo de referencias e identidad del núcleo H0

**Fecha**: 2026-11-12. **Estado**: implementado por solicitud de A; revisión de B pendiente.

**Decisión**: A adelanta Article mínima y su migración antes de `H0_StockCore`. B conserva
claves foráneas reales a Article desde el inicio. El contexto aplica configuraciones por
entidad desde archivos separados, manteniendo una única cadena de migraciones. No se
renombra ni regenera `H0Identity` ya existente. Tras integrar cambios, quien genere después
regenera su migración local aún no compartida si su snapshot quedó obsoleto.

El núcleo consume `ICurrentActor` sin depender del transporte. A lo implementa con Identity,
exigiendo un usuario autenticado existente, sin fallback al ejecutar fuera de HTTP. Las
pruebas pueden usar un actor controlado y las pruebas HTTP conservan la sesión real.

**Motivo**: evita tablas de stock con referencias huérfanas y dos definiciones de artículo,
y permite a B desarrollar el protocolo sin esperar al mantenimiento completo de HU-08.

**Alternativa descartada**: crear las tablas de B sin las claves foráneas y añadirlas en
HU-08, porque difiere la comprobación de integridad y puede exigir reparar referencias.

**Impacto y alcance**: no cambia estados ni efectos de stock, ni sustituye la futura
familia obligatoria, política inicial o auditoría de altas de HU-08. Article de H0 solo
se crea en preparación controlada de pruebas; no hay alta de negocio provisional. El
contrato completo de núcleo sigue en stock-operations: operación → documentos → ubicaciones
→ artículos, y escritura atómica de documento, diario, saldos, reservas aplicables y resultado.
El esqueleto y Receive/Reserve/Ship mínimos de B se validan en SQL Server real; las reglas
completas se concretan en sus historias. CustomerOrder mínimo queda a cargo de B en HU-03.

La spec consumidora HU-08 mantiene sus requisitos y registra esta preparación en su
[handoff H0](../../../specs/002-gestionar-datos-maestros/h0-handoff.md). HU-02/HU-03/HU-04/
HU-05/HU-07 aún deben consumir estos acuerdos al preparar sus specs. No se modifican
obligaciones constitucionales ni se acredita una revisión A+B no realizada.

## R-11. Concreciones de integración del diseño HU-08

**Fecha**: 2026-11-12. **Estado**: diseño propuesto; revisión A+B pendiente antes de integrar.

Actualización de implementación 2026-11-17: el usuario autorizó adelantar la base común
desde A. Resultado/ejecutor/bloqueos, auditoría append-only y familias están implementados,
con pruebas descritas en la evidencia local de HU-08. La huella común ordena propiedades
JSON ordinalmente tras normalizar el DTO; los arrays conservan su orden contractual.
Las partes restantes de esta decisión siguen pendientes y la revisión de B no está realizada.

**Decisión**: conservar la marca monotónica IsDefinitionLocked en Article, fijada por la
primera referencia bajo el bloqueo/transacción comunes. Retirar una línea de borrador no
elimina ese hecho; la comprobación para borrar sigue basada en referencias e historial,
no únicamente en la marca. Los lectores de uso deben cubrir los consumidores integrados.

OperationId es la clave idempotente; OperationResult común añade cabeceras reproducibles
ETag/Location y permiso original, para autorizar y reproducir un resultado incluso después
de borrar el maestro. MasterDataAudit conserva EntityId histórico sin FK al maestro y
acciones de eliminación. El rol SQL inventory_runtime permite aplicar DENY sobre evidencias
antes de que el bootstrap cree el usuario de ejecución.

Family y Supplier se coordinan en la fase documental con recursos canónicos propios,
manteniendo el orden global. B entrega ejecutor/resultados H0 y A los consume en HU-08;
las contribuciones de A al componente común se revisan con B sin duplicar implementaciones.

Remediación aprobada del análisis HU-08: la preparación de familia se realiza mediante
handler HTTP de mantenimiento autenticado, ICurrentActor y ejecutor común, en un modo
explícito sobre esquema expandido y sin DDL en la API. No se acepta un ActorId manual.
El gate de cambios de definición comprueba lectores y enlaces de primera referencia de
los consumidores presentes antes de habilitar el endpoint para esos cambios; las nuevas
features completan esa integración antes de habilitar sus escritores. No exige esperar
historias futuras cuando H1 acredita ausencia de esos consumidores.

**Motivo**: evitar desbloqueo por pérdida de referencias actuales, evidencia que impida
borrar o se borre en cascada, replay que falle con 404 tras un DELETE y permisos SQL
dependientes de un login configurable. No cambia estados ni aritmética de inventario.

**Alternativas**: solo consultas Any, auditoría con FK/cascada a Article, caché idempotente
en memoria, recurso vivo obligatorio para recuperar resultado y protocolos privados por feature.

**Impacto**: HU-08 y escritores de HU-01/HU-03/HU-04/HU-05/HU-07; HU-06 consume la misma
StockPolicy y participa en cambios de estado cuando esté integrada. La incorporación exige
revisión cruzada de entidades, contratos y migraciones. El diseño particular, fuentes y
validación están en el [plan HU-08](../../../specs/002-gestionar-datos-maestros/plan.md).
