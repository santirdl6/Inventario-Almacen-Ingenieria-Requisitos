import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/session/session.service';
import { flushNetworkError, flushProblem, httpMock, locationsTestProviders, signIn, text } from '../../locations/testing/locations-testing';
import { OrderDetailPage } from './order-detail.page';

/** Como settle(), pero deja terminar las cadenas de promesas (await tras cada respuesta HTTP). */
async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 4; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

// HU-05 T016 · Detalle del pedido, reservar y liberar (US2.4, US3, US4; contracts/stock-ui.md).
const ORDER = '10000000-0000-4000-8000-000000000001';
const LINE_A = 'aaaaaaaa-0000-4000-8000-000000000001';
const LINE_B = 'bbbbbbbb-0000-4000-8000-000000000002';
const RESERVATION = '20000000-0000-4000-8000-000000000002';
const ALLOCATION = '40000000-0000-4000-8000-000000000004';
const KEY = 'inventario.pending-operations';
const MANAGER = ['Inventory.Read', 'Reservations.Manage'];

function order(overrides: Record<string, unknown> = {}) {
  return {
    id: ORDER, externalReference: 'PED-1001', customerReference: 'Cliente Norte', state: 'Open',
    lines: [
      // Orden del detalle por Id de línea: ART-02 antes que ART-01; la UI ordena por SKU.
      { id: LINE_A, articleId: 'art-2', ordered: '3.000', shipped: '0.000', pending: '3.000', reservedRemaining: '0.000', reservations: [] },
      { id: LINE_B, articleId: 'art-1', ordered: '7.000', shipped: '0.000', pending: '7.000', reservedRemaining: '0.000', reservations: [] }
    ],
    ...overrides
  };
}

const ALLOCATIONS = [{ id: ALLOCATION, articleId: 'art-1', locationId: 'loc-1',
  lotId: 'lot-1', allocated: '7.000', consumed: '0.000', released: '0.000', remaining: '7.000' }];

function reservedOrder() {
  return order({ lines: [
    { id: LINE_B, articleId: 'art-1', ordered: '7.000', shipped: '0.000', pending: '7.000', reservedRemaining: '7.000',
      reservations: [{ id: RESERVATION, state: 'Active', allocations: ALLOCATIONS }] }
  ] });
}

function context(version: string, overrides: Record<string, unknown> = {}) {
  return {
    orderVersion: version, asOf: '2026-11-20T10:00:00Z',
    articles: [
      { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true, allowsFractional: false },
      { id: 'art-2', sku: 'ART-02', name: 'Arandela', baseUnit: 'KG', isActive: false, tracksLot: false, allowsFractional: true }
    ],
    locations: [{ id: 'loc-1', code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N01', isActive: true }],
    lots: [{ id: 'lot-1', articleId: 'art-1', code: 'L-001', isUntracked: false }],
    ...overrides
  };
}

function flushDetail(body: object, etag: string, version = etag) {
  httpMock().expectOne(`/api/v1/customer-orders/${ORDER}`).flush(body, { headers: { ETag: etag } });
  return version;
}

async function flushPair(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }, body: object, etag: string, version = etag) {
  flushDetail(body, etag);
  await settle(fixture);
  httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`).flush(context(version));
  await settle(fixture);
}

function configure(permissions = MANAGER, id = 'user-1') {
  TestBed.configureTestingModule({ imports: [OrderDetailPage], providers: locationsTestProviders({ id: ORDER }) });
  signIn(permissions);
  TestBed.inject(SessionService).user.update(user => user && { ...user, id });
}

async function render() {
  const fixture = TestBed.createComponent(OrderDetailPage);
  await settle(fixture);
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

function click(root: HTMLElement, selector: string) {
  const element = root.querySelector<HTMLButtonElement>(selector);
  if (!element) throw new Error(`No existe ${selector}`);
  element.click();
}

function type(root: HTMLElement, selector: string, value: string) {
  const input = root.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('OrderDetailPage', () => {
  beforeEach(() => { sessionStorage.clear(); configure(); });
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  it('combina el detalle y el contexto: SKU, nombre, unidad e «Inactivo» del contexto, líneas por SKU', async () => {
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"');
    expect(text(root.querySelector('h1'))).toContain('PED-1001');
    expect(text(root)).toContain('Cliente Norte');
    expect(text(root)).toContain('Abierto');
    const lines = Array.from(root.querySelectorAll('[data-testid="line"]')).map(text);
    expect(lines[0]).toContain('ART-01 · Tornillo');
    expect(lines[0]).toContain('Pedido 7.000 UD');
    expect(lines[1]).toContain('ART-02 · Arandela');
    expect(lines[1]).toContain('Inactivo');
    expect(lines[1]).toContain('Pendiente 3.000 KG');
  });

  // Convergence C3 (G2; FR-026, US2.1): el identificador de la operación del alta sale del
  // contexto, así que se ve igual tras crear, tras cerrar sesión o al entrar por «Abrir pedido».
  it('muestra la operación del alta que da el contexto, sin depender de cómo se llegó al pedido', async () => {
    const { fixture, root } = await render();
    flushDetail(order(), '"v1"');
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`)
      .flush(context('"v1"', { creationOperationId: '90000000-0000-4000-8000-000000000009' }));
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="creation-operation"]')))
      .toBe('Pedido creado con la operación 90000000-0000-4000-8000-000000000009');
  });

  it('sin operación de alta en el contexto no muestra ese dato', async () => {
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"');
    expect(root.querySelector('[data-testid="creation-operation"]')).toBeNull();
  });

  // Convergence C4 (G3; FR-015): el diálogo lista todas las combinaciones del artículo, no solo
  // la primera página. 130 combinaciones: dos páginas de 100 (el máximo de GET /stock).
  it('reservar: recorre todas las páginas de existencias y deja reservar en una combinación posterior a la 25', async () => {
    const stockRow = (index: number) => ({
      location: { id: `loc-${index}`, code: `N${String(index).padStart(3, '0')}`, aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage', isActive: true },
      lot: { id: 'lot-1', code: 'L-001', isUntracked: false }, physical: '1.000', reserved: '0.000', available: '1.000'
    });
    const page = (number: number, items: object[]) => ({
      asOf: 'x', article: { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true },
      totals: { physical: '130.000', reserved: '0.000', available: '130.000' }, items, page: number, pageSize: 100, totalCount: 130
    });
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"');
    click(root, `[data-testid="reserve-${LINE_B}"]`);
    await settle(fixture);
    const first = httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('page') === '1');
    expect(first.request.params.get('pageSize')).toBe('100');
    expect(first.request.params.get('includeEmpty')).toBe('false');
    first.flush(page(1, Array.from({ length: 100 }, (_, index) => stockRow(index + 1))));
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('page') === '2')
      .flush(page(2, Array.from({ length: 30 }, (_, index) => stockRow(index + 101))));
    await settle(fixture);

    expect(root.querySelectorAll('[data-testid="reserve-dialog"] tbody tr')).toHaveLength(130);
    type(root, '[data-testid="reserve-qty-loc-126-lot-1"]', '1');
    await settle(fixture);
    click(root, '[data-testid="reserve-review"]');
    await settle(fixture);
    click(root, '[data-testid="reserve-confirm"]');
    await settle(fixture);
    const request = httpMock().expectOne('/api/v1/reservations');
    expect(request.request.body).toEqual({ customerOrderLineId: LINE_B, allocations: [{ locationId: 'loc-126', lotId: 'lot-1', quantity: '1' }] });
    request.flush({ operationId: 'op-r', reservationId: RESERVATION, customerOrderId: ORDER, movementId: 'm' },
      { status: 201, statusText: 'Created', headers: { Location: `/api/v1/reservations/${RESERVATION}` } });
    await settle(fixture);
    await flushPair(fixture, order(), '"v2"');
  });

  it('si orderVersion no coincide con el ETag vuelve a leer y no reserva con un ETag mezclado', async () => {
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"', '"v2"');
    expect(root.querySelector('[data-testid="line"]')).toBeNull();
    await flushPair(fixture, order(), '"v2"');
    click(root, `[data-testid="reserve-${LINE_B}"]`);
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('articleId') === 'art-1').flush({
      asOf: '2026-11-20T10:00:00Z', article: { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true },
      totals: { physical: '10.000', reserved: '0.000', available: '10.000' },
      items: [{ location: { id: 'loc-1', code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage', isActive: true },
        lot: { id: 'lot-1', code: 'L-001', isUntracked: false }, physical: '10.000', reserved: '0.000', available: '10.000' }],
      page: 1, pageSize: 100, totalCount: 1
    });
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="reserve-dialog"]'))).toContain('Máximo reservable 7.000 UD');
    type(root, '[data-testid="reserve-qty-loc-1-lot-1"]', '7');
    await settle(fixture);
    click(root, '[data-testid="reserve-review"]');
    await settle(fixture);
    click(root, '[data-testid="reserve-confirm"]');
    await settle(fixture);
    const request = httpMock().expectOne('/api/v1/reservations');
    expect(request.request.headers.get('If-Match')).toBe('"v2"');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body).toEqual({ customerOrderLineId: LINE_B, allocations: [{ locationId: 'loc-1', lotId: 'lot-1', quantity: '7' }] });
    request.flush({ operationId: 'op-r', reservationId: RESERVATION, customerOrderId: ORDER, movementId: 'm' },
      { status: 201, statusText: 'Created', headers: { Location: `/api/v1/reservations/${RESERVATION}` } });
    await settle(fixture);
    // Sin ETag en la respuesta: se vuelven a leer el pedido y su contexto.
    await flushPair(fixture, reservedOrder(), '"v3"');
    expect(text(root.querySelector('[role="status"]'))).toContain('op-r');
    expect(text(root.querySelector('[data-testid="line"]'))).toContain('Reservado 7.000 UD');
    expect(text(root.querySelector('[data-testid="reservation"]'))).toContain('P01-E01-N01');
    expect(text(root.querySelector('[data-testid="reservation"]'))).toContain('L-001');
  });

  it('un 409 al reservar muestra el detalle del servidor, conserva las cantidades y ofrece ver existencias', async () => {
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"');
    click(root, `[data-testid="reserve-${LINE_B}"]`);
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush({
      asOf: 'x', article: { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true },
      totals: { physical: '10.000', reserved: '0.000', available: '10.000' },
      items: [{ location: { id: 'loc-1', code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage', isActive: true },
        lot: { id: 'lot-1', code: 'L-001', isUntracked: false }, physical: '10.000', reserved: '0.000', available: '10.000' }],
      page: 1, pageSize: 100, totalCount: 1
    });
    await settle(fixture);
    type(root, '[data-testid="reserve-qty-loc-1-lot-1"]', '5');
    await settle(fixture);
    click(root, '[data-testid="reserve-review"]');
    await settle(fixture);
    click(root, '[data-testid="reserve-confirm"]');
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/reservations'), 409, 'InsufficientStock', 'Disponible insuficiente (disponible 3.000).');
    await settle(fixture);
    const dialog = root.querySelector('[data-testid="reserve-dialog"]')!;
    expect(text(dialog.querySelector('[role="alert"]'))).toContain('disponible 3.000');
    expect(dialog.querySelector('a[href="/existencias"]')?.textContent).toContain('Ver existencias actuales');
    expect(root.querySelector<HTMLInputElement>('[data-testid="reserve-qty-loc-1-lot-1"]')!.value).toBe('5');
  });

  it('libera con el ETag de la reserva, motivo obligatorio y releaseId generado al abrir', async () => {
    const { fixture, root } = await render();
    await flushPair(fixture, reservedOrder(), '"v1"');
    click(root, `[data-testid="release-${RESERVATION}"]`);
    await settle(fixture);
    httpMock().expectOne(`/api/v1/reservations/${RESERVATION}`).flush({
      id: RESERVATION, state: 'Active', customerOrderId: ORDER, customerOrderLineId: LINE_B, articleId: 'art-1',
      allocations: ALLOCATIONS
    }, { headers: { ETag: '"r1"' } });
    await settle(fixture);
    const dialog = () => root.querySelector('[data-testid="release-dialog"]')!;
    expect(text(dialog())).toContain('P01-E01-N01');
    expect(text(dialog())).toContain('Remanente 7.000 UD');
    type(root, `[data-testid="release-qty-${ALLOCATION}"]`, '2');
    await settle(fixture);
    click(root, '[data-testid="release-review"]');
    await settle(fixture);
    expect(text(dialog().querySelector('[role="alert"]'))).toContain('Indica el motivo');
    type(root, '#release-reason', ' El cliente reduce el pedido ');
    await settle(fixture);
    click(root, '[data-testid="release-review"]');
    await settle(fixture);
    click(root, '[data-testid="release-confirm"]');
    await settle(fixture);
    const request = httpMock().expectOne(`/api/v1/reservations/${RESERVATION}/release`);
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    expect(request.request.body.reason).toBe('El cliente reduce el pedido');
    expect(request.request.body.releaseId).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body.allocations).toEqual([{ allocationId: ALLOCATION, quantity: '2' }]);
    request.flush({ operationId: 'op-l', reservationId: RESERVATION, releaseId: request.request.body.releaseId, movementId: 'm', state: 'Active' });
    await settle(fixture);
    await flushPair(fixture, reservedOrder(), '"v2"');
    expect(text(root.querySelector('[role="status"]'))).toContain('op-l');
  });

  // --- Convergence C1 (G1, FR-027/FR-028): el foco va al primer error tras un rechazo ---

  async function reserveUntilConfirm(quantity = '7') {
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"');
    click(root, `[data-testid="reserve-${LINE_B}"]`);
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush({
      asOf: 'x', article: { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true },
      totals: { physical: '10.000', reserved: '0.000', available: '10.000' },
      items: [{ location: { id: 'loc-1', code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage', isActive: true },
        lot: { id: 'lot-1', code: 'L-001', isUntracked: false }, physical: '10.000', reserved: '0.000', available: '10.000' }],
      page: 1, pageSize: 25, totalCount: 1
    });
    await settle(fixture);
    type(root, '[data-testid="reserve-qty-loc-1-lot-1"]', quantity);
    await settle(fixture);
    click(root, '[data-testid="reserve-review"]');
    await settle(fixture);
    click(root, '[data-testid="reserve-confirm"]');
    await settle(fixture);
    return { fixture, root };
  }

  async function releaseUntilConfirm() {
    const { fixture, root } = await render();
    await flushPair(fixture, reservedOrder(), '"v1"');
    click(root, `[data-testid="release-${RESERVATION}"]`);
    await settle(fixture);
    httpMock().expectOne(`/api/v1/reservations/${RESERVATION}`).flush({
      id: RESERVATION, state: 'Active', customerOrderId: ORDER, customerOrderLineId: LINE_B, articleId: 'art-1', allocations: ALLOCATIONS
    }, { headers: { ETag: '"r1"' } });
    await settle(fixture);
    type(root, `[data-testid="release-qty-${ALLOCATION}"]`, '2');
    type(root, '#release-reason', 'El cliente reduce el pedido');
    await settle(fixture);
    click(root, '[data-testid="release-review"]');
    await settle(fixture);
    click(root, '[data-testid="release-confirm"]');
    await settle(fixture);
    return { fixture, root };
  }

  it('reservar: un 422 por asignación lleva el foco a su cantidad', async () => {
    const { fixture, root } = await reserveUntilConfirm();
    flushProblem(httpMock().expectOne('/api/v1/reservations'), 422, 'ValidationFailed', 'Revisa los datos marcados de la reserva.',
      { errors: { 'allocations[0].quantity': ['La cantidad admite como máximo tres decimales.'] } });
    await settle(fixture);
    const input = root.querySelector<HTMLInputElement>('[data-testid="reserve-qty-loc-1-lot-1"]')!;
    expect(document.activeElement).toBe(input);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.value).toBe('7');
  });

  it('reservar: un 409 sin campo lleva el foco al aviso del diálogo', async () => {
    const { fixture, root } = await reserveUntilConfirm();
    flushProblem(httpMock().expectOne('/api/v1/reservations'), 409, 'InsufficientStock', 'Disponible insuficiente (disponible 3.000).');
    await settle(fixture);
    const alert = root.querySelector('[data-testid="reserve-dialog"] [role="alert"]');
    expect(document.activeElement).toBe(alert);
  });

  it('liberar: un 422 del motivo lleva el foco al motivo', async () => {
    const { fixture, root } = await releaseUntilConfirm();
    flushProblem(httpMock().expectOne(`/api/v1/reservations/${RESERVATION}/release`), 422, 'ValidationFailed',
      'Revisa los datos marcados de la liberación.', { errors: { reason: ['El motivo no puede superar 500 caracteres.'] } });
    await settle(fixture);
    const reason = root.querySelector<HTMLTextAreaElement>('#release-reason')!;
    expect(document.activeElement).toBe(reason);
    expect(reason.getAttribute('aria-invalid')).toBe('true');
    expect(reason.value).toBe('El cliente reduce el pedido');
  });

  // --- Convergence C2 (G4, FR-025, US5.4): 412 en los diálogos ---

  it('reservar: un 412 relee el pedido, conserva las cantidades y reenvía con el ETag nuevo', async () => {
    const { fixture, root } = await reserveUntilConfirm('7');
    const first = httpMock().expectOne('/api/v1/reservations');
    expect(first.request.headers.get('If-Match')).toBe('"v1"');
    flushProblem(first, 412, 'VersionConflict', 'Otra persona cambió el registro.');
    await settle(fixture);
    const dialog = () => root.querySelector('[data-testid="reserve-dialog"]')!;
    expect(text(dialog().querySelector('[role="alert"]'))).toContain('Otra persona cambió el pedido o la reserva');
    // Se vuelven a leer pedido y contexto; el diálogo sigue abierto con lo escrito.
    await flushPair(fixture, order(), '"v2"');
    expect(dialog()).not.toBeNull();
    expect(root.querySelector<HTMLInputElement>('[data-testid="reserve-qty-loc-1-lot-1"]')!.value).toBe('7');

    click(root, '[data-testid="reserve-review"]');
    await settle(fixture);
    click(root, '[data-testid="reserve-confirm"]');
    await settle(fixture);
    const again = httpMock().expectOne('/api/v1/reservations');
    expect(again.request.headers.get('If-Match')).toBe('"v2"');
    expect(again.request.headers.get('Idempotency-Key')).not.toBe(first.request.headers.get('Idempotency-Key'));
    expect(again.request.body).toEqual(first.request.body);
    again.flush({ operationId: 'op-r2', reservationId: RESERVATION, customerOrderId: ORDER, movementId: 'm' }, { status: 201, statusText: 'Created' });
    await settle(fixture);
    await flushPair(fixture, reservedOrder(), '"v3"');
    expect(text(root.querySelector('[role="status"]'))).toContain('op-r2');
  });

  it('liberar: un 412 relee la reserva, conserva cantidad, motivo y releaseId y reenvía con el ETag nuevo', async () => {
    const { fixture, root } = await releaseUntilConfirm();
    const path = `/api/v1/reservations/${RESERVATION}/release`;
    const first = httpMock().expectOne(path);
    expect(first.request.headers.get('If-Match')).toBe('"r1"');
    flushProblem(first, 412, 'VersionConflict', 'Otra persona cambió el registro.');
    await settle(fixture);
    // El diálogo relee la versión de la reserva y el pedido se vuelve a leer.
    httpMock().expectOne(`/api/v1/reservations/${RESERVATION}`).flush({
      id: RESERVATION, state: 'Active', customerOrderId: ORDER, customerOrderLineId: LINE_B, articleId: 'art-1', allocations: ALLOCATIONS
    }, { headers: { ETag: '"r2"' } });
    await settle(fixture);
    await flushPair(fixture, reservedOrder(), '"v2"');
    const dialog = root.querySelector('[data-testid="release-dialog"]')!;
    expect(text(dialog.querySelector('[role="alert"]'))).toContain('Otra persona cambió el pedido o la reserva');
    expect(root.querySelector<HTMLInputElement>(`[data-testid="release-qty-${ALLOCATION}"]`)!.value).toBe('2');
    expect(root.querySelector<HTMLTextAreaElement>('#release-reason')!.value).toBe('El cliente reduce el pedido');

    click(root, '[data-testid="release-review"]');
    await settle(fixture);
    click(root, '[data-testid="release-confirm"]');
    await settle(fixture);
    const again = httpMock().expectOne(path);
    expect(again.request.headers.get('If-Match')).toBe('"r2"');
    expect(again.request.body).toEqual(first.request.body);
    expect(again.request.body.releaseId).toBe(first.request.body.releaseId);
    again.flush({ operationId: 'op-l2', reservationId: RESERVATION, releaseId: first.request.body.releaseId, movementId: 'm', state: 'Active' });
    await settle(fixture);
    await flushPair(fixture, reservedOrder(), '"v3"');
    expect(text(root.querySelector('[role="status"]'))).toContain('op-l2');
  });

  it('sin Reservations.Manage no hay botones de reservar ni liberar', async () => {
    TestBed.resetTestingModule();
    configure(['Inventory.Read']);
    const { fixture, root } = await render();
    await flushPair(fixture, reservedOrder(), '"v1"');
    expect(root.querySelector('[data-testid^="reserve-"]')).toBeNull();
    expect(root.querySelector('[data-testid^="release-"]')).toBeNull();
    expect(text(root)).toContain('Tornillo');
  });

  async function lostReservation() {
    const { fixture, root } = await render();
    await flushPair(fixture, order(), '"v1"');
    click(root, `[data-testid="reserve-${LINE_B}"]`);
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/stock').flush({
      asOf: 'x', article: { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: true },
      totals: { physical: '10.000', reserved: '0.000', available: '10.000' },
      items: [{ location: { id: 'loc-1', code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage', isActive: true },
        lot: { id: 'lot-1', code: 'L-001', isUntracked: false }, physical: '10.000', reserved: '0.000', available: '10.000' }],
      page: 1, pageSize: 100, totalCount: 1
    });
    await settle(fixture);
    type(root, '[data-testid="reserve-qty-loc-1-lot-1"]', '7');
    await settle(fixture);
    click(root, '[data-testid="reserve-review"]');
    await settle(fixture);
    click(root, '[data-testid="reserve-confirm"]');
    await settle(fixture);
    const request = httpMock().expectOne('/api/v1/reservations');
    const key = request.request.headers.get('Idempotency-Key')!;
    flushNetworkError(request);
    await settle(fixture);
    return { fixture, root, key };
  }

  it('con una reserva pendiente no deja reservar otra y la intención se restaura tras recargar', async () => {
    const { root, key } = await lostReservation();
    expect(text(root)).toContain('Resultado pendiente de comprobar');
    expect(text(root)).toContain('Comprueba la reserva pendiente antes de reservar de nuevo');
    expect(root.querySelector<HTMLButtonElement>(`[data-testid="reserve-${LINE_B}"]`)!.disabled).toBe(true);

    // Recarga con el mismo usuario: la intención (clave, cuerpo e If-Match) se restaura.
    TestBed.resetTestingModule();
    configure();
    const again = await render();
    await flushPair(again.fixture, order(), '"v1"');
    const check = Array.from(again.root.querySelectorAll('button')).find(button => text(button) === 'Comprobar resultado')!;
    check.click();
    await settle(again.fixture);
    httpMock().expectOne(`/api/v1/operations/${key}`).flush({}, { status: 404, statusText: 'Not Found' });
    await settle(again.fixture);
    Array.from(again.root.querySelectorAll('button')).find(button => text(button) === 'Repetir la misma intención')!.click();
    await settle(again.fixture);
    const replay = httpMock().expectOne('/api/v1/reservations');
    expect(replay.request.headers.get('Idempotency-Key')).toBe(key);
    expect(replay.request.headers.get('If-Match')).toBe('"v1"');
    expect(replay.request.body).toEqual({ customerOrderLineId: LINE_B, allocations: [{ locationId: 'loc-1', lotId: 'lot-1', quantity: '7' }] });
    replay.flush({ operationId: key, reservationId: RESERVATION, customerOrderId: ORDER, movementId: 'm' }, { status: 201, statusText: 'Created' });
    await settle(again.fixture);
    // Recuperada: se vuelven a leer el pedido y su contexto.
    await flushPair(again.fixture, reservedOrder(), '"v2"');
    expect(sessionStorage.getItem(KEY)).toBe('[]');
  });

  it('cerrar sesión con una reserva pendiente y volver con el mismo usuario: «Abrir pedido» y solo comprobar', async () => {
    // 1. user-1 reserva y pierde la respuesta: queda pendiente con appRoute.
    const { key } = await lostReservation();
    // 2. Cierre de sesión: se retiran cuerpo e If-Match; quedan operationId y appRoute.
    (TestBed.inject(SessionService) as unknown as { ended: { next(): void } }).ended.next();
    const stored = sessionStorage.getItem(KEY)!;
    expect(stored).toContain(key);
    expect(stored).toContain(`/existencias/pedidos/${ORDER}`);
    expect(stored).not.toContain('customerOrderLineId');
    expect(stored).not.toContain('"v1"');

    // 3. Nuevo TestBed conservando sessionStorage; vuelve user-1.
    TestBed.resetTestingModule();
    configure();
    const back = await render();
    await flushPair(back.fixture, order(), '"v1"');
    // 4. «Abrir pedido» con la ruta del pedido, sin «Repetir»; «Comprobar» hace un solo GET.
    const open = Array.from(back.root.querySelectorAll('a')).find(link => text(link) === 'Abrir pedido');
    expect(open?.getAttribute('href')).toBe(`/existencias/pedidos/${ORDER}`);
    expect(text(back.root)).not.toContain('Repetir la misma intención');
    Array.from(back.root.querySelectorAll('button')).find(button => text(button) === 'Comprobar resultado')!.click();
    await settle(back.fixture);
    httpMock().expectOne(`/api/v1/operations/${key}`).flush({}, { status: 404, statusText: 'Not Found' });
    await settle(back.fixture);
    expect(text(back.root)).not.toContain('Repetir la misma intención');
    httpMock().verify();

    // 5. Con user-2 la referencia no aparece.
    TestBed.resetTestingModule();
    configure(MANAGER, 'user-2');
    const other = await render();
    await flushPair(other.fixture, order(), '"v1"');
    expect(text(other.root)).not.toContain('Resultado pendiente de comprobar');
    expect(Array.from(other.root.querySelectorAll('a')).some(link => text(link) === 'Abrir pedido')).toBe(false);
  });
});
