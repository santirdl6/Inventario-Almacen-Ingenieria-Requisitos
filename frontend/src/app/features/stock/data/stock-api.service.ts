import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, expand, map, reduce } from 'rxjs';
import { StockArticleOption, StockFilter, StockQueryResult, StockRow } from './stock.models';

/** Máximo de GET /stock (contracts/stock-api.md): las lecturas completas piden páginas de 100. */
const FULL_PAGE_SIZE = 100;

/** HU-05 T015 · Lecturas de /existencias. Solo lee: esta pantalla no tiene mutaciones. */
@Injectable({ providedIn: 'root' })
export class StockApiService {
  private readonly http = inject(HttpClient);

  stock(filter: StockFilter, pageSize = 25): Observable<StockQueryResult> {
    let params = new HttpParams().set('articleId', filter.articleId).set('includeEmpty', filter.includeEmpty)
      .set('page', filter.page).set('pageSize', pageSize);
    if (filter.locationId) params = params.set('locationId', filter.locationId);
    if (filter.lotId) params = params.set('lotId', filter.lotId);
    return this.http.get<StockQueryResult>('/api/v1/stock', { params });
  }

  /**
   * Convergence C4 (G3; FR-004, FR-015) · Todas las combinaciones ubicación-lote del artículo, sin
   * filtros: recorre las páginas de 100 hasta `totalCount`. Si una combinación se repite entre
   * páginas (cambio concurrente), se queda la última leída.
   */
  allRows(articleId: string, includeEmpty: boolean): Observable<StockRow[]> {
    const read = (page: number) => this.stock({ articleId, includeEmpty, page }, FULL_PAGE_SIZE);
    return read(1).pipe(
      expand(result => result.items.length && result.page * result.pageSize < result.totalCount ? read(result.page + 1) : EMPTY),
      reduce((rows, result) => [...rows, ...result.items], [] as StockRow[]),
      map(rows => [...new Map(rows.map(row => [`${row.location.id}-${row.lot.id}`, row])).values()]));
  }

  /** Selector propio (research D-15): SKU o nombre, incluidos los inactivos. */
  searchArticles(query: string): Observable<StockArticleOption[]> {
    const params = new HttpParams().set('q', query).set('state', 'all').set('page', 1).set('pageSize', 25);
    return this.http.get<{ items: StockArticleOption[] }>('/api/v1/articles', { params }).pipe(
      map(page => page.items.map(({ id, sku, name, baseUnit, isActive, tracksLot }) => ({ id, sku, name, baseUnit, isActive, tracksLot }))));
  }
}
