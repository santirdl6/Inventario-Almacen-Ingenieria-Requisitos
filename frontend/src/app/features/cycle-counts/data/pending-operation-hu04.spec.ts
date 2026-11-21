import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../../core/session/session.service';
import { PendingOperationStore, mutationPermission } from '../../../core/operations/pending-operation.store';

// HU-04 T013 (P-06, D-15): ampliaciones de la base de A para las operaciones de /inventarios, probadas
// aquí para no modificar las specs de A (contracts/cycle-counts-ui.md § Rutas).
const COUNT = '40000000-0000-4000-8000-000000000004';
const LINE = '50000000-0000-4000-8000-000000000005';

function signIn(permissions: string[], id = 'user-1'): void {
  TestBed.inject(SessionService).user.set({ id, email: `${id}@example.test`, roles: [], permissions });
}

describe('mutationPermission() de las operaciones de HU-04', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => sessionStorage.clear());

  it('asigna Counts.Record a crear, editar, iniciar, registrar líneas y enviar', () => {
    expect(mutationPermission('/api/v1/cycle-counts')).toBe('Counts.Record');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}`)).toBe('Counts.Record');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/start`)).toBe('Counts.Record');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/lines`)).toBe('Counts.Record');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/lines/${LINE}`)).toBe('Counts.Record');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/submit`)).toBe('Counts.Record');
  });

  it('asigna Inventory.Adjust a revisar, confirmar y cancelar', () => {
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/review`)).toBe('Inventory.Adjust');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/confirm`)).toBe('Inventory.Adjust');
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/cancel`)).toBe('Inventory.Adjust');
  });

  it('no trata lecturas ni rutas mal formadas como intenciones y no cambia las anteriores', () => {
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/context`)).toBeNull();
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/lines/${LINE}/shipment-candidates`)).toBeNull();
    expect(mutationPermission('/api/v1/cycle-counts/no-es-uuid/confirm')).toBeNull();
    expect(mutationPermission(`/api/v1/cycle-counts/${COUNT}/otra`)).toBeNull();
    expect(mutationPermission(`/api/v1/shipments/${COUNT}/confirm`)).toBe('Shipping.Record');
    expect(mutationPermission('/api/v1/reservations')).toBe('Reservations.Manage');
    expect(mutationPermission('/api/v1/families')).toBe('MasterData.Manage');
  });

  it('abre la intención de confirmar solo con Inventory.Adjust y conserva la ruta /inventarios/{id}', () => {
    signIn(['Inventory.Read', 'Counts.Record', 'Inventory.Adjust']);
    const store = TestBed.inject(PendingOperationStore);
    const operation = store.begin('POST', `/api/v1/cycle-counts/${COUNT}/confirm`, {}, '"AQ=="', `/inventarios/${COUNT}`);
    expect(operation.appRoute).toBe(`/inventarios/${COUNT}`);
    expect(store.begin('POST', '/api/v1/cycle-counts', { locationIds: ['x'] }, undefined, '/inventarios/nuevo').appRoute)
      .toBe('/inventarios/nuevo');
    signIn(['Inventory.Read', 'Counts.Record'], 'user-2');
    expect(() => store.begin('POST', `/api/v1/cycle-counts/${COUNT}/confirm`, {}, '"AQ=="')).toThrow();
  });

  it('descarta rutas de la aplicación ajenas a /inventarios/(nuevo|{uuid})', () => {
    signIn(['Inventory.Read', 'Counts.Record']);
    const store = TestBed.inject(PendingOperationStore);
    expect(store.begin('POST', `/api/v1/cycle-counts/${COUNT}/start`, {}, '"AQ=="', '/inventarios/otra').appRoute).toBeUndefined();
  });
});
