# Contrato de interacción: HU-05 — /existencias

**Base común**: [ui.md](../../../.specify/memory/project/contracts/ui.md): estados de operación 1–7,
información que no debe ocultarse, accesibilidad. Base de frontend de A: layout,
`SessionService`, `permissionGuard` y `apiProblem()`. Este documento solo concreta las pantallas
de HU-05.

El área `/existencias` ya está declarada en `operation-areas.ts` con `Inventory.Read`.
`features/stock/stock.routes.ts` sustituye el placeholder.

| Ruta | Guard adicional | Pantalla | Spec |
|------|-----------------|----------|------|
| `/existencias` | — | Consulta de existencias de un artículo, con las pestañas «Existencias» y «Reservas». **Construida sin la pestaña «Reservas»** (S2). | US1, US3.8 |
| `/existencias/pedidos` | — | Búsqueda de pedidos de cliente por referencia externa y estado. **No construida** (S1): al pedido se llega desde el alta o por URL. | US2.4 |
| `/existencias/pedidos/nuevo` | `permissionGuard`, `Shipping.Record` | Alta de pedido. | US2.1–2.3 |
| `/existencias/pedidos/:id` | — | Detalle del pedido; reservar y liberar si `has('Reservations.Manage')`. | US2.4, US3, US4 |

## Consulta de existencias

- Selector de artículo propio (SKU o nombre, incluidos inactivos, con `GET /articles?q=&state=all`).
  No se usa `article-search` de HU-02, que enlaza fijo a `/ubicaciones/articulo/:id` (research D-15).
  Hasta elegir un artículo no se consulta, y se muestra «Elige un artículo para ver sus
  existencias» (US1.4).
- Filtros opcionales «Ubicación» y «Lote». El de lote solo aparece si el artículo tiene
  seguimiento (caso límite). **Construido**: las opciones se toman de la consulta del artículo sin
  filtros y se conservan al filtrar. Si esa consulta tiene más de una página, se leen todas sus
  combinaciones en páginas de 100, una vez por artículo y casilla (Convergence C4). La tabla sigue
  paginada de 25 en 25.
- Casilla «Mostrar ubicaciones sin existencias» (`includeEmpty`), desmarcada por defecto
  (clarificación 2).
- Totales (físico, reservado y disponible, con la unidad) en un bloque separado de la tabla, con la
  leyenda «Totales de todo el filtro» (ui.md, «Stock»).
- La tabla muestra ubicación (código y pasillo/estantería/nivel), lote o «sin lote», físico,
  reservado, disponible y unidad.
- Texto «Datos a las {asOf}» y botón «Actualizar» (US1.8).
- Si el artículo está inactivo, etiqueta textual «Inactivo» junto al nombre (US1.6, FR-007).
- Con totales a cero se muestra «No hay existencias para esta selección» (US1.7, FR-008).
- Pestaña «Reservas» (**no construida**, S2; la API `GET /stock/reservations` sí existe): compromisos activos por pedido, ubicación y lote con asignado, consumido,
  liberado y remanente, y enlace a cada pedido (US3.8).

## Alta de pedido

- Campos «Referencia del pedido» y «Cliente», y líneas con artículo y «Cantidad ({unidad})».
- «Añadir línea» y «Quitar línea». Un artículo ya usado en otra línea no se puede elegir otra vez,
  y el servidor lo revalida.
- Revisión antes de confirmar (estado 2) y confirmación (estado 3) con la intención idempotente
  conservada.
- Los errores por línea se asocian a su campo y los datos válidos se conservan (estado 5).
- Al confirmar se muestra el identificador y se navega al detalle, que también lo muestra (C3).

## Detalle del pedido, reservar y liberar

- **Dos lecturas combinadas** (research D-11):
  - `GET /customer-orders/{id}` aporta los datos propios y el `ETag` que usarán los `If-Match`;
  - `GET /customer-orders/{id}/context` aporta, sin `ETag`, el SKU, nombre, unidad y estado de cada
    artículo, y el código y estado de cada ubicación y lote.

  La pantalla une ambas por id. Si `context.orderVersion` difiere del `ETag` del detalle, vuelve a
  leer las dos antes de mostrar o permitir reservar o liberar, igual que la ficha de artículo de
  HU-08. «Actualizar» repite las dos lecturas.
- Cabecera con referencia, cliente y estado en texto, y «Pedido creado con la operación {id}» con
  el `creationOperationId` del contexto (Convergence C3, FR-026). Al venir del servidor, se ve
  igual tras crear, tras cerrar sesión, al entrar por «Abrir pedido» o por URL. Por línea: artículo (SKU y nombre del
  contexto, con «Inactivo» en texto si procede), pedido, servido, pendiente y reservado remanente,
  con la unidad del contexto, y las reservas con sus asignaciones (ubicación y lote del contexto).
- **Reservar** (por línea, con `Reservations.Manage` y el pedido `Open`): diálogo que lista las
  existencias del artículo con `GET /stock` (ubicación, lote y disponible). **Construido**: todas las
  combinaciones con existencias, leídas en páginas de 100 hasta `totalCount` (Convergence C4). El
  responsable escribe
  la cantidad en una o varias filas. Se muestra el máximo reservable, mín(pendiente − reservado,
  disponible), como ayuda, y el servidor revalida. Revisión → confirmar con el `If-Match` del pedido.
- **Liberar** (por reserva activa): diálogo con las asignaciones y su remanente, la cantidad por
  asignación y «Motivo» (obligatorio, texto libre). El `releaseId` se genera al abrir el diálogo y
  se conserva con la intención hasta confirmar o descartar. Revisión → confirmar con el `If-Match`
  de la reserva.
- Tras confirmar se vuelven a leer el pedido y su contexto, porque no hay `ETag` en la respuesta, y
  se muestran el identificador de la operación y el nuevo reservado. El diálogo de liberar obtiene
  el `ETag` de la reserva con `GET /reservations/{id}` (solo datos propios) y toma los nombres y
  códigos del contexto del pedido.

## Estados de operación (ui.md 1–7) en esta feature

| Estado | Comportamiento |
|--------|----------------|
| Confirmando | Botón deshabilitado. La intención (clave, cuerpo congelado e `If-Match`) queda en el transporte común `MasterDataMutationsService` / `PendingOperationStore` (research D-15): sobrevive a la recarga, pertenece al usuario que la abrió y al cerrar sesión solo conserva la referencia. |
| Conflicto 409 | `InsufficientStock` y `ReservedStockConflict` muestran el `detail` del servidor, que nombra el disponible o el remanente, y ofrecen «Ver existencias actuales». |
| `VersionConflict` 412 | «Otra persona cambió el pedido o la reserva»; recargar sin perder las cantidades escritas. |
| Respuesta perdida | «Resultado pendiente de comprobar», mostrado con `OperationStatusComponent`, también tras recargar; botones «Comprobar resultado» (`check()`, `GET /operations/{id}`) y «Repetir la misma intención» (`retry()`, con la clave, el cuerpo y el `If-Match` originales). |
| Otra reserva con una pendiente | El almacén común identifica la intención por recurso y todas las reservas son `POST /api/v1/reservations`. Mientras una reserva del usuario esté pendiente de comprobar, **no puede enviar otra reserva**, ni siquiera de otro pedido. Se explica con texto («Comprueba la reserva pendiente antes de reservar de nuevo») y el enlace «Revisar esta operación pendiente» del layout. Así no se duplica un compromiso. Las liberaciones (`/reservations/{id}/release`) se bloquean solo por reserva. |
| 403 | Mensaje de permiso; las acciones no permitidas no se muestran, aunque el servidor es la barrera. |

## Navegación y recuperación de una operación pendiente

La referencia pendiente es la que guarda `PendingOperationStore` de A tras R1/R2: no es sensible,
está ligada al usuario (`actorId`) y sobrevive a la recarga. Al cerrar sesión pierde el cuerpo y el
`If-Match`, pero conserva el resto. Para HU-05 incluye:

| Dato | Origen | Se conserva al cerrar sesión |
|------|--------|------------------------------|
| `operationId` | Clave idempotente (ya existe). | Sí |
| Tipo de operación | Se deduce de `method` + `resource` (ya existen): `POST /api/v1/customer-orders` = alta, `POST /api/v1/reservations` = reserva, `POST /api/v1/reservations/{id}/release` = liberación. | Sí |
| Ruta del recurso con el id del pedido | **Campo nuevo** `appRoute`: `/existencias/pedidos/{orderId}` en reservas y liberaciones; `/existencias/pedidos/nuevo` en el alta, cuyo id aún no se conoce. | Sí |
| Cuerpo e `If-Match` | Intención original. | **No** (se retiran, R2) |

Hace falta el campo nuevo porque ni el recurso de reservar (`/api/v1/reservations`) ni el de
liberar (`/api/v1/reservations/{reservationId}/release`) contienen el id del pedido. **Construido**
(`f867b61`, `013d429`): `restore()` solo admite `/existencias/pedidos/(nuevo|{uuid})`, y HU-05 abre
la intención con `PendingOperationStore.begin(…, appRoute)` seguida de
`MasterDataMutationsService.send()`, sin modificar `mutate()`. El
`customerOrderLineId` del cuerpo desaparece al cerrar sesión. `appRoute` es una ruta de la
aplicación, sin datos del cuerpo. Se fija al abrir la intención y nunca se usa para enviar nada.

El panel «Resultado pendiente de comprobar» (`OperationStatusComponent`) ofrece, para estas
operaciones:

- **«Abrir pedido»**, un enlace a `appRoute`, **aunque el cuerpo se haya retirado** al cerrar
  sesión. El enlace resumido del layout («Revisar esta operación pendiente») usa la misma ruta.
- **«Comprobar resultado»**: `GET /operations/{operationId}`, con la sesión del mismo usuario.
  - Si confirma la operación, se resuelve la referencia y se vuelve a leer el pedido (detalle y
    contexto). En un alta recuperada se navega a la `Location` del resultado.
  - Si responde 404, la operación sigue pendiente. Sin cuerpo no se ofrece «Repetir la misma
    intención»: el usuario rehace la reserva o la liberación desde el pedido una vez comprobada,
    con una intención nueva.
- Con otro usuario, la referencia no se muestra ni se comprueba (R2).

## Accesibilidad

Todos los estados se comunican con texto (y con icono cuando lo haya), nunca solo con color.
Tablas con encabezados y nombre accesible. El foco va al primer error tras rechazo: al primer
campo con `aria-invalid` o, si el error no es de un campo, al aviso (tarea de convergencia C1, que
cierra G1 de [validation/converge-1.md](../validation/converge-1.md)). Los diálogos se
operan con teclado, tienen el foco atrapado y cierran con Escape. Las etiquetas están en español y
la unidad acompaña a cada cantidad.
