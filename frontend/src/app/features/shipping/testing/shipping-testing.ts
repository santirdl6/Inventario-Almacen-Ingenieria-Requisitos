import { httpMock } from '../../locations/testing/locations-testing';

// HU-03 · Datos y utilidades comunes de las specs de /salidas (T015, T016).
export async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 6; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

export const ORDER = '10000000-0000-4000-8000-000000000001';
export const LINE = 'aaaaaaaa-0000-4000-8000-000000000001';
export const SHIPMENT = '30000000-0000-4000-8000-000000000003';
export const SHIPMENT_LINE = 'cccccccc-0000-4000-8000-000000000001';
export const N01 = '50000000-0000-4000-8000-000000000001';
export const N02 = '50000000-0000-4000-8000-000000000002';
export const LOT = '60000000-0000-4000-8000-000000000001';
export const RECORD = ['Inventory.Read', 'Shipping.Record'];

export function order(reservations: unknown[] = [], state = 'Open') {
  return {
    id: ORDER, externalReference: 'PED-0042', customerReference: 'Cliente Norte', state,
    lines: [{ id: LINE, articleId: 'art-1', ordered: '6.000', shipped: '0.000', pending: '6.000', reservedRemaining: '7.000', reservations }]
  };
}

export const RESERVED = [{
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

export function stockPage(count: number) {
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
  context = shipmentContext(version), reservations: unknown[] = [], corrections: unknown[] = []) {
  httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`).flush(body, { headers: { ETag: version } });
  await settle(fixture);
  httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}/context`).flush(context);
  await settle(fixture);
  httpMock().expectOne(`/api/v1/customer-orders/${ORDER}`).flush(order(reservations), { headers: { ETag: '"o1"' } });
  await settle(fixture);
  httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`).flush(orderContext());
  await settle(fixture);
  // HU-04 Convergence C3 (US4.3): una salida confirmada busca los conteos que la corrigen.
  if (body.state === 'Confirmed') {
    httpMock().expectOne(r => r.url === '/api/v1/cycle-counts' && r.params.get('correctsShipmentId') === SHIPMENT)
      .flush({ items: corrections, page: 1, pageSize: 100, totalCount: corrections.length });
    await settle(fixture);
  }
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

