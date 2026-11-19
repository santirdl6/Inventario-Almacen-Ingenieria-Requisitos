import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TimeoutError, firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { asProblem, fieldMessages, isUncertain } from '../shared/operation';
import { detail, flushNetworkError, flushProblem, httpMock, locationsTestProviders, mutation } from '../testing/locations-testing';
import { LocationsApi } from './locations-api.service';

describe('LocationsApi', () => {
  let api: LocationsApi;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: locationsTestProviders() });
    api = TestBed.inject(LocationsApi);
  });

  afterEach(() => httpMock().verify());

  it('lista con filtros y sin enviar filtros vacíos', async () => {
    const result = firstValueFrom(api.list({ search: '  p01 ', kind: '', status: 'all', page: 2 }));
    const request = httpMock().expectOne(r => r.url === '/api/v1/locations');
    expect(request.request.params.get('search')).toBe('p01');
    expect(request.request.params.has('kind')).toBe(false);
    expect(request.request.params.get('status')).toBe('all');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 25, totalCount: 0 });
    expect((await result).page).toBe(2);
  });

  it('get devuelve el ETag de la respuesta', async () => {
    const result = firstValueFrom(api.get('abc'));
    httpMock().expectOne('/api/v1/locations/abc').flush(detail(), { headers: { ETag: '"AAA="' } });
    expect((await result).etag).toBe('"AAA="');
  });

  it('create envía Idempotency-Key y devuelve el recurso de la mutación', async () => {
    const body = { code: 'P01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage' as const };
    const result = firstValueFrom(api.create(body, 'key-1'));
    const request = httpMock().expectOne('/api/v1/locations');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(request.request.body).toEqual(body);
    request.flush(mutation(detail({ code: 'P01' })), { status: 201, statusText: 'Created', headers: { ETag: '"V1"' } });
    const created = await result;
    expect(created.etag).toBe('"V1"');
    expect(created.value.code).toBe('P01');
  });

  it('update envía If-Match e Idempotency-Key, sin reconocimiento de recepción', async () => {
    const body = { code: 'P01', aisle: 'P01', rack: 'E01', level: 'N02', kind: 'Storage' as const, isActive: true };
    const result = firstValueFrom(api.update('id-1', body, '"V1"', 'key-2'));
    const request = httpMock().expectOne('/api/v1/locations/id-1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"V1"');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key-2');
    expect(Object.keys(request.request.body)).not.toContain('acknowledgeNoActiveReceiving');
    request.flush(mutation(detail({ level: 'N02' })), { headers: { ETag: '"V2"' } });
    expect((await result).value.level).toBe('N02');
  });

  it('consulta contenido y ubicaciones de un artículo', () => {
    api.stock('loc-1').subscribe();
    api.articleLocations('art-1', 3).subscribe();
    expect(httpMock().expectOne(r => r.url === '/api/v1/locations/loc-1/stock').request.params.get('page')).toBe('1');
    expect(httpMock().expectOne(r => r.url === '/api/v1/articles/art-1/locations').request.params.get('page')).toBe('3');
  });

  it('los errores se leen con apiProblem: código, detalle y campos', async () => {
    const result = firstValueFrom(api.create({ code: 'P01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage' }, 'k'));
    flushProblem(httpMock().expectOne('/api/v1/locations'), 409, 'DuplicateCode', 'El código ya existe.',
      { errors: { Code: ['El código ya existe.'] } });
    const problem = asProblem(await result.catch(error => error));
    expect(problem.status).toBe(409);
    expect(problem.code).toBe('DuplicateCode');
    expect(problem.detail).toBe('El código ya existe.');
    expect(fieldMessages(problem)).toEqual({ code: 'El código ya existe.' });
  });

  it('red caída, 5xx y timeout dejan el resultado incierto; 4xx es definitivo', async () => {
    const network = firstValueFrom(api.get('x'));
    flushNetworkError(httpMock().expectOne('/api/v1/locations/x'));
    const error = await network.catch(e => e);
    expect(isUncertain(error)).toBe(true);
    expect(asProblem(error).detail).toContain('No hay conexión');
    expect(isUncertain(new HttpErrorResponse({ status: 503 }))).toBe(true);
    expect(isUncertain(new TimeoutError())).toBe(true);
    expect(isUncertain(new HttpErrorResponse({ status: 412 }))).toBe(false);
  });
});
