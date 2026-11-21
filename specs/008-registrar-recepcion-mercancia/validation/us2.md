# Incidencias de recepción — evidencia parcial

2026-11-21. `ReceivingDiscrepancyTests` cubre un borrador de solo incidencias, rechazo de
aceptación superior al pendiente y de distribución incompleta, y confirmación de la
cantidad válida con incidencia histórica conservada. En la selección de recepción SQL
pasaron 14/14 pruebas, incluidas estas y las de confirmación/recuperación.

El recorrido `receiving-discrepancies.spec.ts` pasó junto con `receiving-partial.spec.ts`
y `receiving-recovery.spec.ts`: **3/3 en Chrome**, compra real aceptada por Mailpit,
base/proyecto `inventario-hu07-e2e-hu01` aislado, cuentas retiradas y servicios detenidos.
La incidencia sin aceptación se reabrió sin alterar el pendiente. No acredita todos los
casos de T020–T024 ni la aceptación final de la historia.
