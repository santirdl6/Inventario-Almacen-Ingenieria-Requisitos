import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { PurchaseOrderFormPage } from './purchase-order-form.page';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { SuppliersApiService } from '../../master-data/suppliers/suppliers-api.service';
import { ArticlesApiService } from '../../master-data/articles/articles-api.service';
import { locationsTestProviders, signIn } from '../../locations/testing/locations-testing';
import { purchase, purchaseContext } from '../testing/purchasing-testing';

describe('Formulario de pedido de compra', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: locationsTestProviders() });
    signIn(['Inventory.Read', 'Purchasing.Manage']);
    const page = { items: [], page: 1, pageSize: 25, totalCount: 0 };
    vi.spyOn(TestBed.inject(SuppliersApiService), 'list').mockResolvedValue(page);
    vi.spyOn(TestBed.inject(ArticlesApiService), 'list').mockResolvedValue(page);
    vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'settings').mockResolvedValue({ currency: 'EUR' });
  });
  afterEach(() => sessionStorage.clear());

  it('conserva dos líneas identificables aunque el artículo se repita', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderFormPage);
    fixture.detectChanges(); await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const page = fixture.componentInstance;
    page.addLine();
    const rows = page.form.controls.lines.controls;
    rows[0].controls.articleId.setValue('same'); rows[1].controls.articleId.setValue('same');
    expect(rows).toHaveLength(2);
    expect(rows[0].controls.id.value).not.toBe(rows[1].controls.id.value);
    expect(rows[0].controls.id.value).toMatch(/^[0-9a-f-]{36}$/);
    expect(fixture.nativeElement.textContent).toContain('Cantidad');
    expect(fixture.nativeElement.textContent).toContain('Precios en EUR');
  });

  it('422 conserva cantidades exactas y los datos válidos', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderFormPage);
    fixture.detectChanges(); await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const page = fixture.componentInstance;
    page.form.patchValue({ supplierId: 'supplier', expectedDeliveryDate: '2026-12-01' });
    page.form.controls.lines.at(0).patchValue({ articleId: 'article', quantity: '10.125', unitPrice: '0.00' });
    const save = vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'save').mockRejectedValue(new HttpErrorResponse({ status: 422,
      error: { status: 422, code: 'ValidationFailed', detail: 'Corrige cantidad', errors: { 'lines[0].quantity': ['No admite fracciones'] } } }));
    await page.submit(); fixture.detectChanges();
    expect(save).toHaveBeenCalled();
    expect(page.form.controls.lines.at(0).controls.quantity.value).toBe('10.125');
    expect(page.form.controls.supplierId.value).toBe('supplier');
    expect(fixture.nativeElement.textContent).toContain('No admite fracciones');
  });

  it('permite elegir prefijo SKU sin sustituir la búsqueda habitual por código o nombre', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderFormPage);
    fixture.detectChanges(); await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const page = fixture.componentInstance; const list = vi.mocked(TestBed.inject(ArticlesApiService).list);
    page.articleQuery.setValue('ART-'); await page.searchArticles(1);
    expect(list).toHaveBeenLastCalledWith('ART-', 'active', '', 1, undefined);
    page.articleMatch.setValue('skuPrefix'); await page.searchArticles(2);
    expect(list).toHaveBeenLastCalledWith('', 'active', '', 2, 'ART-');
    expect(fixture.nativeElement.querySelector('#article-match')).not.toBeNull();
  });

  async function editing(value = purchase()) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: locationsTestProviders({ id: value.id }) });
    signIn(['Inventory.Read', 'Purchasing.Manage']);
    const page = { items: [], page: 1, pageSize: 25, totalCount: 0 };
    vi.spyOn(TestBed.inject(SuppliersApiService), 'list').mockResolvedValue(page);
    vi.spyOn(TestBed.inject(ArticlesApiService), 'list').mockResolvedValue(page);
    const detail = vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'detail')
      .mockResolvedValue({ resource: value, etag: '"v1"', context: purchaseContext() });
    const fixture = TestBed.createComponent(PurchaseOrderFormPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    return { fixture, detail };
  }

  it('conserva referencias inactivas fuera de la página sin reescribir sus condiciones', async () => {
    const { fixture } = await editing(); fixture.detectChanges();
    expect(fixture.componentInstance.selectedSupplier()?.isActive).toBe(false);
    expect(fixture.componentInstance.unit(purchase().lines[0].articleId)).toBe('UD');
    expect(fixture.componentInstance.form.controls.lines.at(0).controls.unitPrice.value).toBe('0.00');
    expect(fixture.nativeElement.textContent).toContain('Inactivo');
    expect(fixture.nativeElement.textContent).toContain('Completa el correo');
  });

  it('un pedido congelado no permite otro guardado', async () => {
    const { fixture } = await editing(purchase({ state: 'Sent', sendStatus: 'Accepted' }));
    const save = vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'save');
    await fixture.componentInstance.submit();
    expect(save).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.disabled).toBe(true);
  });

  it('412 compara por ID de línea y exige aceptar expresamente la revisión antes de guardar', async () => {
    const { fixture, detail } = await editing();
    const page = fixture.componentInstance;
    const current = purchase(); current.lines[0].orderedQuantity = '6.000';
    detail.mockResolvedValue({ resource: current, etag: '"v2"', context: { ...purchaseContext(), purchaseOrderVersion: '"v2"' } });
    page.problem.set({ status: 412, code: 'VersionConflict', detail: 'Otra revisión', errors: {} });
    await page.review();
    expect(page.comparison()[0].id).toBe(current.lines[0].id);
    expect(page.comparison()[0].local).toContain('10.000');
    expect(page.comparison()[0].current).toContain('6.000');
    const save = vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'save').mockRejectedValue(new Error('Prueba controlada'));
    await page.submit(); expect(save).not.toHaveBeenCalled();
    page.acceptReview(); await page.submit();
    expect(save).toHaveBeenCalledWith(current.id, expect.objectContaining({ lines: [expect.objectContaining({ quantity: '10.000' })] }), '"v2"');
  });
});
