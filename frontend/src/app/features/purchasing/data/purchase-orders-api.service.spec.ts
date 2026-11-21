import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PurchaseOrdersApiService } from './purchase-orders-api.service';
import { SessionService } from '../../../core/session/session.service';

describe('Transporte HTTP de compras', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'buyer-1', email: 'buyer@test', roles: ['Comprador'],
      permissions: ['Inventory.Read', 'Purchasing.Manage', 'Purchasing.Send'] });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); sessionStorage.clear(); });

  it('conserva clave y precondición al guardar mediante el transporte común', async () => {
    const http = TestBed.inject(HttpTestingController);
    const api = TestBed.inject(PurchaseOrdersApiService);
    const id = '11111111-1111-1111-1111-111111111111';
    const value = { supplierId: '22222222-2222-2222-2222-222222222222', expectedDeliveryDate: '2026-12-01',
      lines: [{ id: crypto.randomUUID(), articleId: '33333333-3333-3333-3333-333333333333', quantity: '10.000', unitPrice: '0.00', expectedDeliveryDate: null }] };
    const saving = api.save(id, value, '"v1"');
    const request = http.expectOne(`/api/v1/purchase-orders/${id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"v1"');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body).toEqual(value);
    request.flush({ operationId: request.request.headers.get('Idempotency-Key'), resource: { id } }, { headers: { ETag: '"v2"' } });
    expect((await saving).id).toBe(id);
  });

  it('rechaza combinar detalle y contexto de otra revisión', async () => {
    const http = TestBed.inject(HttpTestingController);
    const promise = TestBed.inject(PurchaseOrdersApiService).detail('order-1');
    const failure = expect(promise).rejects.toThrow(/cambi|revisión/i);
    http.expectOne('/api/v1/purchase-orders/order-1').flush({ id: 'order-1' }, { headers: { ETag: '"v1"' } });
    await Promise.resolve();
    http.expectOne('/api/v1/purchase-orders/order-1/context').flush({ purchaseOrderVersion: '"v2"' });
    await failure;
  });

  it('acepta contexto que termina primero solo si corresponde al ETag del detalle', async () => {
    const http = TestBed.inject(HttpTestingController);
    const promise = TestBed.inject(PurchaseOrdersApiService).detail('order-1');
    http.expectOne('/api/v1/purchase-orders/order-1/context').flush({ purchaseOrderVersion: '"v1"' });
    http.expectOne('/api/v1/purchase-orders/order-1').flush({ id: 'order-1' }, { headers: { ETag: '"v1"' } });
    expect((await promise).etag).toBe('"v1"');
  });
});
