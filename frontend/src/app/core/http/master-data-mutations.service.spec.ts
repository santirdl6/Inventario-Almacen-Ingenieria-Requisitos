import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MasterDataMutationsService } from './master-data-mutations.service';
import { SessionService } from '../session/session.service';
import { PendingOperationStore } from '../operations/pending-operation.store';

describe('Recuperación de mutaciones', () => {
  let http: HttpTestingController; let mutations: MasterDataMutationsService; let session: SessionService;
  beforeEach(() => { sessionStorage.clear(); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); http = TestBed.inject(HttpTestingController); mutations = TestBed.inject(MasterDataMutationsService); session = TestBed.inject(SessionService); session.user.set({ id: 'actor', email: 'g@example.test', roles: [], permissions: ['MasterData.Manage'] }); });
  afterEach(() => { http.verify(); sessionStorage.clear(); });
  it('conserva intención y datos al expirar sesión y no reenvía al autenticar', async () => {
    const saved = mutations.mutate('POST', '/api/v1/families', { code: 'A', name: 'Borrador' }); const rejected = expect(saved).rejects.toBeDefined();
    http.expectOne('/api/v1/families').flush({}, { status: 401, statusText: 'Unauthorized' }); await rejected;
    expect(session.user()).toBeNull(); expect(mutations.pending.operations()).toHaveLength(1);
    session.user.set({ id: 'other', email: 'other@example.test', roles: [], permissions: ['MasterData.Manage'] });
    await expect(mutations.send(mutations.pending.operations()[0])).rejects.toBeDefined(); http.expectNone('/api/v1/families');
  });
  it('reautoriza cada reintento y respeta Retry-After', async () => {
    const saved = mutations.mutate('POST', '/api/v1/suppliers', { code: 'A', name: 'Proveedor' }); const rejected = expect(saved).rejects.toBeDefined();
    http.expectOne('/api/v1/suppliers').flush({}, { status: 503, statusText: 'Unavailable', headers: { 'Retry-After': '10' } }); await rejected;
    const pending = TestBed.inject(PendingOperationStore).operations()[0];
    await expect(mutations.send(pending)).rejects.toThrow('Espera'); http.expectNone('/api/v1/suppliers');
    session.user.update(user => ({ ...user!, permissions: [] }));
    await expect(mutations.send(pending)).rejects.toBeDefined(); http.expectNone('/api/v1/suppliers');
  });

  it('reenvía solo los datos originales del almacén aunque el llamador entregue otra copia', async () => {
    const pending = mutations.pending.begin('PUT', '/api/v1/families/10000000-0000-0000-0000-000000000001', { name: 'Original' }, '"AQIDBAUGBwg="');
    const result = mutations.send({ ...pending, method: 'DELETE', payload: { name: 'Alterado' }, etag: '"otra"' });
    const request = http.expectOne(pending.resource);
    expect(request.request.method).toBe('PUT'); expect(request.request.body).toEqual({ name: 'Original' });
    expect(request.request.headers.get('If-Match')).toBe(pending.etag);
    request.flush({ resource: { id: 'family' } }); await result;
    await expect(mutations.send(pending)).rejects.toThrow('ya no está pendiente');
    http.expectNone(pending.resource);
  });
});
