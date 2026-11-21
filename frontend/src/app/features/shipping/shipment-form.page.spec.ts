import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { ShipmentFormPage } from './shipment-form.page';

export async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 6; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

// HU-03 T015 (US1.1, US1.8–US1.10; FR-004–FR-006a, FR-024; contracts/shipping-ui.md § Preparar y editar).
export const ORDER = '10000000-0000-4000-8000-000000000001';
export const LINE = 'aaaaaaaa-0000-4000-8000-000000000001';
export const SHIPMENT = '30000000-0000-4000-8000-000000000003';
export const SHIPMENT_LINE = 'cccccccc-0000-4000-8000-000000000001';
const N01 = '50000000-0000-4000-8000-000000000001';
const N02 = '50000000-0000-4000-8000-000000000002';
const LOT = '60000000-0000-4000-8000-000000000001';
export const RECORD = ['Inventory.Read', 'Shipping.Record'];

export function order(reservations: unknown[] = [], state = 'Open') {
  return {
    id: ORDER, externalReference: 'PED-0042', customerReference: 'Cliente Norte', state,
    lines: [{ id: LINE, articleId: 'art-1', ordered: '6.000', shipped: '0.000', pending: '6.000', reservedRemaining: '7.000', reservations }]
  };
}

const RESERVED = [{
  id: 'r-1', state: 'Active', allocations: [
    // Por Id: la de N02 va antes que la de N01.
    { id: 'a-2', articleId: 'art-1', locationId: N01, lotId: LOT, allocated: '4.000', consumed: '0.000', released: '0.000', remaining: '4.000' },
    { id: 'a-1', articleId: 'art-1', locationId: N02, lotId: LOT, allocated: '3.000', consumed: '0.000', released: '0.000', remaining: '3.000' }
  ]
}];

export function orderContext(isActive = true) {
  return {
    orderVersion: '"o1"', asOf: '2026-11-21T10:00:00Z', creationOperationId: null,
    articles: [{ id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive, tracksLot: false, allowsFractional: false }],
    locations: [
      { id: N01, code: 'N01', aisle: 'P01', rack: 'E01', level: 'N01', isActive: true },
      { id: N02, code: 'N02', aisle: 'P01', rack: 'E01', level: 'N02', isActive: true }
    ],
    lots: [{ id: LOT, articleId: 'art-1', code: 'SIN-LOTE', isUntracked: true }]
  };
}

export function shipment(state = 'Draft', quantity = '2.000', operationId: string | null = null) {
  return {
    id: SHIPMENT, customerOrderId: ORDER, state, occurredAt: '2026-11-21T08:00:00Z', recordedAt: '2026-11-21T08:05:00Z',
    actorId: 'user-1', operationId,
    lines: [{ id: SHIPMENT_LINE, customerOrderLineId: LINE, articleId: 'art-1', locationId: N01, lotId: LOT, quantity,
      ownReservationConsumed: state === 'Draft' ? '0.000' : '2.000', consumptions: [] }]
  };
}

export function shipmentContext(version = '"s1"', isActive = true, review = [{
  lineId: SHIPMENT_LINE, pending: '6.000', physical: '10.000', reserved: '7.000', ownReserved: '4.000', otherReserved: '3.000', possible: '6.000'
}]) {
  const base = orderContext(isActive);
  return {
    shipmentVersion: version, orderVersion: '"o1"', asOf: '2026-11-21T10:00:00Z', actorName: 'op@h0.test',
    order: { id: ORDER, externalReference: 'PED-0042', customerReference: 'Cliente Norte', state: 'Open' },
    articles: base.articles, locations: [base.locations[0]], lots: base.lots, review
  };
}

function stockPage(count: number) {
  return {
    asOf: 'x', article: { id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: true, tracksLot: false },
    totals: { physical: '0', reserved: '0', available: '0' }, page: 1, pageSize: 100, totalCount: count,
    items: Array.from({ length: count }, (_, index) => ({
      location: { id: `loc-${index + 1}`, code: `B${String(index + 1).padStart(2, '0')}`, aisle: 'P', rack: 'E', level: 'N', kind: 'Storage', isActive: true },
      lot: { id: LOT, code: 'SIN-LOTE', isUntracked: true }, physical: '5.000', reserved: '0.000', available: '5.000'
    }))
  };
}

/** Lecturas de una salida existente: detalle + contexto de la salida y detalle + contexto del pedido. */
export async function flushExisting(fixture: { detectChanges(): void }, body = shipment(), version = '"s1"',
  context = shipmentContext(version), reservations: unknown[] = []) {
  httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`).flush(body, { headers: { ETag: version } });
  await settle(fixture);
  httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}/context`).flush(context);
  await settle(fixture);
  httpMock().expectOne(`/api/v1/customer-orders/${ORDER}`).flush(order(reservations), { headers: { ETag: '"o1"' } });
  await settle(fixture);
  httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`).flush(orderContext());
  await settle(fixture);
}

export function click(root: HTMLElement, selector: string) {
  const element = root.querySelector<HTMLButtonElement>(selector);
  if (!element) throw new Error(`No existe ${selector}`);
  element.click();
}

export function type(root: HTMLElement, selector: string, value: string) {
  const input = root.querySelector<HTMLInputElement>(selector)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('ShipmentFormPage · preparar y editar el borrador', () => {
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function renderNew(reservations: unknown[] = RESERVED) {
    TestBed.configureTestingModule({ imports: [ShipmentFormPage], providers: locationsTestProviders({ orderId: ORDER }) });
    signIn(RECORD);
    const fixture = TestBed.createComponent(ShipmentFormPage);
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}`).flush(order(reservations), { headers: { ETag: '"o1"' } });
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`).flush(orderContext());
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  async function renderExisting() {
    TestBed.configureTestingModule({ imports: [ShipmentFormPage], providers: locationsTestProviders({ id: SHIPMENT }) });
    signIn(RECORD);
    const fixture = TestBed.createComponent(ShipmentFormPage);
    await settle(fixture);
    await flushExisting(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('propone los orígenes de la reserva propia por Id, acotados al pendiente, y los marca', async () => {
    const { fixture, root } = await renderNew();
    expect(text(root.querySelector('h1'))).toContain('PED-0042');
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    const rows = Array.from(root.querySelectorAll('[data-testid="shipment-line"]'));
    expect(rows.map(row => text(row.querySelector('[data-testid="origin"]')))).toEqual(['N02 · sin lote', 'N01 · sin lote']);
    expect(rows.map(row => row.querySelector<HTMLInputElement>('input')!.value)).toEqual(['3.000', '3.000']);
    expect(rows.every(row => text(row).includes('Propuesto desde la reserva'))).toBe(true);
    expect(text(rows[0])).toContain('Pendiente 6.000 UD');
  });

  it('sin reserva propia no propone nada; el selector ofrece todas las combinaciones, también la 30', async () => {
    const { fixture, root } = await renderNew([]);
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    expect(root.querySelectorAll('[data-testid="shipment-line"]')).toHaveLength(0);
    click(root, `[data-testid="choose-origin-${LINE}"]`);
    await settle(fixture);
    const request = httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('articleId') === 'art-1');
    expect(request.request.params.get('pageSize')).toBe('100');
    expect(request.request.params.get('includeEmpty')).toBe('false');
    request.flush(stockPage(30));
    await settle(fixture);
    const options = Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="bucket-option"]'));
    expect(options).toHaveLength(30);
    options[29].click();
    await settle(fixture);
    const row = root.querySelector('[data-testid="shipment-line"]')!;
    expect(text(row.querySelector('[data-testid="origin"]'))).toBe('B30 · sin lote');
    expect(row.querySelector<HTMLInputElement>('input')!.value).toBe('');
    expect(text(row)).not.toContain('Propuesto desde la reserva');
  });

  it('guardar un borrador nuevo envía clave, cuerpo y appRoute y abre la salida', async () => {
    const { fixture, root } = await renderNew();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    type(root, '[data-testid="shipment-line"] input', '2');
    await settle(fixture);
    click(root, '[data-testid="save"]');
    await settle(fixture);
    const request = httpMock().expectOne('/api/v1/shipments');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.headers.has('If-Match')).toBe(false);
    expect(request.request.body.customerOrderId).toBe(ORDER);
    expect(request.request.body.lines).toEqual([
      { customerOrderLineId: LINE, locationId: N02, lotId: LOT, quantity: '2' },
      { customerOrderLineId: LINE, locationId: N01, lotId: LOT, quantity: '3.000' }]);
    expect(Date.parse(request.request.body.occurredAt)).toBeLessThanOrEqual(Date.now());
    const pending = JSON.parse(sessionStorage.getItem('inventario.pending-operations')!);
    expect(pending[0].appRoute).toBe(`/salidas/pedidos/${ORDER}`);
    request.flush({ operationId: 'op-1', shipment: { ...shipment(), lines: [] } },
      { status: 201, statusText: 'Created', headers: { ETag: '"s1"', Location: `/api/v1/shipments/${SHIPMENT}` } });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(`/salidas/${SHIPMENT}`);
  });

  it('un 422 por línea marca su cantidad, conserva los datos y lleva el foco al primer error', async () => {
    const { fixture, root } = await renderNew();
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    type(root, '[data-testid="shipment-line"] input', '9');
    click(root, '[data-testid="save"]');
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/shipments'), 422, 'ValidationFailed', 'Revisa las cantidades marcadas de la salida.',
      { errors: { 'lines[0].quantity': ['La suma de esta línea de pedido supera su pendiente (6.000).'] } });
    await settle(fixture);
    const input = root.querySelector<HTMLInputElement>('[data-testid="shipment-line"] input')!;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.value).toBe('9');
    expect(text(root.querySelector('[data-testid="shipment-line"]'))).toContain('supera su pendiente');
    expect(document.activeElement).toBe(input);
  });

  it('editar un borrador envía PUT con If-Match y un 412 recarga sin perder lo escrito', async () => {
    const { fixture, root } = await renderExisting();
    expect(root.querySelector<HTMLInputElement>('[data-testid="shipment-line"] input')!.value).toBe('2.000');
    type(root, '[data-testid="shipment-line"] input', '5');
    click(root, '[data-testid="save"]');
    await settle(fixture);
    const request = httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"s1"');
    expect(request.request.body.customerOrderId).toBeUndefined();
    flushProblem(request, 412, 'VersionConflict', 'Otra persona cambió el registro.');
    await settle(fixture);
    await flushExisting(fixture, shipment('Draft', '1.000'), '"s2"');
    expect(text(root.querySelector('[role="alert"]'))).toContain('Otra persona cambió la salida');
    expect(root.querySelector<HTMLInputElement>('[data-testid="shipment-line"] input')!.value).toBe('5');

    click(root, '[data-testid="save"]');
    await settle(fixture);
    expect(httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`).request.headers.get('If-Match')).toBe('"s2"');
  });

  it('eliminar pide confirmación, envía DELETE con If-Match y vuelve al pedido con el aviso', async () => {
    const { fixture, root } = await renderExisting();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    click(root, '[data-testid="delete"]');
    await settle(fixture);
    httpMock().expectNone(`/api/v1/shipments/${SHIPMENT}`);
    expect(text(root.querySelector('[data-testid="delete-confirmation"]'))).toContain('¿Eliminar el borrador?');
    click(root, '[data-testid="delete-confirm"]');
    await settle(fixture);
    const request = httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.get('If-Match')).toBe('"s1"');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(`/salidas/pedidos/${ORDER}`,
      { state: { notice: 'Borrador eliminado. No se ha modificado ninguna existencia.' } });
  });
});
