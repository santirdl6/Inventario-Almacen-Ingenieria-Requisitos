# Research: HU-04 — Realizar inventarios cíclicos y ajustar diferencias

**Fecha**: 2026-11-21. **Spec**: [spec.md](spec.md) (clarificación 2026-11-21, dos decisiones).
**Base**: [constitución](../../.specify/memory/constitution.md) ·
[modelo común § Conteos](../../.specify/memory/project/data-model.md#conteos--persona-b--hu-04) ·
[protocolo de stock § Conteo](../../.specify/memory/project/contracts/stock-operations.md#conteo-sin-transacción-humana-abierta) ·
[contrato HTTP § HU-04](../../.specify/memory/project/contracts/api.md#hu-04--b-conteos-y-ajustes) ·
HU-03 [research D-03 y D-06](../005-registrar-salida-mercancia/research.md) y
[convergencia](../005-registrar-salida-mercancia/validation/converge-1.md) ·
[contrato HU-05 → HU-03](../004-consultar-existencias/contracts/hu03-integration.md).

Código reutilizado, leído para decidir: `StockCore` (`Features/Stock/Core`), `InventoryLocks` e
`InventoryOperationExecutor` (`Infrastructure/Persistence/Operations`), `ILocationUsage`
(`Features/Locations`) y `StockBalanceReader`.

Sin incógnitas abiertas. Las decisiones marcadas «(B)» las toma B en este plan; las que cambian
documentos o archivos comunes o de A están en la tabla P-xx del [plan](plan.md#cambios-al-modelo-común-pendientes-de-a).

## D-01. Ubicación del código: `Features/CycleCounts`

**Decisión**: feature vertical `backend/src/Inventario.Api/Features/CycleCounts` (comandos,
consultas, endpoints, contributor de uso del artículo) y dominio en `Domain/Stock/CycleCount*.cs`,
como `Features/Shipping` de HU-03. Frontend en `frontend/src/app/features/cycle-counts/`, donde ya
está el placeholder de `/inventarios` (H0) y el área `inventarios` con `Counts.Record`.

**Alternativas**: dentro de `Features/Stock` (HU-05). Se descarta: mezcla dos historias y dos
ciclos de documento en un mismo módulo.

## D-02. Ciclo del documento y quién hace cada paso

**Decisión**: `Draft → Counting → AwaitingApproval → Confirmed`; `Draft`, `Counting` y
`AwaitingApproval` → `Cancelled`. Es el ciclo del modelo común, sin estados nuevos.

| Paso | Estado de partida | Permiso |
|------|-------------------|---------|
| Crear y editar el alcance | — / `Draft` | `Counts.Record` |
| Iniciar (congelar y capturar el teórico) | `Draft` | `Counts.Record` |
| Registrar el contado, añadir o retirar hallazgos | `Counting` | `Counts.Record` |
| Enviar a aprobación | `Counting` | `Counts.Record` |
| Revisar: motivos y vínculo a salidas (D-09) | `AwaitingApproval` | `Inventory.Adjust` |
| Confirmar | `AwaitingApproval` | `Inventory.Adjust` |
| Cancelar con motivo | `Draft`, `Counting`, `AwaitingApproval` | `Inventory.Adjust` |

El borrador no se elimina: se cancela, como dice el contrato común. Un borrador no tiene líneas
(se crean al iniciar), así que cancelarlo no deja referencias al artículo.

**Alternativas**: permitir eliminar borradores, como HU-03 (Q1). Se descarta: el contrato común no
tiene `DELETE` de conteos y cancelar ya cubre el caso sin dejar efectos.

## D-03. Tipo de documento y orden de bloqueos

**Decisión**: un tipo nuevo, `cycle-count` (cumple `^[a-z][a-z0-9-]{0,31}$`). Recurso
`document:cycle-count:{id}`. Orden ordinal de todos los tipos (`u` 0x75 < `y` 0x79, `-` < `:`):

`customer-order-reference:` < `customer-order:` < `cycle-count:` < `reservation-release:` < `reservation:` < `shipment:`

Ningún comando de HU-04 combina `cycle-count` con otro tipo de documento: la salida vinculada (D-09)
está confirmada e inmutable por los triggers de HU-03 y se lee sin bloquearla.

| Comando | Documentos | Ubicaciones | Artículos |
|---------|------------|-------------|-----------|
| Crear / editar borrador | `cycle-count:{id}` | las del alcance (para comprobar que están activas) | — |
| Iniciar | `cycle-count:{id}` | **todas** las del alcance | los de los saldos con físico > 0 en ellas, leídos tras bloquear las ubicaciones |
| Registrar contado | `cycle-count:{id}` | — | — |
| Añadir / retirar hallazgo | `cycle-count:{id}` | — | el del hallazgo (contrato de escritores, D-08) |
| Enviar / revisar | `cycle-count:{id}` | — | — |
| Confirmar | `cycle-count:{id}` | **todas** las del alcance | los de **todas** las líneas |
| Cancelar | `cycle-count:{id}` | **todas** las del alcance | — |

Iniciar, confirmar y cancelar toman todas las ubicaciones porque cambian su congelación. Los
escritores de B ya leen `IsInOpenCountAsync` **después** de `location:{id}`
(`ShipmentCommands`, `ReservationCommands.ReserveAsync`, `LocationCommands` al desactivar o
cambiar el tipo). Así, el inicio y cada escritor se serializan en la ubicación: o el teórico
incluye el movimiento, o el movimiento ve la congelación (FR-017). Las ubicaciones nuevas en saldo
no pueden aparecer en otra ubicación: el teórico de una ubicación solo cambia bajo su bloqueo.

En la confirmación, `StockCore.ApplyAsync` vuelve a pedir `cycle-count:{id}`, las ubicaciones y los
artículos ajustados: ya están retenidos y `InventoryLocks` no los repite.

**Alternativas**: bloquear solo las ubicaciones con diferencia al confirmar. Se descarta: liberar la
congelación de todas exige su bloqueo, igual que congelarlas.

## D-04. Congelación persistente y lector real (INT-HU04-03)

**Decisión**: la congelación es la fila `CountLocations(CountId, LocationId, IsFrozen)` con un índice
único filtrado por `LocationId` cuando `IsFrozen = 1` (modelo común). `IsFrozen` pasa a 1 al iniciar
y a 0 al confirmar o cancelar, en la misma transacción que el estado del conteo.

`StockBalanceLocationUsage.IsInOpenCountAsync` (HU-02, archivo de B) deja de devolver `false` y
consulta `CountLocations` con `IsFrozen = 1`, en el `DbContext` y la transacción del comando. Con eso,
sin tocar sus comandos:

- salidas (HU-03) y reservas (HU-05) devuelven `409 CountScopeFrozen` sobre el alcance;
- desactivar o cambiar el tipo de una ubicación vacía en conteo da `409 CountScopeFrozen` (una no
  vacía ya da antes `LocationNotEmpty`);
- `inOpenCount` de las consultas de ubicaciones es real.

La liberación de reservas (`ReservationCommands.ReleaseAsync`) **no** consulta la congelación y así
se queda: es la vía para resolver un contado inferior al reservado (clarificación Q1, FR-015).

El inicio comprueba la congelación de cada ubicación con el mismo lector, tras bloquearla, y
devuelve `409 CountScopeFrozen` nombrando el conteo que la ocupa. El índice filtrado es la defensa
SQL si un escritor olvidara el bloqueo.

Recepciones: HU-01 (A) aún no tiene escritor. Su escritor deberá consultar el mismo lector tras
`location:{id}`; se avisa en P-05. El endpoint de pruebas `POST /testing/stock-receipts` (HU-05
D-17, excluido en Production) no lo consulta y no se cambia.

**Alternativas**: un campo `FrozenByCountId` en `Locations` (HU-02). Se descarta: cambia una entidad
compartida y mezcla el estado del conteo con la ubicación.

## D-05. Teórico capturado y revalidado

**Decisión**: al iniciar, bajo los bloqueos de D-03, se crea una línea esperada por cada
`StockBalance` de las ubicaciones del alcance con `Physical > 0`, con `TheoreticalPhysical` = su
físico. Los saldos con físico 0 no generan línea; si el artículo aparece, se registra como hallazgo.

Al confirmar se revalida, bajo los bloqueos, que el físico actual de cada bucket coincide con el
teórico (0 si no hay saldo). Si no coincide, `409 CountTheoreticalChanged` sin cambios y con el
bucket. Con la congelación no debería ocurrir; detecta un escritor que no la comprueba.

**Alternativas**: recalcular el teórico al confirmar y ajustar contra el actual. Se descarta: ocultaría
el movimiento intermedio, que es justo lo que el reparto pide evitar.

## D-06. Un movimiento de ajuste por conteo, con una entrada por línea (B)

**Decisión**: confirmar crea **un** `StockMovement` `Adjustment` con fuente `cycle-count`/`{id}` y una
`StockEntry` `AdjustUp` o `AdjustDown` por cada línea con diferencia distinta de cero, por la
cantidad absoluta. Sin diferencias no se llama a `StockCore` y no hay movimiento (US2.5).

- **Motivo del movimiento**: «Ajuste por conteo cíclico». El motivo de **cada diferencia** se conserva
  en su `CountLine.Reason`, inmutable tras cerrar (D-07). El conteo es el documento de soporte, como
  el pedido lo es de la salida.
- **Soporte**: `SupportReference` = el código del conteo (`CountCode`, D-10).
- **Contraparte**: `CounterpartyNotApplicableReason` = «Ajuste de inventario: sin contraparte
  externa». Nunca `CounterpartyReference`, tampoco al corregir una salida.
- **Responsables**: el actor del movimiento es quien autoriza; quién contó cada línea está en
  `CountLine.CountedBy`.
- **Fecha del hecho**: el instante de la confirmación.

Esto mantiene la unicidad común «tipo/documento/Confirm» (índice
`SourceDocumentType, SourceDocumentId, Kind` del diario) y la etiqueta «Conteo» que el historial de
artículo de A ya muestra para `cycle-count`. La spec decía «un movimiento por cada línea» (FR-010,
FR-021); este plan la ajusta a «una entrada del movimiento del conteo por cada línea», sin cambiar
qué se registra.

**Alternativas**: un movimiento por línea con fuente `cycle-count-line`. Se descarta: rompe la
unicidad común por documento, necesita bloquear de antemano un documento por línea para que
`ApplyAsync` no salte de fase, y el historial de A no reconoce ese tipo (archivo de A).

## D-07. Protección SQL del conteo cerrado: triggers sin depender del orden de EF (B)

Lección de HU-03 D-06: EF no garantiza el orden de las sentencias de una `SaveChanges`. El diseño
evita que un mismo guardado modifique a la vez la cabecera y las filas que su trigger vigila:
**confirmar y cancelar no modifican líneas**. Los motivos y los vínculos se fijan antes, en la
revisión (D-09), con la cabecera en `AwaitingApproval`.

- `CycleCounts`: `DENY DELETE`. `TR_CycleCounts_ClosedImmutable` (`AFTER UPDATE`): `THROW 51401` si
  alguna fila de `deleted` está `Confirmed` o `Cancelled`.
- `CountLines`: `TR_CountLines_OpenCount` (`AFTER INSERT, UPDATE, DELETE`): `THROW 51402` si la
  cabecera de alguna fila está `Confirmed` o `Cancelled`; `51403` si se borra una línea esperada
  (`IsFound = 0`); `51404` si con la cabecera en `AwaitingApproval` cambia `CountedPhysical`.
  Iniciar inserta líneas mientras la cabecera pasa de `Draft` a `Counting`; el trigger admite los dos
  estados, así que el orden de EF no importa.
- `CountLocations`: `TR_CountLocations_Scope` (`AFTER INSERT, UPDATE, DELETE`): insertar o borrar
  solo con la cabecera en `Draft` (`51405`); actualizar solo `IsFrozen` (`51406`), de 0 a 1 con la
  cabecera en `Draft` o `Counting` y de 1 a 0 con la cabecera en `Counting`, `AwaitingApproval`,
  `Confirmed` o `Cancelled` (`51407`). Como una cabecera cerrada no vuelve atrás, una ubicación
  liberada no se puede volver a congelar con ese conteo.
- Todos solo hacen `THROW`, sin `ROLLBACK` (decisión M3 de HU-03), y se declaran con `HasTrigger`.

**Alternativas**: fijar motivos y vínculos en el mismo `POST /confirm`, con una tabla de resoluciones
append-only y totales declarados (patrón de HU-03 C1). Se descarta: más esquema y más triggers para
obtener lo mismo que una revisión previa.

## D-08. Uso del artículo: contributor en `AddStockCore` (INT-HU04-01)

**Decisión**: `CycleCountArticleUsageContributor` (`IArticleUsageContributor` e
`IArticleFirstReferenceContributor`) con `ReferencingEntityTypes = [CountLine]`, registrado en
`AddStockCore` (H0, B), como HU-05 y HU-03. Referencia: existe alguna `CountLine` del artículo.
Historia: existe alguna en un conteo `Confirmed` o `Cancelled` (evidencia conservada).

- Iniciar marca la primera referencia de los artículos capturados con `ArticleReferenceTracker`, bajo
  su bloqueo (D-03).
- Añadir un hallazgo la marca igual; retirarlo antes de enviar retira la referencia. Así se cubre la
  «retirada permitida de borradores» de INT-HU04-01.
- `Hu05SchemaTests` comprueba el conjunto exacto de tipos con FK a `Article`: se añade `CountLine`.

**Alternativas**: no declarar el uso. Se descarta: HU-08 dejaría borrar un artículo con un conteo.

## D-09. Corrección de una salida confirmada: vínculo en la revisión (clarificación Q2)

**Decisión**: en `AwaitingApproval`, `PUT /cycle-counts/{id}/review` (`Inventory.Adjust`) fija por
línea el motivo y, opcionalmente, `correctsShipmentId`. Se valida:

- la salida existe y está `Confirmed` (`422`, campo de la línea);
- alguna de sus líneas tiene el mismo artículo, ubicación y lote que la línea del conteo (`422`);
- la línea tiene diferencia distinta de cero (`422`).

`GET /cycle-counts/{id}/lines/{lineId}/shipment-candidates` lista, paginadas y de más reciente a más
antigua, las salidas confirmadas que tocaron ese bucket, con su pedido y sus vínculos previos. El
ajuste no toca `Shipments`, `CustomerOrderLines.ShippedQuantity` ni las reservas o sus consumos
(FR-019). La salida se lee sin bloquear porque es inmutable (triggers de HU-03).

**Alternativas**: un ajuste directo vinculado a la salida (`POST /movements/{id}/corrections`).
Descartado en la clarificación: segundo escritor de ajustes y físico no comprobado.

## D-10. Código legible del conteo

**Decisión**: `CountCode` = `CC-` + los 12 primeros caracteres hexadecimales del Id, en mayúsculas,
calculado al crear y con índice único (una colisión, improbable, rechaza el alta y se repite con otro
Id). Es el soporte del movimiento y lo que ve el trabajador.

**Alternativas**: secuencia. Se descarta: otro objeto de esquema para algo solo visual.

## D-11. Precondiciones: versión del conteo

**Decisión**: `If-Match` con la versión del conteo en todas las escrituras sobre un conteo existente
(editar, iniciar, contar, hallazgos, enviar, revisar, confirmar y cancelar); `Idempotency-Key` en
todas. Cada escritura renueva la versión de la cabecera, también la que solo cambia líneas.

Límite aceptado: dos personas que cuentan a la vez el mismo conteo se pisan la versión y la segunda
recibe 412 y recarga sin perder lo escrito. La versión por línea queda en «Si hay tiempo».

**Alternativas**: versión por línea. Más precisa, pero multiplica los `ETag` y la pantalla.

## D-12. Lecturas: detalle propio, contexto y lista

**Decisión** (lección 1):

- `GET /cycle-counts/{id}`: solo datos propios, con `ETag` fuerte: cabecera, ubicaciones del alcance y
  líneas ordenadas por Id con teórico, contado, diferencia, motivo, `correctsShipmentId`, quién y
  cuándo.
- `GET /cycle-counts/{id}/context`: sin `ETag` y con `countVersion`: SKU, nombre, unidad, admisión de
  fracciones y estado activo de cada artículo; código de cada ubicación; reservado **actual** de cada
  bucket; referencia externa del pedido de cada salida vinculada; nombres de los responsables.
- `GET /cycle-counts?state=&page=&pageSize=`: lista paginada (`Inventory.Read`) con estado, código,
  ubicaciones, creador y fechas.

La pantalla combina detalle y contexto, comprueba que `countVersion` coincide con el `ETag` y lee
todas las páginas en los selectores (lección 6).

## D-13. `StockCore`: operación de ajuste (H0, B)

**Decisión**: `StockOperation.Adjust` → `MovementKind.Adjustment`, con
`StockCore.PrepareAdjustment(context, lines)` donde cada línea lleva la diferencia con signo.

- `AdjustUp`: crea el lote y el saldo si no existen, como `Receive` (hallazgos y lotes nuevos).
- `AdjustDown`: rechaza con `ReservedStockConflict` si deja el físico por debajo del reservado del
  bucket y con `InsufficientStock` si supera el físico.
- Los artículos desactivados se admiten (INT-HU04-02): se ajustan con la autorización y el motivo del
  conteo.

**Alternativas**: escribir entradas desde `Features/CycleCounts`. Se descarta: otra vía de escritura
al diario, prohibida por el contrato de stock.

## D-14. Migración `HU04_CycleCounts`

**Decisión**: una migración aditiva sobre `20261121135335_HU03_Shipments`, la última presente (lección 5),
con tres tablas, `DENY DELETE` en `CycleCounts`, los tres triggers de D-07 a mano y `HasTrigger` en
las tres configuraciones. Se regenera si cambia la base (fusión del PR #10 o migraciones de A).
`dotnet ef migrations has-pending-model-changes` debe quedar limpio.

## D-15. Frontend sobre la base de A, HU-05 y HU-03

**Decisión**: sustituye el placeholder de `/inventarios` con tres pantallas: lista, nuevo borrador y
detalle por estado (`/inventarios/{id}`: alcance, contar, revisar y confirmar). Reutiliza:

- `MasterDataMutationsService` con `begin(…, appRoute)` + `send()` y `PendingOperationStore`;
- `OperationStatusComponent` para el resultado pendiente de comprobar, con el ID de la operación
  visible al recuperar;
- `apiProblem()`, foco al primer `aria-invalid` o al aviso tras un rechazo, relectura en 412 y tras los
  409 de estado;
- `allRows` para leer todas las páginas en los selectores de ubicaciones y artículos.

Diferencias en texto («Faltan 2», «Sobran 3», «Sin diferencia») además del color. La unidad junto a
cada cantidad. La corrección de salida muestra expresamente «No cambia lo servido del pedido ni las
reservas consumidas».

Requiere en archivos de A (P-06): `mutationPermission()` para `/api/v1/cycle-counts…` y `APP_ROUTE`
para `/inventarios/(nuevo|{uuid})`.

## D-16. Pruebas

**Decisión**: xUnit con `IdentityFixture` contra SQL Server real, receta Docker de specs/003.

- Carreras con conexiones independientes y `LockBarrier` en **ambos órdenes**: inicio de conteo
  frente a confirmar salida, reservar y desactivar la ubicación; confirmar frente a liberar;
  añadir y retirar un hallazgo frente a edición de definición y borrado del artículo (INT-HU04-01).
- `FailAfterSaveInterceptor` en la confirmación (V-08).
- Invariantes en cada confirmación: físico = contado; Σ entradas del bucket = diferencia; ninguna
  ubicación congelada en un conteo cerrado; diario = saldo.
- Vitest/TestBed para las pantallas y Playwright para E2E-03 (contar, revisar y confirmar).

Cada tarea empieza con su rojo anotado; si no hay rojo natural, se comprueba con una mutación temporal
sin commit (método de HU-03).
