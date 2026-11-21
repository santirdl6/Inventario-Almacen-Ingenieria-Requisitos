# Evidencia de escritores — HU-06

- `StockAlertTests` verifica dos reservas concurrentes sobre conexiones independientes, unicidad, replay, rollback después de guardar y veinte lotes distribuidos en dos ubicaciones. `StockArticleIntegrationTests` verifica V-11 y la recuperación por recepción, así como el bloqueo de borrado por historial sin bloqueo de definición.
- `StockCoreTests` cubre los efectos disponibles de recibir, reservar, liberar y servir. Las pruebas de `Receiving` recorren confirmación, compensación y conciliación; `ConfirmReceipt` y `ReconcileReceiptVoucher` consumen `StockCore.ApplyAsync`, y `CorrectReceipt` consume `StockCore.CorrectReceiptAsync`. Ambos puntos invocan el evaluador después de aplicar todos sus efectos, antes del commit.
- Resultado de la ejecución conjunta: **233 aprobadas, 0 fallidas, 2 omitidas por configuración**. Comando y demás verificaciones en [aceptación](acceptance.md).
- El núcleo no acredita por sí solo los escritores documentales futuros de HU-03/04/05. Cada uno debe demostrar que consume el punto común y conserva atomicidad al integrarse.
