import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionService } from './session.service';

describe('Sesión común', () => {
  let session: SessionService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    session = TestBed.inject(SessionService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); document.cookie = 'XSRF-TOKEN=; Max-Age=0; path=/'; });

  it('usa CSRF nativo, carga permisos tras login y renueva antes de logout', async () => {
    const login = session.login('gestor@example.test', 'Password_2026!');
    document.cookie = 'XSRF-TOKEN=anonymous; path=/';
    http.expectOne('/api/v1/auth/csrf').flush({ requestToken: 'anonymous' });
    await Promise.resolve();
    const post = http.expectOne('/api/v1/auth/login');
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe('anonymous');
    document.cookie = 'XSRF-TOKEN=authenticated; path=/';
    post.flush({ requestToken: 'authenticated' });
    await Promise.resolve();
    http.expectOne('/api/v1/auth/me').flush({ id: 'actor', email: 'gestor@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    expect(await login).toBe(true);
    expect(session.has('MasterData.Manage')).toBe(true);
    const logout = session.logout();
    http.expectOne('/api/v1/auth/csrf').flush({ requestToken: 'authenticated' });
    await Promise.resolve();
    const end = http.expectOne('/api/v1/auth/logout');
    expect(end.request.headers.get('X-XSRF-TOKEN')).toBe('authenticated');
    end.flush(null, { status: 204, statusText: 'No Content' });
    await logout;
    expect(session.state()).toBe('anonymous'); expect(session.user()).toBeNull();
  });

  it('distingue falta de sesión de indisponibilidad del servidor', async () => {
    const anonymous = session.refresh();
    http.expectOne('/api/v1/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(await anonymous).toBe(false); expect(session.state()).toBe('anonymous');
    const unavailable = session.refresh();
    http.expectOne('/api/v1/auth/me').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(await unavailable).toBe(false); expect(session.state()).toBe('unavailable');
  });
});
