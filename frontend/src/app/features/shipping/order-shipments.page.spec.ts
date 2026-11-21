import { TestBed } from '@angular/core/testing';
import { httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { OrderShipmentsPage } from './order-shipments.page';

async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 5; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

// HU-03 T014 (US4.1, US4.3, FR-018; contracts/shipping-ui.md § Pedido y sus salidas).
const ORDER = '10000000-0000-4000-8000-000000000001';
const LINE = 'aaaaaaaa-0000-4000-8000-000000000001';
const DRAFT = '30000000-0000-4000-8000-000000000003';
const CONFIRMED = '30000000-0000-4000-8000-000000000004';

function order(state = 'Open', shipped = '2.000', pending = '4.000') {
  return {
    id: ORDER, externalReference: 'PED-0042', customerReference: 'Cliente Norte', state,
    lines: [{ id: LINE, articleId: 'art-1', ordered: '6.000', shipped, pending, reservedRemaining: '1.000', reservations: [] }]
  };
}

const CONTEXT = {
  orderVersion: '"v1"', asOf: '2026-11-21T10:00:00Z', creationOperationId: null,
  articles: [{ id: 'art-1', sku: 'ART-01', name: 'Tornillo', baseUnit: 'UD', isActive: false, tracksLot: false, allowsFractional: false }],
  locations: [], lots: []
};

const SHIPMENTS = {
  items: [
    { id: DRAFT, state: 'Draft', occurredAt: '2026-11-21T09:00:00Z', recordedAt: '2026-11-21T09:05:00Z', actorName: 'op@h0.test', lineCount: 1, totalQuantity: '1.000' },
    { id: CONFIRMED, state: 'Confirmed', occurredAt: '2026-11-20T09:00:00Z', recordedAt: '2026-11-20T09:05:00Z', actorName: 'op@h0.test', lineCount: 1, totalQuantity: '2.000' }
  ],
  page: 1, pageSize: 25, totalCount: 2
};

describe('OrderShipmentsPage', () => {
  afterEach(() => httpMock().verify());

  async function render(permissions: string[], state = 'Open') {
    TestBed.configureTestingModule({ imports: [OrderShipmentsPage], providers: locationsTestProviders({ orderId: ORDER }) });
    signIn(permissions);
    const fixture = TestBed.createComponent(OrderShipmentsPage);
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}`).flush(state === 'Open' ? order() : order(state, '6.000', '0.000'),
      { headers: { ETag: '"v1"' } });
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`).flush(CONTEXT);
    await settle(fixture);
    httpMock().expectOne(r => r.url === '/api/v1/shipments' && r.params.get('customerOrderId') === ORDER).flush(SHIPMENTS);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('muestra el pedido, sus pendientes con la unidad y las salidas con su estado en texto', async () => {
    const { root } = await render(['Inventory.Read', 'Shipping.Record']);
    expect(text(root.querySelector('h1'))).toContain('PED-0042');
    expect(text(root)).toContain('Cliente Norte');
    expect(text(root)).toContain('Abierto');
    const line = text(root.querySelector('[data-testid="order-line"]'));
    expect(line).toContain('ART-01 · Tornillo');
    expect(line).toContain('Inactivo');
    expect(line).toContain('Pendiente 4.000 UD');
    expect(line).toContain('Servido 2.000 UD');
    const rows = Array.from(root.querySelectorAll('[data-testid="shipment-row"]')).map(text);
    expect(rows[0]).toContain('Borrador');
    expect(rows[1]).toContain('Confirmada');
    expect(rows[1]).toContain('op@h0.test');
    // Un solo artículo en el pedido: el total lleva su unidad (FR-025).
    expect(rows[1]).toContain('2.000 UD');
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('[data-testid="shipment-row"] a')).map(link => link.getAttribute('href'));
    expect(links).toEqual([`/salidas/${DRAFT}`, `/salidas/${CONFIRMED}`]);
    const create = root.querySelector<HTMLAnchorElement>('[data-testid="new-shipment"]');
    expect(create?.getAttribute('href')).toBe(`/salidas/pedidos/${ORDER}/nueva`);
  });

  it('un pedido servido lo avisa y no ofrece «Nueva salida»', async () => {
    const { root } = await render(['Inventory.Read', 'Shipping.Record'], 'Fulfilled');
    expect(text(root)).toContain('Servido');
    expect(text(root.querySelector('[data-testid="fulfilled"]'))).toBe('El pedido está servido: no admite nuevas salidas.');
    expect(root.querySelector('[data-testid="new-shipment"]')).toBeNull();
  });

  it('al volver tras eliminar un borrador muestra el aviso que trae la navegación', async () => {
    history.replaceState({ notice: 'Borrador eliminado. No se ha modificado ninguna existencia.' }, '');
    const { root } = await render(['Inventory.Read', 'Shipping.Record']);
    expect(text(root.querySelector('[role="status"]'))).toBe('Borrador eliminado. No se ha modificado ninguna existencia.');
    history.replaceState(null, '');
  });

  it('sin Shipping.Record se consulta todo pero no hay acciones', async () => {
    const { root } = await render(['Inventory.Read']);
    expect(root.querySelectorAll('[data-testid="shipment-row"]')).toHaveLength(2);
    expect(root.querySelector('[data-testid="new-shipment"]')).toBeNull();
  });
});
