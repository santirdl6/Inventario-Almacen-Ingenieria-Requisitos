---
description: "Tareas ejecutables de HU-07 — Crear, enviar y seguir pedidos a proveedores"
---

# Tasks: HU-07 — Crear, enviar y seguir pedidos a proveedores

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contratos](contracts/api.md) y [quickstart.md](quickstart.md).
**Fecha**: 2026-11-20 · **Responsable**: Persona A · **Estado**: implementación en curso; evidencia por tarea en validation/.
**Feature**: `007-gestionar-pedidos-proveedores`; no implica crear o cambiar rama Git.

**Contexto obligatorio**: [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md),
[decisiones comunes](../../.specify/memory/project/research.md),
[modelo común](../../.specify/memory/project/data-model.md),
[HTTP](../../.specify/memory/project/contracts/api.md),
[stock](../../.specify/memory/project/contracts/stock-operations.md),
[UI](../../.specify/memory/project/contracts/ui.md),
[validación conjunta](../../.specify/memory/project/quickstart.md) y
[backlog receptor](../001-reparto-specs-inventario/integration-tasks.md#hu-07--compras--persona-a).

**Pruebas exigidas**: la spec exige evidencia FR/SC, atomicidad, concurrencia, correo,
recuperación y escritores reales INT-HU07-01/02. Por ello se incluyen pruebas SQL/HTTP,
frontend y E2E. Dentro de cada historia escribir primero sus pruebas y comprobar que
detectan la ausencia del comportamiento antes de implementarlo. SQL real, nunca EF InMemory.

**Límites de aceptación**: HU-07 no implementa recepciones/stock. V-02/V-06 y SC-006 con
recepciones reales se aceptan en HU-01. SC-008 con compradores y restauración completa
V-14 siguen en H5-03/H5-02; preparar su guion no equivale a ejecutarlos. La revisión A+B
de componentes compartidos precede a su integración y se registra solo cuando ocurra.

## Format: `[ID] [P?] [Story?] Description`

- Todas las tareas empiezan con checkbox e ID secuencial. Solo las fases US llevan [US1]…[US5].
- [P] significa archivos distintos en la misma oleada **cuando sus dependencias estén completas**;
  no autoriza saltar requisitos previos ni editar simultáneamente un archivo común.
- Las restricciones entre comillas se transcriben del modelo/contratos. No reinterpretarlas
  ni añadir límites de negocio por el tamaño del ensayo de rendimiento.
- Los checkpoints se validan con sus pruebas; no son otros checkboxes ni resultados ya obtenidos.

## Path Conventions

Rutas relativas a la raíz. Backend productivo en `backend/src/Inventario.Api/`, pruebas
en el proyecto **existente** `backend/tests/Inventario.IntegrationTests/` (incluidas reglas
puras/renderizado; no es necesario crear otro proyecto). Frontend en `frontend/src/app/`,
E2E en `frontend/e2e/`. Evidencia de esta feature en
`specs/007-gestionar-pedidos-proveedores/validation/`.

## Phase 1: Setup — Entorno y soporte compartido

**Objetivo**: preparar una ejecución reproducible sobre la base existente, sin sustituir
catálogos, sesión, transporte, ejecutor ni cadena de migraciones.

- [X] T001 Registrar la línea base y la cadena integrada real en `specs/007-gestionar-pedidos-proveedores/validation/baseline.md`: estado/rama, versiones de `backend/src/Inventario.Api/Inventario.Api.csproj` y `frontend/package.json`, última migración y resultados del build/suite existentes usando `quickstart.md`. Preservar cambios locales y migraciones compartidas; considerar HU02 y HU08MovementCounterparty y cualquier integración posterior, sin fijar un snapshot antiguo.
- [X] T002 Crear el soporte de pruebas HTTP/SQL en `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingTestSupport.cs` y `backend/tests/Inventario.IntegrationTests/Purchasing/ControlledPurchaseMailTransport.cs`, reutilizando IdentityFixture y los interceptores existentes: usuarios persistidos, cuentas/claims controlados por permiso, maestros creados por handlers reales, barreras SQL, observaciones SMTP y puntos de fallo exclusivos de pruebas. Worker deshabilitado salvo prueba específica, bases/logins aleatorios y cero modificación de InventoryDev.
- [X] T003 [P] Ampliar `scripts/provision-e2e-users.cs` y `frontend/e2e/fixtures/master-data.fixture.ts` con un perfil explícito HU-07 y Comprador, conservando perfiles HU-08/HU-02, y añadir `frontend/e2e/fixtures/purchasing.fixture.ts`: proyecto/base/volúmenes/Mailpit aislados, acceso al buzón de ese proyecto, sesión/antiforgery reales y limpieza solo de recursos propios. No ampliar implícitamente privilegios de cuentas ordinarias; mantener compatibilidad de `frontend/playwright.config.ts` con HU08_E2E_BASE_URL.
  - Para el E2E de recuperación T046, configurar solo su proceso API aislado con PurchaseDelivery__Enabled=true, PurchaseDelivery__MaxAttempts=1, PurchaseDelivery__PollSeconds=1, PurchaseDelivery__TimeoutSeconds=5 y PurchaseDelivery__LeaseSeconds=40; comprobar 40 > 5 + 30. Conservar los defaults ordinarios y las pruebas de cinco intentos de integración.
  - Añadir controles de fixture para detener/arrancar exclusivamente `smtp` del proyecto Compose E2E identificado explícitamente, esperar disponibilidad real de SMTP/buzón con plazos acotados y restaurarlo en `finally`. No detener servicios de InventoryDev ni introducir rutas de fallo en la API. Las capacidades dependientes del worker se activan después de T027 y se verifican en T046; no bloquean el fixture inicial de sesión.

**Checkpoint**: soporte de pruebas aislado y línea base reproducible. T002 y T003 pueden
trabajarse en paralelo tras T001.

## Phase 2: Foundational — Persistencia e integración comunes

**Objetivo**: esquema compartido por las cinco historias, referencias reales y registro
de feature; completar esta fase antes de implementar historias. Los comportamientos
particulares de pedido, envío y retry se implementan en sus fases, no en el inicializador.

- [X] T004 Crear `backend/src/Inventario.Api/Domain/Purchasing/PurchaseOrder.cs`, `backend/src/Inventario.Api/Domain/Purchasing/PurchaseOrderLine.cs` y sus archivos `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/PurchaseOrderConfiguration.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/PurchaseOrderLineConfiguration.cs`, con entidades y transiciones internas protegidas según estas restricciones literales de `data-model.md`:
  - Id de pedido: «Guid PK, generado por aplicación al iniciar la intención de alta en servidor.» Number: «nvarchar(32), único; secuencia SQL BIGINT, `PO-00000001`, sin reinicio ni obligación de continuidad.» SupplierId: «Guid FK Suppliers; obligatorio.»
  - SupplierSnapshot: «JSON válido: id, code(64), name(200), email(256 nullable), taxIdentifier(64 nullable), schemaVersion=1.» Currency: «char(3), ISO 4217, configuración única capturada en alta; no editable por HTTP.» ExpectedDeliveryDate: «date, obligatorio, puede estar en pasado.»
  - State: «Draft / Sent / PartiallyReceived / Received; CHECK.» SendStatus: «None / Pending / Accepted / Failed; CHECK.» CreatedBy / CreatedAt: «FK Guid a Identity / datetimeoffset UTC del servidor.» SentAt: «datetimeoffset UTC nullable; instante de aceptación SMTP conocido y persistido.» RevisionStamp / Version: «Guid interno de toque del agregado / rowversion SQL.»
  - Línea Id: «Guid PK estable, recibido del cliente, no vacío; solo puede pertenecer a un pedido.» PurchaseOrderId: «Guid FK PurchaseOrders; índice (PurchaseOrderId, Position) único.» Position: «int positivo, orden del array guardado; no identidad de línea.» ArticleId: «Guid FK Articles; índice para lector de referencias.»
  - ArticleSnapshot: «JSON válido: id, sku(64), name(200), baseUnit(32), allowsFractional, tracksLot, schemaVersion=1.» OrderedQuantity: «decimal(18,3), CHECK >0.» ReceivedQuantity: «decimal(18,3), default 0; CHECK ≥0 y ≤OrderedQuantity.» UnitPrice: «decimal(18,2), CHECK ≥0.» ExpectedDeliveryDate de línea: «date NOT NULL, hereda la general si se omite al guardar.»
  - «≥1 línea; IDs y posiciones de líneas únicos dentro del pedido. No unicidad por ArticleId.» «Solo Draft/None editable. Draft con Pending/Failed queda congelado.» «None/Pending/Failed implican Draft y SentAt null. Accepted implica SentAt no nulo y State Sent/PartiallyReceived/Received. No hay recibido positivo en Draft.» Pending se calcula, no se almacena; todas las FK restrictivas. Cambiar líneas/estado/acumulados toca RevisionStamp. Al reordenar líneas evitar colisiones transitorias del índice de posición dentro de la misma transacción, sin borrar/recrear IDs retenidas.
- [X] T005 [P] Crear `backend/src/Inventario.Api/Domain/Purchasing/PurchaseOrderDelivery.cs`, `backend/src/Inventario.Api/Domain/Purchasing/OutboxMessage.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/PurchaseOrderDeliveryConfiguration.cs` y `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` con estas restricciones de `data-model.md`:
  - Delivery Id / PurchaseOrderId: «Guid PK / FK único: una sola entrega por pedido en este alcance.» RequestedBy / RequestOperationId: «FK Identity / OperationResults originales.» RequiredPermission: «`Purchasing.Send`, validado por servidor.» CreatedAt: «datetimeoffset UTC.»
  - RecipientEmail / SenderEmail: «nvarchar(256), un buzón validado, sin CR/LF.» MessageId: «nvarchar(255), único, generado una vez, independiente de Idempotency-Key.» DocumentSnapshot: «JSON válido de pedido y líneas revisados, sin datos vivos derivados.»
  - DocumentBytes / MimeBytes: «varbinary(max), HTML UTF-8 y mensaje MIME completo originales.» DocumentName / MediaType: «nvarchar(128) / nvarchar(100); `pedido-{number}.html`, `text/html; charset=utf-8`.» DocumentHash / MessageHash: «SHA-256 binario(32).» TemplateVersion / ReviewedToken: «versión de renderizado / digest de revisión.» No actualizar contenido después de insertar Delivery.
  - Outbox: «Una fila Guid Id, Type=PurchaseOrderEmail, DocumentId FK a PurchaseOrder y DeliveryId FK único a Delivery.» State: «Pending / Processing / Sent / Failed. Pending/Processing proyectan SendStatus Pending.» ActiveAuthorizationOperationId / AuthorizedBy: «FK al último envío/reintento humano confirmado / su actor; inicialmente los originales.»
  - Cycle / CycleAttempts / Attempts: «ciclo desde 1, claims del ciclo 0..5, claims históricos ≥CycleAttempts; nunca borrar contador histórico.» NextAttemptAt: «UTC SQL, elegibilidad de Pending.» LeaseOwner / LeaseGeneration / LeaseUntil: «instancia UUID / bigint monotónico / UTC SQL; presentes mientras Processing.» CurrentAttemptId / FinishOperationId: «UUID generados al claim, estables para su finalización.»
  - LastErrorCode / LastErrorSummary: «código público acotado(64) / texto saneado(500), sin secretos/trazas.» AcceptanceUncertain: «hubo intento de aceptación no comprobable; Failed no lo borra.» CreatedAt / SentAt / Version: «UTC / aceptación opcional / rowversion.» Añadir CHECK de contadores/generación/lease e índices (State, NextAttemptAt)/(State, LeaseUntil); el payload vive en Delivery, no en una copia mutable.
- [X] T006 [P] Crear `backend/src/Inventario.Api/Domain/Purchasing/PurchaseOrderAudit.cs`, `backend/src/Inventario.Api/Domain/Purchasing/PurchaseDeliveryEvent.cs`, `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/PurchaseOrderAuditConfiguration.cs`, `backend/src/Inventario.Api/Infrastructure/Persistence/Configurations/PurchaseDeliveryEventConfiguration.cs` y `backend/src/Inventario.Api/Features/Purchasing/PurchaseOrderAuditWriter.cs`: evidencia append-only diferida hasta que el ejecutor añada OperationResult, sin commit autónomo. Restricciones literales:
  - Audit: «Id, PurchaseOrderId FK, Action (Create/Update/RequestSend/RetrySend), ActorId FK Identity, OperationId FK OperationResults, RecordedAt UTC, Before/After JSON válido según acción.» «Incluye IDs de líneas retiradas sin FK a estas ni a Article/Supplier.»
  - Event: «Id, DeliveryId FK, AttemptId opcional, LeaseGeneration opcional, Kind (Requested/Retried/Claimed/LeaseExpired/Accepted/TransientFailure/PermanentFailure/Uncertain/LateObservation), ActorId FK, AuthorizationOperationId FK, OperationId FK, ExecutionKind (Human/Worker), WorkerInstanceId opcional, RecordedAt SQL UTC, ObservedAt UTC opcional y Outcome JSON saneado.»
  - «Cada inicio y desenlace es una inserción distinta; no se actualiza un evento Claimed al finalizar.» «Unicidad por (OperationId, Kind, DeliveryId).» «inventory_runtime tiene DENY UPDATE/DELETE sobre ambas tablas y Delivery, como sobre OperationResults». FK restrictivas; no credenciales/tokens en evidencia.
- [X] T007 INT-HU07-01: implementar `backend/src/Inventario.Api/Infrastructure/Persistence/PurchasingArticleUsageContributor.cs` y su registro en `backend/src/Inventario.Api/Features/Purchasing/PurchasingRegistration.cs`: declarar PurchaseOrderLine en IArticleUsageContributor e IArticleFirstReferenceContributor, Any real con el contexto del intento, referencia documental sin inventar historial de stock. Integrar cobertura con ArticleUsageCoverageValidator/ArticleDefinitionReadiness; no devolver false ante error SQL o lector ausente. Depende de T004–T006; el enlace real del escritor se hará en T013 y probará T011/T019.
- [X] T008 Generar con EF la migración de nombre `HU07PurchasingOutbox` en `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/` y sus Designer/snapshot desde el modelo integrado tras T004–T007, conservando `backend/src/Inventario.Api/Infrastructure/Persistence/Migrations/InventoryDbContextModelSnapshot.cs` combinado y una sola cadena; el timestamp será el real de generación. Incluir secuencia, índices (CreatedAt, Number, Id), SupplierId/State, Number único, JSON/CHECK/FK y DENY de T006 al rol inventory_runtime. Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingMigrationTests.cs` para base vacía y actualización desde esquema previo con datos preservados; verificar EF sin cambios pendientes. Regenerar solo la migración local no compartida si otra integración adelanta la cadena.
- [X] T009 Crear `backend/src/Inventario.Api/Features/Purchasing/PurchasingEndpoints.cs` y completar `backend/src/Inventario.Api/Features/Purchasing/PurchasingRegistration.cs` y el registro en `backend/src/Inventario.Api/Program.cs`; agregar `backend/src/Inventario.Api/Features/Purchasing/PurchasingOptions.cs` con Purchasing:Currency ISO 4217/default local EUR. Reutilizar grupo autenticado, policies Inventory.Read/Purchasing.Manage/Purchasing.Send, antiforgery, ProblemDetails y ejecutor existentes; no publicar rutas hasta tener su handler ni registrar worker todavía. Depende de T008.

**Checkpoint**: esquema/cobertura/DI listos y pruebas de migración correctas; no escritores
HTTP de compra habilitados sin el enlace real de primera referencia y su evidencia.

## Phase 3: US1 — Preparar y corregir un pedido de compra (P1) · MVP

**Objetivo**: alta/edición vertical y consulta mínima del borrador para reabrirlo.
**Prueba independiente**: proveedor y dos artículos activos de HU-08; crear 10 UD,
reabrir, corregir/añadir/retirar líneas y comprobar que stock/reservas/diario no cambian.

### Pruebas primero

- [X] T010 [P] [US1] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseOrderDraftTests.cs`: Q07-01/02 y US1.1–4/7, 201/200, número único, recibido cero, precio cero, fechas heredadas/propias/pasadas, artículo repetido con IDs independientes, precisión/límites decimal, campos blancos/extra, IDs repetidas/ajenas y referencia inactiva/nula. Probar errores por línea, replay antes de ETag, falta de permiso, rollback sin pedido parcial y ausencia de efectos en stock. Depende de Phase 2.
- [X] T011 [P] [US1] INT-HU07-01/02: crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingMasterDataIntegrationTests.cs` para primera línea real que marca definición, retirada del último uso de un artículo manteniendo otra línea del pedido, marca monotónica y borrado permitido solo sin referencias/historial. Probar borrar con línea viva, snapshots tras cambios y edición/desactivación/reactivación de Supplier. Añadir carreras de alta/edición contra cambio de definición/borrado/desactivación en ambos órdenes, con conexiones/barreras SQL independientes y espera observada; no preparar la marca por SQL como sustituto del escritor.
- [X] T012 [P] [US1] Crear `frontend/src/app/features/purchasing/pages/purchase-order-form.page.spec.ts` y `frontend/src/app/features/purchasing/data/purchase-orders-api.service.spec.ts`: FormArray con IDs estables y artículos repetidos, exactitud decimal sin float, 422 conserva campos/foco, 412 compara por línea, aviso de correo ausente, cabeceras de alta/PUT y reabrir selección inactiva/fuera de página. Verificar que no se muestra éxito ante red/timeout ni se habilita editar un pedido congelado.

### Implementación e integración

- [X] T013 [US1] Implementar `backend/src/Inventario.Api/Features/Purchasing/Commands/PurchaseOrderWrite.cs` y `backend/src/Inventario.Api/Features/Purchasing/Commands/PurchaseOrderDraftCommands.cs`: normalizar UUID/fechas/decimales y fechas heredadas sin consultar maestros antes del replay; usar ejecutor común, conjunto documental pedido/proveedores viejo-nuevo y artículos viejos-nuevos en orden. Releer tras locks, validar Draft/None, activos solo para nuevas asignaciones, fracciones y ≥1 línea; capturar snapshots solo al asignar, mantener IDs y tocar RevisionStamp. Llamar ArticleReferenceTracker en el mismo contexto/transacción; guardar cabecera/líneas/marca para obtener ETag antes de añadir auditoría con FK al resultado. No StockCore, commit propio ni ActorId del cliente. Depende de T010/T011.
- [X] T014 [P] [US1] Crear `backend/src/Inventario.Api/Features/Purchasing/Queries/PurchaseOrderQueries.cs` y `backend/src/Inventario.Api/Features/Purchasing/PurchaseOrderDtos.cs` para detalle propio/contexto mínimos: SNAPSHOT coherente, ETag fuerte de cabecera solo sobre datos propios/históricos; contexto sin ETag con no-store/purchaseOrderVersion/maestros vigentes y motivos. Pending=Ordered−Received, sin campos editables de recibido. Depende de T010/T011, puede avanzar junto a T013.
- [X] T015 [US1] Mapear POST /purchase-orders, GET/PUT /purchase-orders/{id}, GET /purchase-orders/{id}/context y GET /purchase-orders/settings en `backend/src/Inventario.Api/Features/Purchasing/PurchasingEndpoints.cs` tras T013/T014: permisos por acción, key UUID, If-Match fuerte en PUT (428/412, rechazar weak/*), respuestas `{ operationId, resource }`, Location/ETag históricos y no-store. Settings devuelve la moneda configurada con Inventory.Read para mostrarla antes del alta. Traducir errores del contrato sin filtrar SQL ni reconstruir respuesta desde el recurso actual.
- [X] T016 [US1] Ampliar `frontend/src/app/core/operations/pending-operation.store.ts`, `frontend/src/app/core/http/master-data-mutations.service.ts` y `frontend/src/app/core/operations/operation-status.component.ts`, con pruebas en `frontend/src/app/core/operations/purchasing-pending-operation.spec.ts`: permisos por método/ruta, grupo por pedido entre PUT/send y grupo de alta, bloqueo de intención incompatible, snapshot/actor/ETag inmutables y navegación a Compras. Mantener referencias antiguas y logout sin payload/ETag; IdempotencyConflict no descarta referencia. Coordinar las mismas funciones con cualquier ampliación HU-05 integrada, conservando ambos conjuntos de rutas. Depende de T012; no depende de T015 para el transporte simulado.
- [X] T017 [US1] Crear `frontend/src/app/features/purchasing/data/purchase-orders-api.service.ts`, `frontend/src/app/features/purchasing/data/purchase-order.models.ts` y `frontend/src/app/features/purchasing/pages/purchase-order-form.page.ts` tras T012/T015/T016: alta/corrección mediante transporte común, selectores paginados reutilizados, FormArray por UUID, fecha civil/moneda/unidad explícitas y cantidades/precios strings. Consultar settings para la moneda de un alta y conservar la Currency histórica al editar, bloqueando el guardado si no puede cargarse. Mantener snapshots, datos válidos y edición local en conflicto, comparar por ID de línea y no mezclar detalle/contexto de distintas versiones; limpiar formulario al logout.
- [X] T018 [US1] Crear `frontend/src/app/features/purchasing/purchasing.routes.ts` y la consulta mínima en `frontend/src/app/features/purchasing/pages/purchase-order-detail.page.ts`, integrar Compras en `frontend/src/app/app.routes.ts` y `frontend/src/app/operation-areas.ts`: /compras/nuevo, /:id/editar y /:id con guards correspondientes, estáticas primero. Añadir raíz provisional /compras con Inventory.Read en `frontend/src/app/features/purchasing/pages/purchasing-home.page.ts`: título Compras, operaciones pendientes del actor y enlace Nuevo pedido solo con Purchasing.Manage, sin afirmar que no hay pedidos al no consultar aún el listado. Probar en `frontend/src/app/features/purchasing/purchasing.routes.spec.ts` la entrada desde el menú con comprador y cuenta de solo consulta. Mostrar documento guardado y enlace de corrección solo Draft/None; recuperar alta por Location/id y refrescar recurso vigente. Depende de T017; listado y seguimiento completo se amplían en US3.
- [X] T019 [US1] Ejecutar T010–T012 y sus regresiones de transporte/maestros, corregir lo que falle en sus archivos y registrar `specs/007-gestionar-pedidos-proveedores/validation/us1.md` con FR-001–008/022/025–030 y evidencia INT-HU07-01/02 parcial por escenario. Incluir reordenación de posiciones y avance de ETag por cambio solo de línea. No completar aún INT-HU07-02: falta probar el primer envío contra desactivación en US2. Depende de T013–T018.

**Checkpoint MVP**: US1 vertical, Q07-01/02/07 y parte de Q07-08/12 correctos, sin correo
ni recepción, con entrada /compras operable desde el menú para ambos permisos de lectura/gestión.
Permisos, auditoría, rollback y recuperación básica ya son obligatorios;
no se aplazan por estar también descritos en US4/US5.

## Phase 4: US2 — Revisar y solicitar el envío al proveedor (P1)

**Objetivo**: revisar documento/contacto exactos, conservar solicitud única y enviar por
SMTP sin transacción abierta; solo aceptación persistida cambia a Enviado.
**Prueba independiente**: un pedido US1, SMTP controlado/Mailpit y comprador; comprobar
revisión, 202 Pending, documento recibido idéntico y Sent/Accepted, sin recepción.

### Pruebas primero

- [X] T020 [P] [US2] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseOrderSendTests.cs` y `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseOrderSendConcurrencyTests.cs`: US2.1–7/Q07-03, preview con impedimentos, falta/cambio de correo, cambio de documento/configuración, If-Match y reviewToken, primer envío con inactivos, dos initial y PUT frente a send, rollback de solicitud y replay 202 histórico después de Accepted. INT-HU07-02: primera solicitud contra desactivación de Supplier/Article en ambos órdenes con barreras SQL; cambios posteriores no cambian Delivery. Depende del checkpoint US1.
- [X] T021 [P] [US2] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseDeliveryWorkerTests.cs` y `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseOrderDocumentTests.cs`: documento/MIME estable y escapado, una aceptación efectiva, fallo de Disconnect posterior no revoca aceptación, Pending/Failed no pasan a Sent, dos workers sin dos concesiones vigentes, lease vencido/fence antiguo, reinicio tras claim y finalización SQL repetida sin segundo SMTP. Comando sin autorización persistida no cambia ni envía. Estas garantías se verifican **antes de habilitar el primer envío**, aunque US4 amplíe los escenarios.
- [X] T022 [P] [US2] Crear `frontend/src/app/features/purchasing/pages/purchase-order-send.page.spec.ts`: revisión por teclado con número/contacto/líneas/moneda, conserva ETag+reviewToken, impide confirmar con bloqueos, 202 muestra Pending y congela edición, cambio de contacto exige nueva revisión y respuesta tardía no se aplica a otra identidad. Usar contrato conocido, sin depender de implementación backend para escribir las pruebas.

### Implementación e integración

- [X] T023 [US2] Fijar MailKit 5.0.0 en `backend/src/Inventario.Api/Inventario.Api.csproj` y crear `backend/src/Inventario.Api/Infrastructure/Messaging/PurchaseMailOptions.cs`, `backend/src/Inventario.Api/Infrastructure/Messaging/PurchaseDeliveryOptions.cs`, `backend/src/Inventario.Api/Infrastructure/Messaging/PurchaseOrderDocumentRenderer.cs` y `backend/src/Inventario.Api/Features/Purchasing/Queries/PurchaseSendReview.cs`. Validar buzón único sin CR/LF, opciones y Currency; defaults polling 5 s, lease 120 s, SMTP 30 s, MaxAttempts 5 (admitido 1–5), backoff 5/30/120/300 s, lease > timeout+30 s. HTML UTF-8 autocontenido escapado, MIME/MessageId/Date/boundaries generados una vez; digest canónico SHA-256 Base64 incluye documento, versión pedido/proveedor/artículos y remitente/plantilla/configuración. No secretos en snapshots; no regenerar contenido al retry. Depende de las pruebas T020–T022 escritas; verificación backend en validation/us2.md.
- [X] T024 [US2] Factorizar el núcleo privado de `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/InventoryOperationExecutor.cs` e introducir la entrada tipada en `backend/src/Inventario.Api/Infrastructure/Messaging/PurchaseDeliveryCommands.cs`, conservando entrada HTTP/ICurrentActor/policies. Claim/Finish/LateObservation solo aceptan IDs y observación, resuelven autoridad/actor desde Delivery y OperationResult de envío/retry confirmados y validan relación/permiso/recurso bajo locks. Mismos scopes limpios, transacción, operation lock, huella, replay, tres intentos y timeout 5 s; método lógico POST y recursos internos del contrato. Sin HttpContext simulado, ActorId libre, bypass público, callback arbitrario ni ejecutor paralelo. Probar regresión del ejecutor antes de continuar. Depende de T020/T021.
- [X] T025 [US2] Implementar `backend/src/Inventario.Api/Features/Purchasing/Queries/PurchaseOrderSendPreviewQuery.cs` y `backend/src/Inventario.Api/Features/Purchasing/Commands/RequestPurchaseOrderSend.cs`: preview SNAPSHOT solo Draft/None, documento/contacto/impedimentos/versiones; initial revalida ETag, proveedor/artículos activos y token después de locks pedido+supplier→artículos. Conservar Delivery/outbox/evento Requested/auditoría/OperationResult conjuntamente, mover Draft/None a Draft/Pending y congelar edición; 202 con ETag/Location e IDs. Respetar SaveChanges diferido de FK a resultado, no SMTP en endpoint, no segunda Delivery; rechazar revisión vieja con SendReviewChanged. Depende de T023/T024.
- [X] T026 [US2] Implementar Claim/Finish/LateObservation en `backend/src/Inventario.Api/Infrastructure/Messaging/PurchaseDeliveryCommands.cs` tras T024/T025: operation→purchase-order+purchase-order-delivery (ambos en una llamada ordenada por recurso canónico), lectura decisoria autoritativa y SQL UTC. Claim fija AttemptId/FinishOperationId/owner/generación/lease, incrementa contadores una vez y registra evento; replay de claim no concede lease actual. Finish exige fence vigente, actualiza compra/outbox/versión/evento/resultado juntos; aceptación no degrada un recibido existente. Fallos transitorios reprograman, permanentes/presupuesto agotado producen Failed, expiración conserva incertidumbre y observación tardía solo evidencia/no-op. Resultado técnico 2xx no equivale a aceptación SMTP. Preparar hashes/UUID una vez por comando local.
- [X] T027 [US2] Crear `backend/src/Inventario.Api/Infrastructure/Messaging/MailKitPurchaseMailTransport.cs` y `backend/src/Inventario.Api/Infrastructure/Messaging/PurchaseDeliveryWorker.cs`, y registrar servicios/opciones en `backend/src/Inventario.Api/Program.cs` y `docker-compose.yml` conservando smtp:1025/Mailpit. Leer MIME conservado y comprobar hash, verificar lease antes de SMTP, una conexión/envío por intento sin transacción SQL, plazo total/cancelación 30 s y un envío concurrente por instancia. Clasificar rechazo/aceptación/incertidumbre; tras fallo SQL recuperar/repetir finish, nunca volver a SendAsync por ese fallo. No registrar worker en inicialización/mantenimiento; PurchaseDelivery:Enabled=false pausa sin perder trabajo y TLS es obligatorio fuera de local. Depende de T023/T026.
- [X] T028 [P] [US2] Crear `backend/src/Inventario.Api/Features/Purchasing/Queries/PurchaseOrderDeliveryQueries.cs` tras T025: GET delivery mínimo coherente sin ETag con purchaseOrderVersion, None sin solicitud, Pending/Accepted/Failed, attempts/instantes/incidencia saneada/incertidumbre/canRetry por permiso actual. Servir DocumentBytes originales como attachment con nombre seguro, Content-Type text/html; charset=utf-8 y nosniff; sin SMTP crudo, lease/owner, secretos ni contenido inline en la SPA. Puede avanzar junto a T026/T027 en sus propios archivos.
- [X] T029 [US2] Crear `frontend/src/app/features/purchasing/pages/purchase-order-send.page.ts` y ampliar `frontend/src/app/features/purchasing/data/purchase-orders-api.service.ts` tras T022/T025/T028: revisar documento/contacto con ETag/token asociados, confirmar initial mediante transporte común, mantener datos ante rechazo y diferenciar solicitud Pending de aceptación/entrega final. Usar estados textuales/teclado, grupo de pedido y limpieza al logout; mostrar documento histórico sin ejecutar HTML arbitrario. El retry manual se añade en T044, conservando aquí la prohibición de modificar entrega congelada.
- [X] T030 [US2] Mapear preview, send initial, delivery y delivery/document en `backend/src/Inventario.Api/Features/Purchasing/PurchasingEndpoints.cs`, publicar sus contratos OpenAPI y añadir /compras/:id/enviar en `frontend/src/app/features/purchasing/purchasing.routes.ts` tras T025–T029. Aplicar Purchasing.Send al preview/send, Inventory.Read a seguimiento/documento, no-store y todas las precondiciones; ampliar detalle mínimo de `frontend/src/app/features/purchasing/pages/purchase-order-detail.page.ts` con enlace y estados. No exponer comandos internos ni rutas de prueba productivas.
- [X] T031 [US2] Ejecutar T020–T022 sobre HTTP/SQL real y transporte controlado, verificar aceptación real/MIME en Mailpit y registrar `specs/007-gestionar-pedidos-proveedores/validation/us2.md`: Q07-03/06/08/09/10/12 aplicables, cero SMTP de solicitud revertida, cero stock/diario y evidencia final del primer envío en INT-HU07-02. Incluir falta/revocación posterior de sesión sin cancelar autorización persistida, pero no conceder retry humano sin permiso actual. Depende de T030.
- [X] T032 [US2] Añadir `frontend/e2e/purchasing-send.spec.ts` con perfil T003 y flujo real crear→revisar→solicitar→Mailpit→Accepted, incluyendo cambio de destinatario antes de confirmar y edición bloqueada tras solicitar. Conservar número/DeliveryId/MessageId/documento iguales y comprobar navegación por teclado. Ejecutar y guardar resultados en `specs/007-gestionar-pedidos-proveedores/validation/us2.md` tras T031.

**Checkpoint**: envío inicial completo con garantías técnicas de recuperación/permiso/
atomicidad activas, documento estable y ambas integraciones receptoras probadas. US4
añade el recorrido explícito de retry y pruebas adversas ampliadas; no habilita por primera
vez el lease, el fencing o la recuperación del worker.

## Phase 5: US3 — Consultar pedidos, estados y cantidades pendientes (P1)

**Objetivo**: búsqueda/listado/detalle y seguimiento coherentes, conservando historia.
**Prueba independiente**: pedidos de varios proveedores y cuenta Inventory.Read;
filtrar/paginar/reabrir y verificar documento, recibido y pendiente sin escribirlos.

### Pruebas primero

- [X] T033 [P] [US3] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseOrderQueryTests.cs`: Q07-11/12, filtros q/supplier/state/from/to, límites/página vacía con total real, lista/count SNAPSHOT, detalle propio vs contexto, maestros modificados/inactivos sin reescribir snapshots y ETag distinto al cambiar solo línea/estado. Probar proyección de 0/10, 6/4 y 10/0 con preparación técnica de lectura claramente identificada, CHECK de no exceso y campos de acumulados rechazados por HTTP; no presentarla como recepción HU-01 ejecutada. Depende de T019; no requiere US2 salvo casos de aceptación, que esperan T031.
- [X] T034 [P] [US3] Crear `frontend/src/app/features/purchasing/pages/purchase-order-list.page.spec.ts` y `frontend/src/app/features/purchasing/pages/purchase-order-detail.page.spec.ts`: lista vacía vs error/página fuera de rango, navegación conservando filtros, estados separados/indicadores inactivos, detalle recibido/pendiente no editable y purchaseOrderVersion desalineada obliga a refrescar. Polling termina por cambio de vista/actor/estado; error de actualización conserva último dato con aviso.

### Implementación e integración

- [X] T035 [US3] Completar listado en `backend/src/Inventario.Api/Features/Purchasing/Queries/PurchaseOrderQueries.cs` y mapear GET /purchase-orders en `backend/src/Inventario.Api/Features/Purchasing/PurchasingEndpoints.cs`: «page desde 1, pageSize 25 por defecto, máximo 100», q por prefijo de número, supplierId/state y período de creación [from,to), validando from<to. Orden Number/Id estable y count+items en SNAPSHOT, vacío fuera de rango con total real, no error ocultado como 200. Depende de T033.
- [X] T036 [US3] Completar las proyecciones/estado derivado en `backend/src/Inventario.Api/Features/Purchasing/PurchaseOrderDtos.cs`, `backend/src/Inventario.Api/Domain/Purchasing/PurchaseOrder.cs` y documentar el punto de consumo HU-01 en `specs/007-gestionar-pedidos-proveedores/contracts/integration.md`: recibido/pending por línea, Accepted con 0 recibido→Sent, parcial→PartiallyReceived y todo completo→Received. Preservar toque de cabecera para futuro escritor atómico; no crear Receipt, endpoint de recibido ni escritor alternativo. Depende de T033; los ensayos 6+4/6+6 con recepciones reales siguen siendo aceptación de HU-01.
- [X] T037 [P] [US3] Completar `frontend/src/app/features/purchasing/pages/purchase-order-detail.page.ts` y `frontend/src/app/features/purchasing/data/purchase-orders-api.service.ts` tras T030/T034/T036: detalle propio/contexto/delivery identificados por versión, cantidad/unidad/precio/fecha/recibido/pending, indicadores inactivos y descarga original. Polling cada 5 s solo visible/Pending, refresco manual y limpieza de suscripciones; mantener datos y explicar error de consulta. No hacer Accepted a partir del 202 histórico.
- [X] T038 [US3] Crear `frontend/src/app/features/purchasing/pages/purchase-order-list.page.ts` y completar `frontend/src/app/features/purchasing/purchasing.routes.ts` tras T034/T035/T037: sustituir la raíz provisional /compras de T018 por el listado con filtros/paginador, proveedores históricos incluso inactivos, estado logístico/correo textuales y recuperación de página inválida. Retirar `frontend/src/app/features/purchasing/pages/purchasing-home.page.ts` si queda sin uso y actualizar `frontend/src/app/features/purchasing/purchasing.routes.spec.ts` manteniendo acceso desde el menú para comprador/solo consulta y las rutas existentes. Mostrar acciones según permisos; usar datos del total, no items.length, para decidir vacío global y conservar las operaciones pendientes accesibles desde el listado.
- [X] T039 [US3] Añadir el recorrido de consulta en `frontend/e2e/purchasing-query.spec.ts`, ejecutar T033/T034 y guardar `specs/007-gestionar-pedidos-proveedores/validation/us3.md` con Q07-11/12, FR-018–022/030 y límites de SC-006. Comprobar selección histórica/inactividad/errores/paginación y que consultar no cambia pedidos ni stock. Depende de T035–T038; no cerrar aquí V-02/V-06 de HU-01.

**Checkpoint**: consultas completas; no exige recepciones reales para cerrar técnicamente
HU-07. Si HU-01 ya está integrada, ejecutar además sus casos reales sin reemplazar su
responsabilidad ni duplicar escritores.

## Phase 6: US4 — Resolver fallos y comprobar envíos inciertos (P2)

**Objetivo**: retry explícito autorizado sobre entrega original y recuperación verificable
de HTTP/SMTP/reinicio. La infraestructura técnica básica ya está implementada en US2.
**Prueba independiente**: provocar fallo SMTP conocido, reintentar y perder respuestas;
comprobar misma Delivery/MessageId/documento y resultado recuperable sin otra compra.

### Pruebas primero

- [X] T040 [P] [US4] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseDeliveryRetryTests.cs`: Failed→Pending con nueva clave/ETag actual, misma Delivery/MIME/MessageId/contacto, ciclo nuevo con Attempts históricos e incertidumbre conservados; maestros/contactos cambiados no sustituyen compromiso. Replay de retry vs repetición de initial, dos retries simultáneos, Pending/Accepted no reintentables, Delivery ajena, falta de permiso y respuesta HTTP perdida. Depende de T031 y checkpoint US3 para la secuencia ordinaria.
- [X] T041 [P] [US4] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchaseDeliveryRecoveryTests.cs`: ampliar Q07-09 con dos scopes/procesos coordinados, caída antes/después de SMTP, timeout tras DATA, acuse externo perdido, claim histórico vencido, owner antiguo que vuelve y autorización antigua frente a ciclo posterior. Forzar commit local incierto y demostrar que se recupera/repite finish sin reenviar SMTP; comprobar presupuesto/backoff y persistencia tras recrear worker. No usar esperas largas como única prueba de solapamiento.
- [X] T042 [P] [US4] Crear `frontend/src/app/features/purchasing/pages/purchase-order-recovery.spec.ts`: pérdida de respuesta de alta/PUT/send/retry, recarga, 404 de operaciones, Retry-After, logout/login con misma/otra cuenta, respuesta tardía y replay 202 histórico con delivery actual Accepted/Failed. Probar que PUT/send se bloquean por pedido, payload retirado no se reconstruye y retry conocido tiene otra clave pero misma Delivery.

### Implementación e integración

- [X] T043 [US4] Implementar `backend/src/Inventario.Api/Features/Purchasing/Commands/RetryPurchaseOrderDelivery.cs` e integrar kind retry en `backend/src/Inventario.Api/Features/Purchasing/PurchasingEndpoints.cs`: nueva intención con Purchasing.Send/antiforgery/If-Match actual, locks pedido+delivery, comprobar Failed y pertenencia, incrementar Cycle/reiniciar solo CycleAttempts/cambiar autoridad activa, tocar versión y escribir Retried/auditoría/202 con resultado propio en un commit. No crear Delivery, revalidar maestros para sustituir contenido ni cambiar MessageId/MIME/destinatario. Depende de T040 y protocolo ya implementado en T026.
- [X] T044 [P] [US4] Completar retry conocido en `frontend/src/app/features/purchasing/pages/purchase-order-send.page.ts` y `frontend/src/app/features/purchasing/data/purchase-orders-api.service.ts`: Failed muestra compromiso congelado/incidencia/intentos e incertidumbre, nueva acción solo después de resolver intención HTTP pendiente y con permiso actual. Diferenciar «Comprobar/Repetir la misma intención» de «Reintentar envío», refrescar detalle/delivery tras resultado y no regenerar destino. Depende de T042 y contratos T040; puede avanzar junto a T043 con transporte simulado.
- [X] T045 [P] [US4] Completar los casos de T042 en `frontend/src/app/core/operations/pending-operation.store.ts`, `frontend/src/app/core/operations/operation-status.component.ts` y `frontend/src/app/core/http/master-data-mutations.service.ts`: aislamiento por actor/generación, referencias conservadas tras logout, 404 no definitivo, espera Retry-After y recuperación por método/ruta/clave/cuerpo/ETag originales. Los enlaces de Compras deben derivarse de la referencia de ruta/Location/resultado, sin exigir payload tras logout; no permitir nueva intención incompatible. Mantener maestros/ubicaciones y cualquier ampliación HU-05 integrada. Depende de T016/T042; coordinar sin tocar los archivos de T044.
- [X] T046 [US4] Crear `frontend/e2e/purchasing-recovery.spec.ts` tras T043–T045: error SMTP controlado y retry autorizado, pérdida de respuesta tras commit y recarga para alta/PUT/send/retry, misma cuenta que recupera y otra que no reutiliza; verificar operación/entrega/documento/estado vigentes. Usar perfil aislado T003 y fallos desde el entorno de tests, nunca endpoints de inyección productivos. Registrar resultados browser y Mailpit en `specs/007-gestionar-pedidos-proveedores/validation/us4.md`.
  - Con las opciones reducidas de T003 y SMTP inicialmente disponible, guardar el pedido y su revisión; detener solo el SMTP aislado **antes de solicitar** envío. Esperar mediante estado observable Draft/Failed tras el único intento y comprobar entrega conservada y edición bloqueada. Restaurar SMTP, esperar disponibilidad y ejecutar retry con nueva operación; esperar Accepted y comprobar mismos DeliveryId/MessageId/destinatario/documento, sin otra compra. Restaurar servicio en `finally`, sin sleeps como criterio de éxito ni ampliar globalmente el timeout de Playwright para esperar el backoff ordinario.
  - Mantener rechazo permanente, presupuesto ordinario, timeout tras DATA, aceptación incierta y fencing en T021/T041; el transporte controlado en integración no sustituye el SMTP del proceso API de este E2E. Seguir el [guion SMTP E2E](quickstart.md#fallo-smtp-y-recuperación-e2e).
- [X] T047 [US4] Ejecutar T040/T041/T042 con fallos controlados y corregir desviaciones en sus implementaciones; completar `specs/007-gestionar-pedidos-proveedores/validation/us4.md` con Q07-04/05/06/09, FR-014–017/025/027 y pruebas de reanudación. Identificar explícitamente los casos donde puede existir copia SMTP y demostrar que no es otra compra; no usar Failed como prueba de rechazo externo si hubo incertidumbre. Depende de T043–T046.
- [X] T048 [US4] Actualizar `README.md`, `.env.example` y `specs/007-gestionar-pedidos-proveedores/quickstart.md` con opciones SMTP/worker realmente implementadas y pasos de pausa/reanudación en entorno aislado, sin secretos. Documentar PurchaseDelivery:Enabled=false para restauración, comprobar que pausa/reinicio conserva intención/edición bloqueada, y referenciar H5-02 antes de reactivar trabajo restaurado. No ejecutar ni declarar la restauración operativa completa como cierre de HU-07. Depende de T047.

**Checkpoint**: Q07-04/05/09 y retries concurrentes correctos; resultados HTTP históricos
inmutables y aceptación externa incierta comunicada sin promesa de entrega única.

## Phase 7: US5 — Operar con autorización, autoría y cambios fiables (P2)

**Objetivo**: acreditar integralmente permisos, autoría, conflictos y atomicidad de todos
los caminos ya implementados. Estos controles existen desde US1/US2; esta fase amplía y
cierra su evidencia, no los introduce tarde.
**Prueba independiente**: cuentas de consulta/gestión/envío, dos sesiones con la misma
revisión, fallos antes/durante commit y repetición de acciones confirmadas.

### Pruebas y verificación transversal

- [X] T049 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingAuthorizationTests.cs`: matriz de todas las rutas de `contracts/api.md` (401/403/400), permiso de consulta/gestión/envío independiente con identidades persistidas controladas, Administrador sin compras implícitas y actor/estado/acumulados suministrados por cliente rechazados. Probar autorización actual de replay/consulta de resultado y autoridad persistida del worker tras expirar sesión; comando interno con Delivery/permiso/operación de origen ajenos no tiene efectos. Depende de US1–US4.
- [X] T050 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingAtomicityAuditTests.cs`: fallos después de cabecera/líneas/marca y antes de evidencia/resultado, después de solicitud y antes de commit, y en claim/finish/retry. Comparar todas las filas/stock antes-después, ausencia de correo sin autorización conservada, ningún evento/result duplicado por replay y Before/After/actor/instantes correctos. Recuperar commit incierto sin reconstruir resultado desde recurso evolucionado, relacionando ejecución Worker con autorización humana original/activa según corresponda.
- [X] T051 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingConcurrencyTests.cs`: dos PUT sobre misma revisión, cambio concurrente de proveedor/líneas durante descubrimiento de locks frente a send, orden documental viejo/nuevo y replanificación ante recurso no declarado. Reutilizar las barreras del soporte para demostrar ambos órdenes, lecturas tras locks, respuesta compatible y ninguna adquisición de fase anterior. Reutilizar/asertar resultados de T011/T020/T040 sin duplicar los mismos casos; añadir solo las interacciones multicomando pendientes.
- [X] T052 [P] [US5] Crear `backend/tests/Inventario.IntegrationTests/Purchasing/PurchasingRuntimeIntegrityTests.cs`: con login SQL runtime real comprobar DENY UPDATE/DELETE en Delivery/auditoría/eventos/OperationResults y CHECK/FK de cantidades/estados/JSON/contadores/leases. Comprobar restricciones sin aplicar SQL administrativo para saltarlas; asegurar que quitar línea legítima no borra evidencia ni evita la marca monotónica, y que no se introdujo permiso DDL a la API.
- [X] T053 [US5] Crear `frontend/src/app/features/purchasing/purchasing-authorization.spec.ts` y `frontend/e2e/purchasing-permissions.spec.ts`: guards/rutas/acciones por permiso, consulta de inactivos, teclado/etiquetas/errores conservados y logout que limpia formulario/revisión/memoria. Contrastar UI con las denegaciones reales del servidor; reforzar solo las desviaciones observadas en `frontend/src/app/features/purchasing/purchasing.routes.ts` y sus páginas, sin una segunda autorización cliente. Depende de T049 y componentes US1–US4.
- [X] T054 [US5] Ejecutar T049–T053, corregir defectos en los comandos/ejecutor/audit writer/endpoints afectados y registrar `specs/007-gestionar-pedidos-proveedores/validation/us5.md` con US5.1–6, FR-023–028, Q07-06/07/08/10 y SC-002/004/005. Enlazar evidencia propia de INT-HU07-01/02 y las fases tempranas; conservar traceId/errores públicos comprensibles sin SMTP/SQL/credenciales. No certificar revisiones A+B o aceptación humana inexistentes.

**Checkpoint**: cinco historias verificadas técnicamente y garantías transversales
acreditadas con escritores reales, sin sustituirlas por lectores constantes/dobles de maestros.

## Phase 8: Polish & Cross-Cutting Concerns

**Objetivo**: rendimiento, regresión integrada y paquete de evidencia/entrega técnica.

**Cierre funcional local (2026-11-21)**: el ensayo de cierre supera la muestra mínima sin
errores medidos; p95 de consulta 1,25 s, alta 3,65 s y corrección 3,60 s. T055 queda abierta
por el objetivo de los guardados, con seguimiento en el [backlog común](../001-reparto-specs-inventario/integration-tasks.md#hu-07--compras--persona-a).
T056 y T058–T060 cierran la regresión y sus entregables para este corte, tras registrar el
resultado de T055, sin certificar SC-007. Repetir las comprobaciones afectadas cuando se
cambie el código productivo para resolver rendimiento. [Resultados](validation/performance.md).

- [ ] T055 Preparar `backend/tests/Inventario.IntegrationTests/Purchasing/Hu07PerformanceData.cs` y `scripts/measure-hu07-purchasing.mjs` y ejecutar SC-007 siguiendo el [protocolo reproducible de rendimiento](quickstart.md#protocolo-reproducible-de-rendimiento-sc-007): entorno aislado, 25 sesiones autenticadas, 10.000 artículos, 10.000 pedidos iniciales de 1/10/20 líneas y 1.000.000 de entradas de historial preparadas por comandos/StockCore, no saldos manuales. El ejecutor Node usa Playwright ya existente para medir desde la acción de consulta/guardado hasta su resultado visible confirmado; HTTP es diagnóstico auxiliar. Calentar 2 minutos y medir 10 minutos, con al menos 1.000 muestras por operación y mezcla 50 % consulta/25 % alta/25 % corrección; fijar semilla, filtros y distribución de datos según la guía. Exigir p95 ≤2 s por operación, registrar hardware, muestras, errores y esperas SMTP/conflictos aparte en `specs/007-gestionar-pedidos-proveedores/validation/performance.md`, sin ocultar errores como éxitos. Ensayo sin muestra mínima es inconcluso. Optimizar solo consultas/índices que incumplan, sin otro cálculo de stock. Depende de T054.
- [X] T056 Ejecutar una validación integrada final según `specs/007-gestionar-pedidos-proveedores/quickstart.md`, registrar comandos/resultados en `specs/007-gestionar-pedidos-proveedores/validation/integration.md` y corregir regresiones: backend SQL completo, frontend build/Vitest/tipos/E2E afectados, publicación Docker, configuración Compose y EF sin diferencias. Incluir H0/StockCore/HU-08/HU-02 y las ampliaciones HU-05 presentes, modelo/migración desde cero y esquema previo, MIME real/Mailpit y restart/pausa del worker. Depende de T055 y de cualquier corrección de rendimiento; conservar omisiones reales y su motivo.
- [X] T057 [P] Preparar `specs/007-gestionar-pedidos-proveedores/validation/h5-handoff.md` con el guion SC-008 de ≥3 compradores y referencias H5-03/H5-02: tareas sin asistencia, errores/teclado/estados, registro de incidencias, pausa del correo tras restauración y conciliación antes de reactivarlo. Solo documentar preparación/estado pendiente; aceptación humana, RPO/RTO y restauración operativa permanecen en `specs/001-reparto-specs-inventario/integration-tasks.md`, no bloquean el cierre técnico local. Depende de T048/T054 y puede avanzar junto a T055/T056.
- [X] T058 Completar el handoff verificable a HU-01 en `specs/007-gestionar-pedidos-proveedores/contracts/integration.md` y `specs/007-gestionar-pedidos-proveedores/validation/hu01-handoff.md`: IDs/tipos reales entregados, lectura propia/contexto/ETag, locks purchase-order+receipt→ubicaciones→artículos y obligación de tocar versión al confirmar/corregir acumulados junto a StockCore. Mantener V-02/V-06 (6+4 y dos de 6 contra 10) y SC-006 asignados al escritor HU-01, con estado pendiente explícito hasta su implementación. No crear esa feature ni simular aceptación de recepción real. Depende de T036/T056.
- [X] T059 Preparar `specs/007-gestionar-pedidos-proveedores/validation/shared-review.md` con los diffs/impacto a revisar por A+B: ejecutor/autoridad interna, mappings/migración/snapshot, locks/huellas/ETag, MailKit, permisos y transporte/store compatibles con HU-05. Enlazar evidencia de `specs/007-gestionar-pedidos-proveedores/validation/integration.md` y registrar revisión real si existe; si no, conservarla pendiente antes de integrar, conforme al plan global y COORD-01, sin autoaprobar ni bloquear por ello la evidencia técnica local. Depende de T056/T058.
- [X] T060 Consolidar `specs/007-gestionar-pedidos-proveedores/validation/acceptance.md` con matriz FR-001–030/SC-001–008/Q07/INT, resultados/entorno/fechas/limitaciones SMTP, evidencias US1–US5 y rendimiento. Actualizar `specs/001-reparto-specs-inventario/integration-tasks.md` solo para INT-HU07-01/02 cuando sus escritores/pruebas reales estén acreditados y enlazar `tasks.md`/quickstart. Separar cierre técnico, recepción HU-01, revisión compartida y H5 con sus estados reales; no marcar tareas ni escenarios omitidos como completos. Depende de T056–T059.

## Dependencies & Execution Order

### Dependencias de fase e historias

```text
T001 → {T002, T003}
      → {T004, T005, T006} → T007 → T008 → T009
      → US1 (T010–T019) → US2 (T020–T032)
                        └→ US3 (T033–T039; seguimiento Accepted espera US2)
US2 + US3 → US4 (T040–T048) → US5 (T049–T054)
US5 → T055 → T056 → T058 → T059 → T060
      └→ T057 (guion H5; T060 también lo requiere)
```

- Setup y Foundational bloquean las cinco historias. T003 es independiente del código
  backend pero debe estar listo para el primer E2E T032.
- US1 es el MVP autónomo sin SMTP ni HU-01. US2 consume su pedido. US3 puede desarrollar
  búsqueda/lectura tras US1, pero validar aceptación del correo exige US2. Sus preparaciones
  de recibido son exclusivamente pruebas de lectura, no aceptación de HU-01.
- US4 consume el envío/seguimiento de US2/US3. Lease, fencing, identidad y retry técnico
  ya deben funcionar en US2; US4 entrega retry humano y recuperación ampliada.
- US5 tiene fase de evidencia propia; sus invariantes están presentes desde cada escritor
  y no pueden aplazarse hasta esta fase para habilitar un flujo.
- HU-01/HU-06/H5 no son dependencias de implementación de HU-07. Cambios de B en snapshot,
  Program.cs o transporte se combinan con lo ya integrado, sin reemplazar archivos completos.
- Los IDs expresan orden por defecto. Puede adelantarse una tarea [P] solo tras completar
  sus prerequisitos y comprobar que no comparte archivos activos con otra tarea.

### Orden dentro de cada historia

Pruebas del contrato antes de implementación; modelos compartidos ya están en Phase 2;
handlers/queries antes de mapear sus rutas; backend/transporte antes de integrar páginas;
ejecutar el checkpoint antes de declarar historia completada. Las tareas de evidencia
solo se marcan cuando contienen resultados reales. No hacer commit/push salvo solicitud.

### Ejemplos de paralelismo por historia

| Historia | Oleada independiente | Prerequisito / restricciones |
|----------|----------------------|-----------------------------|
| US1 | T010 + T011 + T012; después T013 + T014 | Phase 2 terminada; pruebas en archivos distintos y commands/queries separados. T016 puede avanzar con transporte simulado; T017 espera API/transporte. |
| US2 | T020 + T021 + T022; después de T025, T026/T027 pueden avanzar mientras T028 prepara consultas | Checkpoint US1; T028 depende de T025 y no modifica comandos/worker. T024 y T026 serializados por archivo común; T027 espera T026. |
| US3 | T033 + T034; después T035 junto a T036 | US1 listo; T037 requiere endpoints de US2 y T036. T038 integra rutas después de T037. |
| US4 | T040 + T041 + T042; T043 + T044 + T045 | US2/US3 listos y pruebas escritas; handlers, página/API y core/operations en archivos distintos. |
| US5 | T049 + T050 + T051 + T052 | US1–US4 listos; bases independientes del fixture y archivos de pruebas distintos; evitar pruebas de carga paralelas a medición de rendimiento. |

Estas oportunidades no requieren delegación automática; también se puede ejecutar todo
secuencialmente. En archivos comunes, coordinar el diff con B, especialmente las extensiones
HU-05 de mutationPermission/route y la cadena de migraciones.

## Implementation Strategy

### MVP primero

T001–T019: borrador completo y reabrible, con referencias/actor/permiso/auditoría/replay/
ETag/rollback y primeras pruebas INT. Validar US1 independientemente antes de añadir correo.
No incluir envío parcial sin persistencia/recuperación en el MVP.

### Entregas incrementales

1. US1: preparar/corregir y consultar borrador, sin mercancía.
2. US2: revisión y correo durables con garantías técnicas desde el primer envío.
3. US3: consulta/seguimiento completos; contrato de recepción listo para HU-01.
4. US4: retry conocido y recuperación ampliada, misma entrega histórica.
5. US5: verificación transversal completa y evidencia constitucional.
6. Phase 8: rendimiento/regresión/paquete de revisión/cierre técnico, conservando HU-01/H5.

## Matriz de cobertura y condiciones de aceptación

| Requisito / escenario | Tareas que implementan o acreditan |
|----------------------|-----------------------------------|
| FR-001/002/004/005; US1.1–3/7; Q07-01/02 | T004, T010, T012–T019. |
| FR-003/006/007/008; US1.4–6; Q07-07/08 | T004, T007, T011, T013, T019; envío T020/T025/T031. |
| FR-009/010; US2.1/4/5/7; Q07-03 | T020, T022–T025, T029–T032. |
| FR-011/012/013; US2.2/3/6 | T005/006, T020/021, T024–T032, T050/T052. |
| FR-014/015/016/017; US4.1–6; Q07-04/09 | T021, T026–T028, T040–T048. |
| FR-018/019/020/021; US3.1–4/6; Q07-11/12 | T014, T028, T033–T039, T058; SC-006 real en HU-01. |
| FR-022; US3.5; Q07-01/08 | T010/011, T019, T031, T033/T039/T050. |
| FR-023/024; US5.1/6; Q07-10 | T006/009, permisos de cada endpoint/comando, T024/T049/T050/T052–T054. |
| FR-025/026/027/028; US5.2–5; Q07-05/06/10 | T010/011, T013–T017, T020/021, T024–T027, T040–T047, T050/T051/T054. |
| FR-029/030; Q07-02/03/05/11 | T012, T016–T018, T022/T029/T032, T034/T037–T039, T042/T044–T046/T053. |
| INT-HU07-01 | T007/T011/T013/T019 y verificación T051/T052/T054/T060; crear/retirar referencia real y ambos órdenes. |
| INT-HU07-02 | T011/T019, T020/T025/T031 y verificación T054/T060; proveedor y primer envío/desactivación reales. |
| SC-001/002/003/004/005 | Checkpoints US1–US5, T056/T060; no deducirlos solo de build. |
| SC-006 / V-02/V-06 | Contrato/lector T036/T058; escritores reales/aceptación en HU-01. |
| SC-007 | T055/T056 con datos/carga representativos. |
| SC-008 / H5-03; V-14 / H5-02 | Preparación T048/T057; ejecución y aprobación en backlog H5. |

**Resumen de descomposición**: 60 tareas; Setup 3, Foundational 6, US1 10, US2 13,
US3 7, US4 9, US5 6 y cierre transversal 6. Solo se marcan al ejecutar y verificar su evidencia.

## Phase 9: Convergence

- [X] T061 Suspender las consultas automáticas de detalle y envío en pestañas ocultas, reanudarlas al volver a una vista visible si el estado sigue pendiente y verificar que no se lanzan consultas duplicadas, según T037, FR-014 y el contrato UI (partial). [Evidencia](validation/integration.md).
