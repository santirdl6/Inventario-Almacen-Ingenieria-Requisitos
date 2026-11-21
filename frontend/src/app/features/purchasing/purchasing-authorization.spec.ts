import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { routes } from '../../app.routes';
import { SessionService } from '../../core/session/session.service';

describe('Permisos de rutas de compras', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)] }));
  it('Inventory.Read no concede gestión ni envío, y Users.Manage no concede compras', async () => {
    const session = TestBed.inject(SessionService); session.state.set('authenticated');
    session.user.set({ id: 'reader', email: 'reader@test', roles: [], permissions: ['Inventory.Read'] });
    const harness = await RouterTestingHarness.create(); await harness.navigateByUrl('/compras/nuevo'); expect(TestBed.inject(Router).url).toBe('/sin-permiso');
    await harness.navigateByUrl('/compras/11111111-1111-1111-1111-111111111111/enviar'); expect(TestBed.inject(Router).url).toBe('/sin-permiso');
    session.user.set({ id: 'admin', email: 'admin@test', roles: [], permissions: ['Users.Manage'] });
    await harness.navigateByUrl('/compras'); expect(TestBed.inject(Router).url).toBe('/sin-permiso');
  });
});
