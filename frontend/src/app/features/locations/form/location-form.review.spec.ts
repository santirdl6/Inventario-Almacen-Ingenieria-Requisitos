import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { SessionService } from '../../../core/session/session.service';
import { detail, flushNetworkError, httpMock, locationsTestProviders, mutation, settle, signIn } from '../testing/locations-testing';
import { LocationFormPage } from './location-form.page';

describe('PR6: recuperación y separación de identidad', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [LocationFormPage], providers: locationsTestProviders() });
    signIn(['Inventory.Read', 'Locations.Manage']);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });
  afterEach(() => { httpMock().verify(); TestBed.resetTestingModule(); sessionStorage.clear(); });
  const body = { code: 'REVIEW-01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage' as const };

  it('restaura el estado pendiente al recrear la pantalla tras perder la respuesta', async () => {
    const first = TestBed.createComponent(LocationFormPage); await settle(first);
    first.componentInstance.form.setValue(body); first.componentInstance.submit();
    flushNetworkError(httpMock().expectOne('/api/v1/locations'));
    await settle(first);
    expect(first.componentInstance.status()).toBe('pending-check'); first.destroy();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [LocationFormPage], providers: locationsTestProviders() });
    signIn(['Inventory.Read', 'Locations.Manage']);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const reloaded = TestBed.createComponent(LocationFormPage); await settle(reloaded);
    expect(reloaded.componentInstance.status()).toBe('pending-check');
    expect(reloaded.componentInstance.form.getRawValue()).toEqual(body);
  });

  it('otra identidad no reutiliza la clave del operario anterior tras cerrar sesión', async () => {
    const first = TestBed.createComponent(LocationFormPage); await settle(first);
    first.componentInstance.form.setValue(body); first.componentInstance.submit();
    const request = httpMock().expectOne('/api/v1/locations');
    const key = request.request.headers.get('Idempotency-Key'); flushNetworkError(request); first.destroy();
    const session = TestBed.inject(SessionService);
    const logout = session.logout();
    httpMock().expectOne('/api/v1/auth/csrf').flush({ requestToken: 'token' }); await Promise.resolve();
    httpMock().expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' }); await logout;
    expect(sessionStorage.getItem('inventario.pending-operations')).not.toContain(body.code);
    expect(sessionStorage.getItem('inventario.pending-operations')).toContain(key!);
    session.user.set({ id: 'user-2', email: 'otro@example.test', roles: ['Operario'], permissions: ['Inventory.Read', 'Locations.Manage'] });
    session.state.set('authenticated');
    const second = TestBed.createComponent(LocationFormPage); await settle(second);
    second.componentInstance.form.setValue(body); second.componentInstance.submit();
    const next = httpMock().expectOne('/api/v1/locations');
    const nextKey = next.request.headers.get('Idempotency-Key');
    next.flush(mutation(detail()), { status: 201, statusText: 'Created', headers: { ETag: '"V1"' } });
    expect(nextKey).not.toBe(key);
  });
});
