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
