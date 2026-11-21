# Handoff de compras a HU-01

**Entrega local**: modelo y lectura de compras disponibles. **Aceptación integrada**:
el escritor real de HU-01 acredita SC-006/V-02/V-06. Véanse el
[contrato de integración](../contracts/integration.md#7-contrato-entregado-a-hu-01-y-revisión-conjunta)
y el [protocolo común de stock](../../../.specify/memory/project/contracts/stock-operations.md).

1. Referenciar `PurchaseOrder.Id` y `PurchaseOrderLine.Id` (UUID estables). Comprobar que
   cada línea pertenece al pedido y que el artículo corresponde; no usar posición ni SKU
   como identidad de recepción. Leer el detalle `GET /api/v1/purchase-orders/{id}` y su
   ETag fuerte; `/context` expone maestros actuales y `purchaseOrderVersion` sin ETag propio.
2. En el ejecutor común, bloquear `purchase-order` y `receipt` juntos, luego ubicaciones y
   artículos; releer pedido, `Accepted`, maestros/destinos y saldo pendiente tras los locks.
3. En la misma transacción, persistir recepción y asignaciones, ejecutar `StockCore`, actualizar
   recibido con `PurchaseOrderLine.SetReceivedQuantity` y recalcular estado mediante
   `PurchaseOrder.DeriveReceiptState`. El método toca la cabecera y EF hace avanzar la
   `rowversion`; la corrección compensatoria sigue el mismo protocolo. No hay commit separado.
4. Con pedido enviado de 10 UD, documentar la primera lectura posterior a cada commit:
   0/10 Enviado → 6/4 Recibido parcialmente → 10/0 Recibido. Un borrador no afecta acumulados.
   Con dos recepciones simultáneas de 6, usar conexiones independientes y barreras: solo una
   puede confirmar. Comparar pedido, stock, diario, recepción y resultado tras el rechazo.

La [regresión local](integration.md) incluye la prueba de proyección y ETag del lector,
sin efectos de receiving. La aceptación integrada con el escritor real de HU-01 y las pruebas
V-02/V-06 se completaron en [Matriz de aceptación HU-01](../../008-registrar-recepcion-mercancia/validation/acceptance.md)
y [Evidencia US1 de HU-01](../../008-registrar-recepcion-mercancia/validation/us1.md).
