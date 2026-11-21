import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, expand, firstValueFrom, map, reduce } from 'rxjs';
import { MasterDataMutationsService, MutationResult } from '../../../core/http/master-data-mutations.service';
import { LocationSummary, Page } from '../../locations/data/location.models';
import {
  CYCLE_COUNTS, CandidatePage, CountCancellation, CountConfirmation, CountContext, CountDetail, CountLinesWrite, CountMutation,
  CountPage, CountReviewWrite, CountView, NEW_COUNT_ROUTE, ShipmentCandidate, countResource, countRoute
} from './cycle-counts.models';

/** Máximo de página de las listas (contracts/api.md § Convenciones). */
const FULL_PAGE_SIZE = 100;

/** El detalle y el contexto se leyeron en revisiones distintas y no coincidieron tras reintentar. */
export class CountChangedError extends Error {
  constructor() { super('El conteo cambió durante la consulta. Vuelve a cargarlo.'); }
}

/**
 * HU-04 T014 · Conteos (D-15). Lecturas como HU-03: detalle con ETag + contexto sin ETag, releídos hasta
 * coincidir (D-12). Las mutaciones usan el transporte común de A (PendingOperationStore.begin +
 * MasterDataMutationsService.send) con appRoute; clave, cuerpo congelado e If-Match quedan en la intención.
 */
@Injectable({ providedIn: 'root' })
export class CycleCountsApiService {
  private readonly http = inject(HttpClient);
  private readonly mutations = inject(MasterDataMutationsService);

  list(state: string, page = 1): Promise<CountPage> {
    let params = new HttpParams().set('page', page).set('pageSize', 25);
    if (state) params = params.set('state', state);
    return firstValueFrom(this.http.get<CountPage>(CYCLE_COUNTS, { params }));
  }

  /** Dos lecturas de la misma revisión; si no coinciden, vuelve a leer ambas (hasta 3 veces). */
  async detail(id: string): Promise<CountDetail> {
    for (let attempt = 0; attempt < 3; attempt++) {
      const response = await firstValueFrom(this.http.get<CountView>(countResource(id), { observe: 'response' }));
      const context = await firstValueFrom(this.http.get<CountContext>(`${countResource(id)}/context`));
      const etag = response.headers.get('ETag');
      if (response.body && etag && context.countVersion === etag) return { count: response.body, context, etag };
    }
    throw new CountChangedError();
  }

  /** Ubicaciones activas de todas las páginas (patrón allRows de HU-05). */
  activeLocations(): Observable<LocationSummary[]> {
    const read = (page: number) => this.http.get<Page<LocationSummary>>('/api/v1/locations',
      { params: new HttpParams().set('status', 'active').set('page', page).set('pageSize', FULL_PAGE_SIZE) });
    return read(1).pipe(
      expand(result => result.items.length && result.page * result.pageSize < result.totalCount ? read(result.page + 1) : EMPTY),
      reduce((rows, result) => [...rows, ...result.items], [] as LocationSummary[]),
      map(rows => [...new Map(rows.map(row => [row.id, row])).values()]));
  }

  /** Salidas candidatas de una línea, todas las páginas (US4; contrato API § Lecturas). */
  candidates(id: string, lineId: string): Observable<ShipmentCandidate[]> {
    const read = (page: number) => this.http.get<CandidatePage>(`${countResource(id)}/lines/${lineId}/shipment-candidates`,
      { params: new HttpParams().set('page', page).set('pageSize', FULL_PAGE_SIZE) });
    return read(1).pipe(
      expand(result => result.items.length && result.page * result.pageSize < result.totalCount ? read(result.page + 1) : EMPTY),
      reduce((rows, result) => [...rows, ...result.items], [] as ShipmentCandidate[]));
  }

  create(locationIds: string[]): Promise<MutationResult<CountMutation>> {
    return this.send('POST', CYCLE_COUNTS, { locationIds }, undefined, NEW_COUNT_ROUTE);
  }

  update(id: string, locationIds: string[], etag: string): Promise<MutationResult<CountMutation>> {
    return this.send('PUT', countResource(id), { locationIds }, etag, countRoute(id));
  }

  start(id: string, etag: string): Promise<MutationResult<CountMutation>> {
    return this.send('POST', `${countResource(id)}/start`, {}, etag, countRoute(id));
  }

  saveLines(id: string, body: CountLinesWrite, etag: string): Promise<MutationResult<CountMutation>> {
    return this.send('PUT', `${countResource(id)}/lines`, body, etag, countRoute(id));
  }

  removeLine(id: string, lineId: string, etag: string): Promise<MutationResult<null>> {
    return this.send('DELETE', `${countResource(id)}/lines/${lineId}`, {}, etag, countRoute(id));
  }

  submit(id: string, etag: string): Promise<MutationResult<CountMutation>> {
    return this.send('POST', `${countResource(id)}/submit`, {}, etag, countRoute(id));
  }

  review(id: string, body: CountReviewWrite, etag: string): Promise<MutationResult<CountMutation>> {
    return this.send('PUT', `${countResource(id)}/review`, body, etag, countRoute(id));
  }

  confirm(id: string, etag: string): Promise<MutationResult<CountConfirmation>> {
    return this.send('POST', `${countResource(id)}/confirm`, {}, etag, countRoute(id));
  }

  cancel(id: string, reason: string, etag: string): Promise<MutationResult<CountCancellation>> {
    return this.send('POST', `${countResource(id)}/cancel`, { reason }, etag, countRoute(id));
  }

  private send<T>(method: 'POST' | 'PUT' | 'DELETE', resource: string, body: unknown, etag: string | undefined,
    appRoute: string): Promise<MutationResult<T>> {
    try {
      return this.mutations.send<T>(this.mutations.pending.begin(method, resource, body, etag, appRoute));
    } catch (error) {
      return Promise.reject(error);
    }
  }
}
