import { TestBed } from '@angular/core/testing';
import { httpMock, locationsTestProviders, settle, signIn, summary, text } from '../testing/locations-testing';
import { LocationListPage } from './location-list.page';

describe('LocationListPage', () => {
  function setup(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({ imports: [LocationListPage], providers: locationsTestProviders({}, query) });
  }

  async function render(permissions: string[] = ['Inventory.Read']) {
    signIn(permissions);
    const fixture = TestBed.createComponent(LocationListPage);
    await settle(fixture);
    return fixture;
  }

  it('busca activas por defecto y muestra tipo y estado con texto; sin gestión no ofrece el alta', async () => {
    setup();
    const fixture = await render();
    const request = httpMock().expectOne(r => r.url === '/api/v1/locations');
    expect(request.request.params.get('status')).toBe('active');
    request.flush({
      items: [summary(), summary({ id: '2', code: 'REC-01', kind: 'Receiving', kindLabel: 'Zona de recepción', isActive: false, statusLabel: 'Inactiva' })],
      page: 1, pageSize: 25, totalCount: 2
    });
    await settle(fixture);
    const rows = Array.from(fixture.nativeElement.querySelectorAll('tbody tr')) as Element[];
    expect(rows.length).toBe(2);
    expect(text(rows[1])).toContain('Zona de recepción');
    expect(text(rows[1])).toContain('Inactiva');
    expect(fixture.nativeElement.querySelector('a[href="/ubicaciones/nueva"]')).toBeNull();
  });

  it('filtra por código o pasillo, tipo y estado', async () => {
    setup();
    const fixture = await render();
    httpMock().expectOne(r => r.url === '/api/v1/locations').flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    const element: HTMLElement = fixture.nativeElement;
    const search = element.querySelector('#search') as HTMLInputElement;
    search.value = 'p01';
    search.dispatchEvent(new Event('input'));
    const kind = element.querySelector('#kind') as HTMLSelectElement;
    kind.value = 'Receiving';
    kind.dispatchEvent(new Event('change'));
    const status = element.querySelector('#status') as HTMLSelectElement;
    status.value = 'all';
    status.dispatchEvent(new Event('change'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = httpMock().expectOne(r => r.url === '/api/v1/locations');
    expect(request.request.params.get('search')).toBe('p01');
    expect(request.request.params.get('kind')).toBe('Receiving');
    expect(request.request.params.get('status')).toBe('all');
  });

  it('la búsqueda inicial llega por la URL (enlace desde un duplicado)', async () => {
    setup({ search: 'P01-E01-N01' });
    await render();
    expect(httpMock().expectOne(r => r.url === '/api/v1/locations').request.params.get('search')).toBe('P01-E01-N01');
  });

  it('sin resultados lo indica, permite limpiar los filtros y con gestión ofrece el alta', async () => {
    setup();
    const fixture = await render(['Inventory.Read', 'Locations.Manage']);
    httpMock().expectOne(r => r.url === '/api/v1/locations').flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    await settle(fixture);
    const element: HTMLElement = fixture.nativeElement;
    expect(text(element.querySelector('[data-testid="empty"]'))).toContain('No hay ubicaciones que coincidan');
    expect(element.querySelector('a[href="/ubicaciones/nueva"]')).not.toBeNull();
    (element.querySelector('[data-testid="empty"] button') as HTMLButtonElement).click();
    expect(httpMock().expectOne(r => r.url === '/api/v1/locations').request.params.get('status')).toBe('active');
  });

  it('un error de carga se muestra en la página', async () => {
    setup();
    const fixture = await render();
    httpMock().expectOne(r => r.url === '/api/v1/locations').flush(
      { status: 422, code: 'ValidationFailed', detail: 'La página empieza en 1.' },
      { status: 422, statusText: 'Unprocessable' });
    await settle(fixture);
    expect(text(fixture.nativeElement.querySelector('[role="alert"]'))).toBe('La página empieza en 1.');
  });
});
