# Inventario y almacén

Base del proyecto con Angular + PrimeNG, ASP.NET Core 10 y SQL Server 2025.
El repositorio incluye la infraestructura ejecutable y una pantalla de estado que consulta
la API y comprueba SQL Server. Las ocho historias del almacén se desarrollarán en sus specs;
el arranque de esta base no implica que ya estén implementadas.

## Arranque con un comando

Requisitos: Docker Engine/Docker Desktop iniciado y Docker Compose v2.20 o posterior,
con al menos 4 GB de memoria disponibles para el entorno. Solo se necesita Docker;
la compilación de Angular y .NET se realiza dentro de las imágenes.

Desde la raíz del proyecto:

```bash
docker compose up
```

Compose construye la imagen, espera a SQL Server, crea la base, aplica las migraciones,
prepara el usuario de ejecución y arranca la aplicación. No hay que copiar `.env`, instalar
SDKs ni ejecutar comandos de base de datos. La primera ejecución descarga dependencias
y puede tardar varios minutos.

- **Aplicación:** http://localhost:8081
- **Estado API/base:** http://localhost:8081/api/v1/health
- **Borrador OpenAPI de H0:** http://localhost:8081/openapi/v1.json (Development)
- **Buzón SMTP de desarrollo:** http://localhost:8025

SQL Server no publica su puerto al host. El correo queda capturado en Mailpit; no se envía
a proveedores reales. El entorno Compose es de desarrollo local, publicado en loopback
y HTTP. El despliegue de producción mantiene el requisito de HTTPS del plan global.

En equipos Apple Silicon/ARM, SQL Server se ejecuta como `linux/amd64`; Docker debe
tener habilitada la emulación x86-64. Los demás contenedores utilizan su arquitectura nativa.
Microsoft soporta SQL Server Linux sobre x64; la emulación es una opción de desarrollo,
no un despliegue de producción soportado.

## Servicios e imagen multi-stage

| Servicio | Función |
|----------|---------|
| `sqlserver` | Base SQL Server con healthcheck de conexión real. |
| `initialize` | Tarea de un solo uso: migraciones, permisos SQL, roles Identity y cuenta inicial. Debe terminar con código 0. |
| `smtp` | Mailpit con persistencia para capturar los correos de desarrollo. |
| `app` | API ASP.NET Core que sirve el frontend Angular compilado en el mismo origen. |
| `tests` | Perfil opcional `test`: pruebas de H0 contra bases SQL aisladas. |

El `Dockerfile` tiene etapas de compilación frontend, publicación backend y ejecución.
La imagen final ejecuta la app como usuario sin privilegios e incluye únicamente runtime,
publicación .NET y recursos Angular. `.dockerignore` excluye SDK outputs, node_modules,
documentación de agentes y archivos de entorno privados.

`initialize` recibe credenciales de administración. `app` utiliza un usuario SQL separado,
sin permisos de crear bases, logins o tablas. La inicialización se puede repetir: no borra
datos ni crea otra base cuando ya existe. Si la configuración o migración falla, Compose
no inicia la app como si el entorno estuviera listo.

## Configuración por variables de entorno

Todos los valores necesarios tienen defaults locales para permitir el arranque directo.
Las contraseñas indicadas son exclusivamente las credenciales públicas de este entorno
de desarrollo. Se pueden sobrescribir desde el shell o mediante un archivo `.env` opcional.

| Variable | Valor por defecto | Uso |
|----------|-------------------|-----|
| `APP_BIND_ADDRESS` | `127.0.0.1` | Interfaz publicada para app y buzón. |
| `APP_PORT` | `8081` | Puerto HTTP de la aplicación en el host. |
| `MAILPIT_PORT` | `8025` | Puerto web del buzón SMTP. |
| `DB_NAME` | `InventoryDev` | Nombre de base; letras, números y `_`, empezando por letra. |
| `SQL_SA_PASSWORD` | `LocalSqlAdmin_2026!` | Inicialización y healthcheck SQL. |
| `SQL_APP_USER` | `inventory_app` | Usuario de ejecución, con las mismas reglas de nombre que la base. |
| `SQL_APP_PASSWORD` | `LocalSqlApp_2026!` | Contraseña del usuario de ejecución. |
| `INITIAL_ADMIN_EMAIL` | `admin@localhost.test` | Cuenta local creada por el inicializador; solo para desarrollo. |
| `INITIAL_ADMIN_PASSWORD` | `LocalAdmin_2026!ChangeMe` | Contraseña local inicial; cambiar antes de compartir el entorno. |
| `SMTP_FROM` | `inventario@localhost.test` | Remitente previsto para el futuro worker de compras. |

Ejemplo para publicar en otro puerto, sin reconstruir Angular:

```bash
APP_PORT=9090 docker compose up -d
```

Alternativamente, copiar `.env.example` a `.env` y editarlo es opcional. `.env` se excluye
del contexto de construcción y del control de versiones. Las variables de entorno del shell
tienen precedencia sobre `.env`. Las claves `Database__*`, `Smtp__*` y `DataProtection__*`
del contenedor siguen la configuración estándar de ASP.NET Core.

Al reutilizar un volumen SQL existente, conservar sus credenciales: cambiar una variable
no cambia automáticamente la contraseña de un login ya creado. La inicialización detecta
una contraseña de ejecución incompatible y falla, en lugar de reemplazar datos o usuarios.

La configuración Compose crea los cinco roles Operario, ResponsableAlmacen, Comprador,
GestorMaestros y Administrador. Solo crea una cuenta: el administrador local indicado arriba.
Repetir la inicialización conserva esa cuenta, su contraseña y sus datos; no crea usuarios
de almacén ni concede todos los permisos al administrador. La administración de cuentas
por API se desarrollará posteriormente. No hay registro público.

## Sesión e Identity — base H0 de la Persona A

Cuenta local inicial: `admin@localhost.test` / `LocalAdmin_2026!ChangeMe`, configurables
antes del primer arranque. La contraseña solo se entrega al inicializador, no al proceso API.
Identity almacena su hash en SQL; las cookies están protegidas con las claves persistidas.

| Método y ruta | Resultado |
|---------------|-----------|
| `GET /api/v1/auth/csrf` | 200 con `{ requestToken }` y las cookies antiforgery. |
| `POST /api/v1/auth/login` | JSON `{ email, password }`; 200 con sesión y token renovado. |
| `GET /api/v1/auth/me` | 200 con `id`, `email`, `roles`, `permissions`; 401 sin sesión. |
| `POST /api/v1/auth/logout` | 204 y eliminación de la sesión; requiere sesión y token. |

El cliente debe conservar cookies y enviar `X-XSRF-TOKEN` en los POST. El token se
obtiene de la cookie legible `XSRF-TOKEN` o del cuerpo JSON. `Inventario.Antiforgery`
es otra cookie interna HttpOnly: no se copia a esa cabecera. Tras login se utiliza el
token renovado; tras logout se solicita uno anónimo nuevo antes del siguiente login.
Angular HttpClient usa los nombres acordados automáticamente con peticiones relativas.

La cookie `Inventario.Session` es HttpOnly, SameSite=Lax y de sesión; el ticket dura
8 horas con renovación deslizante. Las cookies son Secure fuera de Development.
Contraseñas: mínimo 12 caracteres, mayúscula, minúscula, número y símbolo. Cinco fallos
bloquean nuevos accesos durante 15 minutos; la respuesta pública continúa siendo un 401
genérico. Campos inválidos devuelven 422 y antiforgery incorrecto, 400.

Los permisos se derivan de roles mediante policies del servidor. La cuenta Administrador
tiene `Users.Manage`, no permisos implícitos de stock. Para proteger un futuro endpoint,
se utiliza `.RequireAuthorization("MasterData.Manage")` o el permiso que le corresponda.
Las rutas nuevas del grupo `/api/v1` heredan autenticación y el filtro antiforgery;
las excepciones anónimas deben declararse explícitamente.

Los errores usan `application/problem+json`, `code` y `traceId`, sin redirecciones HTML
ni detalles de SQL. El OpenAPI expone los endpoints implementados de H0; el borrador
de los futuros recursos está en [contracts/api.md](.specify/memory/project/contracts/api.md).
Esta entrega proporciona la API de sesión; la pantalla de login pertenece al trabajo de interfaz.

## Comandos habituales

```bash
# Segundo plano y espera de disponibilidad (initialize debe terminar correctamente)
docker compose up -d --wait --wait-timeout 300

# Reconstruir tras cambios de código o dependencias
docker compose up --build -d

# Estado y diagnóstico; initialize en Exited (0) es el resultado esperado
docker compose ps -a
docker compose logs -f app initialize sqlserver

# Detener conservando datos
docker compose stop

# Eliminar contenedores/red; conservar los volúmenes para el siguiente arranque
docker compose down

# Verificar la configuración sin arrancar servicios
docker compose config --quiet

# Prueba integrada: arranque, migraciones, rutas SPA y datos tras recrear contenedores
sh scripts/verify-containers.sh

# Pruebas H0: Identity, permisos, antiforgery y bootstrap SQL (sin SDK local)
docker compose --profile test run --build --rm tests
```

La prueba integrada crea y elimina una tabla técnica `__ContainerPersistenceCheck`,
recrea los contenedores y deja el entorno arrancado. No ejecuta las ocho historias de negocio.

Las pruebas del perfil `test` crean una base `InventoryH0Tests_...` y un login SQL aleatorios,
aplican las migraciones, ejercitan la API real con WebApplicationFactory y los eliminan
al finalizar. No se borran ni se reinician los datos de `InventoryDev`. Para ejecutarlas
fuera de Docker, definir `Testing__SqlConnection` con una conexión administrativa de un
SQL Server de desarrollo/pruebas y ejecutar `dotnet test backend/Inventario.slnx`.

## Persistencia

| Volumen | Contenido |
|---------|-----------|
| `sqlserver-data` | Datos y logs de SQL Server (`/var/opt/mssql`). |
| `app-keys` | Claves Data Protection (`/app/keys`) para mantener las sesiones entre recreaciones. |
| `mailpit-data` | Mensajes capturados en el buzón de desarrollo. |

Los volúmenes sobreviven a `stop`, `down`, reconstrucciones y recreación de contenedores.
No son una copia de seguridad: la política de backup y restauración está en la guía global.

Para **borrar todos los datos del entorno local y empezar de cero**:

```bash
docker compose down --volumes
docker compose up --build
```

## Desarrollo y siguientes funcionalidades

La API sirve Angular desde `wwwroot`; las peticiones del navegador usan `/api/v1` relativo,
sin una URL de backend compilada ni CORS adicional. GET `/api/v1/health` devuelve 200
solo si la conexión SQL funciona y no hay migraciones pendientes; si no, devuelve 503.
Las rutas `/api` desconocidas devuelven 404 y no el HTML de Angular.

El esquema contiene el historial de migraciones, Identity y la referencia mínima `Articles`
para el núcleo H0. Article incluye ID, código, nombre, unidad, fracciones, lotes, estado y
versión; la familia y el mantenimiento completo llegan en HU-08. La migración
`20261112105657_H0ArticleReference` se añade después de `H0Identity` sin sustituirla.
Los comandos pueden consumir `ICurrentActor` para obtener el usuario de la sesión validado
contra Identity. El [handoff H0](specs/002-gestionar-datos-maestros/h0-handoff.md) contiene
el contrato que utiliza B y los resultados de validación.

La autenticación por cookie,
antiforgery, la sesión y el catálogo inicial de roles se incluyen en H0; los endpoints y
permisos de negocio se completarán en sus specs. El diario y worker SMTP se añadirán después.
Al crear el diario,
su migración debe restringir UPDATE/DELETE al usuario de ejecución según el contrato común.
Los permisos de DDL permanecen en el inicializador, no en la API.

Para desarrollo fuera de Docker, consultar los comandos del
[quickstart](.specify/memory/project/quickstart.md); requiere los SDKs correspondientes.

## Documentación del proyecto

- [Constitución](.specify/memory/constitution.md).
- [Plan global](.specify/memory/project-plan.md).
- [Decisiones técnicas](.specify/memory/project/research.md).
- [Modelo compartido](.specify/memory/project/data-model.md).
- [Catálogo y reparto de las ocho historias](specs/001-reparto-specs-inventario/spec.md).
- [Plan de coordinación](specs/001-reparto-specs-inventario/plan.md).
