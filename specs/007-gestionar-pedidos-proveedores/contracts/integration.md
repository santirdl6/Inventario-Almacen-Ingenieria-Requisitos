# Integración y protocolo de entrega HU-07

**Fecha**: 2026-11-20 · [Revisión A+B](../validation/shared-review.md) confirmada por el usuario.
Base: [constitución](../../../.specify/memory/constitution.md),
[plan global](../../../.specify/memory/project-plan.md),
[protocolo común](../../../.specify/memory/project/contracts/stock-operations.md),
[modelo](../data-model.md), [HTTP](api.md) y
[backlog receptor](../../001-reparto-specs-inventario/integration-tasks.md).

## 1. Responsabilidades

HU-07 implementa pedido, lector de referencias, instantáneas, correo y seguimiento.
No crea Receipt, Reservation, StockMovement, StockEntry, Lot ni StockBalance. Alta/edición/
envío de compra no invocan StockCore y sus pruebas comparan stock/diario antes y después.
HU-01 implementa su escritor real de recibido y efectos de stock; H5 conserva restauración
y aceptación operativa. El nuevo escritor satisface INT-HU07-01/02 sin reabrir HU-08.

## 2. Ejecutor compartido y autoridad

Conservar la entrada HTTP de InventoryOperationExecutor: sesión, ICurrentActor y policy
actual, incluso antes de replay. Factorizar su núcleo privado de resiliencia/transacción
para la entrada interna tipada PurchaseDeliveryCommand (Claim/Finish/RecordLateObservation).
No registrar un ejecutor paralelo, ni exponer una ruta HTTP de estas acciones.

La entrada interna admite identificadores de entrega/claim/finalización y observación
saneada; no recibe ActorId, permiso elegido por llamador ni callback arbitrario. Claim
normaliza DeliveryId, AuthorizationOperationId, WorkerInstanceId, AttemptId y
FinishOperationId; Finish normaliza DeliveryId, AttemptId, LeaseGeneration, autorización
del claim y observación. El comando conserva esos valores en sus reintentos, sin resolver
una autorización distinta para formar otra huella. Resuelve:

1. Delivery inmutable y OutboxMessage correspondientes al pedido.
2. ActiveAuthorizationOperationId confirmado de RequestSend/RetrySend, método/ruta,
   Purchasing.Send, actor y resultado que identifica la misma Delivery.
3. Actor histórico existente (FK restrictiva); autorizado al conservar aquella intención.
4. Para Finish, AttemptId/generación y autorización capturada al reclamar, no datos de
   una autorización posterior. Validación decisoria de nuevo después de los locks.

El worker continúa esa autorización; no depende de sesión viva ni de permisos actuales
del solicitante. No hay fallback de ICurrentActor ni HttpContext artificial. Un retry
manual sí pasa por sesión/permiso actual. Evidencia Worker distingue ejecución automática
de decisión humana y enlaza operación/autorización/instancia.

Cada comando interno tiene UUID de operación, método lógico POST y cuerpo normalizado estable; usa el mismo
OperationResult, con recurso lógico no expuesto como endpoint
`/api/v1/internal/purchase-deliveries/{id}/{claim|finish|late-observation}` y permiso Purchasing.Send.
El prefijo respeta la canonización existente de OperationRequest, sin cambiar el hasher
ni registrar estas rutas: una petición HTTP a ellas sigue devolviendo 404.
Un resultado técnico 200 puede significar «fallo SMTP registrado» o «finalización obsoleta
descartada», nunca correo aceptado. Replay valida huella/actor/autoridad original; no debe
usar una autorización posterior para reinterpretar un comando antiguo.

## 3. Bloqueos y planificación

Formato UUID D minúsculas y comparación ordinal del núcleo común. Todos los locks son
Transaction en la misma base/DbPrincipal; timeout 5 s, hasta tres intentos SQL completos.

| Acción | Conjunto de recursos después de operation:{uuid} |
|--------|--------------------------------------------------|
| Crear pedido | documentos purchase-order nuevo + supplier elegido; después artículos distintos. |
| Editar pedido | documentos purchase-order + proveedores viejo/nuevo; después unión de artículos viejos/nuevos. |
| Envío inicial | documentos purchase-order + supplier; después todos sus artículos. |
| Retry manual | documentos purchase-order + purchase-order-delivery. No releer maestros para sustituir el compromiso. |
| Claim/finalizar | documentos purchase-order + purchase-order-delivery. No bloqueos de stock/maestros para el envío congelado. |

Se pasan conjuntos de documentos juntos a DocumentsAsync, que ordena tipos/IDs. No
adquirir supplier después de artículos. En el recurso canónico completo,
`document:purchase-order-delivery:{id}` precede a `document:purchase-order:{id}` porque
`-` precede a `:`; pedir ambos en una sola llamada evita invertir ese orden.
La adquisición se agrupa en un lote SQL por llamada de fase, ejecutando secuencialmente
la lista ya ordenada. Conserva Transaction/public/5 s por recurso y se detiene ante el
primer resultado negativo; el ejecutor revierte también los bloqueos parciales.
Las lecturas previas solo descubren candidatos/IDs. Releer estado decisorio después de
los bloqueos; si cambió el proveedor o aparece un recurso anterior no declarado, descartar
intento y replanificar dentro del presupuesto común. Ninguna decisión usa una entidad EF
rastreada antes de los locks sin recargarla. Para primera referencia, invocar Tracker
con OperationContext del intento; no resolverlo desde el scope HTTP exterior.

Con los locks retenidos: comprobar replay primero, ETag, editabilidad, maestros según
acción, definición y cantidades. Preparar todas las escrituras antes del commit; respetar
el orden de SaveChanges/FK a resultados descrito en [modelo](../data-model.md#persistencia-y-evolución).
El envío inicial no necesita lock Delivery antes de artículos porque aún no existe:
la exclusión por pedido y su índice único impiden otra entrega.

## 4. Worker: claim → SMTP → finish

### Claim persistido

Polling de candidatos Pending elegibles o Processing con lease vencido, sin conceder
propiedad al leer. Generar ClaimOperationId, AttemptId y FinishOperationId una vez antes
de la estrategia SQL; mismo cuerpo en sus reintentos. Adquirir operación/documentos,
revalidar autoridad y elegibilidad según SYSUTCDATETIME(). Si otro worker ya reclamó,
registrar no-op técnico, sin incrementar intentos.

Si expiró el intento anterior, insertar LeaseExpired con aceptación incierta y conservar
sus IDs; agotar ciclo al llegar a cinco claims. Si cabe otro: State Processing, incrementar
LeaseGeneration, Attempts y CycleAttempts, fijar owner/attempt/finish y LeaseUntil=ahora+120 s.
Insertar Claimed y resultado juntos. El tiempo SMTP no consume una transacción abierta.

Tras recuperar el resultado de claim, comprobar propiedad/generación/vigencia actual
antes de empezar SMTP: un replay puede devolver una concesión histórica. No se reutiliza
un AttemptId para una segunda llamada SMTP en el mismo proceso; no se reejecuta el bucle
de envío por una excepción SQL de finalización. Una caída con intento incompleto se
recupera únicamente por nueva concesión tras vencimiento.

### SMTP acotado

Leer los bytes MIME congelados y comprobar su hash; envío con una conexión MailKit propia
por intento, timeout/cancelación total 30 s para conectar/autenticar/transmitir. Un envío
concurrente por instancia; varias instancias se coordinan por lease. No hay renovación
de lease en esta primera entrega: 120 s deja margen respecto a 30 s; una pausa puede
vencerlo y debe respetar fencing al volver. Cancelar trabajo al detener la API, conservando
lo que esté persistido para recuperación posterior.

Persistir observación con fases: aceptado explícitamente / rechazo conocido / resultado
externo incierto. Error después de DATA sin respuesta final, timeout ambiguo o claim
abandonado marcan incertidumbre. Un fallo al desconectar tras SendAsync aceptado no anula
la aceptación. No consultar Mailpit como fuente de verdad en producción.

### Finalización persistida

Usar FinishOperationId del claim y observación congelada. Si SQL falla o commit queda
incierto, recuperar/repetir **esa finalización**, sin volver a SMTP. Dentro de operación +
documentos comprobar AttemptId/owner/generación/vigencia y autorización del claim.

- Aceptado vigente: outbox Sent, compra Accepted/Sent y SentAt, toque de versión,
  evento Accepted y resultado juntos. Nunca degradar recepción ya confirmada.
- Fallo transitorio/ambiguo con presupuesto: outbox Pending y NextAttemptAt según backoff,
  compra Draft/Pending; liberar lease, registrar incidente y evento.
- Rechazo permanente o presupuesto agotado: outbox Failed, compra Draft/Failed,
  liberar lease; conservar AcceptanceUncertain si procede. Retry disponible por HTTP.
- Generación/lease obsoleto: no cambiar proyección ni próximo intento. Registrar
  LateObservation con nueva operación propia si la observación tardía no estaba aún
  conservada; resultado técnico indica que no se aplicó. No afirmar Accepted localmente.

Finishes repetidos son replay, no nuevos eventos. Claims concurrentes y un retry durante
un intento vigente no pueden abrir dos ciclos. Fencing protege el estado SQL; no impide
que SMTP reciba una copia si un proceso estuvo pausado más allá del lease.

## 5. Presupuesto, configuración y reinicios

Defaults: polling 5 s, lease 120 s, SMTP 30 s, máximo cinco claims/ciclo, backoff tras
fallos de intentos 1–4 de 5/30/120/300 s. Rechazo permanente finaliza sin agotar cinco.
Reintentos SQL locales no incrementan attempts por replay. Reloj SQL para elegibilidad/
leases; TimeProvider/control del transporte en pruebas para no depender de largas esperas.

Opciones validadas al arrancar: Purchasing:Currency; Smtp:Host/Port/From/Security
(None local, StartTls obligatorio o SslOnConnect productivo), Username/Password opcionales
por entorno; PurchaseDelivery:Enabled (default true en ejecución ordinaria), PollSeconds,
LeaseSeconds, TimeoutSeconds y MaxAttempts (1–5). Intervalos/plazos positivos, sin downgrade
TLS automático ni certificados aceptados indiscriminadamente. Exigir lease mayor que
timeout + 30 s; límites inválidos
impiden iniciar el worker. Credenciales nunca en Delivery, auditoría ni logs.

No registrar worker en initialize-database/mantenimiento HU-08; en integración usar
transporte controlado/worker deshabilitado salvo pruebas específicas. Enabled=false pausa
entregas sin perderlas y permite la revisión previa tras restauración. Reinicio normal
reanuda outbox persistente; restauración de backup se hace con envío pausado hasta la
conciliación H5/V-14, por posibles correos externos posteriores al punto restaurado.

## 6. Integración receptora verificable

| ID | Entrega y prueba requerida en HU-07 |
|----|-------------------------------------|
| INT-HU07-01 | Contributor de PurchaseOrderLine, registro de primer uso y cobertura de modelo; escritores reales crean/retiran línea; marca no se desbloquea, FK impide borrado mientras vive referencia. Carreras referencia/edición y referencia/borrado en ambos órdenes con conexiones/barreras SQL independientes. Repetir guardado no duplica evidencia. |
| INT-HU07-02 | Pedidos/snapshots sobreviven a edición/desactivación/reactivación de Supplier; nuevas asignaciones y primer envío usan sus locks compartidos. Carreras alta/envío contra desactivación de proveedor/artículo en ambos órdenes; reactivación permite nuevas compras sin reescribir las previas. |

No completar esos checkboxes hasta tener evidencia. Las pruebas se ejecutan sobre API/
handlers reales y SQL Server; no lectores constantes ni marca preparada manualmente como
sustituto de escritor. Auditoría y Delivery inmutables se prueban con usuario SQL runtime.

## 7. Contrato entregado a HU-01 y revisión conjunta

HU-01 referencia IDs de PurchaseOrderLine, adquiere purchase-order y receipt antes de
ubicaciones/artículos, exige Accepted y artículo activo, revalida pendiente. En un solo
commit confirma Receipt/Allocation, actualiza ReceivedQuantity/State/RevisionStamp, aplica
StockCore y resultado común. Recibido nunca supera pedido; corrección deriva estado de
nuevo, sin editar hechos confirmados. HU-07 entrega lectura inmediata coherente; HU-01
acepta V-02 (6+4) y V-06 (dos de 6 contra 10) con sus escritores reales.

Tipos entregados: `Domain/Purchasing/PurchaseOrder` y `PurchaseOrderLine` con IDs estables;
el futuro handler HU-01 llama `PurchaseOrderLine.SetReceivedQuantity` después de validar
su recepción y `PurchaseOrder.DeriveReceiptState` dentro de ese mismo OperationContext.
El segundo recalcula estado y toca RevisionStamp/rowversion incluso cuando solo cambian
acumulados. No invoca SaveChanges ni StockCore por su cuenta. Consultas propias se proyectan
por `PurchaseOrderDetail.From`; proveedor/artículos actuales y progreso se mantienen fuera
del detalle protegido por ETag, con purchaseOrderVersion para combinar lecturas.

`PurchaseOrderQueryTests.Query_only_receipt_preparation_verifies_projection_not_a_real_receiving_workflow`
prepara 6/4 y 10/0 solo para acreditar proyección/versión/CHECK. No tiene Receipt ni efectos
de stock y **no completa SC-006/V-02/V-06**: esa aceptación sigue en el escritor real HU-01.

Antes de integrar, A+B revisan factoring del ejecutor/autoridad, recursos/huellas, permisos,
FK/migración/snapshot, semántica retry y evolución de ETag con HU-01. Consumidores afectados:
HU-08 (lectores/locks), H0/StockCore/HU-02 (regresión ejecutor/UI), HU-01 (acumulados) y
HU-06 (solo navegación a compras, sin compra automática). La base global enlaza estas
concreciones; no se cambian aritmética de stock ni obligaciones constitucionales.
