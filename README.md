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
| `initialize` | Tarea de un solo uso: migraciones y permisos. Debe terminar con código 0. |
| `smtp` | Mailpit con persistencia para capturar los correos de desarrollo. |
| `app` | API ASP.NET Core que sirve el frontend Angular compilado en el mismo origen. |

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
```

La prueba integrada crea y elimina una tabla técnica `__ContainerPersistenceCheck`,
recrea los contenedores y deja el entorno arrancado. No ejecuta las ocho historias de negocio.

## Persistencia

| Volumen | Contenido |
|---------|-----------|
| `sqlserver-data` | Datos y logs de SQL Server (`/var/opt/mssql`). |
| `app-keys` | Claves Data Protection (`/app/keys`), reutilizables por la autenticación futura. |
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

El esquema actual contiene el historial de migraciones. Las entidades, autenticación,
permisos de negocio, diario y worker SMTP se añadirán por sus specs. Al crear el diario,
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
