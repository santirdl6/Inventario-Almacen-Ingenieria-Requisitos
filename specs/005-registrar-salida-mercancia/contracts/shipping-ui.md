# Contrato de interacción: HU-03 — /salidas

**Base común**: [ui.md](../../../.specify/memory/project/contracts/ui.md) (estados de toda operación,
información que no debe ocultarse en una salida, accesibilidad y validación con operarios). Patrones
de HU-05 reutilizados: [stock-ui.md](../../004-consultar-existencias/contracts/stock-ui.md) (detalle +
contexto, recuperación con `appRoute`, foco tras rechazo). API: [shipping-api.md](shipping-api.md).

## Rutas

| Ruta | Pantalla | Acceso | Spec |
|------|----------|--------|------|
| `/salidas` | Buscar pedido por referencia externa | Área (`Inventory.Read`) | FR-001a, US1.11 |
| `/salidas/pedidos/:orderId` | Pedido: pendientes por línea y salidas (borradores y confirmadas) | Área | US4.1, US4.3 |
| `/salidas/pedidos/:orderId/nueva` | Preparar salida con propuesta de orígenes | `permissionGuard('Shipping.Record')` | US1.1, US1.10, FR-006a |
| `/salidas/:id` | Borrador: editar, eliminar, revisar y confirmar · Confirmada: solo lectura | Área; acciones con `Shipping.Record` | US1.2–US1.9, US4.2 |

El área `salidas` de `operation-areas.ts` pasa a `Inventory.Read` (research D-14). Sin
`Shipping.Record` no se muestran «Nueva salida», «Guardar», «Eliminar» ni «Revisar y confirmar».

## Buscar pedido

- Campo «Referencia del pedido» con etiqueta visible y botón «Buscar»; Intro también busca.
- Encontrado: navega a `/salidas/pedidos/:orderId`.
- No encontrado: aviso «No hay ningún pedido con la referencia «…»». El texto se conserva y el foco
  vuelve al campo. Sin sugerencias ni coincidencias parciales.

## Pedido y sus salidas

- Cabecera: referencia, cliente y estado en texto («Abierto», «Servido»).
- Tabla de líneas: artículo (SKU, nombre, «Inactivo» si procede), unidad, pedido, servido, pendiente
  y reservado remanente (de `GET /customer-orders/{id}` + `/context` de HU-05).
- Tabla de salidas: estado en texto («Borrador», «Confirmada»), fecha del hecho, registro,
  responsable, líneas y total; enlace a `/salidas/:id`. **Construido** (T014): el total lleva unidad
  solo si todas las líneas del pedido comparten la misma; si no, «—» (FR-025, analyze-1 M2).
- Pedido servido: aviso «El pedido está servido: no admite nuevas salidas» y sin «Nueva salida».

## Preparar y editar un borrador

- Fecha y hora del hecho, por defecto ahora; no admite futuro.
- Por cada línea de pedido con pendiente: «Añadir». Con reservas propias, se precargan sus orígenes
  con el remanente como cantidad (sin superar el pendiente) y la marca «Propuesto desde la reserva»;
  el operario cambia la cantidad, quita filas o añade otro origen con el selector de buckets del
  artículo. El selector ofrece todas las combinaciones con existencias, sin filas a cero, leídas con
  `StockApiService.allRows` de HU-05 (todas las páginas de `GET /stock?articleId=`; analyze-1 H3).
  Sin reserva propia, la fila queda vacía.
- Cada fila muestra ubicación, lote o «sin lote», cantidad con unidad, y el pendiente de su línea.
  **Convergence C3** (G2): también el artículo («SKU · nombre»). En solo lectura, cada línea empieza
  por el artículo y la cabecera muestra la fecha de registro (US4.1, FR-018).
- «Guardar borrador» crea (`POST`) o sustituye (`PUT`) y navega o se queda en `/salidas/:id`.
- «Eliminar borrador» pide confirmación en un diálogo y, tras el 204, vuelve al pedido con el aviso
  «Borrador eliminado. No se ha modificado ninguna existencia». **Construido** (T015, `8487830`): la
  confirmación es una sección `alertdialog` dentro de la página, y el aviso viaja en el estado de la
  navegación (`history.state`).

## Revisar y confirmar

Diálogo accesible (`cdkTrapFocus`, Escape cierra, foco vuelve al botón):

- Pedido y cliente; por línea: artículo (con «Inactivo» en texto si `isActive = false` en el
  contexto, sin impedir confirmar; US3.2, analyze-1 H5), unidad, ubicación, lote, cantidad, pendiente, físico,
  reservado propio, reservado ajeno y cantidad posible (`review` de `/context`), con la nota «El
  sistema vuelve a comprobar las cantidades al confirmar».
- Línea con cantidad mayor que la posible: aviso en texto en esa fila (no solo color); el botón
  «Confirmar salida» sigue disponible porque el servidor decide.
- «Confirmar salida» envía `orderVersion` del contexto leído para la revisión.
- **Construido** (T016, `fab4cbe`): «Revisar y confirmar» solo está activo con el borrador guardado,
  sin cambios pendientes, porque las cifras salen del contexto de la salida guardada.

## Resultados y errores

| Caso | Comportamiento |
|------|----------------|
| Confirmada (200) | «Salida registrada. Operación {operationId}»; se recargan salida y pedido; la vista pasa a solo lectura. |
| Salida confirmada abierta después | En solo lectura, «Salida confirmada con la operación {operationId}», con el `operationId` de `GET /shipments/{id}`. Se ve igual en otra sesión, por «Abrir salida» o por URL (FR-023, US4.2; analyze-1 H2). |
| 422 | Errores por campo y por línea; foco al primer `aria-invalid`; datos conservados. |
| 409 `InsufficientStock` | Aviso con el `detail` (físico, disponible, cantidad posible) junto a la línea; foco al aviso. **Construido**: el aviso va arriba de la página (G3, límite aceptado en converge-1). El `detail` da la cantidad posible desde Convergence C2 (G1). |
| 409 `ReservedStockConflict` | Aviso «Antes de servir desde otro origen hay que liberar {cantidad} reservadas de la línea {SKU}. Pídelo a un responsable del almacén» con enlace al pedido en `/existencias/pedidos/:id`. |
| 409 `CustomerOrderNotOpen` / `CountScopeFrozen` / `DocumentAlreadyConfirmed` | Aviso con el motivo; recarga del estado actual. **Construido** (Convergence C4, G4): motivo con foco y relectura de salida, contexto y pedido sin perder lo escrito. Con `DocumentAlreadyConfirmed`, la vista pasa a solo lectura. |
| 412 | «Los datos cambiaron desde que los revisaste»: recarga salida, pedido y revisión sin perder las cantidades escritas del borrador en edición. |
| Respuesta perdida | Estado 7 de ui.md con `OperationStatusComponent`: «Resultado pendiente de comprobar», «Comprobar», «Repetir la misma intención» y «Abrir salida» (`appRoute`). |

## Navegación y recuperación de una operación pendiente

Mismo transporte que HU-05: `pending.begin(método, recurso, cuerpo, etag, appRoute)` y `send()`, sin
`idempotent-intent.ts`. `appRoute`:

- alta de borrador: `/salidas/pedidos/{orderId}`;
- edición, eliminación y confirmación: `/salidas/{shipmentId}`.

Requiere ampliar `APP_ROUTE`, `mutationPermission()` y el texto del enlace en archivos de A
(plan, P-06).

## Accesibilidad e idioma

Español y vocabulario del almacén; unidad junto a cada cantidad; estados con texto; tablas con
cabeceras; todo operable por teclado; foco al primer error o al aviso tras un rechazo (patrón C1 de
HU-05).
