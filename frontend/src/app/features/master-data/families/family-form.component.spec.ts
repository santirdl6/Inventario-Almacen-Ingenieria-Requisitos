import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FamilyFormComponent } from './family-form.component';
import { SessionService } from '../../../core/session/session.service';
import { ConfirmationService } from 'primeng/api';

describe('Mantenimiento de familias', () => {
  let http: HttpTestingController;
  let route: { snapshot: { paramMap: ReturnType<typeof convertToParamMap> } };
  beforeEach(() => {
    sessionStorage.clear();
    route = { snapshot: { paramMap: convertToParamMap({}) } };
    TestBed.configureTestingModule({
      imports: [FamilyFormComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: route }]
    });
    TestBed.inject(SessionService).user.set({ id: 'actor', email: 'gestor@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  it('no envía un alta incompleta y conserva el formulario ante código duplicado', async () => {
    const fixture = TestBed.createComponent(FamilyFormComponent); fixture.detectChanges();
    const component = fixture.componentInstance;
    await component.submit(); http.expectNone('/api/v1/families');
    component.form.patchValue({ code: 'DUP', name: 'Nombre válido' });
    const saved = component.submit();
    const request = http.expectOne('/api/v1/families');
    expect(request.request.headers.has('Idempotency-Key')).toBe(true);
    request.flush({ code: 'DuplicateCode', detail: 'Ese código ya está utilizado.', errors: { code: ['Introduce otro código.'] } },
      { status: 409, statusText: 'Conflict' });
    await saved; fixture.detectChanges();
    expect(component.form.getRawValue().name).toBe('Nombre válido');
    expect(component.form.getRawValue().code).toBe('DUP');
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('ya está utilizado');
    expect(fixture.nativeElement.textContent).toContain('Introduce otro código.');
    expect(component.busy()).toBe(false);
  });

  it('un lector conserva la consulta pero no puede producir una mutación', async () => {
    TestBed.inject(SessionService).user.set({ id: 'lector', email: 'l@example.test', roles: ['Operario'], permissions: ['Inventory.Read'] });
    const fixture = TestBed.createComponent(FamilyFormComponent); fixture.detectChanges();
    fixture.componentInstance.form.patchValue({ code: 'A', name: 'Familia' });
    await fixture.componentInstance.submit();
    http.expectNone('/api/v1/families');
    expect(fixture.nativeElement.textContent).toContain('Solo consulta');
    expect(fixture.nativeElement.querySelector('input').disabled).toBe(true);
  });

  it('permite revisar y cancelar una desactivación antes de escribir', async () => {
    const id = '10000000-0000-0000-0000-000000000001';
    route.snapshot.paramMap = convertToParamMap({ id });
    const fixture = TestBed.createComponent(FamilyFormComponent); fixture.detectChanges();
    http.expectOne(`/api/v1/families/${id}`).flush({ id, code: 'GENERAL', name: 'Familia original', isActive: true },
      { headers: { ETag: '"AQIDBAUGBwg="' } });
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    expect(fixture.componentInstance.form.getRawValue().name).toBe('Familia original');
    const confirmation = fixture.debugElement.injector.get(ConfirmationService);
    const review = vi.spyOn(confirmation, 'confirm').mockImplementation(options => {
      expect(options.message).toContain('GENERAL');
      expect(options.message).toContain('asociaciones existentes se conservan');
      options.reject?.(); return confirmation;
    });
    fixture.componentInstance.form.controls.isActive.setValue(false);
    await fixture.componentInstance.submit();
    expect(review).toHaveBeenCalledOnce();
    http.expectNone(request => request.method === 'PUT');
    expect(fixture.componentInstance.form.getRawValue().isActive).toBe(false);
    expect(fixture.componentInstance.busy()).toBe(false);
  });

  it('no permite que un formulario nuevo sustituya una intención restaurada y limpia el borrador al logout', async () => {
    const { PendingOperationStore } = await import('../../../core/operations/pending-operation.store');
    const store = TestBed.inject(PendingOperationStore); store.begin('POST', '/api/v1/families', { code: 'PENDING', name: 'Borrador privado', isActive: true });
    const fixture = TestBed.createComponent(FamilyFormComponent); fixture.detectChanges();
    expect(fixture.componentInstance.form.getRawValue().name).toBe('Borrador privado');
    await fixture.componentInstance.submit(); http.expectNone('/api/v1/families');
    const logout = TestBed.inject(SessionService).logout(); http.expectOne('/api/v1/auth/csrf').flush({}); await Promise.resolve();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' }); await logout;
    expect(fixture.componentInstance.form.getRawValue().name).toBe(''); expect(fixture.componentInstance.form.disabled).toBe(true);
    expect(sessionStorage.getItem('inventario.pending-operations')).not.toContain('Borrador privado');
  });
});
