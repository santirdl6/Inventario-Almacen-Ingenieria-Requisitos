import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SupplierFormComponent } from './supplier-form.component';
import { SessionService } from '../../../core/session/session.service';

describe('Proveedores', () => {
  let http: HttpTestingController; let route: { snapshot: { paramMap: ReturnType<typeof convertToParamMap> } };
  const id = '10000000-0000-0000-0000-000000000001';
  beforeEach(() => {
    sessionStorage.clear(); route = { snapshot: { paramMap: convertToParamMap({}) } };
    TestBed.configureTestingModule({ imports: [SupplierFormComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: route }] });
    TestBed.inject(SessionService).user.set({ id: 'actor', email: 'gestor@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    http = TestBed.inject(HttpTestingController); vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });
  it('admite contacto ausente con aviso y conserva campos ante error de código', async () => {
    const fixture = TestBed.createComponent(SupplierFormComponent); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('deberás completarlo antes de enviar pedidos');
    fixture.componentInstance.form.patchValue({ code: 'DUP', name: 'Conservar', email: 'invalid' });
    await fixture.componentInstance.submit(); http.expectNone('/api/v1/suppliers');
    fixture.componentInstance.form.controls.email.setValue('');
    const saved = fixture.componentInstance.submit(); const request = http.expectOne('/api/v1/suppliers');
    expect(request.request.body.email).toBeNull(); expect(request.request.body.taxIdentifier).toBeNull();
    request.flush({ detail: 'Código ocupado', errors: { code: ['Elige otro código.'] } }, { status: 409, statusText: 'Conflict' });
    await saved; fixture.detectChanges();
    expect(fixture.componentInstance.form.getRawValue().name).toBe('Conservar'); expect(fixture.nativeElement.textContent).toContain('Elige otro código.');
  });
  it('permite cancelar el cambio de estado conservando el contacto', async () => {
    route.snapshot.paramMap = convertToParamMap({ id });
    const fixture = TestBed.createComponent(SupplierFormComponent); fixture.detectChanges();
    http.expectOne(`/api/v1/suppliers/${id}`).flush({ id, code: 'PROV', name: 'Proveedor', email: 'mail@example.test', taxIdentifier: null, isActive: true }, { headers: { ETag: '"AQIDBAUGBwg="' } });
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const confirmation = fixture.debugElement.injector.get(ConfirmationService);
    vi.spyOn(confirmation, 'confirm').mockImplementation(options => { expect(options.message).toContain('PROV'); expect(options.message).toContain('documentos existentes'); options.reject?.(); return confirmation; });
    fixture.componentInstance.form.controls.isActive.setValue(false); await fixture.componentInstance.submit();
    http.expectNone(request => request.method === 'PUT'); expect(fixture.componentInstance.form.getRawValue().email).toBe('mail@example.test');
  });
  it('mantiene los controles de un lector deshabilitados', async () => {
    TestBed.inject(SessionService).user.set({ id: 'reader', email: 'reader@example.test', roles: ['Operario'], permissions: ['Inventory.Read'] });
    const fixture = TestBed.createComponent(SupplierFormComponent); fixture.detectChanges();
    await fixture.componentInstance.submit(); expect(fixture.componentInstance.form.disabled).toBe(true); http.expectNone('/api/v1/suppliers');
  });
});
