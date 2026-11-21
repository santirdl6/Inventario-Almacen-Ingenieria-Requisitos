# Revisión compartida A+B — Pendiente de registro

La revisión se indicó verbalmente como hecha, pero falta registrar una revisión A+B verificable con las personas revisoras, fecha y resultado. T016 permanece abierta hasta completar ese registro.

Alcance de la revisión:

- Cadena EF y borrado restrictivo: `20261121200510_HU06StockAlerts`, snapshot común y distinción entre referencia de borrado y primer uso de la definición en HU-08.
- Bloqueo común del artículo y agregación de saldos persistidos con deltas de EF pendientes. Revisar las entradas de `StockCore` y la integración de escritores futuros HU-03/04/05.
- Coordinación con HU-08 al activar/desactivar, y con HU-07 para propuesta editable sin escritura automática. Revisar permisos `StockPolicy.Manage`, `Alerts.Read` y `Purchasing.Manage`.
- Registrar revisoras/es reales, fecha, resultado e incidencias de la revisión antes de integrar cambios compartidos.
