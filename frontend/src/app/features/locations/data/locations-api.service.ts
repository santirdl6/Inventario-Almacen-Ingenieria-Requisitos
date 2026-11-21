import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, Injector, inject } from '@angular/core';
import { pendingUntilEvent } from '@angular/core/rxjs-interop';
import { Observable, defer, from, map } from 'rxjs';
import { MasterDataMutationsService, MutationResult } from '../../../core/http/master-data-mutations.service';
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
  private readonly mutations = inject(MasterDataMutationsService);
  private readonly injector = inject(Injector);

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

  create(body: CreateLocationRequest): Observable<Versioned<LocationDetail>> {
    return defer(() => from(this.mutations.mutate<LocationMutation>('POST', `${BASE}/locations`, body))).pipe(map(mutated), pendingUntilEvent(this.injector));
  }

  update(id: string, body: UpdateLocationRequest, etag: string): Observable<Versioned<LocationDetail>> {
    return defer(() => from(this.mutations.mutate<LocationMutation>('PUT', `${BASE}/locations/${encodeURIComponent(id)}`, body, etag))).pipe(map(mutated), pendingUntilEvent(this.injector));
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

function mutated(response: MutationResult<LocationMutation>): Versioned<LocationDetail> {
  return { value: (response.body as LocationMutation).resource, etag: response.etag ?? '' };
}
