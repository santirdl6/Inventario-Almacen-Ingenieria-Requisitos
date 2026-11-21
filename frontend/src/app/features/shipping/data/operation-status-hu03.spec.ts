import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { PendingOperationStore } from '../../../core/operations/pending-operation.store';
import { SessionService } from '../../../core/session/session.service';

// HU-03 T013 (P-06, D-14): el panel de una operación pendiente de /salidas ofrece «Abrir salida» para
// la salida y «Abrir pedido» para el alta del borrador (contracts/shipping-ui.md § Navegación).
const ORDER = '10000000-0000-4000-8000-000000000001';
const SHIPMENT = '30000000-0000-4000-8000-000000000003';

describe('OperationStatusComponent con operaciones de HU-03', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OperationStatusComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).user.set({ id: 'user-1', email: 'o@example.test', roles: [],
      permissions: ['Inventory.Read', 'Shipping.Record', 'Reservations.Manage'] });
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  function links(): HTMLAnchorElement[] {
    const fixture = TestBed.createComponent(OperationStatusComponent);
    fixture.detectChanges();
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('a'));
  }

  it('una confirmación de salida pendiente ofrece «Abrir salida» con su ruta', () => {
    TestBed.inject(PendingOperationStore).begin('POST', `/api/v1/shipments/${SHIPMENT}/confirm`, { orderVersion: '"AQ=="' },
      '"AQ=="', `/salidas/${SHIPMENT}`);
    const open = links().find(link => link.textContent?.trim() === 'Abrir salida');
    expect(open?.getAttribute('href')).toBe(`/salidas/${SHIPMENT}`);
    expect(links().some(link => link.textContent?.trim() === 'Abrir pedido')).toBe(false);
  });

  it('el alta de un borrador ofrece «Abrir pedido» hacia el pedido de /salidas', () => {
    TestBed.inject(PendingOperationStore).begin('POST', '/api/v1/shipments', { customerOrderId: ORDER }, undefined,
      `/salidas/pedidos/${ORDER}`);
    const open = links().find(link => link.textContent?.trim() === 'Abrir pedido');
    expect(open?.getAttribute('href')).toBe(`/salidas/pedidos/${ORDER}`);
  });

  it('las operaciones de HU-05 siguen ofreciendo «Abrir pedido»', () => {
    TestBed.inject(PendingOperationStore).begin('POST', '/api/v1/reservations', { customerOrderLineId: 'l' }, '"AQ=="',
      `/existencias/pedidos/${ORDER}`);
    expect(links().map(link => link.textContent?.trim())).toContain('Abrir pedido');
    expect(links().map(link => link.textContent?.trim())).not.toContain('Abrir salida');
  });
});
