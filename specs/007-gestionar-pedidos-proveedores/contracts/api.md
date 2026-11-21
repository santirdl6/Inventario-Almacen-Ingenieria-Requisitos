# Contrato HTTP HU-07

**Fecha**: 2026-11-20 · Contrato bajo `/api/v1`, JSON UTF-8. Implementados en el corte US1:
alta/edición/detalle/contexto/settings y, en US2, preview/send initial/progreso/documento.
Listado y retry humano implementados en US3/US4. Evidencias [US1](../validation/us1.md),
[US2](../validation/us2.md), [US3](../validation/us3.md) y [US4](../validation/us4.md);
[revisión compartida](../validation/shared-review.md) confirmada por el usuario.
Base: [spec](../spec.md), [modelo local](../data-model.md),
[constitución](../../../.specify/memory/constitution.md),
[plan global](../../../.specify/memory/project-plan.md),
[HTTP común](../../../.specify/memory/project/contracts/api.md#hu-07--a-pedidos-a-proveedores)
y [protocolo interno](integration.md).

## Convenciones

Sesión Identity; antiforgery en POST/PUT; Idempotency-Key UUID obligatorio en mutaciones.
Actor, número, estados, snapshots, moneda y acumulados no son entrada editable.
UUID canónico D minúsculas. Decimales como strings exactos, sin exponente; cantidades
decimal(18,3) y precios decimal(18,2), sin redondeo. Aceptar ceros fraccionarios redundantes
si el valor es representable (`"6"` = `"6.000"`), normalizar antes de huella. Fechas civiles
`YYYY-MM-DD`; from/to de creación son instantes ISO con offset, convertidos a UTC.
Rechazar propiedades no reconocidas y valores vacíos/no válidos con errors por campo.

POST alta devuelve 201 y Location al detalle; PUT 200. Ambos devuelven
`{ operationId, resource: PurchaseOrderDetail }` y ETag resultante. Se consulta/reproduce
el resultado común antes de cargar maestros/documento o comprobar If-Match.
If-Match fuerte obligatorio para PUT/send; sin él 428, obsoleto 412. No aceptar `*` ni weak.
Todas las respuestas de compra llevan Cache-Control no-store.

## Recursos y permisos

| Método y ruta | Permiso | Respuesta |
|---------------|---------|-----------|
| POST /purchase-orders | Purchasing.Manage | 201, borrador completo. |
| GET /purchase-orders | Inventory.Read | 200, listado paginado. |
| GET /purchase-orders/settings | Inventory.Read | 200, moneda configurada para preparar un pedido nuevo, sin ETag. |
| GET /purchase-orders/{id} | Inventory.Read | 200, detalle propio con ETag. |
| PUT /purchase-orders/{id} | Purchasing.Manage | 200, reemplazo de contenido editable. |
| GET /purchase-orders/{id}/context | Inventory.Read | 200, maestros/capacidades actuales, sin ETag. |
| GET /purchase-orders/{id}/send-preview | Purchasing.Send | 200, revisión exacta de primera solicitud, sin ETag. |
| POST /purchase-orders/{id}/send | Purchasing.Send | 202, primera solicitud o retry explícito. |
| GET /purchase-orders/{id}/delivery | Inventory.Read | 200, progreso actual, sin ETag. |
| GET /purchase-orders/{id}/delivery/document | Inventory.Read | 200, HTML adjunto original; 404 si no hay entrega. |

Los permisos de escritura no sustituyen la lectura; el rol Comprador existente combina
Inventory.Read/Purchasing.Manage/Purchasing.Send. La API autoriza cada ruta por su permiso.
No se añaden roles ni se otorgan compras implícitas al Administrador.

## Crear y editar

GET /purchase-orders/settings devuelve `{ currency }` desde la misma configuración
Purchasing:Currency que usa el alta. Permite mostrar la moneda exacta antes de escribir
precios, sin asumir EUR en el navegador; un pedido existente conserva su Currency histórica.
No admite cambios de configuración y comparte Cache-Control no-store.

```json
{
  "supplierId": "10000000-0000-0000-0000-000000000001",
  "expectedDeliveryDate": "2026-12-01",
  "lines": [
    {
      "id": "20000000-0000-0000-0000-000000000001",
      "articleId": "30000000-0000-0000-0000-000000000001",
      "quantity": "10.000",
      "unitPrice": "12.50",
      "expectedDeliveryDate": null
    }
  ]
}
```

El mismo esquema vale para POST/PUT. Al menos una línea; UUID de línea obligatorio y
único. Fecha de línea null/omitida hereda la general al guardar. El array indica el orden;
la normalización mantiene ese orden y materializa fechas heredadas antes de calcular la
huella. Crear usa UUID nuevos; editar conserva IDs retenidas, agrega IDs nuevas y retira
las omitidas. Una ID no puede pertenecer a otro pedido ni cambiar de ArticleId. Un artículo
puede figurar en varias líneas. Cantidad positiva, precio ≥0, fecha prevista pasada válida.

POST exige todos los maestros activos; PUT exige activos para nuevas asignaciones.
Referencias inactivas retenidas pueden retirarse o corregirse sin nuevas asignaciones.
Proveedor activo sin correo permite guardar con aviso en contexto.
Los campos recibidos/pendientes no se aceptan ni siquiera con valor cero.

## Lectura y contexto

PurchaseOrderDetail contiene `id, number, supplierId, supplierSnapshot, currency,
expectedDeliveryDate, state, sendStatus, deliveryId|null, createdBy, createdAt, sentAt|null,
lines[]`. Cada línea: `id, position, articleId, articleSnapshot, orderedQuantity,
receivedQuantity, pendingQuantity, unitPrice, expectedDeliveryDate`.
Todos estos datos son propios/persistidos o derivados exclusivamente de ellos. Cada
variación representada avanza Version de cabecera, incluidas líneas y aceptación SMTP.

Context devuelve `purchaseOrderVersion` (mismo valor opaco del ETag), `supplier` actual
con `id, code, name, email, isActive, version`, `articles[]` con `id, isActive, version`
y `editBlockReasons, sendBlockReasons`. Orientativo; los permisos actuales y locks del
comando deciden. No sobrescribir snapshots con ese contexto. Si su purchaseOrderVersion
no coincide con detalle, volver a leer antes de habilitar acciones.

Listado: `q` opcional busca número normalizado por prefijo; `supplierId`, `state`, `from`
inclusivo, `to` exclusivo; validar from < to. `page` desde 1, pageSize 25 por defecto,
máximo 100. Orden Number ascendente, Id ordinal de desempate. Devuelve
`{ items, page, pageSize, totalCount }`; item: identidad/número, proveedor histórico,
fecha creación/previsión, state, sendStatus. Count y filas en SNAPSHOT. Página fuera de
rango devuelve items vacío con totalCount real, sin fingir un error de consulta; UI ofrece
primera/última página válida. Un fallo devuelve ProblemDetails, nunca un 200 vacío.

## Revisión y envío inicial

GET send-preview solo para Draft/None. Lee pedido y maestros en SNAPSHOT y devuelve:

- `purchaseOrderVersion`, `recipientEmail`, `senderEmail`, `supplierVersion`.
- `document`: número, proveedor histórico, moneda y todas las líneas/precios/fechas;
  `documentHtml` escapado y autocontenido para presentar/descargar la revisión.
- `reviewToken`: digest SHA-256 Base64 del material canónico de R07-03.
- `blockReasons`: impedimentos actuales, con texto de corrección.

El remitente y configuración son del servidor, no datos editables de la petición.
Si falta correo o hay maestros inactivos, la revisión muestra impedimentos y no permite
confirmar; el POST vuelve a comprobarlos. El token se recalcula, no requiere tabla temporal.

POST send con If-Match del detalle revisado:

```json
{ "kind": "initial", "reviewToken": "digest-opaco" }
```

Tras replay, adquirir locks/releer; comprobar ETag, Draft/None, actividad y correo válido;
recalcular revisión. Un token que difiere devuelve 409 SendReviewChanged con indicación
de volver a revisar, sin crear Delivery ni emitir SMTP. Solo el contacto del maestro
validado es destinatario. Conservar documento y MIME, autorización, outbox, evidencia y
resultado conjuntamente. No se abre conexión SMTP en el endpoint.

## Reintento de error conocido

Misma ruta send, **nueva Idempotency-Key**, If-Match actual:

```json
{ "kind": "retry", "deliveryId": "40000000-0000-0000-0000-000000000001" }
```

Requiere Failed, Delivery correspondiente y permiso actual. No admite reviewToken,
destinatario ni contenido nuevos. Incrementa ciclo y reactiva el outbox original;
conserva documento, MessageId y contador histórico. No exige que maestros/contacto
actuales coincidan: ejecuta el compromiso congelado, no una nueva compra.
Pending/Accepted u otra Delivery se rechazan sin efectos. Dos retries con distinto
OperationId y mismo ETag no producen dos ciclos; solo uno puede pasar a Pending.

Ambas acciones devuelven 202 con ETag resultante y Location al recurso delivery:

```json
{
  "operationId": "50000000-0000-0000-0000-000000000001",
  "purchaseOrderId": "60000000-0000-0000-0000-000000000001",
  "deliveryId": "40000000-0000-0000-0000-000000000001",
  "state": "Draft",
  "sendStatus": "Pending"
}
```

Esta respuesta original permanece Pending aunque el worker haya avanzado. Tras recibirla
o recuperarla, consultar el estado vigente; no reescribir OperationResult.

## Progreso y documento

Delivery devuelve `purchaseOrderId, purchaseOrderVersion, deliveryId|null, messageId|null,
recipientEmail|null, senderEmail|null,
sendStatus, attempts, cycleAttempts, requestedAt|null, lastAttemptAt|null, nextAttemptAt|null,
acceptedAt|null, lastErrorCode|null, lastErrorSummary|null, acceptanceUncertain, canRetry`.
Sin solicitud: None, attempts=0 y campos de entrega null. canRetry indica Failed y
permiso Purchasing.Send de la sesión actual, sin sustituir la autorización del POST.
No exponer propietario/lease, credenciales, excepciones o respuestas SMTP crudas.
Los contactos congelados permiten revisar un fallo sin confundirlo con cambios actuales
del proveedor; no se obtienen del contexto vivo ni se aceptan como datos editables.

Pending puede estar en espera o ejecución; texto «Envío pendiente». Failed con
acceptanceUncertain advierte que podría existir una copia y que no se conoce rechazo
definitivo. Accepted significa «Aceptado por el servicio de correo», nunca lectura final.
Documento: Content-Disposition attachment con nombre seguro, Content-Type text/html;
charset=utf-8 y X-Content-Type-Options nosniff; bytes originales, sin HTML ejecutado inline
en la SPA. El recurso no acepta transformaciones ni contenido proporcionado por cliente.

## Huella y recuperación

Normalizar DTO sin consultar maestros: UUID, fechas y decimales; materializar herencia
de fechas, sin redondeo, arrays en orden. Hasher común ordena propiedades ordinalmente
y calcula SHA-256 de método/ruta/JSON canónicos; actor comparado aparte, If-Match excluido.
kind inicial/retry y reviewToken/deliveryId forman parte del cuerpo normalizado.

La recuperación incierta conserva método/ruta/cuerpo/clave/ETag originales. Repetir una
operación confirmada devuelve 201/200/202 y cabeceras originales antes de If-Match o estado
vigente. GET /operations/{operationId} exige permiso original; 404 es «todavía no observado».
Otra identidad/cuerpo/ruta produce IdempotencyConflict, no otra compra. Un 409 de identidad
no autoriza al navegador a descartar la referencia o reconstruir la petición.

## Errores particulares

| HTTP / code | Motivo / acción de UI |
|-------------|-----------------------|
| 400 InvalidRequest | JSON, UUID, cabeceras o tipos inválidos; antiforgery conserva código común. |
| 401 / 403 | Recuperar sesión / falta permiso; sin efectos. |
| 404 NotFound | Pedido/referencia solicitada no existe; distinguir de resultado aún no observado. |
| 409 SupplierInactive / ArticleInactive | Nueva asignación o primer envío incompatible; corregir/revisar maestros. |
| 409 PurchaseOrderNotEditable | Ya no Draft/None; conservar formulario y consultar estado. |
| 409 SendAlreadyRequested / DeliveryNotRetryable | Hay solicitud o no está Failed; consultar entrega existente. |
| 409 SendReviewChanged | Contacto/documento/configuración cambió desde revisión; revisar de nuevo. |
| 409 IdempotencyConflict | No reconstruir ni emitir intención alternativa para resolverla. |
| 412 VersionConflict / 428 PreconditionRequired | Comparar revisión actual por IDs de línea / falta If-Match. |
| 422 ValidationFailed | Campos o lines[index].campo inválidos; email ausente/no válido impide envío. |
| 503 LockTimeout / DependencyUnavailable | Aplicar Retry-After, comprobar operación antes de repetir. |

ProblemDetails y traceId según contrato común. Los errores no generan OperationResult
de éxito ni confirman cambios parciales. No hay endpoints para cancelar, borrar, editar
acumulados, cambiar destinatario congelado o reenviar una entrega Accepted.
