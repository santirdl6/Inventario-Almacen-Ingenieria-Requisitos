import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { routes } from './app.routes';
import { OPERATION_AREAS } from './operation-areas';
import { AppLayoutComponent } from './core/layout/app-layout.component';
import { CurrentUser, SessionService } from './core/session/session.service';
import { permissionGuard } from './core/session/permission.guard';

const operario: CurrentUser = { id: 'op', email: 'op@example.test', roles: ['Operario'],
  permissions: ['Inventory.Read', 'Locations.Manage', 'Receiving.Record', 'Shipping.Record', 'Counts.Record'] };
const gestor: CurrentUser = { id: 'gm', email: 'gm@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage', 'Inventory.Read'] };
const administrador: CurrentUser = { id: 'ad', email: 'ad@example.test', roles: ['Administrador'], permissions: ['Users.Manage'] };

describe('Áreas de B sobre la base de A', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)]
  }));

  function signIn(user: CurrentUser | null): void {
    const session = TestBed.inject(SessionService);
    session.user.set(user);
    session.state.set(user ? 'authenticated' : 'anonymous');
  }

  it('cada área está en el router común, protegida por el guard de A con un permiso del catálogo', () => {
    for (const area of OPERATION_AREAS) {
      const route = routes.find(item => item.path === area.path)!;
      expect(route.canActivate).toEqual([permissionGuard]);
      expect(route.data).toEqual({ permission: area.permission });
    }
    expect(OPERATION_AREAS.map(area => [area.path, area.permission])).toEqual([
      ['ubicaciones', 'Inventory.Read'], ['existencias', 'Inventory.Read'],
      ['salidas', 'Shipping.Record'], ['inventarios', 'Counts.Record']
    ]);
    expect(routes.at(-1)!.path).toBe('**');
  });

  it('con permiso carga la página de cada área (provisional salvo las ya desarrolladas)', async () => {
    signIn(operario);
    const harness = await RouterTestingHarness.create();
    const titles: Record<string, string> = { ubicaciones: 'Ubicaciones', existencias: 'Existencias', salidas: 'Salidas', inventarios: 'Inventarios cíclicos' };
    const developed = ['ubicaciones']; // HU-02
    for (const area of OPERATION_AREAS) {
      await harness.navigateByUrl(`/${area.path}`);
      expect(TestBed.inject(Router).url).toBe(`/${area.path}`);
      expect(harness.routeNativeElement!.querySelector('h1')!.textContent!.trim()).toBe(titles[area.path]);
      if (developed.includes(area.path)) continue;
      expect(harness.routeNativeElement!.textContent).toContain(area.story);
      expect(harness.routeNativeElement!.textContent).toContain('Pendiente de desarrollo');
    }
    // Carga cuatro módulos diferidos; con la caché fría superaba a veces el límite de 5 s.
  }, 20_000);

  it('sin el permiso del área lleva a sin-permiso, y sin sesión al login conservando el destino', async () => {
    signIn(gestor);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/ubicaciones');
    expect(TestBed.inject(Router).url).toBe('/ubicaciones');
    await harness.navigateByUrl('/salidas');
    expect(TestBed.inject(Router).url).toBe('/sin-permiso');
    await harness.navigateByUrl('/inventarios');
    expect(TestBed.inject(Router).url).toBe('/sin-permiso');

    signIn(null);
    await harness.navigateByUrl('/existencias');
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fexistencias');
  });

  it('el layout de A solo enlaza las áreas que el permiso permite', async () => {
    // La sesión la resuelve /auth/me (probado en session.service.spec); aquí solo importa la navegación.
    vi.spyOn(TestBed.inject(SessionService), 'refresh').mockResolvedValue(true);
    const links = async (user: CurrentUser) => {
      signIn(user);
      const fixture = TestBed.createComponent(AppLayoutComponent);
      await fixture.whenStable();
      fixture.detectChanges();
      const anchors = Array.from(fixture.nativeElement.querySelectorAll('nav a') as NodeListOf<HTMLAnchorElement>);
      fixture.destroy();
      return anchors.map(anchor => anchor.getAttribute('href'));
    };
    const operarioLinks = await links(operario);
    expect(operarioLinks).toEqual(expect.arrayContaining(['/ubicaciones', '/existencias', '/salidas', '/inventarios']));
    const gestorLinks = await links(gestor);
    expect(gestorLinks).toEqual(expect.arrayContaining(['/ubicaciones', '/existencias']));
    expect(gestorLinks).not.toContain('/salidas');
    expect(gestorLinks).not.toContain('/inventarios');
    const adminLinks = await links(administrador);
    for (const area of OPERATION_AREAS) expect(adminLinks).not.toContain(`/${area.path}`);
  });
});
