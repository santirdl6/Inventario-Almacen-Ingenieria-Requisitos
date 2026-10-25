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
organizado por funcionalidades, con Commands y Queries separados. Controllers delgados
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
