import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ReceiptFormPage } from './receipt-form.page';
import { ReceiptsApiService } from '../data/receipts-api.service';
import { SessionService } from '../../../core/session/session.service';

describe('Revisión de recepción', () => {
  it('conserva el borrador y no declara éxito cuando se rechaza la confirmación', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'operario', email: 'op@recepcion.test', roles: ['Operario'], permissions: ['Receiving.Record'] });
    const fixture = TestBed.createComponent(ReceiptFormPage);
    const page = fixture.componentInstance;
    page.id.set('recepcion');
    page.draft = { purchaseOrderId: 'compra', occurredAt: '2026-11-21T10:00:00Z', lines: [{ id: 'línea', purchaseOrderLineId: 'pedido',
      acceptedQuantity: '6', reportedQuantity: '6', allocations: [] }], incidents: [] };
    const api = TestBed.inject(ReceiptsApiService);
    const confirm = vi.spyOn(api, 'confirm').mockRejectedValue(new Error('La recepción cambió de revisión.'));
    await page.confirm();
    expect(confirm).not.toHaveBeenCalled();
    page.reviewed.set({ receipt: { ...page.draft, id: 'recepcion', state: 'Draft', movementId: null, counterpartyReference: null, recordedAt: null, actorId: null },
      etag: '"revisión"', context: { receiptVersion: '"revisión"', purchaseOrderVersion: '"compra"', orderNumber: 'PO-1', lines: [] } });
    await page.confirm();
    expect(confirm).toHaveBeenCalledWith('recepcion', '"revisión"');
    expect(page.draft.lines[0].acceptedQuantity).toBe('6');
    expect(page.receipt()?.state).not.toBe('Confirmed');
    expect(page.error()).toContain('cambió de revisión');
  });
});
