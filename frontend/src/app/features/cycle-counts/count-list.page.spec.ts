import { TestBed } from '@angular/core/testing';
import { httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { CountListPage } from './count-list.page';
import { COUNT, RECORD, U1, U2, settle } from './testing/cycle-counts-testing';

// HU-04 T014 (contracts/cycle-counts-ui.md § Rutas): lista con estado en texto, código, ubicaciones y
// fechas; filtro por estado; «Nuevo conteo» solo con Counts.Record.
const PAGE = {
  items: [
    { id: COUNT, countCode: 'CC-400000000000', state: 'Counting', locationIds: [U1, U2], createdBy: 'u',
      createdAt: '2026-11-21T08:00:00Z', startedAt: '2026-11-21T08:10:00Z', closedAt: null }
  ],
  page: 1, pageSize: 25, totalCount: 30
};

describe('CountListPage', () => {
  afterEach(() => httpMock().verify());

  async function render(permissions: string[]) {
    TestBed.configureTestingModule({ imports: [CountListPage], providers: locationsTestProviders() });
    signIn(permissions);
    const fixture = TestBed.createComponent(CountListPage);
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/cycle-counts' && r.params.get('page') === '1' && !r.params.has('state')).flush(PAGE);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('muestra cada conteo con código, estado en texto, número de ubicaciones y enlace al detalle', async () => {
    const { root } = await render(RECORD);
    const row = root.querySelector('table[aria-label="Conteos"] tbody tr');
    expect(text(row)).toContain('CC-400000000000');
    expect(text(row)).toContain('En conteo');
    expect(text(row)).toContain('2 ubicaciones');
    expect(row?.querySelector('a')?.getAttribute('href')).toBe(`/inventarios/${COUNT}`);
    expect(root.querySelector('a[data-testid="new-count"]')?.getAttribute('href')).toBe('/inventarios/nuevo');
  });

  it('filtra por estado y pagina sin perder el filtro', async () => {
    const { fixture, root } = await render(RECORD);
    const select = root.querySelector<HTMLSelectElement>('select#count-state')!;
    select.value = 'AwaitingApproval';
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/cycle-counts' && r.params.get('state') === 'AwaitingApproval'
      && r.params.get('page') === '1').flush(PAGE);
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('button[data-testid="next-page"]')!.click();
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/cycle-counts' && r.params.get('state') === 'AwaitingApproval'
      && r.params.get('page') === '2').flush({ ...PAGE, page: 2 });
    await settle(fixture);
    expect(text(root)).toContain('Página 2 de 2');
  });

  it('sin Counts.Record no ofrece «Nuevo conteo»', async () => {
    const { root } = await render(['Inventory.Read', 'Inventory.Adjust']);
    expect(root.querySelector('a[data-testid="new-count"]')).toBeNull();
  });
});
