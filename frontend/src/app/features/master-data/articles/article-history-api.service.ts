import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ArticleMovementPage, MovementFilters, MovementFilterField, MovementFilterPage } from './article-history.models';
import { Article } from './article.models';

@Injectable({ providedIn: 'root' })
export class ArticleHistoryApiService {
  private readonly http = inject(HttpClient);
  article(id: string): Promise<Article> { return firstValueFrom(this.http.get<Article>(`/api/v1/articles/${id}`)); }
  options(id: string, field: MovementFilterField, q: string, page: number): Promise<MovementFilterPage> {
    const params = new HttpParams().set('field', field).set('q', q).set('page', page).set('pageSize', 25);
    return firstValueFrom(this.http.get<MovementFilterPage>(`/api/v1/articles/${id}/movements/filters`, { params }));
  }
  list(id: string, filters: MovementFilters, page: number): Promise<ArticleMovementPage> {
    let params = new HttpParams().set('page', page).set('pageSize', 25);
    for (const [key, value] of Object.entries(filters)) if (value.trim()) params = params.set(key, key === 'from' || key === 'to' ? new Date(value).toISOString() : value.trim());
    return firstValueFrom(this.http.get<ArticleMovementPage>(`/api/v1/articles/${id}/movements`, { params }));
  }
}
