# Modelo de datos HU-07

**Fecha**: 2026-11-20 · Esquema aditivo generado y verificado en SQL aislado mediante
`20261120111422_HU07PurchasingOutbox`; escritores de borrador verificados en US1.
Correo implementado y verificado en [US2](validation/us2.md)/[US4](validation/us4.md);
consumidor HU-01 pendiente. [Evidencia de persistencia](validation/foundation.md).
Fuentes: [spec](spec.md), [investigación](research.md),
[constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md) y
[modelo común](../../.specify/memory/project/data-model.md#compras-y-recepciones--persona-a--hu-07-hu-01).
Las siguientes entidades concretan ese modelo; no duplican Article, Supplier u OperationResult.

## Relaciones

```text
Supplier 1 ── * PurchaseOrder 1 ── * PurchaseOrderLine * ── 1 Article
                       │
                       ├── 0..1 PurchaseOrderDelivery 1 ── 1 OutboxMessage
                       │                      └── * PurchaseDeliveryEvent
                       └── * PurchaseOrderAudit
Identity / OperationResult ── referencias restrictivas de autoría/autorización/evidencia
HU-01 ReceiptLine ── PurchaseOrderLine (futura, no creada por HU-07)
```

Todas las FK son restrictivas, sin cascadas de borrado histórico. HU-07 no ofrece DELETE
de pedido. La retirada física de una línea solo ocurre en Draft/None y dentro de edición
auditada; el registro de auditoría no tiene FK a esa línea ni a su artículo retirado.

## PurchaseOrder

| Campo | Tipo / restricciones |
|-------|----------------------|
| Id | Guid PK, generado por aplicación al iniciar la intención de alta en servidor. |
| Number | nvarchar(32), único; secuencia SQL BIGINT, `PO-00000001`, sin reinicio ni obligación de continuidad. |
| SupplierId | Guid FK Suppliers; obligatorio. |
| SupplierSnapshot | JSON válido: id, code(64), name(200), email(256 nullable), taxIdentifier(64 nullable), schemaVersion=1. |
| Currency | char(3), ISO 4217, configuración única capturada en alta; no editable por HTTP. |
| ExpectedDeliveryDate | date, obligatorio, puede estar en pasado. |
| State | Draft / Sent / PartiallyReceived / Received; CHECK. |
| SendStatus | None / Pending / Accepted / Failed; CHECK. |
| CreatedBy / CreatedAt | FK Guid a Identity / datetimeoffset UTC del servidor. |
| SentAt | datetimeoffset UTC nullable; instante de aceptación SMTP conocido y persistido. |
| RevisionStamp / Version | Guid interno de toque del agregado / rowversion SQL. |

La primera captura y cada reasignación explícita de proveedor fijan SupplierSnapshot.
Corregir cantidad, fecha o contacto del maestro no lo refresca. El destinatario vigente
solo aparece en contexto/revisión; la Delivery guarda el utilizado finalmente.

Invariantes del agregado:

- ≥1 línea; IDs y posiciones de líneas únicos dentro del pedido. No unicidad por ArticleId.
- Solo Draft/None editable. Draft con Pending/Failed queda congelado.
- None/Pending/Failed implican Draft y SentAt null. Accepted implica SentAt no nulo y
  State Sent/PartiallyReceived/Received. No hay recibido positivo en Draft.
- Al cambiar líneas, estado de correo visible o acumulados se modifica RevisionStamp,
  aunque no cambien otros escalares de cabecera; Version cubre todo el detalle propio.
- Líneas, cabecera y entrega se leen en una transacción SNAPSHOT. Los campos actuales de
  maestros y el número de intentos no pertenecen a ese DTO con ETag.

## PurchaseOrderLine

| Campo | Tipo / restricciones |
|-------|----------------------|
| Id | Guid PK estable, recibido del cliente, no vacío; solo puede pertenecer a un pedido. |
| PurchaseOrderId | Guid FK PurchaseOrders; índice (PurchaseOrderId, Position) único. |
| Position | int positivo, orden del array guardado; no identidad de línea. |
| ArticleId | Guid FK Articles; índice para lector de referencias. |
| ArticleSnapshot | JSON válido: id, sku(64), name(200), baseUnit(32), allowsFractional, tracksLot, schemaVersion=1. |
| OrderedQuantity | decimal(18,3), CHECK >0. |
| ReceivedQuantity | decimal(18,3), default 0; CHECK ≥0 y ≤OrderedQuantity. |
| UnitPrice | decimal(18,2), CHECK ≥0. |
| ExpectedDeliveryDate | date NOT NULL, hereda la general si se omite al guardar. |

PendingQuantity es proyección `OrderedQuantity - ReceivedQuantity`, no otro contador.
Cantidad/precio se validan sin redondeo silencioso; números fuera de decimal(18,s) se
rechazan. Un artículo entero exige parte fraccionaria cero. Unidad y lotes proceden del
artículo bajo bloqueo y luego de su instantánea protegida; no hay lotes físicos en compra.
Se permite repetir ArticleId con condiciones distintas. Cambiar artículo requiere nueva
identidad de línea; las IDs retenidas no se pueden reutilizar para otra referencia.

Edición sobre maestros inactivos: puede conservar referencias ya asignadas y retirarlas;
no incorporar proveedor/artículo inactivo como nueva asignación. Corregir condiciones
de una referencia retenida no cambia su identidad ni sus instantáneas.
La primera solicitud de envío exige todos sus maestros activos. Familia inactiva no
invalida por sí sola el artículo activo.

### INT-HU07-01 y primera referencia

PurchasingArticleUsageContributor declara PurchaseOrderLine en IArticleUsageContributor
e IArticleFirstReferenceContributor. Consulta Any real sobre sus FK y comparte contexto.
Es referencia documental, no historial de stock. El escritor llama ArticleReferenceTracker
antes del commit bajo bloqueo de artículo. La marca IsDefinitionLocked es monotónica.
Retirar la última línea permite reevaluar borrado por referencias/historial reales, pero
nunca devuelve la definición a editable. El borrado de Article con línea viva se rechaza.

## PurchaseOrderDelivery — compromiso inmutable

| Campo | Tipo / restricciones |
|-------|----------------------|
| Id / PurchaseOrderId | Guid PK / FK único: una sola entrega por pedido en este alcance. |
| RequestedBy / RequestOperationId | FK Identity / OperationResults originales. |
| RequiredPermission | `Purchasing.Send`, validado por servidor. |
| CreatedAt | datetimeoffset UTC. |
| RecipientEmail / SenderEmail | nvarchar(256), un buzón validado, sin CR/LF. |
| MessageId | nvarchar(255), único, generado una vez, independiente de Idempotency-Key. |
| DocumentSnapshot | JSON válido de pedido y líneas revisados, sin datos vivos derivados. |
| DocumentBytes / MimeBytes | varbinary(max), HTML UTF-8 y mensaje MIME completo originales. |
| DocumentName / MediaType | nvarchar(128) / nvarchar(100); `pedido-{number}.html`, `text/html; charset=utf-8`. |
| DocumentHash / MessageHash | SHA-256 binario(32). |
| TemplateVersion / ReviewedToken | versión de renderizado / digest de revisión. |

Snapshots/MIME no contienen credenciales SMTP, cookies ni tokens de sesión. El rol
inventory_runtime no puede UPDATE/DELETE de Delivery. No se re-renderiza al reintentar.
Un endpoint autenticado sirve DocumentBytes como adjunto, no ejecuta HTML en la aplicación.

## OutboxMessage — coordinación mutable

Una fila Guid Id, Type=PurchaseOrderEmail, DocumentId FK a PurchaseOrder y DeliveryId FK
único a Delivery. Su payload efectivo es la Delivery inmutable, no otra copia actualizable.

| Campos | Regla |
|--------|-------|
| State | Pending / Processing / Sent / Failed. Pending/Processing proyectan SendStatus Pending. |
| ActiveAuthorizationOperationId / AuthorizedBy | FK al último envío/reintento humano confirmado / su actor; inicialmente los originales. |
| Cycle / CycleAttempts / Attempts | ciclo desde 1, claims del ciclo 0..5, claims históricos ≥CycleAttempts; nunca borrar contador histórico. |
| NextAttemptAt | UTC SQL, elegibilidad de Pending. |
| LeaseOwner / LeaseGeneration / LeaseUntil | instancia UUID / bigint monotónico / UTC SQL; presentes mientras Processing. |
| CurrentAttemptId / FinishOperationId | UUID generados al claim, estables para su finalización. |
| LastErrorCode / LastErrorSummary | código público acotado(64) / texto saneado(500), sin secretos/trazas. |
| AcceptanceUncertain | hubo intento de aceptación no comprobable; Failed no lo borra. |
| CreatedAt / SentAt / Version | UTC / aceptación opcional / rowversion. |

Índice de candidatos por (State, NextAttemptAt) y (State, LeaseUntil), sin asumir propiedad
al leerlo. CHECK de contadores/generación/estado y coherencia de lease. Un retry manual
incrementa Cycle, reinicia solo CycleAttempts, conserva Attempts, incertidumbre e historial,
pone Pending y cambia la autorización activa. No modifica Delivery.

## PurchaseOrderAudit y PurchaseDeliveryEvent — append-only

PurchaseOrderAudit: Id, PurchaseOrderId FK, Action (Create/Update/RequestSend/RetrySend),
ActorId FK Identity, OperationId FK OperationResults, RecordedAt UTC, Before/After JSON
válido según acción. Incluye IDs de líneas retiradas sin FK a estas ni a Article/Supplier.

PurchaseDeliveryEvent: Id, DeliveryId FK, AttemptId opcional, LeaseGeneration opcional,
Kind (Requested/Retried/Claimed/LeaseExpired/Accepted/TransientFailure/PermanentFailure/
Uncertain/LateObservation), ActorId FK, AuthorizationOperationId FK, OperationId FK,
ExecutionKind (Human/Worker), WorkerInstanceId opcional, RecordedAt SQL UTC, ObservedAt
UTC opcional y Outcome JSON saneado. Cada inicio y desenlace es una inserción distinta;
no se actualiza un evento Claimed al finalizar. Unicidad por (OperationId, Kind, DeliveryId).
inventory_runtime tiene DENY UPDATE/DELETE sobre ambas tablas y Delivery, como sobre
OperationResults; se comprueba con la conexión real de ejecución.

## Transiciones y propiedad de los escritores

| Hecho | Compra / correo | Escritor |
|-------|-----------------|----------|
| Alta y edición | Draft / None; recibido 0 | HU-07 HTTP. |
| Primera solicitud conservada | Draft / Pending; Delivery única | HU-07 HTTP. |
| Claim/reprogramación | Draft / Pending | HU-07 worker; estado técnico separado. |
| Error terminal/presupuesto agotado | Draft / Failed | HU-07 worker. |
| Retry autorizado | Draft / Pending, misma Delivery | HU-07 HTTP. |
| Aceptación SMTP persistida | Sent / Accepted | HU-07 worker, solo una transición efectiva. |
| Recepciones confirmadas/correcciones | Sent/PartiallyReceived/Received según acumulados; Accepted | HU-01, junto a stock y documentos. |

Una finalización repetida no devuelve un pedido PartiallyReceived/Received a Sent.
Received/PartiallyReceived pueden volver al estado derivado de acumulados corregidos
solo mediante corrección autorizada HU-01. HU-07 no expone setters de recibido/estado.

## Persistencia y evolución

- Configuraciones por entidad, mismo InventoryDbContext y cadena de migraciones. Generar
  migración aditiva `HU07PurchasingOutbox` sobre el snapshot integrado real, que ya incluye
  HU02 y HU08MovementCounterparty; si cambia antes de compartir, regenerar solo la local.
- Índices de listado (CreatedAt, Number, Id), SupplierId/State y Number único; orden HTTP
  canónico por Number/Id. Filtros y count dentro del mismo snapshot.
  Tras el perfil SC-007 se añade (State, CreatedAt, Number, Id) mediante la migración
  aditiva `20261120194234_HU07PurchasingQueryIndexes`, sin alterar la migración de tablas.
  El selector compartido incorpora (IsActive, Sku, Id) con cobertura de campos de listado
  mediante `20261121025020_HU07ArticleLookupIndex`; no crea otra entidad Article.
- Para obtener rowversion en una respuesta puede guardarse primero cabecera/líneas/marca
  dentro de la transacción. No agregar todavía entidades con FK al OperationResult no
  insertado. Después construir respuesta con ETag, añadir evidencia/Delivery/outbox y
  dejar al ejecutor insertar resultado y efectuar SaveChanges final. Nunca commit intermedio.
- SQL CHECK cubre precisión representable/rangos/estados/JSON y FK; las invariantes entre
  filas (líneas no vacías, snapshots, suma recibida) se revalidan en handlers bajo locks.
- Datos existentes, permisos y migraciones compartidas se preservan. Aplicar desde una
  base vacía y una base con compras ausentes pero maestros/stock/HU02 previos; comprobar
  modelo/snapshot y DENY. Revisión A+B pendiente antes de integrar.
