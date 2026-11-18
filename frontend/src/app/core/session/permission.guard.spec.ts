import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { permissionGuard } from './permission.guard';
import { SessionService } from './session.service';

describe('Permisos de navegación', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] }));
  const route = { data: { permission: 'MasterData.Manage' } } as unknown as ActivatedRouteSnapshot;
  const target = { url: '/maestros/familias/nuevo' } as RouterStateSnapshot;
  it('no convierte Administrador en gestor de inventario', async () => {
    const session = TestBed.inject(SessionService);
    session.state.set('authenticated');
    session.user.set({ id: 'admin', email: 'admin@example.test', roles: ['Administrador'], permissions: ['Users.Manage'] });
    const result = await TestBed.runInInjectionContext(() => permissionGuard(route, target));
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/sin-permiso');
    session.user.set({ id: 'gestor', email: 'g@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    expect(await TestBed.runInInjectionContext(() => permissionGuard(route, target))).toBe(true);
  });
  it('conserva el destino solicitado al pedir sesión', async () => {
    TestBed.inject(SessionService).state.set('anonymous');
    const result = await TestBed.runInInjectionContext(() => permissionGuard(route, target));
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toContain('/login?returnUrl=');
  });
});
