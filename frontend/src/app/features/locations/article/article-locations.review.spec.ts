import { TestBed } from '@angular/core/testing';
import { httpMock, locationsTestProviders, settle, signIn } from '../testing/locations-testing';
import { ArticleLocationsPage } from './article-locations.page';

describe('PR6: página fuera de rango tras cambiar stock', () => {
  it('no afirma que el artículo no está almacenado si aún hay 25 buckets en otras páginas', async () => {
    TestBed.configureTestingModule({ imports: [ArticleLocationsPage], providers: locationsTestProviders({ articleId: 'article' }) });
    signIn(); const fixture = TestBed.createComponent(ArticleLocationsPage); await settle(fixture);
    const page = { asOf: '2026-11-19T17:00:00Z', article: { id: 'article', sku: 'ART-01', name: 'Artículo', unit: 'UD', isActive: true },
      totals: { physical: '26.000', reserved: '0.000' }, page: 1, pageSize: 25, totalCount: 26,
      items: Array.from({ length: 25 }, (_, i) => ({ location: { id: 'location-' + i, code: 'L-' + i, description: 'Pasillo P01', kind: 'Storage', isActive: true }, lotCode: 'SIN-LOTE', physical: '1.000', reserved: '0.000' })) };
    httpMock().expectOne(request => request.url.endsWith('/locations')).flush(page); await settle(fixture);
    fixture.componentInstance.load(2);
    httpMock().expectOne(request => request.params.get('page') === '2').flush({ ...page, page: 2, totalCount: 25, totals: { physical: '25.000', reserved: '0.000' }, items: [] });
    httpMock().expectOne(request => request.params.get('page') === '1').flush({ ...page, totalCount: 25, totals: { physical: '25.000', reserved: '0.000' } });
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('[data-testid="empty"]')).toBeNull();
    httpMock().verify(); TestBed.resetTestingModule();
  });
});
