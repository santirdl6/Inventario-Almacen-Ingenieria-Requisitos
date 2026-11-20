# Investigación y decisiones: HU-02 — Ubicaciones

**Fecha**: 2026-11-18, revisado el 2026-11-19 (base integrada de A y recorte) · **Plan**: [plan.md](plan.md) · **Spec**: [spec.md](spec.md)
**Base común**: [research global](../../.specify/memory/project/research.md) (R-01…R-10),
que este documento no repite. Aquí solo constan las decisiones propias de HU-02.

**Revisión 2026-11-19**: D-01, D-03, D-08 y D-09 no cambian en lo esencial. D-02, D-04–D-07 y
D-10–D-12 se reescriben para usar la base común de A; D-13 registra el recorte de la
entrega del sábado. Cada sección revisada indica qué cambia y por qué.

## D-01. Ampliar `Location` de H0, no redefinirla

**Decisión**: conservar la clase `Domain.Locations.Location`, la tabla `Locations` y la
migración `20261118194940_H0_StockCore` sin cambios. HU-02 añade métodos de dominio
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
restricción SQL; una comprobación previa en la aplicación no basta (R-03). Aun así, el
comando comprueba antes de guardar si otra ubicación ocupa el mismo lugar y, si la hay,
responde 409 `DuplicateLocationPlace` con un `detail` que **nombra su código**, porque US1.3
exige identificar la ubicación ya registrada. El índice cubre la carrera entre dos altas
simultáneas; en ese caso el mapper responde el mismo código sin nombrar la existente.

**Traducción del error (revisado 2026-11-19)**: el código repetido ya lo traduce
`MasterDataProblemMapper` de A a 409 `DuplicateCode` con `errors.code` (índice de `Code`),
y se reutiliza. Para el índice físico se añade a ese mapper una rama: 2601/2627 sobre
`UX_Locations_Aisle_Rack_Level` → 409 `DuplicateLocationPlace` con `errors.aisle`,
`errors.rack` y `errors.level`. Se retira `existingLocationId`: `InventoryProblemException`
no lleva extensiones y la interfaz ofrece buscar la ubicación por código.

**Alternativas**: applock global de altas (serializa todo sin necesidad); columna calculada
normalizada (innecesaria porque el dominio ya normaliza al escribir).

**Dependencia**: cambio del modelo común **validado por A** (acuerdo con A, 2026-11-19); el
diff documental se aplica en T068 ([plan §Cambios pendientes de A](plan.md#cambios-al-modelo-común-pendientes-de-a)).

## D-03. «Tiene movimientos» se deriva de `StockBalances`

**Decisión**: una ubicación tiene movimientos si existe al menos una fila en
`StockBalances` con su `LocationId`. Se consulta con el índice existente
(LocationId, ArticleId), sin añadir otro índice a `StockEntries`.

**Motivo**: según el modelo común, cada `StockEntry` modifica una fila de saldo, el saldo
nace con la primera entrada y nunca se borra (no hay DELETE de saldos). Por tanto, la
existencia de saldo ⇔ existencia de alguna entrada confirmada, también si se revirtió
(FR-011: el bloqueo persiste aunque el saldo vuelva a cero). Una prueba de integración
fija esta equivalencia para detectar si una historia futura la rompe.

**Refuerzo**: la regla «nunca se borra un saldo» es una garantía: la migración
`H0_StockCore` ya deniega DELETE sobre `StockBalances` al rol `inventory_runtime` (validado
por A el 2026-11-19 y probado en `StockCoreTests`). HU-02 no añade trabajo aquí.

**Alternativas**: `EXISTS` sobre `StockEntries` exige un índice nuevo por `LocationId` en
una tabla de crecimiento 1.000.000+; columna `HasMovements` mantenida por el núcleo
añade una escritura más a cada movimiento.

## D-04. Coordinación con movimientos mediante el bloqueo de ubicación

**Decisión (revisada 2026-11-19)**: todo comando de HU-02 se ejecuta con el
`InventoryOperationExecutor` de A, que abre la transacción y adquiere `operation:{key}`;
dentro de `change`, el comando adquiere `location:{id}` con `context.Locks.LocationsAsync`,
la misma fase que usa `StockCore`, y solo después lee estado y saldos, valida y escribe.

**Qué cambia y por qué**: la 1.ª iteración extraía un `AppLocks` propio de `StockCore`.
La base integrada ya tiene `InventoryLocks`, que impone el orden operación → documentos →
ubicaciones → artículos y lo comprueba en tiempo de ejecución; usarlo evita un segundo
helper y no requiere tocar `StockCore`.

**Motivo**: `StockCore` adquiere `location:{id}` antes de leer `Location.IsActive` y rechaza
recepciones y reservas en ubicaciones inactivas. Con el mismo bloqueo, desactivación y
recepción simultáneas quedan serializadas (US5.4, FR-016, SC-004). La corrección de
código/pasillo/estantería/nivel también toma el bloqueo, para no cambiar la identificación
mientras llega la primera entrada.

**Alternativas**: solo rowversion (no protege frente a una recepción que no modifica la
fila de `Locations`); SERIALIZABLE sobre `StockBalances` (más difícil de razonar, R-03).

## D-05. Última zona de recepción activa (FR-021)

**Decisión (revisada 2026-11-19)**: desactivar una ubicación `Receiving` o cambiar su tipo a
`Storage` adquiere, después de `location:{id}`, el recurso `locations:receiving` con un
método nuevo `InventoryLocks.ReceivingZonesAsync()` en la fase de ubicaciones (`location:`
precede a `locations:` en orden ordinal, así que se cumple la regla de orden de
`InventoryLocks`), y cuenta las zonas de recepción activas restantes. Si quedaría cero,
responde **409 `NoActiveReceivingLeft`** sin cambios, con un `detail` que explica que antes
debe crearse o activarse otra zona de recepción. `GET /locations/{id}` expone
`isLastActiveReceiving` para que la interfaz lo explique de antemano, pero decide el servidor.

**Qué cambia y por qué**: la 1.ª iteración permitía la retirada con
`acknowledgeNoActiveReceiving`; el acuerdo con A del 2026-11-19 la impide (spec FR-021).
El campo de reconocimiento desaparece del contrato y la interfaz ya no reenvía.

**Motivo**: sin el bloqueo común, dos retiradas simultáneas de las dos últimas zonas verían
cada una «queda otra» y ambas pasarían. `StockCore` no adquiere `locations:receiving`, así
que no hay riesgo de interbloqueo. El método nuevo de `InventoryLocks` lo revisa A (T001).

**Alternativas**: endpoint de vista previa sin bloqueo (no evita la carrera); applock manual
fuera de `InventoryLocks` (saltaría la comprobación de fases de A).

## D-06. Idempotencia con el ejecutor y `OperationResult` de A

**Decisión (revisada 2026-11-19)**: POST y PUT exigen `Idempotency-Key` (UUID; si falta o no
es válido, 400 `InvalidRequest`, con el mismo `Key()` que los endpoints de maestros). El
comando construye un `OperationRequest` con el método, el recurso canónico
`/api/v1/locations` o `/api/v1/locations/{id}`, el permiso `Locations.Manage` y el cuerpo
normalizado (recortado y en mayúsculas). El ejecutor resuelve el replay **antes** de
ejecutar `change`, de modo que un replay tras éxito devuelve el resultado guardado aunque el
If-Match ya esté obsoleto; actor, recurso, permiso o huella distintos → 409
`IdempotencyConflict`.

**Qué cambia y por qué**: se retira `LocationCommandRunner` y su `BeforeCommit`. El ejecutor
de A ya hace transacción, reintentos, replay, permiso y `OperationResult`; la prueba de
atomicidad L-15 usa el `FailAfterSaveInterceptor` existente, que falla después de guardar y
antes del commit. US5.3 puede comprobarse repitiendo la misma clave o con
`GET /operations/{id}` de A.

## D-07. Auditoría con `MasterDataAudit` y `MasterDataAuditWriter` de A

**Decisión (revisada 2026-11-19)**: cada alta, corrección, desactivación y reactivación llama a
`MasterDataAuditWriter.Record(context, MasterDataEntity.Location, id, action, before, after)`
dentro de `change`, después de guardar la ubicación; el ejecutor la confirma junto con
`OperationResult`. Before/After son `{code, aisle, rack, level, kind, isActive}`.

**Qué cambia y por qué**: la tabla ya existe (`HU08AuditInfrastructure`) con su DENY de
UPDATE/DELETE, así que HU-02 no la crea. Solo hace falta admitir la entidad: valor
`Location` en `MasterDataEntity` y en el CHECK `CK_MasterDataAudit_Entity` (migración
`HU02_Locations`). Son cambios aditivos en archivos de A, señalados para su revisión.

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
implementa tras `ILocationUsage`, con una implementación que comprueba existencias y
reservas en `StockBalances` y que HU-04 ampliará con `CountLocation.IsFrozen` (T034 ⛔).
Hasta entonces las pruebas de conteo quedan como evidencia parcial pendiente. Sin cambios
en la revisión del 2026-11-19, salvo que el código de `3b0c0ae` se porta adaptado.

## D-11. Frontend en `features/locations` sobre la base de A

**Decisión (revisada 2026-11-19)**: se portan las pantallas ya escritas en `8377a12`
(componentes standalone, formularios reactivos tipados, signals, PrimeNG) adaptándolas a la
base de A, sin traer la configuración de pruebas de esa rama:

- Errores con `apiProblem()` (`status`, `code`, `detail`, `errors`) en lugar de `ApiError`.
- Permisos con `SessionService.has('Locations.Manage')`; `nueva` y `:id/editar` protegidas con
  `permissionGuard` y `data.permission`; la lista y el detalle heredan `Inventory.Read` del
  área. Se retira `location-permissions.ts`.
- Parámetros `id` y `articleId` leídos con `ActivatedRoute`, porque A no activa
  `withComponentInputBinding`.
- Mensajes de éxito y error en la propia página (`role="status"` / `role="alert"`), sin
  `MessageService` ni avisos globales.
- Búsqueda de artículos (US2.1) con `GET /articles?q=` de A; se retira el acceso provisional.

**Revisión de A en PR #6**: las mutaciones usan el transporte común de intenciones,
ampliado con la ruta locations y Locations.Manage sin conceder ese permiso a maestros.
La intención conserva actor, cuerpo y ETag originales; se restaura al recargar y bloquea
una intención diferente mientras sea incierta. Logout retira cuerpo/ETag y conserva
solo la referencia para consulta. Comprobar consulta GET /operations; un 404 no es fallo
definitivo y habilita un replay explícito con la misma clave. La UI ya no utiliza las
funciones de claves de `idempotent-intent.ts`; la clasificación de estados sigue disponible.

**Alternativa**: `VersionConflictComponent` de A para el 412; se mantiene el panel de
`8377a12` porque ya está probado y A no tiene etiquetas para pasillo, estantería y nivel.

## D-12. Pruebas

**Decisión (revisada 2026-11-19)**: pruebas en `Inventario.IntegrationTests/Locations/`, un
archivo por historia, con `IClassFixture<IdentityFixture>` y `StockTestHarness` para preparar
existencias con el núcleo (sin duplicar utilidades). Cada historia prueba su **recorrido
habitual, su error principal y sus permisos**; US5 concentra concurrencia, versiones,
idempotencia y atomicidad. Frontend: Vitest/TestBed por pantalla; Playwright solo para el
recorrido principal, con el fixture de A (`master-data.fixture.ts`) y su cuenta Operario.

**Qué cambia y por qué**: se retiran L-13 y L-14 como pruebas propias. El DENY sobre
`StockBalances` ya lo prueba `StockCoreTests` y el de `MasterDataAudit`,
`OperationProtocolTests` de A; la consecuencia práctica de D-03 (identificación bloqueada
aunque el saldo vuelva a cero) la comprueba L-07.

**Alternativas**: InMemory/SQLite descartados (R-08): no verifican índices únicos,
collation ni applocks.

## D-14. Caracteres admitidos en código, pasillo, estantería y nivel

**Decisión (2026-11-19, tercera sesión de clarify)**: solo A–Z, a–z (se guardan en mayúsculas),
0–9 y guion. Se comprueba en tres sitios con la misma lista ASCII explícita:

- **Dominio**: `Location.HasOnlyAllowedCharacters` sobre el texto recortado **antes** de pasar a
  mayúsculas (`ToUpperInvariant` convertiría «ı» en «I» y «ſ» en «S»); `Normalize` devuelve el
  mensaje del campo (422 `ValidationFailed`) y el constructor lo exige. `LocationWrite.Read` solo
  pasa a mayúsculas un texto que ya cumple la regla.
- **Base de datos**: CHECK `CK_Locations_{Code,Aisle,Rack,Level}_Chars` con
  `COLLATE Latin1_General_BIN2 NOT LIKE N'%[^-0-9A-Z]%'` (el guion al principio de la clase:
  al final, con `nvarchar` y collation binaria, no se interpretaba como literal).
- **Interfaz**: validador `^[A-Za-z0-9-]*$` sobre el valor recortado, con el mensaje en el campo.

**Motivo**: la intercalación de las columnas (`Latin1_General_100_CI_AS`) ordena «Á» y «Ñ» dentro de
A–Z, así que una comprobación con esa intercalación los dejaría pasar.

**Datos existentes**: la migración `HU02_LocationCharacters` comprueba antes de crear los CHECK y, si
hay valores que incumplen, se detiene (THROW 50003) nombrando hasta 20 ubicaciones, sin modificar
datos. En la base de E2E, «NAY - A2» (inactiva, sin movimientos) se corrigió a «NAY-A2» desde la
aplicación antes de desplegar, con auditoría `Update`. Riesgo residual: una ubicación con
movimientos y caracteres no admitidos no puede corregirse por la aplicación (FR-011); no existe en
ninguna base actual y requeriría una decisión explícita.

**Alternativas**: eliminar o convertir los espacios interiores (rechazada en clarify: se rechaza,
no se transforma); `char.IsLetter` o la intercalación de SQL Server (admiten acentos y Ñ).

## D-13. Recorte para la entrega del sábado

**Decisión (2026-11-19)**: quedan fuera de esta entrega la medición de rendimiento (SC-005,
antes T064) y la validación con operarios (SC-006, antes T066). La revisión de accesibilidad
separada (antes T065) se sustituye por comprobaciones de teclado, foco y textos en las specs
de Vitest y en el E2E.

**Motivo**: plazo. Las dos primeras necesitan datos de volumen y sesiones con trabajadores
que no caben antes del sábado; el diseño no las impide.

**Consecuencia**: desviación registrada en
[plan.md § Complexity Tracking](plan.md#complexity-tracking): HU-02 puede integrarse, pero
no darse por aceptada hasta aportar SC-005 y SC-006.
