import { TestBed } from '@angular/core/testing';
import { flushProblem, httpMock, locationsTestProviders, settle, signIn, text } from '../../locations/testing/locations-testing';
import { StockQueryPage } from './stock-query.page';

// HU-05 T015 (US1; FR-001–FR-008): consulta de existencias según contracts/stock-ui.md.
const ARTICLE = { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true };
const location = (code: string) => ({ id: `loc-${code}`, code, aisle: 'P01', rack: 'E01', level: code, kind: 'Storage', isActive: true });
const row = (code: string, lot: { id: string; code: string; isUntracked: boolean }, physical: string, reserved: string, available: string) =>
  ({ location: location(code), lot, physical, reserved, available });
const L001 = { id: 'lot-1', code: 'L-001', isUntracked: false };

function result(overrides: Record<string, unknown> = {}) {
  return {
    asOf: '2026-11-20T10:00:00Z', article: ARTICLE,
    totals: { physical: '6.000', reserved: '2.000', available: '4.000' },
    items: [row('N01', L001, '2.000', '2.000', '0.000')], page: 1, pageSize: 25, totalCount: 2, ...overrides
  };
}

describe('StockQueryPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [StockQueryPage], providers: locationsTestProviders() });
    signIn(['Inventory.Read']);
  });

  async function render() {
    const fixture = TestBed.createComponent(StockQueryPage);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  async function choose(fixture: Awaited<ReturnType<typeof render>>['fixture'], root: HTMLElement, article = ARTICLE) {
    const input = root.querySelector<HTMLInputElement>('#stock-article')!;
    input.value = 'art';
    input.dispatchEvent(new Event('input'));
    root.querySelector<HTMLFormElement>('form[aria-label="Buscar artículo"]')!.dispatchEvent(new Event('submit'));
    httpMock().expectOne(r => r.url === '/api/v1/articles' && r.params.get('q') === 'art' && r.params.get('state') === 'all')
      .flush({ items: [{ ...article, family: { id: 'f', code: 'F', name: 'F' }, allowsFractional: false }], page: 1, pageSize: 25, totalCount: 1 });
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('[data-testid="article-option"]')!.click();
    await settle(fixture);
  }

  it('no consulta hasta elegir un artículo y lo pide expresamente', async () => {
    const { root } = await render();
    expect(text(root)).toContain('Elige un artículo para ver sus existencias');
    httpMock().expectNone(r => r.url === '/api/v1/stock');
  });

  it('muestra los totales de todo el filtro desde totals, separados de la tabla y con la unidad', async () => {
    const { fixture, root } = await render();
    await choose(fixture, root);
    const request = httpMock().expectOne(r => r.url === '/api/v1/stock');
    expect(request.request.params.get('articleId')).toBe('art-1');
    expect(request.request.params.get('includeEmpty')).toBe('false');
    request.flush(result());
    await settle(fixture);

    const totals = text(root.querySelector('[data-testid="totals"]'));
    expect(totals).toContain('Totales de todo el filtro');
    expect(totals).toContain('Físico 6.000 UD');
    expect(totals).toContain('Reservado 2.000 UD');
    expect(totals).toContain('Disponible 4.000 UD');
    const cells = Array.from(root.querySelectorAll('tbody tr')).map(text);
    expect(cells).toHaveLength(1);
    expect(cells[0]).toContain('N01');
    expect(cells[0]).toContain('L-001');
    expect(text(root.querySelector('[data-testid="as-of"]'))).toMatch(/^Datos a las 20\/11\/2026 \d{2}:\d{2}:\d{2}/);
    // El filtro de lote solo aparece en artículos con seguimiento.
    expect(root.querySelector('#stock-lot')).not.toBeNull();
  });

  it('oculta las filas a cero por defecto y las muestra con la casilla, que repite la consulta', async () => {
    const { fixture, root } = await render();
    await choose(fixture, root);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush(result());
    await settle(fixture);

    const box = root.querySelector<HTMLInputElement>('#stock-include-empty')!;
    expect(box.checked).toBe(false);
    expect(text(box.closest('label'))).toBe('Mostrar ubicaciones sin existencias');
    box.click();
    await settle(fixture);
    const again = httpMock().expectOne(r => r.url === '/api/v1/stock');
    expect(again.request.params.get('includeEmpty')).toBe('true');
    again.flush(result({ items: [row('N01', L001, '2.000', '2.000', '0.000'), row('N02', L001, '0.000', '0.000', '0.000')] }));
    await settle(fixture);
    expect(root.querySelectorAll('tbody tr')).toHaveLength(2);
  });

  it('filtra por ubicación y lote y «Actualizar» repite la misma consulta', async () => {
    const { fixture, root } = await render();
    await choose(fixture, root);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush(result({
      items: [row('N01', L001, '2.000', '2.000', '0.000'), row('N02', L001, '4.000', '0.000', '4.000')]
    }));
    await settle(fixture);

    const select = root.querySelector<HTMLSelectElement>('#stock-location')!;
    select.value = 'loc-N02';
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    const filtered = httpMock().expectOne(r => r.url === '/api/v1/stock');
    expect(filtered.request.params.get('locationId')).toBe('loc-N02');
    filtered.flush(result({ items: [row('N02', L001, '4.000', '0.000', '4.000')], totals: { physical: '4.000', reserved: '0.000', available: '4.000' } }));
    await settle(fixture);
    // Las opciones de ubicación se conservan aunque la tabla ya esté filtrada.
    expect(Array.from(select.options).map(option => option.value)).toEqual(['', 'loc-N01', 'loc-N02']);

    root.querySelector<HTMLButtonElement>('[data-testid="refresh"]')!.click();
    await settle(fixture);
    expect(httpMock().expectOne(r => r.url === '/api/v1/stock').request.params.get('locationId')).toBe('loc-N02');
  });

  // Convergence C4 (G3; FR-004): con más de 25 combinaciones, las opciones de filtro salen de todas
  // las páginas del artículo (pageSize 100) y la tabla sigue paginada de 25 en 25.
  it('con 30 combinaciones las opciones de ubicación y lote salen de todas, no solo de la primera página', async () => {
    const lot = (index: number) => ({ id: `lot-${index}`, code: `L-${String(index).padStart(3, '0')}`, isUntracked: false });
    const rows = Array.from({ length: 30 }, (_, index) =>
      row(`N${String(index + 1).padStart(2, '0')}`, lot(index + 1), '1.000', '0.000', '1.000'));
    const { fixture, root } = await render();
    await choose(fixture, root);
    httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('pageSize') === '25')
      .flush(result({ items: rows.slice(0, 25), totalCount: 30 }));
    await settle(fixture);
    const all = httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('pageSize') === '100');
    expect(all.request.params.get('page')).toBe('1');
    expect(all.request.params.get('includeEmpty')).toBe('false');
    expect(all.request.params.has('locationId')).toBe(false);
    all.flush(result({ items: rows, pageSize: 100, totalCount: 30 }));
    await settle(fixture);

    const locations = Array.from(root.querySelector<HTMLSelectElement>('#stock-location')!.options).map(option => option.value);
    expect(locations).toHaveLength(31);
    expect(locations).toContain('loc-N30');
    const lots = Array.from(root.querySelector<HTMLSelectElement>('#stock-lot')!.options).map(option => option.value);
    expect(lots).toHaveLength(31);
    expect(lots).toContain('lot-30');
    // La tabla sigue mostrando 25 filas; la página 2 no vuelve a pedir todas las combinaciones.
    expect(root.querySelectorAll('tbody tr')).toHaveLength(25);
    root.querySelector<HTMLButtonElement>('nav.pager button:last-of-type')!.click();
    await settle(fixture);
    const second = httpMock().expectOne(r => r.url === '/api/v1/stock');
    expect(second.request.params.get('page')).toBe('2');
    expect(second.request.params.get('pageSize')).toBe('25');
    second.flush(result({ items: rows.slice(25), page: 2, totalCount: 30 }));
    await settle(fixture);
    httpMock().expectNone(r => r.url === '/api/v1/stock');
  });

  it('artículo sin lote e inactivo: «sin lote», «Inactivo» en texto y sin filtro de lote', async () => {
    const untracked = { ...ARTICLE, tracksLot: false, isActive: false };
    const { fixture, root } = await render();
    await choose(fixture, root, untracked);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush(result({
      article: untracked, items: [row('N01', { id: 'lot-0', code: 'SIN-LOTE', isUntracked: true }, '2.000', '0.000', '2.000')]
    }));
    await settle(fixture);
    expect(text(root.querySelector('tbody tr'))).toContain('sin lote');
    expect(text(root.querySelector('tbody tr'))).not.toContain('SIN-LOTE');
    expect(text(root.querySelector('h2'))).toContain('Inactivo');
    expect(root.querySelector('#stock-lot')).toBeNull();
  });

  it('sin existencias lo dice con texto y no como error', async () => {
    const { fixture, root } = await render();
    await choose(fixture, root);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush(result({
      totals: { physical: '0.000', reserved: '0.000', available: '0.000' }, items: [], totalCount: 0
    }));
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="empty"]'))).toBe('No hay existencias para esta selección');
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('un 404 se muestra en la página con apiProblem() y sin tabla', async () => {
    const { fixture, root } = await render();
    await choose(fixture, root);
    flushProblem(httpMock().expectOne(r => r.url === '/api/v1/stock'), 404, 'NotFound', 'El recurso solicitado no existe.');
    await settle(fixture);
    expect(text(root.querySelector('[role="alert"]'))).toBe('El artículo ya no existe.');
    expect(root.querySelector('table')).toBeNull();
  });
});
