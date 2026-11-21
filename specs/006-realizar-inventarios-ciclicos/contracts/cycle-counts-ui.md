# Contrato de interacción: HU-04 — /inventarios

Concreta el [contrato UI común](../../../.specify/memory/project/contracts/ui.md) (fila `/inventarios`,
estados de operación e información que no debe ocultarse). Decisiones en [research D-15](../research.md#d-15-frontend-sobre-la-base-de-a-hu-05-y-hu-03).

## Rutas

| Ruta | Pantalla | Visible para |
|------|----------|--------------|
| `/inventarios` | Lista de conteos con estado, código, ubicaciones y fechas; filtro por estado; «Nuevo conteo». | Inventory.Read; botón con Counts.Record |
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

## Resultados y errores

- Éxito: «Ajuste registrado. Operación …» con el ID visible; tras recargar o volver por «Abrir
  conteo», el mismo ID.
- Pendiente de comprobar (fallo de red o 5xx): `OperationStatusComponent` con «Comprobar resultado»;
  no se muestra éxito sin confirmación.
- 422: el error junto a cada campo, foco al primero, sin perder lo escrito.
- 412: aviso «Otra persona ha cambiado el conteo», relectura y conservación de lo escrito.
- 409 (`CountScopeFrozen`, `CountStateConflict`, `CountTheoreticalChanged`, `ReservedStockConflict`):
  aviso con foco y relectura del conteo; en `ReservedStockConflict`, enlace al pedido para liberar la
  reserva.

## Accesibilidad e idioma

Español, vocabulario del almacén, unidad junto a cada cantidad, estados y diferencias con texto (no
solo color), diálogos con teclado y foco atrapado, etiquetas explícitas en cada campo.
