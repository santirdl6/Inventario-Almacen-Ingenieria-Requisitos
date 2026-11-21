import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { PurchaseOrderSendPage } from './purchase-order-send.page';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { locationsTestProviders, signIn } from '../../locations/testing/locations-testing';
import { purchase, purchaseContext, delivery } from '../testing/purchasing-testing';

describe('Retry conocido de correo', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: locationsTestProviders({ id: purchase().id }) }); signIn(['Inventory.Read', 'Purchasing.Send']);
    const api = TestBed.inject(PurchaseOrdersApiService);
    vi.spyOn(api, 'detail').mockResolvedValue({ resource: purchase({ sendStatus: 'Failed', deliveryId: delivery().deliveryId }), etag: '"v2"', context: { ...purchaseContext(), purchaseOrderVersion: '"v2"' } });
    vi.spyOn(api, 'delivery').mockResolvedValue(delivery({ sendStatus: 'Failed', canRetry: true, acceptanceUncertain: true }));
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); sessionStorage.clear(); vi.restoreAllMocks(); });
  it('reintenta con la entrega congelada y otra intención, sin usar el correo actual del maestro', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderSendPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const api = TestBed.inject(PurchaseOrdersApiService);
    const send = vi.spyOn(api, 'requestSend').mockImplementation(async () => {
      vi.mocked(api.detail).mockResolvedValue({ resource: purchase({ sendStatus: 'Pending', deliveryId: delivery().deliveryId }), etag: '"v3"', context: { ...purchaseContext(), purchaseOrderVersion: '"v3"' } });
      vi.mocked(api.delivery).mockResolvedValue(delivery({ purchaseOrderVersion: '"v3"' }));
      return { operationId: 'new-operation', purchaseOrderId: purchase().id, deliveryId: delivery().deliveryId!, state: 'Draft', sendStatus: 'Pending' };
    });
    await fixture.componentInstance.retry();
    expect(send).toHaveBeenCalledWith(purchase().id, { kind: 'retry', deliveryId: delivery().deliveryId }, '"v2"');
    fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('revisado@test');
    expect(fixture.nativeElement.textContent).toContain('Envío pendiente');
  });

  it('404 de operación no resuelve la incertidumbre ni permite una intención incompatible', async () => {
    const mutations = TestBed.inject(MasterDataMutationsService); const http = TestBed.inject(HttpTestingController);
    const path = `/api/v1/purchase-orders/${purchase().id}/send`;
    const original = mutations.pending.begin('POST', path, { kind: 'retry', deliveryId: delivery().deliveryId }, '"v2"');
    const check = mutations.check(original);
    http.expectOne(`/api/v1/operations/${original.operationId}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    expect(await check).toBeNull(); expect(mutations.pending.forResource(path)?.operationId).toBe(original.operationId);
    expect(() => mutations.pending.begin('POST', path, { kind: 'initial', reviewToken: 'new' }, '"v3"')).toThrow();
  });

  it('Retry-After retrasa la repetición y conserva clave, cuerpo y ETag originales', async () => {
    const mutations = TestBed.inject(MasterDataMutationsService); const http = TestBed.inject(HttpTestingController);
    const now = Date.now(); const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    const path = `/api/v1/purchase-orders/${purchase().id}/send`;
    const body = { kind: 'retry', deliveryId: delivery().deliveryId };
    const original = mutations.pending.begin('POST', path, body, '"v2"');
    const first = mutations.send(original); const failure = expect(first).rejects.toBeDefined();
    http.expectOne(path).flush({ status: 503, code: 'LockTimeout' }, { status: 503, statusText: 'Unavailable', headers: { 'Retry-After': '2' } }); await failure;
    await expect(mutations.send(original)).rejects.toThrow(/Espera/); http.expectNone(path);
    clock.mockReturnValue(now + 2_100);
    const second = mutations.send(original); const request = http.expectOne(path);
    expect(request.request.headers.get('Idempotency-Key')).toBe(original.operationId); expect(request.request.headers.get('If-Match')).toBe('"v2"');
    expect(request.request.body).toEqual(body); request.flush({ deliveryId: delivery().deliveryId }, { status: 202, statusText: 'Accepted' }); await second;
  });
});
