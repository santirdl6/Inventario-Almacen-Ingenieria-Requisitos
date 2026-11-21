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
