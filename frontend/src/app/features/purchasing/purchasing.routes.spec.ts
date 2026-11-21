import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { AppLayoutComponent } from '../../core/layout/app-layout.component';
import { SessionService } from '../../core/session/session.service';
import { PurchaseOrdersApiService } from './data/purchase-orders-api.service';
import { SuppliersApiService } from '../master-data/suppliers/suppliers-api.service';
import { ArticlesApiService } from '../master-data/articles/articles-api.service';
import { PurchaseOrderFormPage } from './pages/purchase-order-form.page';
import { purchase, purchaseContext } from './testing/purchasing-testing';

describe('Entrada de compras desde el menú', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)] });
    vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'list').mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    vi.spyOn(TestBed.inject(SuppliersApiService), 'list').mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });
  it.each([true, false])('la raíz es accesible con consulta; gestión=%s', async manage => {
    const session = TestBed.inject(SessionService);
    session.user.set({ id: 'reader', email: 'reader@test', roles: [], permissions: ['Inventory.Read', ...(manage ? ['Purchasing.Manage'] : [])] });
    session.state.set('authenticated'); vi.spyOn(session, 'refresh').mockResolvedValue(true);
    const layout = TestBed.createComponent(AppLayoutComponent); await layout.whenStable(); layout.detectChanges();
    const link = layout.nativeElement.querySelector('nav a[href="/compras"]') as HTMLAnchorElement;
    expect(link).not.toBeNull();
    const harness = await RouterTestingHarness.create(); await harness.navigateByUrl(link.getAttribute('href')!);
    expect(TestBed.inject(Router).url).toBe('/compras');
    expect(harness.routeNativeElement!.querySelector('h1')!.textContent).toBe('Compras');
    expect(!!harness.routeNativeElement!.querySelector('a[href="/compras/nuevo"]')).toBe(manage);
    layout.destroy();
  });

  it('reutilizar la ruta de edición cambia el documento y no conserva el borrador del pedido anterior', async () => {
    const session = TestBed.inject(SessionService);
    session.user.set({ id: 'buyer', email: 'buyer@test', roles: [], permissions: ['Inventory.Read', 'Purchasing.Manage'] }); session.state.set('authenticated');
    const api = TestBed.inject(PurchaseOrdersApiService);
    const one = purchase().id; const two = '66666666-6666-6666-6666-666666666666';
    vi.spyOn(api, 'detail').mockImplementation(async id => ({ resource: purchase({ id, number: id === one ? 'PO-A' : 'PO-B',
      lines: purchase().lines.map(line => ({ ...line, id: id === one ? line.id : '77777777-7777-7777-7777-777777777777' })) }),
      etag: id === one ? '"a1"' : '"b1"', context: { ...purchaseContext(), purchaseOrderVersion: id === one ? '"a1"' : '"b1"' } }));
    vi.mocked(TestBed.inject(SuppliersApiService).list).mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    vi.spyOn(TestBed.inject(ArticlesApiService), 'list').mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    const harness = await RouterTestingHarness.create(); await harness.navigateByUrl(`/compras/${one}/editar`);
    const component = harness.routeDebugElement!.componentInstance as PurchaseOrderFormPage;
    await vi.waitFor(() => expect(component.loading()).toBe(false));
    component.form.controls.lines.at(0).controls.quantity.setValue('9');
    await harness.navigateByUrl(`/compras/${two}/editar`);
    expect(harness.routeDebugElement!.componentInstance).toBe(component);
    await vi.waitFor(() => expect(component.loading()).toBe(false));
    expect(component.original()?.id).toBe(two); expect(component.form.controls.lines.at(0).controls.quantity.value).toBe('10.000');
    component.form.controls.lines.at(0).controls.quantity.setValue('6');
    const saving = vi.spyOn(api, 'save').mockRejectedValue(new Error('Prueba controlada tras capturar el documento'));
    await component.submit();
    expect(saving).toHaveBeenCalledWith(two, expect.anything(), '"b1"');
  }, 20_000);
});
