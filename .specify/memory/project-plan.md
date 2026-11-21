# Plan global de inventario y almacén

**Date**: 2026-10-25 | **Ámbito**: todas las specs del proyecto.
**Ampliación**: entorno contenedorizado y base ejecutable H0, 2026-10-25.
**Persona A / H0**: API de sesión Identity, migración y bootstrap de identidad, contratos
y pruebas de integración SQL. Preparación técnica transversal, sin nueva spec de negocio.
**Desbloqueo A → B, 2026-11-12**: referencia mínima de Article, configuraciones por entidad
e ICurrentActor implementados para que el núcleo H0 de B pueda consumir identidad y claves
foráneas reales. La revisión cruzada A+B de esta ampliación sigue pendiente de registrarse.
**Base normativa**: [constitución v1.0.0](constitution.md).
**Origen**: solicitud de arquitectura conjunta con Angular + PrimeNG, ASP.NET Core
con CQRS ligero y SQL Server; [catálogo y reparto](../../specs/001-reparto-specs-inventario/spec.md).

Este documento y sus anexos en `project/` son la fuente única de acuerdos técnicos
compartidos. Cada spec tiene su propio plan de implementación y referencia esta base.

## Summary

Establecer la arquitectura y los contratos compartidos de las ocho historias, conservando
el reparto A: HU-08, HU-07, HU-01, HU-06; B: HU-02, HU-05, HU-03, HU-04.
Cada persona desarrollará verticalmente sus pantallas, endpoints, reglas y pruebas.
La constitución v1.0.0 es la norma superior; este plan concreta su aplicación técnica.

Una SPA Angular y una API ASP.NET Core, organizadas por funcionalidad, trabajan sobre una
única base SQL Server. CQRS separa comandos y consultas dentro del proceso. Un servicio
común de inventario aplica saldos, reservas e historial en la misma transacción que los
documentos. Las consultas leen cambios confirmados sin proyecciones diferidas.

Los acuerdos de [research.md](project/research.md), [data-model.md](project/data-model.md) y
[contracts](project/contracts/api.md) son la base a referenciar desde cada futura spec. Estas
podrán concretar sus flujos, sin crear otro cálculo de disponibilidad ni otra vía de escritura.

## Technical Context

**Language/Version**: TypeScript 6.0.x con Angular/CLI 22.2.x y Node 24 LTS;
C# 14 en .NET/ASP.NET Core 10 LTS. Baseline investigada: Angular 22.2.3 y TS 6.0.3.

**Primary Dependencies**: PrimeNG 22.1.4 y CDK 22 compatible; RxJS 7.8.x;
EF Core 10 y Microsoft.EntityFrameworkCore.SqlServer del mismo parche; ASP.NET Core
Identity; OpenAPI del backend. Handlers invocados directamente por DI.

**Storage**: SQL Server 2025; una base, un InventoryDbContext por comando, migraciones
EF Core compartidas; dinero decimal(18,2), cantidades decimal(18,3), UTC datetimeoffset,
identificadores UUID y rowversion para edición de documentos.
La preparación de la base habilita READ_COMMITTED_SNAPSHOT y ALLOW_SNAPSHOT_ISOLATION
para las lecturas consistentes definidas en research R-03.

**Testing**: xUnit; WebApplicationFactory con SQL Server real aislado; Vitest/TestBed;
Playwright. Matriz de validación en [quickstart.md](project/quickstart.md).

**Target Platform**: SPA de navegador en español, Chrome/Edge vigentes; API sobre
Linux o Windows x64 con HTTPS; SQL Server local x64 o remoto. Desarrollo en macOS
admitido con SQL Server remoto/VM x64, o emulación amd64 para desarrollo con Compose.
Worker de correo previsto en el proceso API siempre activo.

**Project Type**: aplicación web interna; monolito modular; REST JSON bajo /api/v1.

**Performance Goals**: objetivos iniciales, no mediciones: p95 ≤ 2 s para consulta paginada
y confirmación de hasta 20 líneas, excluyendo espera por conflicto y envío SMTP;
25 sesiones concurrentes, 10.000 artículos y 1.000.000 de entradas de historial.
Confirmación de stock y alerta interna visibles en la primera consulta tras el commit.

**Constraints**: ninguna confirmación parcial; no stock negativo ni reservas superiores
al físico; no duplicados por reintento; historial no editable; ubicación obligatoria;
permisos de servidor y usuario auditado. RPO ≤ 15 min / RTO ≤ 4 h propuestos, con
aprobación y restauración demostrada antes de producción. La base H0 incluye frontend/API
de estado y arranque Compose; la implementación del inventario corresponde a las ocho specs.

**Scale/Scope**: una pyme, un almacén inicial con varias ubicaciones, dos desarrolladores,
ocho futuras specs. Alta/consulta del pedido de cliente mínimo y reservas en HU-05,
consumidos por las salidas de HU-03 (reparto confirmado 2026-11-19);
sin facturación, módulo comercial completo ni reservas automáticas.

## Constitution Check

*GATE: revisión previa a Phase 0 y repetición después de Phase 1. PASS significa
conformidad del diseño; no certifica una implementación ni una validación operativa.*

| Principio o regla | Compromiso verificable de diseño | Antes | Después |
|------------------|----------------------------------|-------|---------|
| I. Correspondencia almacén/sistema | Confirmación al realizar la operación; incidencias explícitas; conciliación de diario/saldos; comprobante de contingencia e importación con identidad única. | PASS | PASS |
| II. Ubicación precisa | Código único, pasillo/estantería/nivel; saldo por artículo/ubicación/lote; recepción exige destino incluso temporal. | PASS | PASS |
| III. Uso intuitivo | Formularios en español, revisión antes de confirmar, errores conservando datos, validación con operarios y resultados no basados solo en color. | PASS | PASS |
| IV. Trazabilidad | Diario append-only, actor de sesión, instantes del hecho y registro, unidad, motivo y documento; correcciones compensatorias relacionadas. | PASS | PASS |
| V. Integridad | Transacciones y bloqueos SQL, idempotencia, permisos y restricciones; concurrencia comprobada en SQL real; restauración con objetivos definidos. | PASS | PASS |
| Controles operativos | Artículo/unidad únicos; autorizar ajustes; conteos por ubicación con motivo y responsables; calendario de conteos definido antes de operar. | PASS | PASS |
| Revisión y gobernanza | Cada spec referencia esta matriz y la constitución vigente; evidencia por historia, revisión cruzada y actualización de contratos afectados. | PASS | PASS |

Evidencia de diseño: [entidades e invariantes](project/data-model.md),
[protocolo transaccional](project/contracts/stock-operations.md),
[errores y permisos](project/contracts/api.md), [interacción](project/contracts/ui.md)
y [escenarios V-01 a V-14](project/quickstart.md). No se detectan excepciones constitucionales.
Los flujos adicionales de devoluciones y traslados quedan sujetos al mismo protocolo cuando
se especifiquen; no se presentan como funcionalidades ya cubiertas por las ocho historias.

## Project Structure

### Documentación global y por spec

```text
AGENTS.md                         # Lectura de la base global en el flujo del proyecto
.specify/memory/
├── constitution.md               # Principios y gobernanza
├── project-plan.md               # Acuerdos técnicos globales (este documento)
└── project/
    ├── research.md
    ├── data-model.md
    ├── quickstart.md
    └── contracts/
        ├── api.md
        ├── stock-operations.md
        └── ui.md
specs/
└── 001-reparto-specs-inventario/
    ├── spec.md                   # Catálogo y propuesta de reparto 4+4
    ├── plan.md                   # Hitos y coordinación de esa propuesta
    └── checklists/requirements.md
```

### Source Code (repository root)

La base ejecutable incluye el proyecto API, el frontend, el inicializador y la migración
de infraestructura. Las carpetas de funcionalidades y suites de negocio siguientes
son la estructura prevista para las futuras specs.

```text
Dockerfile                       # Node build → .NET publish → runtime sin privilegios
.dockerignore
docker-compose.yml
.env.example
README.md
scripts/verify-containers.sh
backend/
├── Inventario.slnx
├── src/Inventario.Api/
│   ├── Features/
│   │   ├── MasterData/       # A: HU-08
│   │   ├── Purchasing/       # A: HU-07
│   │   ├── Receiving/        # A: HU-01
│   │   ├── Replenishment/    # A: HU-06
│   │   ├── Locations/        # B: HU-02
│   │   ├── Stock/            # B: HU-05, reservas y servicio común
│   │   ├── Shipping/         # B: HU-03
│   │   └── CycleCounts/      # B: HU-04
│   ├── Domain/
│   └── Infrastructure/
│       ├── Persistence/Migrations/
│       ├── Identity/
│       └── Messaging/
└── tests/
    ├── Inventario.UnitTests/
    └── Inventario.IntegrationTests/
frontend/
├── src/app/
│   ├── core/                # sesión, HTTP, errores y navegación
│   ├── shared/              # controles y presentación reutilizables
│   └── features/            # mismas ocho áreas del backend
└── e2e/                     # Playwright
```

**Structure Decision**: un proyecto backend productivo, carpetas por funcionalidad y dos
proyectos de pruebas. Dentro de cada feature: Commands, Queries y transporte HTTP.
Domain contiene reglas comunes; Infrastructure configura persistencia, identidad y correo.
No se crean ocho bases ni capas desplegables. Cada persona mantiene ambos extremos de sus
funciones. Los archivos transversales se revisan conjuntamente antes de fusionarse.

### Alcance global y coordinación

La investigación y el diseño compartido están documentados en `project/`.
No quedan incógnitas técnicas bloqueantes; los defaults se revisarán al detallar cada historia.
La revisión constitucional de este diseño es PASS, sin excepciones.

La secuencia H0–H5, el reparto de trabajo y los puntos de encuentro se mantienen en el
[plan de coordinación](../../specs/001-reparto-specs-inventario/plan.md).
Cada futura spec añade su propio plan, tareas y evidencia de aceptación, sin copiar los
acuerdos de arquitectura. La base técnica global no depende de la selección de una feature
en `.specify/feature.json` ni de una rama Git concreta.

### Entorno contenedorizado común

El arranque local se realiza con `docker compose up` desde la raíz, sin SDKs locales,
sin copia obligatoria de `.env` y sin inicialización manual. El
[README](../../README.md) contiene los comandos y la tabla de variables.

- Un `Dockerfile` de tres etapas compila Angular/PrimeNG, publica ASP.NET Core y crea
  una imagen de ejecución sin privilegios. La API sirve los recursos de Angular en el
  mismo origen; la URL `/api/v1` relativa evita configuración de backend en el bundle.
- Servicios: `sqlserver` (SQL Server 2025 Developer), `initialize` (misma imagen app,
  modo de inicialización y finalización), `app` y `smtp` (Mailpit).
- SQL debe superar su healthcheck real; `initialize` crea la base y aplica migraciones
  EF Core, habilita RCSI/SNAPSHOT y configura un usuario de ejecución sin permisos DDL.
  La app solo se inicia si el inicializador termina correctamente. No hay reseteo ni
  carga de datos de negocio al arrancar. Después de la migración de Identity se crean
  los cinco roles y la cuenta administrativa configurada. La reinicialización no resetea
  su contraseña ni duplica roles/usuarios.
- La automatización de migraciones se limita al Compose de desarrollo. En producción
  se mantiene la aplicación explícita de migraciones y la validación previa de cambios.
- `sqlserver-data`, `app-keys` y `mailpit-data` conservan base, claves Data Protection
  y mensajes. El borrado de volúmenes es una acción explícita, nunca parte del arranque.
- Puertos y credenciales se configuran por entorno, con defaults públicos de desarrollo;
  la credencial administrativa se entrega solo a SQL/inicializador, no a la API.
  El usuario de ejecución tiene lectura/escritura sin DDL. Al crear el diario, su migración
  añadirá las restricciones de UPDATE/DELETE exigidas por el contrato de trazabilidad.
- Publicación local en loopback y HTTP; producción conserva HTTPS. SQL no publica puerto
  al host. Mailpit captura los mensajes sin enviarlos a proveedores reales.
- SQL Server usa `linux/amd64`; Apple Silicon requiere emulación Docker para este entorno
  local. La ejecución de producción se realiza sobre SQL Server x64 soportado.

La base de Persona A / H0 incorpora Identity con claves Guid, endpoints de sesión,
policies de permisos por rol y errores ProblemDetails con traceId. La cookie interna
antiforgery y el token de Angular son distintos y se renueva el token de petición al
autenticar. Los endpoints nuevos exigen sesión por defecto y declaran sus excepciones
anónimas. Cookies Secure fuera de Development; el Compose local mantiene HTTP en loopback.
OpenAPI generado solo en Development documenta health y sesión. Los contratos de negocio
siguen siendo borradores para las ocho historias.

El modo de inicialización registra su DbContext de Identity con la conexión administrativa
de destino, migra y siembra cuentas. La API registra únicamente la conexión de ejecución.
Ambos procesos comparten el modelo, no sus credenciales administrativas. La configuración
de diseño respeta ConnectionStrings__Inventory y no requiere una contraseña ficticia
embebida para generar migraciones. La confianza en el certificado SQL autofirmado se
habilita expresamente en el Compose local, no por defecto en la configuración general.

La preparación A → B incorpora `Domain/MasterData/Article`, tabla `Articles` y
`InventoryDbContext.Articles`, mediante `20261112105657_H0ArticleReference` sobre
`20261029111255_H0Identity`. Incluye ID Guid, SKU único, nombre, unidad base, fracciones,
lotes, estado y rowversion. Es una referencia técnica sin altas HTTP ni mantenimiento:
familia obligatoria, proveedores, auditoría y política inicial se completan en HU-08.
No se crean artículos ni existencias de negocio al arrancar. Las referencias de B deben
usar este Article y restringir el borrado, sin crear otra entidad paralela.

La aclaración de HU-08 del 2026-11-12 fija que unidad base, seguimiento por lote y admisión
de fracciones solo pueden cambiarse sin documentos, reservas ni movimientos asociados,
incluidos borradores. La regla se recoge en R-05, modelo y contratos comunes; su revisión
con B queda pendiente antes de integrar los cambios de las historias consumidoras.

El diseño de HU-08 concreta esa regla con una marca monotónica IsDefinitionLocked, cobertura
de referencias reales, auditoría que sobrevive al borrado y OperationResult reproducible
con ETag/Location y permiso original. El rol SQL inventory_runtime coordina las restricciones
append-only sin depender del nombre de login configurado. Estas concreciones están
implementadas en el corte H1 descrito abajo, con revisión de B pendiente, y registradas
en el modelo y contratos comunes.
El [plan HU-08](../../specs/002-gestionar-datos-maestros/plan.md) detalla migración de familia,
dependencias H0 y validación; no se crea un segundo ejecutor ni otra cadena de migraciones.

El 2026-11-16 el usuario autorizó a A a adelantar el resultado/ejecutor/bloqueos comunes
ante la ausencia de entrega B. Es el mismo componente que consumirá H0_StockCore, con
pruebas SQL reales y revisión de B pendiente antes de integrar; no reasigna las operaciones
ni las cuatro historias de B. Las migraciones de B deben partir de la cadena que incluya
esta base común, evitando crear OperationResult por segunda vez.

Estado verificado 2026-11-17: esa base, auditoría/rol SQL, familias y catálogo Article H1
están implementados y pasan 64 pruebas backend, 22 unitarias frontend y 7 E2E, cuatro de
ellas en Chromium. También hay sesión/layout frontend mínimos. Cadena añadida:
H0OperationProtocol → HU08AuditInfrastructure → HU08Families → HU08MasterDataExpand →
HU08RequireArticleFamily, sin modificar migraciones H0 previas. La marca de definición,
familia obligatoria y política inicial cero están implementadas; evidencia en
[US2](../../specs/002-gestionar-datos-maestros/validation/us2.md). Este estado no acredita
referencias/stock integrados, revisión de B ni aceptación humana.

La preparación de FamilyId se diseña como mantenimiento HTTP autenticado: actor real de
Identity, MasterData.Manage, antiforgery y ejecutor común, con la conexión de ejecución sin
DDL. El modo explícito de transición no habilita escritores ordinarios; las migraciones
siguen siendo externas. Antes de admitir cambios de definición se verifica lector y enlace
de primera referencia de cada consumidor presente; las features futuras completan esa
integración antes de habilitarse. Mantenimiento/gate H1 verificados en ese corte; los
consumidores futuros y la revisión cruzada siguen pendientes, sin despliegue productivo.

`Domain/Identity/ICurrentActor.GetRequiredActorIdAsync` es el contrato de actor de los
comandos. Su implementación scoped usa el principal autenticado de Identity y comprueba
que el usuario existe en el mismo contexto; sin actor válido rechaza la escritura, con
401 ProblemDetails en HTTP. No acepta ActorId del cliente ni asigna un actor predeterminado
fuera de una petición. Las pruebas del núcleo pueden sustituir la interfaz por un actor
controlado que corresponda a un usuario de su base de pruebas.

El contexto aplica `IEntityTypeConfiguration` del ensamblado después de configurar Identity;
cada entidad nueva tiene su archivo en `Infrastructure/Persistence/Configurations/`.
Esto reduce conflictos de configuración, pero no divide el contexto ni la cadena de
migraciones. B genera `H0_StockCore` después de integrar `H0ArticleReference`, regenerando
su migración local no compartida si partía de un snapshot antiguo.
La preparación y evidencia particulares están en el
[handoff H0 de HU-08](../../specs/002-gestionar-datos-maestros/h0-handoff.md).

Las pruebas WebApplicationFactory usan SQL Server real y bases/logins temporales con
nombre aleatorio. El perfil `test` permite ejecutarlas sin SDK local, con una imagen
backend independiente de la compilación Angular. No altera InventoryDev.
El diario, reservas y Receive/Reserve/Ship mínimos para probar el protocolo corresponden
a la parte B de H0; sus reglas completas se concretan en HU-01/HU-03/HU-04/HU-05.
El worker SMTP y la interfaz de login se desarrollan en sus cortes posteriores.
La parte B del núcleo de stock y la revisión conjunta de H0
se coordinan con el [plan de reparto](../../specs/001-reparto-specs-inventario/plan.md).

### Sincronización con las futuras specs

La reserva de numeración se mantiene en [docs/agrupamiento.md](../../docs/agrupamiento.md):
B 004–006 (HU-05/HU-03/HU-04), A 007–009 (HU-07/HU-01/HU-06). El acuerdo adelanta el
pedido mínimo a HU-05 sin cambiar modelo, estados, permisos ni cálculo de stock;
HU-03 consume el mismo pedido y las reservas para confirmar salidas y actualizar servido.

El cierre técnico de HU-08 asigna las integraciones de nuevos escritores al
[backlog receptor por HU](../../specs/001-reparto-specs-inventario/integration-tasks.md).
Sus tareas se incorporan a cada spec consumidora al crearla; no bloquean la entrega del
catálogo ya implementado. La revisión de cambios compartidos se conserva en COORD-01
y las condiciones de puesta en servicio en H5, con las mismas obligaciones constitucionales.

La ampliación `20261119171332_HU08MovementCounterparty` añade al diario
CounterpartyNotApplicableReason opcional y exige contraparte o motivo en cada hecho nuevo.
Conserva filas y huellas históricas; detalle en el modelo y protocolo comunes. Se prepara
localmente por la solicitud del usuario de terminar HU-08, con revisión cruzada antes de
integrar/fusionar registrada en COORD-01; no se presume una aprobación de B.

1. Cada futura spec y plan enlaza este plan, la constitución vigente y las secciones
   de contratos/modelo que consume. Identifica HU original y responsable principal.
2. Incluye su matriz de principios aplicables, aceptación, permisos y dependencias.
3. Cambiar cantidades, reservas, estados o efectos transaccionales requiere revisar
   las dos personas, modificar aquí el acuerdo común y registrar el impacto en specs consumidoras.
4. Un cambio técnico no modifica por sí mismo la constitución; si altera una obligación
   normativa, se tramita la enmienda según Governance antes de aceptar ese diseño.
5. Una única cadena de migraciones: nombrar por HU y propósito; coordinar generación
   sobre la última migración integrada. Si dos cambios alteran el snapshot, regenerar
   la migración local aún no compartida, sin borrar migraciones ya aplicadas en entornos comunes.
6. Integrar por cortes verticales y ejecutar pruebas de contratos/SQL afectadas antes
   de continuar con el siguiente hito. Estimar por historia antes de comprometer fechas.

### Estrategia de aceptación

La guía quickstart define herramientas, ejecución y resultados V-01 a V-14. Se priorizan
concurrencia, rollback, duplicación, reservas propias/ajenas, ubicaciones y reconstrucción
del historial. Los recorridos de UI se contrastan con trabajadores representativos:
completan recepción y salida sin asistencia técnica ni errores no detectados; se conserva
evidencia y se corrigen los fallos antes de aprobar la entrega.

Antes de producción: calendario de conteos aprobado por el responsable del almacén;
responsables de registrar/autorizar/revisar identificados; RPO/RTO aprobados y demostrados;
procedimiento de contingencia y conciliación practicado. Estos son criterios derivados
de la constitución, no tareas declaradas ya realizadas.

Revisión documental final (2026-10-25): ocho HU asignadas 4+4; los cinco principios
tienen controles y escenarios; modelo y contratos comparten efectos, estados y precisión;
las incógnitas técnicas tienen defaults explícitos en research; no se requieren excepciones.
Se revisaron en particular reservas remanentes al servir desde otro origen, liberaciones
parciales repetidas y recuperación de respuestas perdidas. Builds, tests de aplicación,
usabilidad y restauración de las historias siguen pendientes. La validación del contenedor
se realiza por separado con la guía común y su prueba de persistencia.

### Concreción de diseño HU-07, 2026-11-20

El [plan HU-07](../../specs/007-gestionar-pedidos-proveedores/plan.md) concreta compras y
correo sin cambiar arquitectura ni efectos/estados de stock. Propone MailKit 5.0.0 en el
BackgroundService previsto y factoring privado del ejecutor único para comandos internos
tipados, autorizados por una solicitud persistida, sin sesión simulada ni ActorId libre.
Su [protocolo](../../specs/007-gestionar-pedidos-proveedores/contracts/integration.md)
define claim/SMTP/finalización separados, lease/fencing y recuperación; el retry humano
usa nueva operación HTTP sobre la misma entrega/MessageId/documento. No actualiza resultados
históricos. Se conservan permisos actuales y primera referencia de borrador HU-08.

Entrega funcional local cerrada con [evidencia](../../specs/007-gestionar-pedidos-proveedores/validation/acceptance.md);
SC-007 sigue pendiente en guardados. El usuario confirmó la [revisión A+B de HU-07](../../specs/007-gestionar-pedidos-proveedores/validation/shared-review.md).
Impacto: HU-08 consume cobertura/locks reales
de PurchaseOrderLine; H0/StockCore/HU-02 requieren regresión del ejecutor/transporte; HU-01
actualiza versión del pedido junto a sus acumulados y acepta V-02/V-06; HU-06 mantiene
solo navegación a compras, sin generación automática. HU-03/HU-04/HU-05 conservan su
protocolo/aritmética y deben seguir consumiendo el mismo ejecutor. H5 mantiene restauración
y aceptación humana. No se acredita revisión ni aceptación por esta actualización.

## Complexity Tracking

Optimización local HU-07 del 2026-11-21: tras dos ensayos SC-007 inconclusos se reduce
la lectura de snapshots antes de paginar y el número de viajes para bloqueos de una fase,
sin cambiar su protocolo. La migración aditiva `20261120194234_HU07PurchasingQueryIndexes`
añade el índice estado/creación/número/ID sobre la cadena que incluye HU07PurchasingOutbox.
`20261121025020_HU07ArticleLookupIndex` añade cobertura por estado/SKU/ID del catálogo,
consumida por el filtro opcional skuPrefix sin cambiar q, entidades o permisos.
La semántica y los permisos permanecen sujetos a las pruebas comunes. Perfil y ensayos
en [rendimiento HU-07](../../specs/007-gestionar-pedidos-proveedores/validation/performance.md).

Sin violaciones constitucionales que justificar. Los bloqueos, el diario y la idempotencia
responden directamente a integridad y trazabilidad. El outbox se limita al correo de compras;
la alerta interna es transaccional y no necesita mensajería externa.
