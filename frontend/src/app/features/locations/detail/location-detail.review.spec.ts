import { TestBed } from '@angular/core/testing';
import { detail, httpMock, locationsTestProviders, settle, signIn, text } from '../testing/locations-testing';
import { LocationDetailPage } from './location-detail.page';

describe('PR6: contenido cuya última página desaparece', () => {
  it('recarga una página válida sin presentar una ubicación con existencias como vacía', async () => {
    const location = detail({ isEmpty: false, hasMovements: true });
    TestBed.configureTestingModule({ imports: [LocationDetailPage], providers: locationsTestProviders({ id: location.id }) });
    signIn(); const fixture = TestBed.createComponent(LocationDetailPage); fixture.detectChanges();
    httpMock().expectOne(`/api/v1/locations/${location.id}`).flush(location, { headers: { ETag: '"V1"' } });
    const first = { asOf: '2026-11-19T17:00:00Z', location, hasReservations: false, inOpenCount: false,
      page: 1, pageSize: 25, totalCount: 26, items: Array.from({ length: 25 }, (_, i) => ({
        article: { id: 'article-' + i, sku: 'A-' + i, name: 'Artículo', unit: 'UD', isActive: true },
        lotCode: 'SIN-LOTE', physical: '1.000', reserved: '0.000', unit: 'UD' })) };
    httpMock().expectOne(request => request.url.endsWith('/stock')).flush(first); await settle(fixture);
    const next = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(button => text(button) === 'Siguiente')!;
    next.click(); httpMock().expectOne(request => request.params.get('page') === '2').flush({ ...first, page: 2, totalCount: 25, items: [] });
    httpMock().expectOne(request => request.params.get('page') === '1').flush({ ...first, totalCount: 25 }); await settle(fixture);
    expect(fixture.nativeElement.querySelector('[data-testid="empty"]')).toBeNull();
    expect(fixture.componentInstance.stock()?.page).toBe(1);
    expect(fixture.componentInstance.stock()?.totalCount).toBe(25);
    httpMock().verify(); TestBed.resetTestingModule();
  });
});
