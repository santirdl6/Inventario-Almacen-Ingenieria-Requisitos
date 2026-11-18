# Investigación y decisiones: HU-02 — Ubicaciones

**Fecha**: 2026-11-18 · **Plan**: [plan.md](plan.md) · **Spec**: [spec.md](spec.md)
**Base común**: [research global](../../.specify/memory/project/research.md) (R-01…R-10),
que este documento no repite. Aquí solo constan las decisiones propias de HU-02.

## D-01. Ampliar `Location` de H0, no redefinirla

**Decisión**: conservar la clase `Domain.Locations.Location`, la tabla `Locations` y la
migración `20261116194714_H0_StockCore` sin cambios. HU-02 añade métodos de dominio
(corrección, cambio de tipo, desactivación, reactivación) y una migración aditiva
`HU02_Locations` posterior a `H0_StockCore`.

**Motivo**: `StockCore`, `StockBalances` y `StockEntries` ya referencian `Locations` con
FK restringidas; regenerar `H0_StockCore` rompería la cadena compartida (plan global,
sincronización §5).

**Alternativas**: redefinir la entidad o crear `LocationDetails` paralela, descartadas por
duplicar la fuente de verdad y obligar a reparar referencias.

## D-02. Unicidad física en SQL con índice único

**Decisión**: índice único `UX_Locations_Aisle_Rack_Level` sobre (Aisle, Rack, Level),
con collation `Latin1_General_100_CI_AS` en las tres columnas, igual que `Code`. El dominio
ya recorta y pasa a mayúsculas; la collation sin distinguir mayúsculas es defensa adicional.
El índice no filtra por `IsActive` (FR-003: incluye inactivas). Antes de crear el índice,
la migración comprueba que no haya combinaciones duplicadas y, si las hay, falla con un
mensaje explícito en lugar de borrar o renombrar datos.

**Motivo**: dos altas concurrentes del mismo hueco solo se evitan de forma fiable con una
restricción SQL; una comprobación previa en la aplicación no basta (R-03). El handler
traduce las violaciones 2601/2627 a un 409 por campo.

**Alternativas**: applock global de altas (serializa todo sin necesidad); columna calculada
normalizada (innecesaria porque el dominio ya normaliza al escribir).

**Dependencia**: es un cambio del modelo común. Confirmado por B, **pendiente de validación
de A en el PR** ([plan §Cambios pendientes de A](plan.md#cambios-al-modelo-común-pendientes-de-a)).

## D-03. «Tiene movimientos» se deriva de `StockBalances`

**Decisión**: una ubicación tiene movimientos si existe al menos una fila en
`StockBalances` con su `LocationId`. Se consulta con el índice existente
(LocationId, ArticleId), sin añadir otro índice a `StockEntries`.

**Motivo**: según el modelo común, cada `StockEntry` modifica una fila de saldo, el saldo
nace con la primera entrada y nunca se borra (no hay DELETE de saldos). Por tanto, la
existencia de saldo ⇔ existencia de alguna entrada confirmada, también si se revirtió
(FR-011: el bloqueo persiste aunque el saldo vuelva a cero). Una prueba de integración
fija esta equivalencia para detectar si una historia futura la rompe.

**Refuerzo**: la regla «nunca se borra un saldo» pasa de convención a garantía:
`DatabaseBootstrapper` añade `DENY DELETE ON OBJECT::dbo.StockBalances` al usuario de
ejecución (UPDATE sigue permitido, porque el núcleo actualiza cantidades). Así D-03 no
depende de que ninguna historia futura respete la convención por disciplina.

**Alternativas**: `EXISTS` sobre `StockEntries` exige un índice nuevo por `LocationId` en
una tabla de crecimiento 1.000.000+; columna `HasMovements` mantenida por el núcleo
añade una escritura más a cada movimiento.

## D-04. Coordinación con movimientos mediante el bloqueo de ubicación

**Decisión**: todo comando de HU-02 que modifique una ubicación existente sigue el mismo
orden que el [protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md):
transacción READ COMMITTED → `operation:{key}` → `location:{id}` → (si aplica)
`locations:receiving` → leer estado y saldos → validar → escribir ubicación, auditoría y
`OperationResult` → commit. El helper de applock que hoy es privado en `StockCore` se
extrae a `Infrastructure/Persistence/AppLocks` y lo usan ambos, sin cambiar su semántica.

**Motivo**: `StockCore` ya adquiere `location:{id}` antes de leer `Location.IsActive` y
rechaza recepciones y reservas en ubicaciones inactivas. Con el mismo bloqueo,
desactivación y recepción simultáneas quedan serializadas (US5.4, FR-016, SC-004):
o entra la recepción y la desactivación ve existencias, o entra la desactivación y la
recepción ve la ubicación inactiva. La corrección de código/pasillo/estantería/nivel
también toma el bloqueo, para no cambiar la identificación mientras llega la primera entrada.

**Alternativas**: solo rowversion (no protege frente a una recepción que no modifica la
fila de `Locations`); SERIALIZABLE sobre `StockBalances` (más difícil de razonar, R-03).

## D-05. Última zona de recepción activa (FR-021)

**Decisión**: desactivar una ubicación `Receiving` o cambiar su tipo a `Storage` adquiere,
después de `location:{id}`, el bloqueo `locations:receiving` y cuenta las zonas de recepción
activas restantes. Si quedaría cero y la petición no trae `acknowledgeNoActiveReceiving: true`,
responde 409 `NoActiveReceivingLeft` sin cambios. La interfaz muestra la advertencia y,
si el usuario confirma, reenvía con el reconocimiento y una clave de idempotencia nueva,
porque el cuerpo cambia (véase D-06). Si el estado se lee antes, `GET /locations/{id}`
expone `isLastActiveReceiving` para avisar de antemano, pero la decisión es del servidor.

**Motivo**: la advertencia debe ser verificable en servidor (SC-004) y no depender de una
lectura previa en el navegador. Sin el bloqueo común, dos desactivaciones simultáneas de
las dos últimas zonas verían cada una «queda otra» y ambas pasarían sin aviso.
`StockCore` no adquiere `locations:receiving`, así que no se introduce riesgo de interbloqueo.

**Alternativas**: endpoint de vista previa sin bloqueo (no evita la carrera); impedirlo
siempre (rechazado en la clarificación 5).

## D-06. Idempotencia reutilizando `OperationResult`

**Decisión**: POST y PUT de ubicaciones exigen cabecera `Idempotency-Key` (UUID). Se guarda
`OperationResult` con `Resource` = `locations` o `locations/{id}`, huella SHA-256 del cuerpo
normalizado (recortado, mayúsculas) y el cuerpo de respuesta, en la misma transacción.
Misma clave + mismo actor + mismo recurso + misma huella → se devuelve el resultado
guardado (también si el If-Match ya quedó obsoleto por ese mismo éxito); cualquier
diferencia → 409 `IdempotencyConflict`. Un cambio de contenido (p. ej. añadir el
reconocimiento de D-05) es una intención nueva con clave nueva.

**Motivo**: misma semántica que el núcleo y que el contrato HTTP común; evita una segunda
tabla de idempotencia. No se añade `GET /operations/{id}`: la comprobación de resultado de
US5.3 se hace repitiendo la misma clave, como permite el contrato común.

## D-07. Auditoría con `MasterDataAudit` común

**Decisión**: cada alta, corrección, desactivación y reactivación escribe una fila en
`MasterDataAudit` (EntityType `Location`, Action `Create`/`Update`/`Deactivate`/`Reactivate`,
Before/After JSON, ActorId de `ICurrentActor`, RecordedAt del servidor, OperationId) en la
misma transacción. Si la tabla no existe todavía al integrar, `HU02_Locations` la crea con
exactamente los campos del modelo común y el inicializador deniega UPDATE/DELETE al
usuario de ejecución, como en el diario.

**Motivo**: FR-015 y principio V (cambios de maestros auditables). La entidad ya está
definida en el modelo común y HU-08 (A) la necesitará igual; crear una tabla propia de
ubicaciones duplicaría el concepto.

**Coordinación**: no cambia la documentación común, pero sí implementa por primera vez
una tabla compartida. Si A integra antes su versión, B regenera `HU02_Locations` sin esa
parte (plan global, sincronización §5). Se señala en el PR para revisión de A.

## D-08. Un PUT para datos y estado, con reglas por tipo de cambio

**Decisión**: se mantiene el recurso del contrato común `PUT /locations/{id}` con
`code, aisle, rack, level, kind, isActive`. Una petición que cambie `isActive` no puede
cambiar a la vez otros campos (422 `StateChangeMustBeSeparate`), para que la revisión y
confirmación de FR-020 y la acción de auditoría sean inequívocas.

**Motivo**: evita ampliar el contrato con rutas nuevas; la regla de separación mantiene
la claridad de la interfaz y la auditoría.

**Alternativa**: `POST /locations/{id}/deactivation|reactivation`, más explícita pero
exige más cambios al contrato común. Puede adoptarse si A lo prefiere en la revisión.

## D-09. Consultas de existencias desde `StockBalances`

**Decisión**: `GET /articles/{id}/locations` y `GET /locations/{id}/stock` leen
`StockBalances` unidas a `Locations`, `Lots` y `Articles`, proyectadas a DTO con
AsNoTracking. Se excluyen buckets con Physical = 0 y Reserved = 0. Página y totales se
leen en una transacción SNAPSHOT de solo lectura (R-03), con `asOf` del servidor; los
totales agregan todo el filtro, no solo la página. No se recalcula nada desde el diario.

**Motivo**: requisito del usuario y del modelo común (Available calculado, saldo derivado
del protocolo). Mostrar físico y reservado es suficiente para FR-007/FR-008; el cálculo y
la pantalla de disponibilidad pertenecen a HU-05.

## D-10. Conteo abierto como punto de extensión

**Decisión**: la regla «no desactivar ni cambiar tipo con conteo abierto» (FR-012) se
implementa tras una interfaz `ILocationUsage` con una implementación H1 que comprueba
existencias y reservas en `StockBalances`, y que HU-04 ampliará con `CountLocation.IsFrozen`.
Hasta entonces las pruebas de conteo quedan como evidencia parcial pendiente (spec,
Alcance y dependencias).

## D-11. Frontend en `features/locations`

**Decisión**: componentes standalone, formularios reactivos tipados, signals y PrimeNG
(Table, InputText, Select, Dialog/ConfirmDialog, Message, Tag con texto). Servicio HTTP
propio de la feature; reutiliza `ApiError` y el interceptor de `core/http`. Clave de
idempotencia por intención con `crypto.randomUUID()`, guardada en sessionStorage hasta
recibir una respuesta definitiva (contrato UI, estado 7). El XSRF lo gestiona HttpClient
con los nombres por defecto (`XSRF-TOKEN` / `X-XSRF-TOKEN`), ya acordados en R-06.

**Dependencias**: la rama `b/h0-frontend-base` (ruta diferida, layout, `ApiError`) no está
fusionada en `b/hu-02-ubicaciones`; debe integrarse antes de empezar el frontend.
La búsqueda de artículos por código o nombre (US2.1) consume `GET /articles` de HU-08 (A);
mientras no exista, la pantalla de artículo admite abrirse desde un identificador y la
prueba de US2.1 queda pendiente de esa entrega. El inicio de sesión en la interfaz es
trabajo común aún no entregado; las pruebas E2E de HU-02 dependen de él.

## D-12. Pruebas

**Decisión**: `LocationsTests` en `Inventario.IntegrationTests` con el patrón de
`StockCoreTests`: `IClassFixture<IdentityFixture>`, base y login aleatorios, contextos
de aplicación con la conexión restringida, existencias preparadas solo con `StockCore`
y `TestActor`, concurrencia con `Task.WhenAll` sobre contextos independientes. Las
pruebas HTTP (permisos, ETag/If-Match, Idempotency-Key, ProblemDetails) usan
`fixture.Factory()` con sesión real y antiforgery como `IdentityTests`.
Frontend: Vitest/TestBed para servicio, formularios y estados; Playwright cuando exista
el login de la interfaz.

**Alternativas**: InMemory/SQLite descartados (R-08): no verifican índices únicos,
collation ni applocks.
