# Recuperación de recepción — evidencia parcial

2026-11-21. `ReceivingRecoveryTests` verificó resultado histórico por operación, replay
de la misma intención, rechazo de una nueva confirmación y un solo efecto en StockCore;
la prueba de fallo tras el primer guardado verifica rollback y retry de la intención.

`receiving-recovery.spec.ts` pasó en Chrome junto con los recorridos parcial e incidencias
(3/3). Intercepta la respuesta después de que la API confirmase, recarga, comprueba el
resultado y verifica 6 recibidas/4 pendientes. Los casos de logout, otra identidad y
Retry-After de recepción requieren todavía su evidencia específica. La restricción real de
conteo depende de HU-04 y no se acredita con el lector actual fijo a falso.
