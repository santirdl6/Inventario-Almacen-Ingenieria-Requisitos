import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { flushNetworkError, flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { CountNewPage } from './count-new.page';
import { COUNT, RECORD, U1, U2, count, location, settle } from './testing/cycle-counts-testing';

// HU-04 T014 (US1; contracts/cycle-counts-ui.md § Rutas): elegir ubicaciones activas de todas las
// páginas y guardar el borrador con la intención común (begin + send, appRoute /inventarios/nuevo).
const U3 = '50000000-0000-4000-8000-000000000003';

describe('CountNewPage', () => {
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function render() {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [CountNewPage], providers: locationsTestProviders() });
    signIn(RECORD);
    const fixture = TestBed.createComponent(CountNewPage);
    await settle(fixture);
    // Todas las páginas de ubicaciones activas (máximo 100 por página).
    httpMock().expectOne(r => r.url === '/api/v1/locations' && r.params.get('status') === 'active' && r.params.get('page') === '1'
      && r.params.get('pageSize') === '100').flush({ items: [location(U1, 'U1'), location(U2, 'U2')], page: 1, pageSize: 100, totalCount: 101 });
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/locations' && r.params.get('page') === '2')
      .flush({ items: [location(U3, 'U3')], page: 2, pageSize: 100, totalCount: 101 });
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function check(root: HTMLElement, id: string): void {
    const box = root.querySelector<HTMLInputElement>(`input[type="checkbox"][value="${id}"]`)!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
  }

  it('ofrece las ubicaciones activas de todas las páginas, con su código', async () => {
    const { root } = await render();
    expect(Array.from(root.querySelectorAll('label')).map(label => text(label))).toEqual(expect.arrayContaining(['U1', 'U2', 'U3']));
  });

  it('sin ubicaciones elegidas no envía y lleva el foco al error', async () => {
    const { fixture, root } = await render();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle(fixture);
    expect(text(root.querySelector('[role="alert"]'))).toContain('Elige al menos una ubicación');
    expect(root.querySelector('fieldset')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('guarda el borrador con Idempotency-Key y navega al conteo', async () => {
    const { fixture, root } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    check(root, U2);
    check(root, U1);
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle(fixture);
    const request = httpMock().expectOne(r => r.method === 'POST' && r.url === '/api/v1/cycle-counts');
    expect(request.request.body).toEqual({ locationIds: [U1, U2] });
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    request.flush({ operationId: 'op-1', count: count() }, { status: 201, statusText: 'Created', headers: { ETag: '"c1"', Location: `/api/v1/cycle-counts/${COUNT}` } });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(`/inventarios/${COUNT}`);
  });

  it('un 422 marca el campo, lleva el foco y conserva la selección', async () => {
    const { fixture, root } = await render();
    check(root, U1);
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle(fixture);
    flushProblem(httpMock().expectOne(r => r.method === 'POST' && r.url === '/api/v1/cycle-counts'), 422, 'ValidationFailed',
      'Revisa los datos.', { errors: { 'locationIds[0]': ['La ubicación U1 está inactiva.'] } });
    await settle(fixture);
    expect(text(root)).toContain('La ubicación U1 está inactiva.');
    expect(root.querySelector('fieldset')?.getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector<HTMLInputElement>(`input[value="${U1}"]`)!.checked).toBe(true);
  });

  it('un fallo de red deja la operación pendiente de comprobar', async () => {
    const { fixture, root } = await render();
    check(root, U1);
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle(fixture);
    flushNetworkError(httpMock().expectOne(r => r.method === 'POST' && r.url === '/api/v1/cycle-counts'));
    await settle(fixture);
    expect(text(root)).toContain('Resultado pendiente de comprobar');
    expect(text(root)).toContain('Abrir conteo');
  });
});
