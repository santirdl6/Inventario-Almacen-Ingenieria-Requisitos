import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { PurchaseOrderListPage } from './purchase-order-list.page';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { SuppliersApiService } from '../../master-data/suppliers/suppliers-api.service';
import { locationsTestProviders, signIn } from '../../locations/testing/locations-testing';
import { purchase } from '../testing/purchasing-testing';

describe('Listado de compras', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: locationsTestProviders() }); signIn(['Inventory.Read']);
    vi.spyOn(TestBed.inject(SuppliersApiService), 'list').mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });
  it('una página vacía con total positivo permite volver sin afirmar ausencia global', async () => {
    const api = TestBed.inject(PurchaseOrdersApiService);
    vi.spyOn(api, 'list').mockResolvedValue({ items: [], page: 2, pageSize: 25, totalCount: 25 });
    const fixture = TestBed.createComponent(PurchaseOrderListPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('No hay pedidos para estos filtros');
    expect(fixture.nativeElement.textContent).toContain('Esta página ya no tiene filas');
    expect(fixture.nativeElement.querySelector('button[aria-label="Primera página"]')).not.toBeNull();
  });
  it('un fallo de consulta conserva filas anteriores y no se presenta como listado vacío', async () => {
    const api = TestBed.inject(PurchaseOrdersApiService);
    const list = vi.spyOn(api, 'list').mockResolvedValue({ items: [purchase()], page: 1, pageSize: 25, totalCount: 1 });
    const fixture = TestBed.createComponent(PurchaseOrderListPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    list.mockRejectedValue(new HttpErrorResponse({ status: 503, error: { detail: 'Consulta no disponible' } }));
    await fixture.componentInstance.load(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(purchase().number);
    expect(fixture.nativeElement.textContent).toContain('Consulta no disponible');
    expect(fixture.nativeElement.textContent).not.toContain('No hay pedidos para estos filtros');
  });
});
