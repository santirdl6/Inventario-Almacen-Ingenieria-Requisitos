import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { StockArticleOption, StockFilter, StockQueryResult } from './stock.models';

/** HU-05 T015 · Lecturas de /existencias. Solo lee: esta pantalla no tiene mutaciones. */
@Injectable({ providedIn: 'root' })
export class StockApiService {
  private readonly http = inject(HttpClient);

  stock(filter: StockFilter): Observable<StockQueryResult> {
    let params = new HttpParams().set('articleId', filter.articleId).set('includeEmpty', filter.includeEmpty)
      .set('page', filter.page).set('pageSize', 25);
    if (filter.locationId) params = params.set('locationId', filter.locationId);
    if (filter.lotId) params = params.set('lotId', filter.lotId);
    return this.http.get<StockQueryResult>('/api/v1/stock', { params });
  }

  /** Selector propio (research D-15): SKU o nombre, incluidos los inactivos. */
  searchArticles(query: string): Observable<StockArticleOption[]> {
    const params = new HttpParams().set('q', query).set('state', 'all').set('page', 1).set('pageSize', 25);
    return this.http.get<{ items: StockArticleOption[] }>('/api/v1/articles', { params }).pipe(
      map(page => page.items.map(({ id, sku, name, baseUnit, isActive, tracksLot }) => ({ id, sku, name, baseUnit, isActive, tracksLot }))));
  }
}
