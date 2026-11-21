# Investigación de HU-08 — Datos maestros

**Fecha**: 2026-11-12. **Estado**: decisiones de diseño completas; implementación y revisión
cruzada de contratos compartidos pendientes. **Origen**: [spec aclarada](spec.md).

## Base consumida y método

Prevalecen la [constitución](../../.specify/memory/constitution.md), el
[plan global](../../.specify/memory/project-plan.md) y sus anexos:
[decisiones](../../.specify/memory/project/research.md),
[modelo](../../.specify/memory/project/data-model.md),
[HTTP](../../.specify/memory/project/contracts/api.md),
[stock](../../.specify/memory/project/contracts/stock-operations.md),
[UI](../../.specify/memory/project/contracts/ui.md) y
[validación](../../.specify/memory/project/quickstart.md).

Se realizaron dos investigaciones de solo lectura, backend/persistencia e interfaz/pruebas,
contrastadas con el código y con documentación oficial de EF Core. Esta fase no ejecuta
pruebas funcionales ni presenta los diseños como componentes existentes.

## R-HU08-01. Reutilización de la base y límites de H1

**Decisión**: ampliar la misma Article H0, InventoryDbContext, Identity y API Minimal existente.
Handlers Commands/Queries invocados por DI en `Features/MasterData`; configuraciones por entidad.
Reutilizar versiones fijadas y CQRS ligero del plan global, sin nuevas capas desplegables.

**Fundamento**: existen Article mínima, ICurrentActor, sesión y 18 casos de integración
verificados previamente; no existen Family, Supplier, auditoría, políticas, OperationResult,
ejecutor transaccional de negocio, maestros HTTP ni pantallas. El [handoff](h0-handoff.md)
es la referencia del estado H0, no evidencia de HU-08 implementada.

**Alternativas**: descartadas otra Article, otro contexto, MediatR obligatorio, repositorios
genéricos y un segundo servicio de stock. La base global ya resuelve el stack.

## R-HU08-02. Evolución aditiva y datos H0

**Decisión**: migraciones coordinadas sobre el último snapshot integrado. Crear Families,
Suppliers, MasterDataAudit y ampliar Articles con FamilyId e IsDefinitionLocked. Conservar
UUID, SKU, definición, referencias y datos previos. StockPolicy es la entidad común de
HU-08/HU-06; las altas de Article crean su fila con mínimo cero.

FamilyId se incorpora nullable durante la transición. Un handler HTTP de mantenimiento
autenticado asigna las filas H0 a familias válidas mediante un mapa explícito aprobado;
la migración de cierre falla si quedan filas sin familia y después exige NOT NULL y FK.
No se crea silenciosamente una familia GENERAL ni se deja nulabilidad productiva permanente.
Registrar actor, fecha, mapa e inventario de filas afectadas; las asignaciones se auditan.
El actor proviene de ICurrentActor en una sesión con MasterData.Manage y antiforgery, no
de parámetros SQL ni del mapa. El handler consume el ejecutor común, su conexión sin DDL
y una proyección nullable del estado expandido; conserva UUID y guarda cambios, evidencia
y resultado por lote atómicamente. Solo se habilita con configuración explícita y esquema
expandido aún sin cierre; las migraciones DDL siguen fuera de la API.
No hay endpoints de alta de artículos nuevos hasta completar esta transición. Si el catálogo
H0 está vacío, la transición se completa sin intervención ni datos de negocio automáticos.

**Fundamento**: permite conservar las referencias que B ya utilice y cumplir la familia
obligatoria. El backfill puede cambiar rowversion; los clientes deben refrescar sus ETags.

**Alternativas**: descartados recrear Articles, sustituir UUID, asignar Guid.Empty y borrar
fixtures de B como estrategia de migración. También se descarta un script de preparación
de datos con ActorId proporcionado manualmente o un actor de respaldo fuera de HTTP.
Los fixtures futuros crean primero su familia.

## R-HU08-03. Bloqueo permanente de definición y referencias actuales

**Decisión**: representar «desde la primera referencia» con `Article.IsDefinitionLocked`,
inicialmente false y monotónica a true. La primera línea de documento, reserva o movimiento
la activa en la misma transacción que crea la referencia, bajo el bloqueo común de artículo.
Quitar una línea de borrador no restablece la posibilidad de reinterpretar la definición.
Actualizar unidad, lotes o fracciones exige marca false y ausencia autoritativa de referencias.
El backfill activa la marca si ya existen referencias en tablas integradas.

El permiso de borrar se evalúa aparte con referencias actuales e historial: la marca sola
no impide eliminar un artículo que vuelve a no tener documentos, reservas ni movimientos.
La auditoría y los resultados de operaciones sobreviven a ese borrado.

Un lector de uso de artículo agrupa consultas reales por feature integrada. Su registro
debe cubrir todas las FK consumidoras presentes en el modelo; una dependencia ausente o
una tabla inaccesible no se interpreta como «sin uso». En el H1 sin consumidores no hay
tablas de documentos contra las que consultar; esa capacidad se acredita al arrancar y
en pruebas. Las nuevas features aportan su comprobación antes de habilitar sus escritores.
El gate de cambios de definición exige además el enlace verificado de primera referencia
para cada consumidor presente, no solo un lector de uso. Debe completarse antes de habilitar
esa parte del PUT; un consumidor incompleto causa DependencyUnavailable. No se exige que
ya existan las historias futuras para admitir un H1 que acredita no tener esos consumidores.

**Fundamento**: una consulta Any actual no recuerda una referencia retirada, y rowversion
no protege por sí sola la inserción simultánea de referencias. Marca y consultas cumplen
propósitos distintos: inmutabilidad desde primer uso y borrado sin huérfanos.

**Alternativas**: descartados deducir uso del saldo, liberar la definición al cancelar
documentos, un lector productivo que siempre devuelve false y duplicar el diario en HU-08.

## R-HU08-04. Transacción, familia y proveedor

**Decisión**: consumir el ejecutor común del núcleo H0 de B: intención → documentos →
ubicaciones → artículos, contexto limpio por intento, máximo tres intentos completos y
applocks transaccionales conforme al contrato global. Family y Supplier participan en la
fase documental para coordinar asignación/desactivación y futuras compras; no se introduce
una fase de bloqueo posterior al artículo. Las lecturas decisorias ocurren tras los bloqueos.

Alta de artículo guarda Article, StockPolicy, auditoría y OperationResult conjuntamente.
Editar/borrar/activar guarda sus propios efectos y evidencia en la misma transacción.
La desactivación no escribe cantidades. Si HU-06 está integrada, su evaluación de alertas
se ejecuta dentro de esa transacción; no se sustituye por un callback vacío.

**Fundamento**: EF con EnableRetryOnFailure no constituye por sí solo una transacción
reintentable de negocio. Véase [resiliencia EF](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).

**Alternativas**: descartados commit autónomo del núcleo, transacciones durante la edición
humana, mutex en memoria y reintentar SaveChanges sin volver a validar.

## R-HU08-05. Replay incluso después de eliminar

**Decisión**: OperationId coincide con Idempotency-Key. OperationResult común conserva
estado, cuerpo y cabeceras reproducibles ETag/Location, más permiso original de la operación.
El replay autorizado se resuelve antes de buscar el maestro o evaluar If-Match. El hash
usa método, ruta canónica y cuerpo normalizado; no incluye el ETag ni tokens de sesión.
Un resultado anterior no se reconstruye desde el estado vivo ni recrea un artículo eliminado.

Para nuevas mutaciones, PUT y DELETE exigen un If-Match fuerte derivado de rowversion;
un conflicto de concurrencia devuelve 412, no se fusiona automáticamente. DELETE devuelve
204; la consulta de operación conserva su resultado aunque GET del artículo devuelva 404.
Consultar resultados exige el permiso original almacenado, incluso sin recurso vivo.

**Fundamento**: SC-004 exige recuperar resultados sin duplicados. Los conflictos de alta
por índice único no son DbUpdateConcurrencyException; se traducen por separado. Véase
[concurrencia EF](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

**Alternativas**: descartados caché idempotente en memoria, comprobar existencia antes del
replay, If-Match comodín y permitir recuperar un resultado sin reautorizar.

## R-HU08-06. Auditoría y permisos SQL

**Decisión**: MasterDataAudit append-only incluye Create/Update/Deactivate/Reactivate/Delete.
EntityId es referencia histórica sin FK al maestro; ActorId referencia Identity con borrado
restringido. OperationId identifica el resultado común. Antes/después excluyen secretos.
El principal de ejecución no puede actualizar ni borrar auditoría ni resultados confirmados.
Usar el rol SQL común inventory_runtime y DENY de tabla, compatible con los permisos DML
existentes; las migraciones preparan el rol y el bootstrap añade al usuario de ejecución.

**Fundamento**: una FK audit→Article impediría el borrado permitido o destruiría evidencia
en cascada. El usuario se crea después de migrar, por lo que las restricciones se asignan
a un rol estable, no a un nombre de login embebido en una migración.

**Alternativas**: descartados auditoría best-effort, solo setters privados, actor del cliente
y conceder MasterData.Manage implícitamente al Administrador.

## R-HU08-07. UI y seguridad de sesión

**Decisión**: Angular standalone, rutas lazy `/maestros`, formularios reactivos tipados,
signals locales, HttpClient y PrimeNG. Integrar la base común de sesión/layout de B; si aún
no está disponible, acordar e incorporar el mínimo común de login, sesión y guards requerido
por esta feature. Sin administración de cuentas ni nuevo sistema de autenticación.

HttpClient usa URL relativa y XSRF nativo. Cada intención conserva UUID, payload y ETag;
no se reintentan todas las escrituras automáticamente. sessionStorage conserva temporalmente
la intención original de maestros, ligada al actor, para consultar su resultado tras recargar
y poder repetir exactamente el mismo payload/clave si continúa incierto. Se elimina al
resolverla o cerrar sesión; si el resultado seguía pendiente al cerrar sesión, conservar
solo su referencia para consulta posterior. Nunca guardar el payload de login, credenciales
ni tokens, ni enviar un formulario vacío o modificado con una clave pendiente.

**Fundamento**: el frontend actual solo tiene main.ts de estado y no tiene router, login,
target test ni suites frontend. Estas piezas no se dan por hechas. Un detalle comunica
capacidad/motivo de edición; el backend siempre revalida, incluso con controles deshabilitados.

**Alternativas**: descartados NgRx, JWT/localStorage, CRUD genérico que esconda las reglas
del artículo y deducir permisos o bloqueo de definición únicamente desde la UI.

## R-HU08-08. Validación y dependencias concretas

**Decisión**: ampliar el proyecto xUnit de integración existente con SQL real aislado;
configurar Vitest/TestBed y Playwright como trabajo pendiente de HU-08. Versiones elegidas
por compatibilidad de metadatos: vitest y @vitest/coverage-v8 5.1.0, jsdom 31.0.0 y
@playwright/test 1.65.0, a instalar con versiones exactas y lockfile al implementar.
El peer de Angular Build 22.2.3 admite Vitest ^4.0.8 o ^5.0.0; jsdom exige Node ≥24.15.0
en la línea 24. El builder previsto es @angular/build:unit-test, runner vitest, buildTarget
inventario:build:development, sin Browser Mode. El perfil Compose test solo ejecuta backend.
Pruebas de fallos, replay después de DELETE y carreras reales con conexiones independientes
preceden a las evidencias integradas de historial y operaciones de las historias consumidoras.

**Fundamento**: escribir escenarios no acredita ejecución. Las 18 pruebas H0 previas son
baseline, no aceptación de maestros. Tres gestores representativos validarán SC-006.

**Alternativas**: descartados EF InMemory para garantías SQL, mocks que acrediten integración
y Playwright como sustituto de la validación con trabajadores.

**Fuentes de dependencias**: peer y esquema del builder instalados en el repositorio;
metadatos publicados de [Vitest 5.1.0](https://registry.npmjs.org/vitest/5.1.0),
[coverage-v8 5.1.0](https://registry.npmjs.org/@vitest/coverage-v8/5.1.0),
[jsdom 31.0.0](https://registry.npmjs.org/jsdom/31.0.0) y
[Playwright Test 1.65.0](https://registry.npmjs.org/@playwright/test/1.65.0).
La compatibilidad investigada no sustituye la instalación y ejecución de las suites.

## Cierre de Phase 0

No quedan decisiones técnicas sin resolución. Las dependencias tienen responsabilidad y
contrato en [integración](contracts/integration.md), y la revisión A+B de las extensiones
compartidas es condición de integración. No constituye una excepción constitucional ni
afirma que el núcleo B, el frontend común o los recorridos de HU-08 ya estén entregados.
