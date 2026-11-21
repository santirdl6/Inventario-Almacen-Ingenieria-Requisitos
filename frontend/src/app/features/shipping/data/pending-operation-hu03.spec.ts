import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/session/session.service';
import { PendingOperationStore, mutationPermission } from '../../../core/operations/pending-operation.store';

// HU-03 T013 (P-06, D-14): ampliaciones de la base de A para las operaciones de /salidas, probadas
// aquí para no modificar las specs de A (contracts/shipping-ui.md § Navegación y recuperación).
const SHIPMENT = '30000000-0000-4000-8000-000000000003';

function signIn(permissions: string[], id = 'user-1'): void {
  TestBed.inject(SessionService).user.set({ id, email: `${id}@example.test`, roles: [], permissions });
}

describe('mutationPermission() de las operaciones de HU-03', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => sessionStorage.clear());

  it('asigna Shipping.Record a crear, editar, eliminar y confirmar una salida', () => {
    expect(mutationPermission('/api/v1/shipments')).toBe('Shipping.Record');
    expect(mutationPermission(`/api/v1/shipments/${SHIPMENT}`)).toBe('Shipping.Record');
    expect(mutationPermission(`/api/v1/shipments/${SHIPMENT}/confirm`)).toBe('Shipping.Record');
    // Lecturas y rutas mal formadas no son intenciones.
    expect(mutationPermission(`/api/v1/shipments/${SHIPMENT}/context`)).toBeNull();
    expect(mutationPermission('/api/v1/shipments/no-es-uuid/confirm')).toBeNull();
    expect(mutationPermission('/api/v1/customer-orders/lookup')).toBeNull();
    // Las de A y HU-05 no cambian.
    expect(mutationPermission('/api/v1/customer-orders')).toBe('Shipping.Record');
    expect(mutationPermission('/api/v1/reservations')).toBe('Reservations.Manage');
    expect(mutationPermission('/api/v1/families')).toBe('MasterData.Manage');
  });

  it('abre la intención de confirmar solo con Shipping.Record', () => {
    signIn(['Inventory.Read', 'Shipping.Record']);
    const store = TestBed.inject(PendingOperationStore);
    expect(store.begin('POST', `/api/v1/shipments/${SHIPMENT}/confirm`, { orderVersion: '"AQ=="' }, '"AQ=="').resource)
      .toBe(`/api/v1/shipments/${SHIPMENT}/confirm`);
    signIn(['Inventory.Read'], 'user-2');
    expect(() => store.begin('POST', '/api/v1/shipments', { customerOrderId: 'x' })).toThrow();
  });
});

describe('appRoute de las intenciones de /salidas (HU-03)', () => {
  const KEY = 'inventario.pending-operations';
  const ORDER = '10000000-0000-4000-8000-000000000001';

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    signIn(['Inventory.Read', 'Shipping.Record']);
  });
  afterEach(() => sessionStorage.clear());

  function reload(): PendingOperationStore {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    signIn(['Inventory.Read', 'Shipping.Record']);
    return TestBed.inject(PendingOperationStore);
  }

  it('admite la ruta del pedido al crear y la de la salida al editar, eliminar o confirmar, y sobreviven a la recarga', () => {
    const store = TestBed.inject(PendingOperationStore);
    const created = store.begin('POST', '/api/v1/shipments', { customerOrderId: ORDER }, undefined, `/salidas/pedidos/${ORDER}`);
    const confirmed = store.begin('POST', `/api/v1/shipments/${SHIPMENT}/confirm`, { orderVersion: '"AQ=="' }, '"AQ=="',
      `/salidas/${SHIPMENT}`);
    expect(created.appRoute).toBe(`/salidas/pedidos/${ORDER}`);
    expect(confirmed.appRoute).toBe(`/salidas/${SHIPMENT}`);
    expect(reload().operations().map(item => item.appRoute)).toEqual([`/salidas/pedidos/${ORDER}`, `/salidas/${SHIPMENT}`]);
  });

  it('descarta rutas de /salidas que no son de un pedido ni de una salida y conserva las de HU-05', () => {
    sessionStorage.setItem(KEY, JSON.stringify([
      { operationId: 'a', actorId: 'user-1', method: 'POST', resource: '/api/v1/shipments', appRoute: '/salidas/otra-cosa' },
      { operationId: 'b', actorId: 'user-1', method: 'POST', resource: '/api/v1/shipments', appRoute: `/salidas/pedidos/${ORDER}/nueva` },
      { operationId: 'c', actorId: 'user-1', method: 'POST', resource: '/api/v1/customer-orders', appRoute: `/existencias/pedidos/${ORDER}` }
    ]));
    expect(reload().operations().map(item => [item.operationId, item.appRoute])).toEqual([
      ['a', undefined], ['b', undefined], ['c', `/existencias/pedidos/${ORDER}`]]);
    sessionStorage.clear();
    const store = reload();
    expect('appRoute' in store.begin('POST', '/api/v1/shipments', { customerOrderId: ORDER }, undefined, '/salidas')).toBe(false);
  });
});
