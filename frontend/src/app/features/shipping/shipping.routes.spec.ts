import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';
import { SessionService } from '../../core/session/session.service';
import { SHIPPING_ROUTES } from './shipping.routes';

// HU-03 T014 (D-14; analyze-1, ajuste de T014): el área /salidas exige Inventory.Read; solo preparar
// una salida nueva exige además Shipping.Record. El servidor sigue decidiendo cada permiso.
describe('Rutas de /salidas', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] }));

  function signIn(permissions: string[]): void {
    const session = TestBed.inject(SessionService);
    session.state.set('authenticated');
    session.user.set({ id: 'u', email: 'u@example.test', roles: [], permissions });
  }

  it('declara búsqueda, pedido, nueva salida y salida; solo «nueva» lleva el guard de Shipping.Record', () => {
    expect(SHIPPING_ROUTES.map(route => route.path)).toEqual(['', 'pedidos/:orderId', 'pedidos/:orderId/nueva', ':id']);
    const guarded = SHIPPING_ROUTES.filter(route => route.canActivate?.length);
    expect(guarded.map(route => route.path)).toEqual(['pedidos/:orderId/nueva']);
    expect(guarded[0].canActivate).toEqual([permissionGuard]);
    expect(guarded[0].data).toEqual({ permission: 'Shipping.Record' });
  });

  it('el guard de «nueva» rechaza a quien solo tiene Inventory.Read y deja pasar con Shipping.Record', async () => {
    const route = { data: SHIPPING_ROUTES.find(item => item.path === 'pedidos/:orderId/nueva')!.data } as unknown as ActivatedRouteSnapshot;
    const target = { url: '/salidas/pedidos/x/nueva' } as RouterStateSnapshot;
    signIn(['Inventory.Read']);
    const denied = await TestBed.runInInjectionContext(() => permissionGuard(route, target));
    expect(TestBed.inject(Router).serializeUrl(denied as UrlTree)).toBe('/sin-permiso');
    signIn(['Inventory.Read', 'Shipping.Record']);
    expect(await TestBed.runInInjectionContext(() => permissionGuard(route, target))).toBe(true);
  });
});
