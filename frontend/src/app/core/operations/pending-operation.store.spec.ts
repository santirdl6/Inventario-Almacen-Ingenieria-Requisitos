import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionService } from '../session/session.service';
import { PendingOperationStore } from './pending-operation.store';
import { MasterDataMutationsService } from '../http/master-data-mutations.service';

describe('Intenciones de maestros', () => {
  let store: PendingOperationStore;
  let http: HttpTestingController;
  let mutations: MasterDataMutationsService;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'actor', email: 'g@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    store = TestBed.inject(PendingOperationStore); http = TestBed.inject(HttpTestingController);
    mutations = TestBed.inject(MasterDataMutationsService);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  it('mantiene la petición original ante timeout, 404 y reintento', async () => {
    const result = mutations.mutate('POST', '/api/v1/families', { code: 'A', name: 'Familia' });
    const rejected = expect(result).rejects.toBeDefined();
    const request = http.expectOne('/api/v1/families');
    const key = request.request.headers.get('Idempotency-Key');
    request.error(new ProgressEvent('error'));
    await rejected;
    const pending = store.operations()[0]; expect(pending.operationId).toBe(key);
    const check = mutations.check(pending);
    http.expectOne(`/api/v1/operations/${key}`).flush({}, { status: 404, statusText: 'Not Found' });
    expect(await check).toBeNull(); expect(store.operations()).toHaveLength(1);
    const retry = mutations.send(pending);
    const replay = http.expectOne('/api/v1/families');
    expect(replay.request.headers.get('Idempotency-Key')).toBe(key);
    expect(replay.request.body).toEqual({ code: 'A', name: 'Familia' });
    replay.flush({ operationId: key, resource: { id: 'family' } }, { status: 201, statusText: 'Created' });
    await retry; expect(store.operations()).toHaveLength(0);
  });

  it('no duplica un envío simultáneo ni cambia su payload original', async () => {
    const body = { code: 'A', name: 'Familia' };
    const first = mutations.mutate('POST', '/api/v1/families', body);
    const second = mutations.mutate('POST', '/api/v1/families', body);
    const request = http.expectOne('/api/v1/families');
    body.name = 'Modificado';
    expect(request.request.body).toEqual({ code: 'A', name: 'Familia' });
    expect(() => store.begin('POST', '/api/v1/families', body)).toThrow();
    request.flush({ ok: true }); await Promise.all([first, second]);
    expect(store.operations()).toHaveLength(0);
  });

  it('no permite persistir credenciales de login en una intención de negocio', () => {
    expect(() => store.begin('POST', '/api/v1/auth/login', { password: 'no-se-guarda' })).toThrow();
    expect(store.operations()).toHaveLength(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('bloquea otra acción sobre el mismo recurso hasta resolver la intención pendiente', () => {
    const resource = '/api/v1/articles/10000000-0000-0000-0000-000000000001';
    const pending = store.begin('PUT', resource, { name: 'Edición pendiente' }, '"AQIDBAUGBwg="');
    expect(() => store.begin('DELETE', resource, null, '"AQIDBAUGBwg="')).toThrow('Comprueba la intención pendiente');
    expect(store.operations()).toEqual([pending]);
    store.resolve(pending.operationId);
    expect(store.begin('DELETE', resource, null, '"AQIDBAUGBwg="').operationId).not.toBe(pending.operationId);
  });

  it('restaura una intención DELETE sin perder clave, payload nulo o ETag', async () => {
    const resource = '/api/v1/articles/10000000-0000-0000-0000-000000000001';
    const original = store.begin('DELETE', resource, undefined, '"AQIDBAUGBwg="');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'actor', email: 'g@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    http = TestBed.inject(HttpTestingController);
    const restored = TestBed.inject(PendingOperationStore).operations()[0];
    expect(restored.operationId).toBe(original.operationId); expect(restored.payload).toBeNull();
    const result = TestBed.inject(MasterDataMutationsService).send(restored);
    const request = http.expectOne(resource);
    expect(request.request.headers.get('If-Match')).toBe(original.etag!);
    expect(request.request.headers.get('Idempotency-Key')).toBe(original.operationId);
    request.flush(null, { status: 204, statusText: 'No Content' }); await result;
  });

  it('al cerrar sesión conserva solo la referencia y no reenvía datos bajo otra identidad', async () => {
    const operation = store.begin('POST', '/api/v1/families', { code: 'A', name: 'Temporal' });
    const session = TestBed.inject(SessionService);
    const logout = session.logout();
    http.expectOne('/api/v1/auth/csrf').flush({ requestToken: 'token' });
    await Promise.resolve();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await logout;
    const reference = store.operations()[0];
    expect(reference.operationId).toBe(operation.operationId); expect('payload' in reference).toBe(false);
    expect(sessionStorage.getItem('inventario.pending-operations')).not.toContain('Temporal');
    session.user.set({ id: 'actor', email: 'g@example.test', roles: [], permissions: ['MasterData.Manage'] });
    await expect(mutations.send(operation)).rejects.toBeDefined();
    session.user.set({ id: 'another', email: 'b@example.test', roles: [], permissions: ['MasterData.Manage'] });
    await expect(mutations.send(reference)).rejects.toBeDefined();
    http.expectNone('/api/v1/families');
  });
});
