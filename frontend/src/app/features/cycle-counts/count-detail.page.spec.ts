import { TestBed } from '@angular/core/testing';
import { flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { CountDetailPage } from './count-detail.page';
import { ART_A, ART_B, COUNT, FOUND, L1, L2, RECORD, U1, U2, context, count, flushDetail, line, location, settle } from './testing/cycle-counts-testing';

// HU-04 T015 (US1, US5; contracts/cycle-counts-ui.md § Contenido por estado, § Resultados y errores).
const RESOURCE = `/api/v1/cycle-counts/${COUNT}`;
const COUNTING = () => count('Counting', [
  line({ id: L1, articleId: ART_A, locationId: U1, theoreticalPhysical: '10.000' }),
  line({ id: L2, articleId: ART_A, locationId: U2, theoreticalPhysical: '5.000' })
]);

describe('CountDetailPage en borrador y en conteo', () => {
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function render(body: object, permissions = RECORD) {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [CountDetailPage], providers: locationsTestProviders({ id: COUNT }) });
    signIn(permissions);
    const fixture = TestBed.createComponent(CountDetailPage);
    await flushDetail(fixture, body);
    if ((body as { state: string }).state === 'Draft' && permissions.includes('Counts.Record')) {
      httpMock().expectOne(r => r.url === '/api/v1/locations' && r.params.get('status') === 'active')
        .flush({ items: [location(U1, 'U1'), location(U2, 'U2')], page: 1, pageSize: 100, totalCount: 2 });
      await settle(fixture);
    }
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function counted(root: HTMLElement, lineId: string): HTMLInputElement {
    return root.querySelector<HTMLInputElement>(`input[data-line="${lineId}"]`)!;
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function button(root: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(root.querySelectorAll('button')).find(item => text(item) === label)!;
  }

  it('en borrador muestra el alcance y avisa de la congelación antes de iniciar con If-Match', async () => {
    const { fixture, root } = await render(count('Draft'));
    expect(text(root.querySelector('h1'))).toContain('CC-400000000000');
    expect(text(root)).toContain('Borrador');
    expect(text(root.querySelector('[data-testid="scope"]'))).toContain('U1');
    expect(text(root.querySelector('[data-testid="freeze-notice"]'))).toContain('quedarán congeladas');
    button(root, 'Iniciar conteo').click();
    await settle(fixture);
    const request = httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/start`);
    expect(request.request.headers.get('If-Match')).toBe('"c1"');
    request.flush({ operationId: 'op-1', count: COUNTING() }, { headers: { ETag: '"c2"' } });
    await flushDetail(fixture, COUNTING(), context('"c2"'), '"c2"');
    expect(text(root)).toContain('En conteo');
  });

  it('un 409 CountScopeFrozen al iniciar avisa con el otro conteo y vuelve a leer', async () => {
    const { fixture, root } = await render(count('Draft'));
    button(root, 'Iniciar conteo').click();
    await settle(fixture);
    flushProblem(httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/start`), 409, 'CountScopeFrozen',
      'La ubicación U1 ya está en el conteo CC-999999999999.');
    await flushDetail(fixture, count('Draft'));
    expect(text(root.querySelector('[role="alert"]'))).toContain('CC-999999999999');
  });

  it('en conteo el contado está vacío, el cero se escribe y la diferencia se dice en texto; no pide motivo', async () => {
    const { fixture, root } = await render(COUNTING());
    expect(counted(root, L1).value).toBe('');
    expect(text(root)).toContain('Contado');
    expect(root.querySelector('[data-testid="reason"]')).toBeNull();
    type(counted(root, L1), '8');
    type(counted(root, L2), '0');
    await settle(fixture);
    expect(text(root.querySelector(`[data-diff="${L1}"]`))).toBe('Faltan 2 UD');
    expect(text(root.querySelector(`[data-diff="${L2}"]`))).toBe('Faltan 5 UD');
    type(counted(root, L2), '5');
    await settle(fixture);
    expect(text(root.querySelector(`[data-diff="${L2}"]`))).toBe('Sin diferencia');
  });

  it('guarda solo lo escrito, con If-Match, y vuelve a leer', async () => {
    const { fixture, root } = await render(COUNTING());
    type(counted(root, L1), '8');
    await settle(fixture);
    button(root, 'Guardar').click();
    await settle(fixture);
    const request = httpMock().expectOne(r => r.method === 'PUT' && r.url === `${RESOURCE}/lines`);
    expect(request.request.body).toEqual({ counted: [{ lineId: L1, countedPhysical: '8' }], found: [] });
    expect(request.request.headers.get('If-Match')).toBe('"c1"');
    const saved = count('Counting', [line({ id: L1, countedPhysical: '8.000', difference: '-2.000' }),
      line({ id: L2, locationId: U2, theoreticalPhysical: '5.000' })]);
    request.flush({ operationId: 'op-2', count: saved }, { headers: { ETag: '"c2"' } });
    await flushDetail(fixture, saved, context('"c2"'), '"c2"');
    expect(counted(root, L1).value).toBe('8');
    expect(text(root.querySelector('[role="status"]'))).toContain('Conteo guardado');
  });

  it('un 412 avisa, vuelve a leer y conserva lo escrito', async () => {
    const { fixture, root } = await render(COUNTING());
    type(counted(root, L1), '7');
    await settle(fixture);
    button(root, 'Guardar').click();
    await settle(fixture);
    flushProblem(httpMock().expectOne(r => r.method === 'PUT' && r.url === `${RESOURCE}/lines`), 412, 'PreconditionFailed', 'Versión distinta.');
    await flushDetail(fixture, COUNTING(), context('"c3"'), '"c3"');
    expect(text(root.querySelector('[role="alert"]'))).toContain('Otra persona ha cambiado el conteo');
    expect(counted(root, L1).value).toBe('7');
  });

  it('una cantidad negativa o con fracción no admitida se marca sin enviar', async () => {
    const { fixture, root } = await render(COUNTING());
    type(counted(root, L1), '2.5');
    await settle(fixture);
    button(root, 'Guardar').click();
    await settle(fixture);
    expect(counted(root, L1).getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('solo admite cantidades enteras');
  });

  it('al enviar con líneas sin contar marca la fila que señala el servidor', async () => {
    const { fixture, root } = await render(COUNTING());
    button(root, 'Enviar a aprobación').click();
    await settle(fixture);
    flushProblem(httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/submit`), 422, 'ValidationFailed',
      'Hay líneas sin contar.', { errors: { 'lines[1].countedPhysical': ['Falta el contado de esta línea (el cero se escribe).'] } });
    await settle(fixture);
    expect(counted(root, L2).getAttribute('aria-invalid')).toBe('true');
    expect(counted(root, L1).getAttribute('aria-invalid')).toBeNull();
    expect(text(root)).toContain('Falta el contado de esta línea');
  });

  it('añade un hallazgo con lote y cantidad y lo envía en found', async () => {
    const { fixture, root } = await render(COUNTING());
    const search = root.querySelector<HTMLInputElement>('#found-article')!;
    type(search, 'cab');
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/articles' && r.params.get('q') === 'cab').flush({
      items: [{ id: ART_B, sku: 'ART-B', name: 'Cable', baseUnit: 'M', isActive: true, tracksLot: true }] });
    await settle(fixture);
    button(root, 'ART-B · Cable').click();
    await settle(fixture);
    const where = root.querySelector<HTMLSelectElement>('#found-location')!;
    where.value = U2;
    where.dispatchEvent(new Event('change'));
    type(root.querySelector<HTMLInputElement>('#found-lot')!, 'l-9');
    type(root.querySelector<HTMLInputElement>('#found-quantity')!, '3');
    await settle(fixture);
    button(root, 'Añadir hallazgo').click();
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="pending-found"]'))).toContain('ART-B');
    button(root, 'Guardar').click();
    await settle(fixture);
    const request = httpMock().expectOne(r => r.method === 'PUT' && r.url === `${RESOURCE}/lines`);
    expect(request.request.body).toEqual({ counted: [], found: [{ articleId: ART_B, locationId: U2, lotCode: 'L-9', countedPhysical: '3' }] });
    request.flush({ operationId: 'op-3', count: COUNTING() }, { headers: { ETag: '"c2"' } });
    await flushDetail(fixture, COUNTING(), context('"c2"'), '"c2"');
  });

  it('retira un hallazgo guardado con DELETE e If-Match', async () => {
    const withFound = count('Counting', [line({ id: FOUND, articleId: ART_A, locationId: U1, isFound: true, theoreticalPhysical: '0.000',
      countedPhysical: '2.000', difference: '2.000' })]);
    const { fixture, root } = await render(withFound);
    expect(text(root)).toContain('Hallazgo');
    button(root, 'Retirar hallazgo').click();
    await settle(fixture);
    const request = httpMock().expectOne(r => r.method === 'DELETE' && r.url === `${RESOURCE}/lines/${FOUND}`);
    expect(request.request.headers.get('If-Match')).toBe('"c1"');
    request.flush(null, { status: 204, statusText: 'No Content', headers: { ETag: '"c2"' } });
    await flushDetail(fixture, COUNTING(), context('"c2"'), '"c2"');
  });

  it('sin Counts.Record el conteo se ve en solo lectura', async () => {
    const { root } = await render(COUNTING(), ['Inventory.Read', 'Inventory.Adjust']);
    expect(counted(root, L1)).toBeNull();
    expect(button(root, 'Enviar a aprobación')).toBeUndefined();
  });
});
