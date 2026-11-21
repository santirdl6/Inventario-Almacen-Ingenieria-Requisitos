import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ArticlesApiService } from './articles-api.service';

describe('Lectura consistente de detalle y contexto', () => {
  let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('rechaza un contexto de otra revisión antes de entregar datos editables', async () => {
    const loaded = TestBed.inject(ArticlesApiService).detail('article');
    const rejected = expect(loaded).rejects.toThrow('Vuelve a cargarlo');
    http.expectOne('/api/v1/articles/article').flush({ id: 'article' }, { headers: { ETag: '"AQIDBAUGBwg="' } });
    await vi.waitFor(() => http.expectOne('/api/v1/articles/article/context').flush({ articleVersion: '"AgIDBAUGBwg="' }));
    await rejected;
  });
  it('envía filtros y página sin confundir familia vacía con un identificador', async () => {
    const service = TestBed.inject(ArticlesApiService);
    const filtered = service.list('Tornillo', 'inactive', 'family', 2);
    http.expectOne(request => request.url === '/api/v1/articles' && request.params.get('q') === 'Tornillo'
      && request.params.get('state') === 'inactive' && request.params.get('familyId') === 'family'
      && request.params.get('page') === '2').flush({ items: [], totalCount: 0, page: 2, pageSize: 25 });
    await filtered;
    const all = service.list('', 'active', '', 1);
    http.expectOne(request => request.url === '/api/v1/articles' && !request.params.has('familyId')).flush({ items: [] }); await all;
  });
});
