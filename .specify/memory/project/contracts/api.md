# Contrato HTTP común

**Versión**: `/api/v1` · **Formato**: JSON UTF-8 · **Fecha**: 2026-10-25.
Contrato inicial para desarrollar las specs; las rutas y esquemas OpenAPI de implementación
deberán ajustarse a él. Cambios compartidos requieren revisión A+B.
**Base compartida**: [plan global](../../project-plan.md), [modelo](../data-model.md).

## Tipos, respuestas y concurrencia

- IDs UUID en cadenas; fechas ISO 8601 con zona, persistidas en UTC.
- Cantidades y precios como cadenas decimales exactas, por ejemplo `"6.000"` y `"12.50"`.
  No se envían resultados de aritmética binaria del navegador como cantidades autoritativas.
- Listas: `page` desde 1, `pageSize` por defecto 25, máximo 100; orden estable por código/ID.
  Respuesta `{ items, page, pageSize, totalCount }`. Filtros vacíos no significan valor cero.
- GET de stock devuelve `asOf`, `totals` y buckets paginados. Totals agrega todo el filtro,
  no solo la página; cada bucket incluye artículo, ubicación, lote, físico, reservado y disponible.
- GET de un recurso editable devuelve ETag de su rowversion. Actualizar, enviar o confirmar
  requiere If-Match; sin él, 428. Ante versión obsoleta, 412 sin efectos parciales.
- Toda mutación de negocio lleva `Idempotency-Key` UUID. La misma clave, ruta, actor y
  cuerpo normalizado devuelve el mismo resultado; contenido distinto, 409. Los replays
  autorizados se resuelven antes de evaluar un If-Match que ya quedó obsoleto por el éxito.
- OperationId coincide con Idempotency-Key. El resultado confirmado conserva también
  ETag/Location y el permiso original; el replay se resuelve antes de comprobar si el
  recurso sigue existiendo, sin ejecutar de nuevo un borrado ni reconstruir su respuesta.
- La huella común es SHA-256 de método en mayúsculas, ruta canónica y JSON normalizado
  del DTO, separados por salto de línea; cada contrato concreta el orden y normalización
  de sus campos. No incluye If-Match, cookies ni tokens; el actor se compara por separado.
- POST de alta: 201 y Location; confirmación: 200 con documento, movimiento(s) y operationId;
  envío de compra: 202 con intención persistida, no afirmación de correo entregado.
- No hay DELETE/PUT/PATCH de saldos ni de movimientos confirmados.
- Datos comunes de confirmación: `occurredAt`, `reason`, referencia de soporte o explicación
  de no aplicabilidad. Actor y recordedAt siempre los asigna el servidor.
  La contraparte se indica con `counterpartyReference` o `counterpartyNotApplicableReason`;
  el núcleo exige evidencia para cada hecho nuevo. La consulta del diario conserva el motivo
  y distingue información histórica no registrada; protocolo de replay en stock-operations.

## Sesión, permisos y errores

Mismo origen, cookie de sesión HttpOnly/SameSite=Lax y antiforgery para todas las mutaciones,
incluido inicio/cierre de sesión. Fuera de Development las cookies son Secure; el HTTP
local de Compose usa SameAsRequest. Angular envía X-XSRF-TOKEN a partir de XSRF-TOKEN.
La cookie interna `Inventario.Antiforgery` es HttpOnly y distinta del token de petición
legible. Los dos tokens deben pertenecer al mismo cliente y a la identidad actual.

| Método y ruta | Entrada / salida |
|---------------|-----------------|
| GET /auth/csrf | Disponible anónimo o autenticado; emite cookie interna y XSRF-TOKEN y devuelve `{ requestToken }` con 200. |
| POST /auth/login | Anónimo y antiforgery; email, password; 200 con cookie de sesión y nuevo `{ requestToken }`. 401 no revela si existe la cuenta. |
| POST /auth/logout | Sesión autenticada y antiforgery; cierra cookie; 204. |
| GET /auth/me | Sesión autenticada; `{ id, email, roles, permissions }`; 401 si no hay sesión. |

Las rutas anteriores se prefijan con /api/v1. No exigen clave idempotente porque no
son mutaciones de documentos; sí protección antiforgery en login/logout. Solicitudes API
sin sesión devuelven 401 JSON, nunca una redirección HTML a login. Las cuentas iniciales
se crean por configuración del entorno durante la inicialización; no hay alta pública.

| Actor | Permisos mínimos |
|-------|------------------|
| Operario | Inventory.Read, Locations.Manage, Receiving.Record, Shipping.Record, Counts.Record. |
| ResponsableAlmacen | Inventory.Read, Reservations.Manage, Inventory.Adjust, Counts.Record, StockPolicy.Manage, Alerts.Read. |
| Comprador | Purchasing.Manage, Purchasing.Send, Inventory.Read, Alerts.Read. |
| GestorMaestros | MasterData.Manage, Inventory.Read. |
| Administrador | Users.Manage; sin permisos implícitos de inventario. La API de gestión de cuentas se incorpora posteriormente. |

Las cuentas pueden combinar roles. Las policies del servidor usan claims `permission`
derivados de roles; los guards del frontend no son una barrera de autorización. Identity
comprueba contraseña, bloqueo temporal y unicidad de correo. La autorización de ajustes y
las acciones de un operario quedan diferenciadas y registradas. La base H0 siembra los
roles anteriores y crea una cuenta local de Administrador desde variables exclusivas del
servicio inicializador. Asignar roles a nuevos usuarios queda para las futuras specs.

Errores `application/problem+json`: type, title, status, detail legible en español,
code, traceId, operationId cuando aplique y errors por campo. No contienen trazas ni SQL.

| Estado | Casos y códigos |
|--------|-----------------|
| 400 | JSON o identificador inválido; antiforgery rechazado. |
| 401 / 403 | Sin sesión / permiso insuficiente. |
| 404 | Recurso inexistente o resultado de operación aún no observado. |
| 409 | InsufficientStock, ReservedStockConflict, CountScopeFrozen, DocumentAlreadyConfirmed, OverReceipt, ArticleInactive, HistoryPreventsDeletion, IdempotencyConflict. |
| 412 / 428 | Versión obsoleta / falta If-Match. |
| 422 | Campos de negocio inválidos, cantidades/unidades o suma de asignaciones incorrectas. |
| 503 | LockTimeout o indisponibilidad tras reintentos; Retry-After. Comprobar operationId antes de repetir. |

Para esta base H0, credenciales incorrectas, cuenta bloqueada o no confirmada devuelven
`InvalidCredentials` con 401 genérico para no revelar si existe la cuenta. Cinco fallos
bloquean nuevos accesos durante 15 minutos. Las contraseñas exigen al menos 12 caracteres,
mayúsculas, minúsculas, número y símbolo. Antiforgery devuelve `AntiforgeryValidationFailed`
con 400; campos vacíos/inválidos, `ValidationFailed` con 422. El healthcheck
no requiere sesión y solo revela `healthy/database ready` o `unhealthy/database unavailable`.

### Estado de implementación del borrador

Implementados: health, csrf, login, me y logout; desde el corte HU-08 del 2026-11-17,
GET de resultados autorizados, GET/POST/PUT de familias y artículos y contexto auxiliar
de Article. El mantenimiento de transición solo se registra en su ventana explícita.
OpenAPI generado en
`/openapi/v1.json` solo en Development, con los endpoints realmente implementados.
Las demás rutas de negocio de las secciones siguientes siguen siendo el borrador compartido.

`/auth/me`, `/auth/logout` y los futuros endpoints requieren sesión por defecto; únicamente
health/csrf/login son anónimos. Ningún grupo de sesión completo lleva AllowAnonymous.
La cookie se llama `Inventario.Session`, no es persistente en el navegador y protege un
ticket de 8 horas con renovación deslizante. Login devuelve un token antiforgery vinculado
al usuario; logout elimina la cookie de sesión y la cookie de petición. Los endpoints de
sesión envían `Cache-Control: no-store`. No se emiten JWT ni se admiten tokens en localStorage.

La base común, familias y artículos ya implementan ETag, idempotencia, errores y paginación del
catálogo. Los documentos/operaciones de stock incorporarán esas garantías en sus cortes;
no se presentan como implementados por la existencia del catálogo.

## Catálogo de recursos

Todas las rutas siguientes son relativas a /api/v1 y requieren sesión.

### HU-08 — A: maestros

| Métodos y ruta | Datos / comportamiento |
|----------------|------------------------|
| GET, POST /families; GET, PUT /families/{id} | code, name, isActive; lectura Inventory.Read, mutación MasterData.Manage. |
| GET, POST /suppliers; GET, PUT /suppliers/{id} | code, name, email, taxIdentifier, isActive. |
| GET, POST /articles; GET, PUT /articles/{id} | sku, name, familyId, baseUnit, allowsFractional, tracksLot, isActive. |
| DELETE /articles/{id} | Solo sin referencias ni historial; con movimientos devuelve HistoryPreventsDeletion. Desactivar mediante PUT conserva datos. |
| GET /articles/{id}/movements | Historial paginado, filtros from/to/actorId/locationId/lotId. Incluye artículo inactivo. |

Familias y proveedores usan la misma política de lectura/escritura. No se ofrecen borrados
de esos recursos en la base inicial. La unidad base, el seguimiento por lote y la admisión
de fracciones solo pueden cambiarse mientras el artículo no tenga documentos, reservas ni
movimientos asociados y no tenga activada la marca persistente de primer uso. Una referencia
en un documento en borrador también fija esa marca; retirar la línea no desbloquea la definición.
La marca sola no sustituye las comprobaciones de referencias e historial para permitir borrar.

Concreciones HTTP de HU-08: PUT y DELETE requieren If-Match fuerte de rowversion, además
de clave idempotente; DELETE exitoso devuelve 204. La consulta auxiliar del artículo informa
capacidades y motivo de bloqueo, sin sustituir la revalidación al guardar. Los DTO, filtros,
resultados y errores particulares se mantienen en el
[contrato HU-08](../../../../specs/002-gestionar-datos-maestros/contracts/api.md).
Los códigos compartidos añaden DuplicateCode, ArticleDefinitionLocked, VersionConflict,
PreconditionRequired y DependencyUnavailable en sus estados HTTP allí especificados.

Concreción de US2: el detalle de Article con ETag solo incluye datos persistidos propios.
GET /articles/{id}/context devuelve familia/política/capacidades derivadas sin ETag, con
articleVersion para detectar lecturas de distinta revisión. Así el ETag fuerte no permanece
igual ante cambios de su representación por datos relacionados. La UI combina ambas lecturas.

La preparación H0 de familia usa exclusivamente el modo HTTP autenticado de mantenimiento
definido en ese contrato: MasterData.Manage, antiforgery, ICurrentActor y resultado idempotente
común, sin ActorId proporcionado por el cliente. No se registra en el modo ordinario ni
entrega DDL al proceso API; no es un alta provisional de artículos.

### HU-02 — B: ubicaciones

GET, POST /locations y GET, PUT /locations/{id}: code, aisle, rack, level, kind, isActive.
Lectura Inventory.Read; mutación Locations.Manage. Código duplicado se rechaza.
GET /articles/{id}/locations devuelve todas sus ubicaciones con cantidades y lote,
paginadas; filtrar isActive no oculta la historia del artículo.

### HU-07 — A: pedidos a proveedores

| Método y ruta | Entrada / salida |
|---------------|-----------------|
| POST /purchase-orders | supplierId, expectedDeliveryDate, lines con articleId, quantity, unitPrice y expectedDeliveryDate. Crea Draft. |
| GET /purchase-orders | Filtros supplierId, state, from, to; cabeceras paginadas. |
| GET, PUT /purchase-orders/{id} | Cabecera y líneas; edición solo Draft con sendStatus None. Incluye ordered/received/pending por línea. |
| POST /purchase-orders/{id}/send | Sin cambiar líneas; guarda outbox y devuelve 202, state Draft, sendStatus Pending, operationId. |
| GET /purchase-orders/{id}/delivery | sendStatus, attempts, lastError resumido, acceptedAt y posibilidad de reintento. |

Purchasing.Manage para alta/edición; Purchasing.Send para enviar o reintentar un fallo.
Un reintento de envío utiliza la misma intención y MessageId, sin duplicar pedido.
Worker cambia a Sent tras aceptación SMTP; las recepciones cambian los estados posteriores.

### HU-01 — A: recepciones

POST /receipts y GET, PUT /receipts/{id}: purchaseOrderId, supplierDocumentReference,
occurredAt, lines con purchaseOrderLineId, reportedQuantity, acceptedQuantity,
discrepancyReason y allocations (locationId, lotCode, quantity).
Para artículo sin lote, lotCode omitido se resuelve a SIN-LOTE.
La respuesta incluye diferencias frente al pendiente y acumulados previstos.
PUT permite corregir el borrador con If-Match; nunca modifica una recepción confirmada.

POST /receipts/{id}/confirm: confirma la revisión con If-Match y clave idempotente;
Receiving.Record. Rechaza compra aún no enviada, artículo inactivo, exceso o falta de destino.
Devuelve cantidades aceptadas, pendientes, nuevo estado de compra y movimientos confirmados.

### HU-03 y HU-05 — B: pedidos de cliente, reservas y salidas

Asignación confirmada 2026-11-19: HU-05 implementa POST/GET de customer-orders y los
recursos de reservations/release; HU-03 implementa shipments y consume ese mismo pedido
y sus reservas. Se conservan las rutas y permisos siguientes; el traslado no añade
edición/cancelación de pedidos ni un módulo comercial.

| Método y ruta | Entrada / salida |
|---------------|-----------------|
| POST /customer-orders | externalReference, customerReference, lines con articleId y quantity. Alta mínima con Shipping.Record; no módulo comercial. |
| GET /customer-orders/{id} | Líneas, pedido/servido/pendiente y reservas; Inventory.Read. |
| POST /reservations | customerOrderLineId, allocations con locationId, lotId y quantity; Reservations.Manage. |
| GET /reservations/{id} | Asignado, consumido, liberado y remanente por bucket. |
| POST /reservations/{id}/release | allocations con id y quantity; motivo; Reservations.Manage. No libera más del remanente. |
| POST /shipments; GET, PUT /shipments/{id} | customerOrderId, occurredAt y lines con customerOrderLineId, locationId, lotId y quantity; edición solo de borrador con If-Match. |
| POST /shipments/{id}/confirm | Shipping.Record; efecto atómico sobre físico, reserva propia y servido del pedido. |

Cada liberación incluye releaseId UUID estable; su reintento lo conserva, pero otra
liberación parcial legítima utiliza otro releaseId. Reservar y liberar son comandos que
confirman su efecto directamente; usan If-Match
del pedido y de la reserva respectivamente. Las cantidades y lotes de las asignaciones
deben pertenecer al artículo de la línea. Solo confirma una salida lo que permite el
[cálculo común](stock-operations.md), respetando las reservas ajenas.

### HU-05 — B: consultas

GET /stock?articleId=...&locationId=...&lotId=...&page=1&pageSize=25: exige articleId;
los demás filtros son opcionales. Inventory.Read. Cada fila identifica artículo, ubicación
y lote y muestra physical, reserved, available. Totales consistentes del filtro completo.
GET /stock/reservations?articleId=... devuelve compromisos por pedido y bucket.
Los resultados incluyen movimientos y ajustes ya confirmados; no borradores.

### HU-04 — B: conteos y ajustes

| Método y ruta | Entrada / salida |
|---------------|-----------------|
| POST /cycle-counts | locationIds no vacíos; crear alcance Draft, Counts.Record. |
| GET /cycle-counts/{id} | Alcance, estado, líneas teóricas/contadas/diferencias y responsables. |
| POST /cycle-counts/{id}/start | Counts.Record; congela y captura teórico. |
| PUT /cycle-counts/{id}/lines | countedPhysical por articleId/locationId/lotId, reason; admite lotes/artículos hallados válidos. |
| POST /cycle-counts/{id}/submit | Counts.Record; exige conteo explícito de todos los buckets esperados, incluido cero. Pasa a AwaitingApproval. |
| POST /cycle-counts/{id}/confirm | Inventory.Adjust; registra aprobación y ajustes, libera alcance; exige motivo por diferencia. |
| POST /cycle-counts/{id}/cancel | Inventory.Adjust; registra motivo, conserva evidencia y libera alcance sin cambiar stock. |

No se toma una línea omitida como cero contado. PUT no permite cambiar el teórico.
Confirmación bloqueada si alguna cantidad contada es inferior a reservas remanentes.

### HU-06 — A: mínimos y alertas

GET, PUT /articles/{id}/stock-policy: minimumAvailable; lectura Inventory.Read,
mutación StockPolicy.Manage con ETag. Cambiar el mínimo evalúa inmediatamente el artículo.
GET /stock-alerts?state=Active: Alerts.Read, paginación, artículo, físico, reservado,
disponible, mínimo, sugerido e instante. Sugerido = max(0, mínimo − disponible).
La alerta no crea ni envía una compra; el comprador abre el flujo HU-07.

### Transversal: operación incierta, corrección y contingencia

- GET /operations/{operationId}: mismo permiso sobre el recurso original y resultado
  confirmado. El permiso se conserva en el resultado para autorizar también tras eliminar
  el recurso. Devuelve operationId, method, resource, status, body, headers y recordedAt;
  headers solo incluye ETag/Location. 404 significa «sin resultado observado», no
  «transacción necesariamente fallida».
- POST /movements/{id}/corrections: Inventory.Adjust; motivo, referencia e impactos
  compensatorios permitidos y correctionId UUID estable, con If-Match del documento origen.
  Responde nuevo movimiento; repetir correctionId no repite el efecto.
- POST /contingencies/reconcile: Inventory.Adjust más permiso de la operación afectada;
  identificador único de comprobante y datos del comando Receive/Ship/Adjustment existente.
  Usa el mismo núcleo, devuelve operación y referencia conciliada. No admite sobrescribir saldos.
- GET /health: comprobación técnica sin detalles sensibles; 200 listo o 503 no disponible.

La base contenedorizada H0 implementa GET /api/v1/health sin sesión: 200 con
`{ "status": "healthy", "database": "ready" }` si SQL responde y no quedan migraciones
pendientes y no está habilitado el modo de mantenimiento HU-08, o 503 con estado genérico.
No expone credenciales ni excepciones.
Las rutas de negocio descritas son contratos para sus futuras specs. Una ruta /api desconocida
devuelve 404, nunca el HTML de la SPA.

## Ejemplo de salida de consulta

```json
{
  "asOf": "2026-10-25T10:00:00Z",
  "totals": { "physical": "12.000", "reserved": "8.000", "available": "4.000" },
  "items": [
    {
      "articleId": "10000000-0000-0000-0000-000000000001",
      "locationId": "20000000-0000-0000-0000-000000000001",
      "lotCode": "L-001",
      "physical": "12.000", "reserved": "8.000", "available": "4.000"
    }
  ],
  "page": 1, "pageSize": 25, "totalCount": 1
}
```

La UI presenta nombres y códigos junto a los IDs. La respuesta OpenAPI final debe declarar
los decimales como strings con precisión y límites, no como float ni double.
