# Handoff H5 — Usabilidad y recuperación operativas de HU-07

**Fecha**: 2026-11-20 · **T057**: guion preparado. **SC-008/H5-03 y V-14/H5-02: pendientes de ejecución y aceptación por responsables reales**.

Base: [constitución III/V](../../../.specify/memory/constitution.md),
[plan global](../../../.specify/memory/project-plan.md), [spec SC-008](../spec.md),
[guía HU-07](../quickstart.md), [recuperación común](../../../.specify/memory/project/quickstart.md#8-restauración-y-apertura-operativa)
y [backlog H5](../../001-reparto-specs-inventario/integration-tasks.md).

## SC-008: ejercicio con al menos tres compradores

Responsables del proyecto/almacén convocan tres personas representativas del puesto,
con cuentas Comprador distintas y datos sintéticos en un entorno aislado. Registrar versión
del código, navegador, fecha, proveedor/artículos, pedido y observador. Usar SMTP de captura;
el observador prepara los fallos, sin explicar cómo resolver la tarea durante el recorrido.

Cada participante realiza, sin asistencia técnica:

1. Entrar desde el menú en Compras y preparar un pedido con proveedor, fecha y dos líneas.
   Identificar unidad y moneda; guardar y volver a abrir el mismo número.
2. Introducir cantidad cero o una fracción no admitida. Encontrar el error, corregirlo
   por teclado y comprobar que los demás campos siguen presentes. Corregir una línea
   guardada, sin confundir guardado con envío o recepción.
3. Abrir la revisión e identificar número, destinatario, artículos, cantidades, precios
   y fechas. Confirmar por teclado solo después de revisar; explicar «Envío pendiente».
4. Consultar un pedido pendiente/en error preparado por el observador. Localizar la
   incidencia y el documento original; distinguir aceptación SMTP de entrega/lectura final.
   Ante resultado HTTP incierto, localizar «Comprobar» antes de emitir otra intención.
5. Con un fallo conocido y SMTP restablecido por el observador, usar el reintento autorizado
   y comprobar el mismo pedido/documento. Filtrar el listado, paginar y volver al detalle.

Para el fallo SMTP puede reutilizarse el [guion aislado E2E](../quickstart.md#fallo-smtp-y-recuperación-e2e)
con presupuesto de un intento solo en ese entorno. La automatización acredita funcionamiento,
pero **no cuenta como participante ni como resultado de este ejercicio**.

### Acta que debe cumplimentarse

| Participante/rol | Fecha y versión | Preparar/reabrir | Corregir inválido | Revisar/teclado | Interpretar pendiente/error | Ayuda solicitada | Incidencias/resultado |
|---|---|---|---|---|---|---|---|
| Comprador 1 — pendiente | — | — | — | — | — | — | — |
| Comprador 2 — pendiente | — | — | — | — | — | — | — |
| Comprador 3 — pendiente | — | — | — | — | — | — | — |

Por incidencia registrar tarea, comportamiento observado, gravedad, pedido/operación,
corrección y repetición afectada. Aprobar SC-008 únicamente cuando los tres completen los
recorridos sin asistencia y sin errores de registro no detectados, con incidencias corregidas.
El acta requiere responsables identificados; no hay firmas ni aprobación registradas aquí.

## H5-02/V-14: restauración y conciliación del correo

La [restauración técnica](recovery-v14.md) verificó la parte de compras en instancias
aisladas. H5-02 conserva el ejercicio operativo y la aprobación de RPO/RTO para el
conjunto de módulos.

1. Antes de arrancar la API restaurada, fijar `PurchaseDelivery:Enabled=false`
   (`PURCHASE_DELIVERY_ENABLED=false` en Compose). Mantener la operativa suspendida en el
   alcance pendiente de conciliar y conservar evidencias externas del correo.
2. Restaurar full/cadena de logs en una instancia independiente. Medir RPO/RTO reales,
   ejecutar CHECKDB y conciliar documentos, diario, saldos y reservas según la guía común.
   Los objetivos propuestos ≤15 min/≤4 h requieren aprobación de responsables.
3. Para compras contrastar PurchaseOrder, Delivery, OutboxMessage, OperationResult y eventos
   con MessageId/documento/destinatario congelados y la evidencia SMTP posterior al punto
   restaurado. Un mensaje externo puede existir aunque la copia restaurada diga Pending.
4. Clasificar cada discrepancia y conservar decisión, responsable y evidencia. No editar
   tablas append-only ni regenerar documentos para ocultarla. Si hace falta una herramienta
   de conciliación no disponible, registrarla como trabajo de H5 antes de reabrir.
5. Verificar comprobantes físicos posteriores y su incorporación idempotente por los
   escritores reales de sus módulos. Solo después de la conciliación y autorización de
   reapertura reactivar el worker. Conservar la posibilidad de copia SMTP ante incertidumbre.

La [prueba de pausa/reanudación](us4.md) comprueba persistencia y continuidad del worker;
no sustituye este ensayo de backup/restauración. H5-02/H5-03 permanecen sin marcar en el backlog.
