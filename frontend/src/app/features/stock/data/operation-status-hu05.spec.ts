import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { PendingOperationStore } from '../../../core/operations/pending-operation.store';
import { SessionService } from '../../../core/session/session.service';

// HU-05 T016: OperationStatusComponent usa appRoute para «Abrir pedido» y para el enlace resumido
// del layout (contracts/stock-ui.md § Navegación y recuperación de una operación pendiente).
const ORDER = '10000000-0000-4000-8000-000000000001';
const ROUTE = `/existencias/pedidos/${ORDER}`;

describe('OperationStatusComponent con operaciones de HU-05', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OperationStatusComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).user.set({ id: 'user-1', email: 'r@example.test', roles: [], permissions: ['Inventory.Read', 'Reservations.Manage', 'MasterData.Manage'] });
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  function render(summaryOnly = false) {
    const fixture = TestBed.createComponent(OperationStatusComponent);
    fixture.componentRef.setInput('summaryOnly', summaryOnly);
    fixture.detectChanges();
    return fixture;
  }

  it('route() usa appRoute cuando existe y conserva las rutas de A', () => {
    const store = TestBed.inject(PendingOperationStore);
    const reservation = store.begin('POST', '/api/v1/reservations', { customerOrderLineId: 'l' }, '"AQ=="', ROUTE);
    const family = store.begin('POST', '/api/v1/families', { code: 'F' });
    const component = render().componentInstance;
    expect(component.route(reservation)).toBe(ROUTE);
    expect(component.route(family)).toBe('/maestros/familias/nuevo');
  });

  it('ofrece «Abrir pedido» aunque el cuerpo se retirase al cerrar sesión, sin repetir la intención', async () => {
    const store = TestBed.inject(PendingOperationStore);
    const operation = store.begin('POST', '/api/v1/reservations', { customerOrderLineId: 'l' }, '"AQ=="', ROUTE);
    (TestBed.inject(SessionService) as unknown as { ended: { next(): void } }).ended.next();
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    const open = Array.from(root.querySelectorAll('a')).find(link => link.textContent?.trim() === 'Abrir pedido');
    expect(open?.getAttribute('href')).toBe(ROUTE);

    const check = fixture.componentInstance.check(store.operations()[0]);
    http.expectOne(`/api/v1/operations/${operation.operationId}`).flush({}, { status: 404, statusText: 'Not Found' });
    await check;
    fixture.detectChanges();
    expect(root.textContent).not.toContain('Repetir la misma intención');
  });

  it('el enlace resumido del layout lleva a appRoute', () => {
    TestBed.inject(PendingOperationStore).begin('POST', '/api/v1/reservations', { customerOrderLineId: 'l' }, '"AQ=="', ROUTE);
    const root = render(true).nativeElement as HTMLElement;
    const link = Array.from(root.querySelectorAll('a')).find(item => item.textContent?.trim() === 'Revisar esta operación pendiente');
    expect(link?.getAttribute('href')).toBe(ROUTE);
    expect(root.textContent).not.toContain('Abrir pedido');
  });

  it('sin appRoute no muestra «Abrir pedido»', () => {
    TestBed.inject(PendingOperationStore).begin('POST', '/api/v1/families', { code: 'F' });
    expect((render().nativeElement as HTMLElement).textContent).not.toContain('Abrir pedido');
  });
});
