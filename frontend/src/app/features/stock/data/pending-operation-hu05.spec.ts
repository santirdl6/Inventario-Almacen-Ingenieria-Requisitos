import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/session/session.service';
import { PendingOperationStore, mutationPermission } from '../../../core/operations/pending-operation.store';

// HU-05 T016: ampliaciones de la base de A para las operaciones de /existencias, probadas aquí
// para no modificar las specs de A (contracts/stock-ui.md § Navegación y recuperación).
const ORDER = '10000000-0000-4000-8000-000000000001';
const RESERVATION = '20000000-0000-4000-8000-000000000002';

function signIn(permissions: string[], id = 'user-1'): void {
  TestBed.inject(SessionService).user.set({ id, email: `${id}@example.test`, roles: [], permissions });
}

describe('mutationPermission() de las operaciones de HU-05', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => sessionStorage.clear());

  it('asigna Shipping.Record al alta y Reservations.Manage a reservar y liberar', () => {
    expect(mutationPermission('/api/v1/customer-orders')).toBe('Shipping.Record');
    expect(mutationPermission('/api/v1/reservations')).toBe('Reservations.Manage');
    expect(mutationPermission(`/api/v1/reservations/${RESERVATION}/release`)).toBe('Reservations.Manage');
    // Rutas que no son mutaciones de HU-05 siguen sin permiso de intención.
    expect(mutationPermission(`/api/v1/customer-orders/${ORDER}`)).toBeNull();
    expect(mutationPermission(`/api/v1/reservations/${RESERVATION}`)).toBeNull();
    expect(mutationPermission('/api/v1/reservations/no-es-uuid/release')).toBeNull();
    // Las de A no cambian.
    expect(mutationPermission('/api/v1/families')).toBe('MasterData.Manage');
    expect(mutationPermission('/api/v1/locations')).toBe('Locations.Manage');
  });

  it('abre la intención solo con el permiso de la operación', () => {
    signIn(['Inventory.Read', 'Shipping.Record']);
    const store = TestBed.inject(PendingOperationStore);
    expect(store.begin('POST', '/api/v1/customer-orders', { externalReference: 'PED-1' }).resource).toBe('/api/v1/customer-orders');
    expect(() => store.begin('POST', '/api/v1/reservations', { customerOrderLineId: 'x' }, '"AQ=="')).toThrow();
  });
});

describe('appRoute de la intención pendiente (HU-05)', () => {
  const KEY = 'inventario.pending-operations';
  const route = `/existencias/pedidos/${ORDER}`;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    signIn(['Inventory.Read', 'Reservations.Manage']);
  });
  afterEach(() => sessionStorage.clear());

  function reload(): PendingOperationStore {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    signIn(['Inventory.Read', 'Reservations.Manage']);
    return TestBed.inject(PendingOperationStore);
  }

  it('se guarda al abrir la intención y sobrevive a la recarga', () => {
    const store = TestBed.inject(PendingOperationStore);
    const operation = store.begin('POST', '/api/v1/reservations', { customerOrderLineId: 'l' }, '"AQ=="', route);
    expect(operation.appRoute).toBe(route);
    const restored = reload().operations()[0];
    expect(restored.operationId).toBe(operation.operationId);
    expect(restored.appRoute).toBe(route);
    expect(restored.payload).toEqual({ customerOrderLineId: 'l' });
  });

  it('se conserva al cerrar sesión, sin el cuerpo ni el If-Match', () => {
    const store = TestBed.inject(PendingOperationStore);
    const operation = store.begin('POST', '/api/v1/reservations', { customerOrderLineId: 'secreto' }, '"AQ=="', route);
    (TestBed.inject(SessionService) as unknown as { ended: { next(): void } }).ended.next();
    const reference = store.operations()[0];
    expect(reference).toEqual({ operationId: operation.operationId, actorId: 'user-1', method: 'POST',
      resource: '/api/v1/reservations', appRoute: route });
    const stored = sessionStorage.getItem(KEY)!;
    expect(stored).toContain(route);
    expect(stored).toContain(operation.operationId);
    expect(stored).not.toContain('secreto');
    expect(stored).not.toContain('AQ==');
  });

  it('solo restaura rutas de pedido válidas y no añade el campo a las intenciones de A', () => {
    sessionStorage.setItem(KEY, JSON.stringify([
      { operationId: 'a', actorId: 'user-1', method: 'POST', resource: '/api/v1/reservations', appRoute: 'https://otro.example/x' },
      { operationId: 'b', actorId: 'user-1', method: 'POST', resource: '/api/v1/customer-orders', appRoute: '/existencias/pedidos/nuevo' },
      { operationId: 'c', actorId: 'user-1', method: 'POST', resource: '/api/v1/reservations', appRoute: '/maestros/articulos/nuevo' }
    ]));
    const operations = reload().operations();
    expect(operations.map(item => [item.operationId, item.appRoute])).toEqual([
      ['a', undefined], ['b', '/existencias/pedidos/nuevo'], ['c', undefined]]);
    expect('appRoute' in operations[0]).toBe(false);
    const store = reload();
    signIn(['MasterData.Manage']);
    expect('appRoute' in store.begin('POST', '/api/v1/families', { code: 'F' })).toBe(false);
  });
});
