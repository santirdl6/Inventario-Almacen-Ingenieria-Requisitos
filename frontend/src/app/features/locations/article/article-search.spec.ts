import { TestBed } from '@angular/core/testing';
import { flushProblem, httpMock, locationsTestProviders, settle, signIn, text } from '../testing/locations-testing';
import { ArticleSearch } from './article-search';

describe('ArticleSearch', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ArticleSearch], providers: locationsTestProviders() });
    signIn();
  });

  async function search(value: string) {
    const fixture = TestBed.createComponent(ArticleSearch);
    await settle(fixture);
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector('#article-query') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    return { fixture, root, request: httpMock().expectOne(r => r.url === '/api/v1/articles') };
  }

  it('busca por código o nombre con el maestro de artículos de A y enlaza a sus ubicaciones', async () => {
    const { fixture, root, request } = await search(' tornillo ');
    expect(request.request.params.get('q')).toBe('tornillo');
    expect(request.request.params.get('state')).toBe('all');
    request.flush({ items: [
      { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true },
      { id: 'art-2', sku: 'ART-02', name: 'Tornillo largo', baseUnit: 'UD', isActive: false }
    ], page: 1, pageSize: 25, totalCount: 2 });
    await settle(fixture);
    const rows = Array.from(root.querySelectorAll('tbody tr'));
    expect(rows[0].querySelector('a')!.getAttribute('href')).toBe('/ubicaciones/articulo/art-1');
    expect(text(rows[1])).toContain('Inactivo');
  });

  it('sin coincidencias lo indica y un error se muestra en la página', async () => {
    const empty = await search('nada');
    empty.request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    await settle(empty.fixture);
    expect(text(empty.root.querySelector('[data-testid="empty"]'))).toContain('No hay artículos');

    empty.root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(r => r.url === '/api/v1/articles'), 422, 'ValidationFailed', 'La búsqueda es demasiado larga.');
    await settle(empty.fixture);
    expect(text(empty.root.querySelector('[role="alert"]'))).toBe('La búsqueda es demasiado larga.');
  });
});
