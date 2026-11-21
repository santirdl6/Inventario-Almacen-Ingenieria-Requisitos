import { PurchaseOrder, PurchaseOrderContext, PurchaseDelivery } from '../data/purchase-order.models';

export function purchase(overrides: Partial<PurchaseOrder> = {}): PurchaseOrder {
  return { id: '11111111-1111-1111-1111-111111111111', number: 'PO-00000001',
    supplierId: '22222222-2222-2222-2222-222222222222',
    supplierSnapshot: { id: '22222222-2222-2222-2222-222222222222', code: 'PROV', name: 'Proveedor histórico', email: null, taxIdentifier: null, schemaVersion: 1 },
    currency: 'EUR', expectedDeliveryDate: '2026-12-01', state: 'Draft', sendStatus: 'None', deliveryId: null,
    createdBy: 'buyer-1', createdAt: '2026-11-20T10:00:00Z', sentAt: null,
    lines: [{ id: '33333333-3333-3333-3333-333333333333', position: 1, articleId: '44444444-4444-4444-4444-444444444444',
      articleSnapshot: { id: '44444444-4444-4444-4444-444444444444', sku: 'ART', name: 'Artículo histórico', baseUnit: 'UD', allowsFractional: false, tracksLot: false, schemaVersion: 1 },
      orderedQuantity: '10.000', receivedQuantity: '0.000', pendingQuantity: '10.000', unitPrice: '0.00', expectedDeliveryDate: '2026-12-01' }], ...overrides };
}
export function purchaseContext(): PurchaseOrderContext {
  return { purchaseOrderVersion: '"v1"', supplier: { id: purchase().supplierId, code: 'PROV', name: 'Proveedor actual', email: null, isActive: false, version: '"s1"' },
    articles: [{ id: purchase().lines[0].articleId, isActive: false, version: '"a1"' }], editBlockReasons: [], sendBlockReasons: ['Completa el correo antes de enviar.'] };
}

export function delivery(overrides: Partial<PurchaseDelivery> = {}): PurchaseDelivery {
  return { purchaseOrderId: purchase().id, purchaseOrderVersion: '"v2"', deliveryId: '55555555-5555-5555-5555-555555555555',
    messageId: 'message@test', recipientEmail: 'revisado@test', senderEmail: 'inventario@test', sendStatus: 'Pending', attempts: 0, cycleAttempts: 0,
    requestedAt: '2026-11-20T10:00:00Z', lastAttemptAt: null, nextAttemptAt: null, acceptedAt: null, lastErrorCode: null, lastErrorSummary: null,
    acceptanceUncertain: false, canRetry: false, ...overrides };
}
