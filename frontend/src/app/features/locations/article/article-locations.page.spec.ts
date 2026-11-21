import { TestBed } from '@angular/core/testing';
import { flushProblem, httpMock, locationsTestProviders, settle, signIn, text } from '../testing/locations-testing';
import { ArticleLocationsPage } from './article-locations.page';

const article = { id: 'art-1', sku: 'ART-01', name: 'Tornillo', unit: 'UD', isActive: true };
const location = (code: string, isActive = true) =>
  ({ id: code, code, description: `Pasillo ${code}`, kind: 'Storage', isActive });

describe('ArticleLocationsPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ArticleLocationsPage], providers: locationsTestProviders({ articleId: 'art-1' }) });
    signIn();
  });

  async function render() {
    const fixture = TestBed.createComponent(ArticleLocationsPage);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('muestra cada ubicación y lote y el total del artículo desde totals, no de la página', async () => {
    const { fixture, root } = await render();
    httpMock().expectOne(r => r.url === '/api/v1/articles/art-1/locations').flush({
      asOf: '2026-11-18T10:00:00Z', article, totals: { physical: '6.000', reserved: '0.000' },
      items: [{ location: location('P01-E01-N01'), lotCode: 'L-001', physical: '2.000', reserved: '0.000' }],
      page: 1, pageSize: 1, totalCount: 2
    });
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="totals"]'))).toBe('Total del artículo: 6.000 UD');
    // Contrato UI: el momento de la consulta (asOf) es visible.
    expect(text(root.querySelector('.meta'))).toMatch(/^Datos a 18\/11\/2026 \d{2}:\d{2}:\d{2}/);
    const row = text(root.querySelector('tbody tr'));
    expect(row).toContain('P01-E01-N01');
    expect(row).toContain('L-001');
    expect(row).toContain('2.000 UD');
    expect(text(root.querySelector('.pager'))).toContain('Página 1 de 2');
    expect(root.querySelector('a[href="/ubicaciones/articulo"]')).not.toBeNull();
  });

  it('dos lotes en la misma ubicación aparecen por separado; inactivos con texto', async () => {
    const { fixture, root } = await render();
    httpMock().expectOne(r => r.url === '/api/v1/articles/art-1/locations').flush({
      asOf: '2026-11-18T10:00:00Z', article: { ...article, isActive: false }, totals: { physical: '5.000', reserved: '1.000' },
      items: [
        { location: location('N02', false), lotCode: 'L-001', physical: '3.000', reserved: '1.000' },
        { location: location('N02', false), lotCode: 'SIN-LOTE', physical: '2.000', reserved: '0.000' }
      ],
      page: 1, pageSize: 25, totalCount: 2
    });
    await settle(fixture);
    const rows = Array.from(root.querySelectorAll('tbody tr')).map(text);
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('Inactiva');
    expect(rows[1]).toContain('Sin lote');
    expect(rows[1]).toContain('Sin reservas');
    expect(text(root.querySelector('h2'))).toContain('Inactivo');
    expect(text(root.querySelector('[data-testid="totals"]'))).toContain('reservado 1.000 UD');
  });

  it('sin existencias lo indica sin tratarlo como error y puede actualizarse', async () => {
    const { fixture, root } = await render();
    httpMock().expectOne(r => r.url === '/api/v1/articles/art-1/locations').flush({
      asOf: '2026-11-18T10:00:00Z', article, totals: { physical: '0.000', reserved: '0.000' }, items: [], page: 1, pageSize: 25, totalCount: 0
    });
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="empty"]'))).toBe('Este artículo no está almacenado en ninguna ubicación.');
    expect(root.querySelector('[role="alert"]')).toBeNull();
    Array.from(root.querySelectorAll('button')).find(b => text(b) === 'Actualizar')!.click();
    httpMock().expectOne(r => r.url === '/api/v1/articles/art-1/locations');
  });

  it('un artículo inexistente se explica', async () => {
    const { fixture, root } = await render();
    flushProblem(httpMock().expectOne(r => r.url === '/api/v1/articles/art-1/locations'), 404, 'NotFound', 'No existe.');
    await settle(fixture);
    expect(text(root.querySelector('[role="alert"]'))).toContain('El artículo no existe.');
  });
});
