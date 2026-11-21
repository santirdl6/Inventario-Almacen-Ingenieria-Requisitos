import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PendingOperationStore } from './pending-operation.store';
import { MasterDataMutationsService } from '../http/master-data-mutations.service';
import { SessionService } from '../session/session.service';

describe('Intenciones de compras', () => {
  const id = '11111111-1111-1111-1111-111111111111';
  beforeEach(() => {
    sessionStorage.clear(); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'buyer-1', email: 'buyer@test', roles: [], permissions: ['Purchasing.Manage', 'Purchasing.Send'] });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); sessionStorage.clear(); });

  it('una edición incierta impide solicitar envío y conserva la intención original', () => {
    const store = TestBed.inject(PendingOperationStore);
    const original = store.begin('PUT', `/api/v1/purchase-orders/${id}`, { lines: [] }, '"v1"');
    expect(() => store.begin('POST', `/api/v1/purchase-orders/${id}/send`, { kind: 'initial', reviewToken: 'token' }, '"v1"')).toThrow();
    expect(store.forResource(`/api/v1/purchase-orders/${id}/send`)?.operationId).toBe(original.operationId);
  });

  it('un conflicto idempotente conserva la referencia para comprobarla', async () => {
    const mutations = TestBed.inject(MasterDataMutationsService);
    const pending = mutations.mutate('POST', '/api/v1/purchase-orders', { lines: [] });
    const failure = expect(pending).rejects.toBeDefined();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/purchase-orders');
    request.flush({ code: 'IdempotencyConflict' }, { status: 409, statusText: 'Conflict' });
    await failure;
    expect(mutations.pending.operations()).toHaveLength(1);
  });

  it('restaura la intención de compra tras recargar y retira sus datos al cerrar sesión', async () => {
    const store = TestBed.inject(PendingOperationStore);
    const original = store.begin('PUT', `/api/v1/purchase-orders/${id}`, { quantity: '10.000' }, '"v1"');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService);
    session.user.set({ id: 'buyer-1', email: 'buyer@test', roles: [], permissions: ['Purchasing.Manage'] });
    const restored = TestBed.inject(PendingOperationStore);
    expect(restored.forResource(`/api/v1/purchase-orders/${id}`)?.operationId).toBe(original.operationId);
    expect(restored.forResource(`/api/v1/purchase-orders/${id}`)?.payload).toEqual({ quantity: '10.000' });
    const http = TestBed.inject(HttpTestingController);
    const closing = session.logout(); http.expectOne('/api/v1/auth/csrf').flush({});
    await Promise.resolve(); http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await closing;
    expect(restored.operations()[0].payload).toBeUndefined();
    expect(restored.operations()[0].etag).toBeUndefined();
    session.user.set({ id: 'buyer-2', email: 'other@test', roles: [], permissions: ['Purchasing.Manage'] });
    expect(restored.forResource(`/api/v1/purchase-orders/${id}`)).toBeUndefined();
    const other = restored.begin('PUT', `/api/v1/purchase-orders/${id}`, { quantity: '10.000' }, '"v1"');
    expect(other.operationId).not.toBe(original.operationId);
  });

  it.each([true, false])('la respuesta tras logout conserva la referencia incluso al volver a la misma cuenta=%s', async sameActor => {
    const mutations = TestBed.inject(MasterDataMutationsService); const session = TestBed.inject(SessionService);
    const http = TestBed.inject(HttpTestingController);
    const pending = mutations.mutate('POST', '/api/v1/purchase-orders', { lines: [] });
    const response = http.expectOne('/api/v1/purchase-orders');
    const key = response.request.headers.get('Idempotency-Key');
    const failure = expect(pending).rejects.toThrow(/sesión|identidad/);
    const closing = session.logout(); http.expectOne('/api/v1/auth/csrf').flush({});
    await Promise.resolve(); http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' }); await closing;
    session.user.set({ id: sameActor ? 'buyer-1' : 'buyer-2', email: 'buyer@test', roles: [], permissions: ['Purchasing.Manage'] });
    response.flush({ operationId: key, resource: { id } }, { status: 201, statusText: 'Created' });
    await failure;
    expect(mutations.pending.operations()).toHaveLength(1);
    expect(mutations.pending.operations()[0].operationId).toBe(key);
    expect(mutations.pending.operations()[0].payload).toBeUndefined();
    expect(mutations.pending.operations()[0].etag).toBeUndefined();
  });

  it('un 401 tardío de la sesión anterior no cierra la cuenta nueva', async () => {
    const mutations = TestBed.inject(MasterDataMutationsService); const session = TestBed.inject(SessionService);
    const http = TestBed.inject(HttpTestingController);
    const pending = mutations.mutate('POST', '/api/v1/purchase-orders', { lines: [] });
    const request = http.expectOne('/api/v1/purchase-orders'); const failure = expect(pending).rejects.toBeDefined();
    const closing = session.logout(); http.expectOne('/api/v1/auth/csrf').flush({}); await Promise.resolve();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' }); await closing;
    session.user.set({ id: 'buyer-2', email: 'other@test', roles: [], permissions: ['Purchasing.Manage'] });
    request.flush({ code: 'Unauthenticated' }, { status: 401, statusText: 'Unauthorized' }); await failure;
    expect(session.user()?.id).toBe('buyer-2');
    expect(mutations.pending.operations()).toHaveLength(1);
  });
});
