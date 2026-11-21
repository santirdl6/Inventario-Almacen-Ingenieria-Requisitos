# Revisión compartida A+B — Confirmada por el usuario

El 2026-11-21 el usuario indicó dar por hecha la revisión compartida de HU-06. Se registra esa confirmación para T016. No se facilitaron nombres ni acta separada.

Alcance de la revisión:

- Cadena EF y borrado restrictivo: `20261121200510_HU06StockAlerts`, snapshot común y distinción entre referencia de borrado y primer uso de la definición en HU-08.
- Bloqueo común del artículo y agregación de saldos persistidos con deltas de EF pendientes. Revisar las entradas de `StockCore` y la integración de escritores futuros HU-03/04/05.
- Coordinación con HU-08 al activar/desactivar, y con HU-07 para propuesta editable sin escritura automática. Revisar permisos `StockPolicy.Manage`, `Alerts.Read` y `Purchasing.Manage`.
- La [evidencia local](acceptance.md) y el [contrato común de stock](../../../.specify/memory/project/contracts/stock-operations.md) recogen el alcance de los escritores actuales y la verificación pendiente de los futuros.
