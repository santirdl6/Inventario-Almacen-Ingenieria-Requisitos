import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { Article, ArticleContext, ArticlePage, ArticleWrite } from './article.models';

@Injectable({ providedIn: 'root' })
export class ArticlesApiService {
  private readonly http = inject(HttpClient); private readonly mutations = inject(MasterDataMutationsService);
  list(q: string, state: string, familyId: string, page: number): Promise<ArticlePage> {
    let params = new HttpParams().set('q', q).set('state', state).set('page', page).set('pageSize', 25);
    if (familyId) params = params.set('familyId', familyId);
    return firstValueFrom(this.http.get<ArticlePage>('/api/v1/articles', { params }));
  }
  async detail(id: string): Promise<{ resource: Article; context: ArticleContext; etag: string }> {
    const response = await firstValueFrom(this.http.get<Article>(`/api/v1/articles/${id}`, { observe: 'response' }));
    const context = await firstValueFrom(this.http.get<ArticleContext>(`/api/v1/articles/${id}/context`));
    const etag = response.headers.get('ETag');
    if (!response.body || !etag || context.articleVersion !== etag)
      throw new Error('El artículo cambió durante la consulta. Vuelve a cargarlo antes de editar.');
    return { resource: response.body, context, etag };
  }
  async save(id: string | null, value: ArticleWrite, etag?: string): Promise<Article> {
    const response = await this.mutations.mutate<{ operationId: string; resource: Article }>(id ? 'PUT' : 'POST',
      id ? `/api/v1/articles/${id}` : '/api/v1/articles', value, etag);
    if (!response.body?.resource) throw new Error('Comprueba el resultado antes de repetir la operación.');
    return response.body.resource;
  }
}
