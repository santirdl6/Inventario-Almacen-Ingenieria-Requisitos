# Contrato de interacción: HU-04 — /inventarios

Concreta el [contrato UI común](../../../.specify/memory/project/contracts/ui.md) (fila `/inventarios`,
estados de operación e información que no debe ocultarse). Decisiones en [research D-15](../research.md#d-15-frontend-sobre-la-base-de-a-hu-05-y-hu-03).

## Rutas

| Ruta | Pantalla | Visible para |
|------|----------|--------------|
| `/inventarios` | Lista de conteos con estado, código, ubicaciones con sus códigos, fechas y responsables (creó, inició, cerró; FR-023); filtro por estado; «Nuevo conteo». | Inventory.Read; botón con Counts.Record |
| `/inventarios/nuevo` | Elegir ubicaciones activas (todas las páginas) y guardar el borrador. | Counts.Record |
| `/inventarios/{id}` | Detalle por estado: alcance (Draft), contar (Counting), revisar y confirmar (AwaitingApproval), solo lectura (cerrado). | Inventory.Read; acciones según permiso |

`appRoute` admitido: `/inventarios/(nuevo|{uuid})` (P-06).

## Contenido por estado

- **Borrador**: ubicaciones del alcance con código; añadir o quitar; «Iniciar conteo» con aviso de que
  congela esas ubicaciones hasta confirmar o cancelar.
- **En conteo**: por ubicación, filas con artículo (SKU · nombre), lote, unidad, teórico, campo
  «Contado» vacío por defecto (el cero se escribe) y diferencia en texto; el motivo no se pide aquí,
  sino en la revisión. «Añadir
  hallazgo» (artículo, lote, cantidad). «Guardar» y «Enviar a aprobación»; las filas sin contar se
  señalan al enviar.
- **Pendiente de aprobación**: tabla de revisión con teórico, contado, diferencia, reservado actual,
  quién contó y cuándo; total de líneas con diferencia. Motivo obligatorio en cada diferencia.
  «Vincular a salida» abre la lista de salidas candidatas del bucket; el aviso dice: «El ajuste
  corrige solo las existencias. No cambia lo servido del pedido ni las reservas consumidas».
  «Confirmar ajuste» abre un diálogo de revisión final; «Cancelar conteo» pide motivo.
- **Cerrado**: solo lectura, con aprobación o cancelación, motivo, operación y enlace al movimiento;
  en cada línea vinculada, la salida corregida con la referencia externa de su pedido (FR-022, US4.2).
- **Aviso de corrección de salida (P-07)**: en el detalle del conteo, siempre que haya una línea
  vinculada a una salida (al vincular, en la revisión final y en el estado cerrado), aviso visible
  de que el pedido no se corrige. Ejemplo: salida de 6 cuando salieron 4 → el ajuste devuelve 2 al
  saldo, pero el pedido sigue mostrando 6 servidas y su estado no cambia. Corregir el pedido
  exigiría su acumulado y estado en el mismo commit, según el
  [contrato común § Correcciones](../../../.specify/memory/project/contracts/stock-operations.md#correcciones-y-contingencia);
  queda fuera de alcance (decisión aprobada por A el 2026-11-21).
- **Aviso en la salida corregida (US4.3, Convergence C3)**: `/salidas/{id}` confirmada consulta
  `GET /cycle-counts?correctsShipmentId={id}` y, si algún conteo la corrige, avisa con el código del
  conteo (enlace a `/inventarios/{id}`) de que el físico se corrigió, pero el pedido sigue contando lo
  registrado como servido, su estado no cambia y las reservas consumidas tampoco.

## Resultados y errores

- Éxito: «Ajuste registrado. Operación …» con el ID visible; tras recargar o volver por «Abrir
  conteo», el mismo ID.
- Pendiente de comprobar (fallo de red o 5xx): `OperationStatusComponent` con «Comprobar resultado»;
  no se muestra éxito sin confirmación.
- 422: el error junto a cada campo, foco al primero, sin perder lo escrito.
- 412: aviso «Otra persona ha cambiado el conteo», relectura y conservación de lo escrito.
- 409 (`CountScopeFrozen`, `CountStateConflict`, `CountTheoreticalChanged`, `ReservedStockConflict`):
  aviso con foco y relectura del conteo; en `ReservedStockConflict`, enlace a `/existencias` para liberar
  la reserva desde el pedido del cliente. El enlace no va al pedido porque ni el 409 ni el contexto del
  conteo lo traen, y un bucket puede tener reservas de varios pedidos (Convergence C4).

## Accesibilidad e idioma

Español, vocabulario del almacén, unidad junto a cada cantidad, estados y diferencias con texto (no
solo color), diálogos con teclado y foco atrapado, etiquetas explícitas en cada campo.
