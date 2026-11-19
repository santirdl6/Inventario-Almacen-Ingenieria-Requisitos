import { HttpClient, HttpHeaders, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  ArticleLocations,
  CreateLocationRequest,
  LocationDetail,
  LocationMutation,
  LocationQuery,
  LocationStock,
  LocationSummary,
  Page,
  UpdateLocationRequest,
  Versioned
} from './location.models';

const BASE = '/api/v1';

/**
 * Cliente HTTP de HU-02. El XSRF lo añade HttpClient (XSRF-TOKEN → X-XSRF-TOKEN). Los errores
 * llegan como HttpErrorResponse y las pantallas los interpretan con apiProblem() de A (vía
 * shared/operation.ts). Las mutaciones exigen Idempotency-Key y, en las modificaciones,
 * If-Match con el ETag leído (contrato HTTP común).
 */
@Injectable({ providedIn: 'root' })
export class LocationsApi {
  private readonly http = inject(HttpClient);

  list(query: LocationQuery = {}): Observable<Page<LocationSummary>> {
    let params = new HttpParams();
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.kind) params = params.set('kind', query.kind);
    if (query.status) params = params.set('status', query.status);
    if (query.page) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    return this.http.get<Page<LocationSummary>>(`${BASE}/locations`, { params });
  }

  get(id: string): Observable<Versioned<LocationDetail>> {
    return this.http
      .get<LocationDetail>(`${BASE}/locations/${encodeURIComponent(id)}`, { observe: 'response' })
      .pipe(map(response => ({ value: response.body as LocationDetail, etag: response.headers.get('ETag') ?? '' })));
  }

  create(body: CreateLocationRequest, idempotencyKey: string): Observable<Versioned<LocationDetail>> {
    return this.http
      .post<LocationMutation>(`${BASE}/locations`, body, {
        observe: 'response',
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey })
      })
      .pipe(map(mutated));
  }

  update(id: string, body: UpdateLocationRequest, etag: string, idempotencyKey: string): Observable<Versioned<LocationDetail>> {
    return this.http
      .put<LocationMutation>(`${BASE}/locations/${encodeURIComponent(id)}`, body, {
        observe: 'response',
        headers: new HttpHeaders({ 'If-Match': etag, 'Idempotency-Key': idempotencyKey })
      })
      .pipe(map(mutated));
  }

  stock(id: string, page = 1): Observable<LocationStock> {
    return this.http.get<LocationStock>(`${BASE}/locations/${encodeURIComponent(id)}/stock`, {
      params: new HttpParams().set('page', page)
    });
  }

  articleLocations(articleId: string, page = 1): Observable<ArticleLocations> {
    return this.http.get<ArticleLocations>(`${BASE}/articles/${encodeURIComponent(articleId)}/locations`, {
      params: new HttpParams().set('page', page)
    });
  }
}

function mutated(response: HttpResponse<LocationMutation>): Versioned<LocationDetail> {
  return { value: (response.body as LocationMutation).resource, etag: response.headers.get('ETag') ?? '' };
}
