export interface PurchaseSupplierSnapshot { id: string; code: string; name: string; email: string | null; taxIdentifier: string | null; schemaVersion: number }
export interface PurchaseArticleSnapshot { id: string; sku: string; name: string; baseUnit: string; allowsFractional: boolean; tracksLot: boolean; schemaVersion: number }
export interface PurchaseLine {
  id: string; position: number; articleId: string; articleSnapshot: PurchaseArticleSnapshot;
  orderedQuantity: string; receivedQuantity: string; pendingQuantity: string; unitPrice: string; expectedDeliveryDate: string;
}
export interface PurchaseOrder {
  id: string; number: string; supplierId: string; supplierSnapshot: PurchaseSupplierSnapshot; currency: string;
  expectedDeliveryDate: string; state: 'Draft' | 'Sent' | 'PartiallyReceived' | 'Received';
  sendStatus: 'None' | 'Pending' | 'Accepted' | 'Failed'; deliveryId: string | null;
  createdBy: string; createdAt: string; sentAt: string | null; lines: PurchaseLine[];
}
export interface PurchaseLineWrite { id: string; articleId: string; quantity: string; unitPrice: string; expectedDeliveryDate: string | null }
export interface PurchaseOrderWrite { supplierId: string; expectedDeliveryDate: string; lines: PurchaseLineWrite[] }
export interface PurchaseOrderContext {
  purchaseOrderVersion: string;
  supplier: { id: string; code: string; name: string; email: string | null; isActive: boolean; version: string };
  articles: { id: string; isActive: boolean; version: string }[]; editBlockReasons: string[]; sendBlockReasons: string[];
}
export interface PurchaseDetail { resource: PurchaseOrder; context: PurchaseOrderContext; etag: string }
export type PurchaseSummary = Pick<PurchaseOrder, 'id' | 'number' | 'supplierId' | 'supplierSnapshot' | 'expectedDeliveryDate' | 'createdAt' | 'state' | 'sendStatus'>;
export interface PurchasePage { items: PurchaseSummary[]; page: number; pageSize: number; totalCount: number }
export interface PurchaseListFilter { q: string; supplierId: string; state: string; from: string; to: string }
export interface PurchaseSendPreview {
  purchaseOrderVersion: string; supplierVersion: string; recipientEmail: string | null; senderEmail: string;
  document: PurchaseOrder; documentHtml: string; reviewToken: string; blockReasons: string[];
}
export type PurchaseSendWrite = { kind: 'initial'; reviewToken: string } | { kind: 'retry'; deliveryId: string };
export interface PurchaseSendResult { operationId: string; purchaseOrderId: string; deliveryId: string; state: PurchaseOrder['state']; sendStatus: PurchaseOrder['sendStatus'] }
export interface PurchaseDelivery {
  purchaseOrderId: string; purchaseOrderVersion: string; deliveryId: string | null; messageId: string | null;
  recipientEmail: string | null; senderEmail: string | null; sendStatus: PurchaseOrder['sendStatus']; attempts: number; cycleAttempts: number;
  requestedAt: string | null; lastAttemptAt: string | null; nextAttemptAt: string | null; acceptedAt: string | null;
  lastErrorCode: string | null; lastErrorSummary: string | null; acceptanceUncertain: boolean; canRetry: boolean;
}

export const purchaseStateLabel = (state: PurchaseOrder['state']) => ({ Draft: 'Borrador', Sent: 'Enviado', PartiallyReceived: 'Recibido parcialmente', Received: 'Recibido' })[state];
export const purchaseSendLabel = (state: PurchaseOrder['sendStatus']) => ({ None: 'Sin solicitar', Pending: 'Envío pendiente', Accepted: 'Aceptado por el servicio de correo', Failed: 'Error' })[state];

export function exactDecimal(text: string, scale: number): string {
  const normalized = text.trim().replace(',', '.');
  if (!/^\d+(?:\.\d+)?$/.test(normalized)) throw new Error('Indica un decimal válido.');
  const [whole, fraction = ''] = normalized.split('.');
  if (fraction.slice(scale).replaceAll('0', '')) throw new Error(`Se admiten hasta ${scale} decimales.`);
  return `${whole.replace(/^0+(?=\d)/, '')}.${fraction.slice(0, scale).padEnd(scale, '0')}`;
}
