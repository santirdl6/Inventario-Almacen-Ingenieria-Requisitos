# Contrato de interacción: HU-05 — /existencias

**Base común**: [ui.md](../../../.specify/memory/project/contracts/ui.md): estados de operación 1–7,
información que no debe ocultarse, accesibilidad. Base de frontend de A: layout,
`SessionService`, `permissionGuard` y `apiProblem()`. Este documento solo concreta las pantallas
de HU-05.

El área `/existencias` ya está declarada en `operation-areas.ts` con `Inventory.Read`.
`features/stock/stock.routes.ts` sustituye el placeholder.

| Ruta | Guard adicional | Pantalla | Spec |
|------|-----------------|----------|------|
| `/existencias` | — | Consulta de existencias de un artículo, con las pestañas «Existencias» y «Reservas». | US1, US3.8 |
| `/existencias/pedidos` | — | Búsqueda de pedidos de cliente por referencia externa y estado. | US2.4 |
| `/existencias/pedidos/nuevo` | `permissionGuard`, `Shipping.Record` | Alta de pedido. | US2.1–2.3 |
| `/existencias/pedidos/:id` | — | Detalle del pedido; reservar y liberar si `has('Reservations.Manage')`. | US2.4, US3, US4 |

## Consulta de existencias

- Selector de artículo propio (SKU o nombre, incluidos inactivos, con `GET /articles?q=&state=all`).
  No se usa `article-search` de HU-02, que enlaza fijo a `/ubicaciones/articulo/:id` (research D-15).
  Hasta elegir un artículo no se consulta, y se muestra «Elige un artículo para ver sus
  existencias» (US1.4).
- Filtros opcionales «Ubicación» y «Lote». El de lote solo aparece si el artículo tiene
  seguimiento (caso límite).
- Casilla «Mostrar ubicaciones sin existencias» (`includeEmpty`), desmarcada por defecto
  (clarificación 2).
- Totales (físico, reservado y disponible, con la unidad) en un bloque separado de la tabla, con la
  leyenda «Totales de todo el filtro» (ui.md, «Stock»).
- La tabla muestra ubicación (código y pasillo/estantería/nivel), lote o «sin lote», físico,
  reservado, disponible y unidad.
- Texto «Datos a las {asOf}» y botón «Actualizar» (US1.8).
- Si el artículo está inactivo, etiqueta textual «Inactivo» junto al nombre (US1.6, FR-007).
- Con totales a cero se muestra «No hay existencias para esta selección» (US1.7, FR-008).
- Pestaña «Reservas»: compromisos activos por pedido, ubicación y lote con asignado, consumido,
  liberado y remanente, y enlace a cada pedido (US3.8).

## Alta de pedido

- Campos «Referencia del pedido» y «Cliente», y líneas con artículo y «Cantidad ({unidad})».
- «Añadir línea» y «Quitar línea». Un artículo ya usado en otra línea no se puede elegir otra vez,
  y el servidor lo revalida.
- Revisión antes de confirmar (estado 2) y confirmación (estado 3) con la intención idempotente
  conservada.
- Los errores por línea se asocian a su campo y los datos válidos se conservan (estado 5).
- Al confirmar se muestra el identificador y se navega al detalle.

## Detalle del pedido, reservar y liberar

- Cabecera con referencia, cliente y estado en texto. Por línea: pedido, servido, pendiente y
  reservado remanente, con la unidad, y las reservas con sus asignaciones.
- **Reservar** (por línea, con `Reservations.Manage` y el pedido `Open`): diálogo que lista las
  existencias del artículo con `GET /stock` (ubicación, lote y disponible). El responsable escribe
  la cantidad en una o varias filas. Se muestra el máximo reservable, mín(pendiente − reservado,
  disponible), como ayuda, y el servidor revalida. Revisión → confirmar con el `If-Match` del pedido.
- **Liberar** (por reserva activa): diálogo con las asignaciones y su remanente, la cantidad por
  asignación y «Motivo» (obligatorio, texto libre). El `releaseId` se genera al abrir el diálogo y
  se conserva con la intención hasta confirmar o descartar. Revisión → confirmar con el `If-Match`
  de la reserva.
- Tras confirmar se vuelve a leer el pedido, porque no hay `ETag` en la respuesta, y se muestran el
  identificador de la operación y el nuevo reservado.

## Estados de operación (ui.md 1–7) en esta feature

| Estado | Comportamiento |
|--------|----------------|
| Confirmando | Botón deshabilitado. La intención (clave, cuerpo congelado e `If-Match`) queda en el transporte común `MasterDataMutationsService` / `PendingOperationStore` (research D-15): sobrevive a la recarga, pertenece al usuario que la abrió y al cerrar sesión solo conserva la referencia. |
| Conflicto 409 | `InsufficientStock` y `ReservedStockConflict` muestran el `detail` del servidor, que nombra el disponible o el remanente, y ofrecen «Ver existencias actuales». |
| `VersionConflict` 412 | «Otra persona cambió el pedido o la reserva»; recargar sin perder las cantidades escritas. |
| Respuesta perdida | «Resultado pendiente de comprobar», mostrado con `OperationStatusComponent`, también tras recargar; botones «Comprobar resultado» (`check()`, `GET /operations/{id}`) y «Repetir la misma intención» (`retry()`, con la clave, el cuerpo y el `If-Match` originales). |
| Otra reserva con una pendiente | El almacén común identifica la intención por recurso y todas las reservas son `POST /api/v1/reservations`. Mientras una reserva del usuario esté pendiente de comprobar, **no puede enviar otra reserva**, ni siquiera de otro pedido. Se explica con texto («Comprueba la reserva pendiente antes de reservar de nuevo») y el enlace «Revisar esta operación pendiente» del layout. Así no se duplica un compromiso. Las liberaciones (`/reservations/{id}/release`) se bloquean solo por reserva. |
| 403 | Mensaje de permiso; las acciones no permitidas no se muestran, aunque el servidor es la barrera. |

## Accesibilidad

Todos los estados se comunican con texto (y con icono cuando lo haya), nunca solo con color.
Tablas con encabezados y nombre accesible. El foco va al primer error tras rechazo. Los diálogos se
operan con teclado, tienen el foco atrapado y cierran con Escape. Las etiquetas están en español y
la unidad acompaña a cada cantidad.
