import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { PendingOperationStore } from '../../../core/operations/pending-operation.store';
import { SessionService } from '../../../core/session/session.service';

// HU-04 T013 (P-06, D-15): el panel de una operación pendiente de /inventarios ofrece «Abrir conteo»
// (contracts/cycle-counts-ui.md § Resultados y errores).
const ORDER = '10000000-0000-4000-8000-000000000001';
const SHIPMENT = '30000000-0000-4000-8000-000000000003';
const COUNT = '40000000-0000-4000-8000-000000000004';

describe('OperationStatusComponent con operaciones de HU-04', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OperationStatusComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).user.set({ id: 'user-1', email: 'r@example.test', roles: [],
      permissions: ['Inventory.Read', 'Counts.Record', 'Inventory.Adjust', 'Shipping.Record'] });
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  function links(): { text: string; href: string | null }[] {
    const fixture = TestBed.createComponent(OperationStatusComponent);
    fixture.detectChanges();
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('a'))
      .map(link => ({ text: link.textContent?.trim() ?? '', href: link.getAttribute('href') }));
  }

  it('una confirmación de conteo pendiente ofrece «Abrir conteo» con su ruta', () => {
    TestBed.inject(PendingOperationStore).begin('POST', `/api/v1/cycle-counts/${COUNT}/confirm`, {}, '"AQ=="', `/inventarios/${COUNT}`);
    expect(links()).toEqual([{ text: 'Abrir conteo', href: `/inventarios/${COUNT}` }]);
  });

  it('el alta de un conteo ofrece «Abrir conteo» hacia /inventarios/nuevo', () => {
    TestBed.inject(PendingOperationStore).begin('POST', '/api/v1/cycle-counts', { locationIds: ['x'] }, undefined, '/inventarios/nuevo');
    expect(links()).toEqual([{ text: 'Abrir conteo', href: '/inventarios/nuevo' }]);
  });

  it('las operaciones de HU-03 siguen ofreciendo «Abrir salida» y «Abrir pedido»', () => {
    const store = TestBed.inject(PendingOperationStore);
    store.begin('POST', `/api/v1/shipments/${SHIPMENT}/confirm`, {}, '"AQ=="', `/salidas/${SHIPMENT}`);
    store.begin('POST', '/api/v1/shipments', { customerOrderId: ORDER }, undefined, `/salidas/pedidos/${ORDER}`);
    expect(links().map(link => link.text)).toEqual(['Abrir salida', 'Abrir pedido']);
  });
});
