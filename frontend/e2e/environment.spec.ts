import { test, expect } from './fixtures/master-data.fixture';

test('el entorno dedicado sirve SQL y frontend sin convertir una API desconocida en HTML', async ({ environment, request }) => {
  const health = await request.get(`${environment.baseURL}/api/v1/health`);
  expect(health.status()).toBe(200);
  expect(await health.json()).toEqual({ status: 'healthy', database: 'ready' });
  const home = await request.get(environment.baseURL);
  expect(home.status()).toBe(200);
  expect(await home.text()).toContain('app-root');
  const missing = await request.get(`${environment.baseURL}/api/v1/unknown`);
  expect(missing.status()).toBe(404);
  expect(missing.headers()['content-type']).toContain('application/problem+json');
});

test('las cuentas preparadas usan sesión real y separan gestión de maestros de administración', async ({ gestor, lector, administrador }) => {
  expect(gestor.me.roles).toEqual(['GestorMaestros']);
  expect(gestor.me.permissions).toContain('MasterData.Manage');
  expect(gestor.me.permissions).toContain('Inventory.Read');
  expect(lector.me.roles).toEqual(['Operario']);
  expect(lector.me.permissions).toContain('Inventory.Read');
  expect(lector.me.permissions).not.toContain('MasterData.Manage');
  expect(administrador.me.roles).toEqual(['Administrador']);
  expect(administrador.me.permissions).toEqual(['Users.Manage']);
  expect(new Set([gestor.me.id, lector.me.id, administrador.me.id]).size).toBe(3);
});

test('un cliente sin CSRF no puede iniciar sesión aunque tenga credenciales válidas', async ({ environment, request }) => {
  const account = environment.accounts.gestor;
  const rejected = await request.post(`${environment.baseURL}/api/v1/auth/login`, {
    data: { email: account.email, password: account.password }
  });
  expect(rejected.status()).toBe(400);
  expect((await rejected.json()).code).toBe('AntiforgeryValidationFailed');
  expect((await request.get(`${environment.baseURL}/api/v1/auth/me`)).status()).toBe(401);
});
