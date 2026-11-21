import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/master-data.fixture';
import type { Page } from '@playwright/test';

async function login(page: Page, email: string, password: string) {
  await page.goto('/login'); await page.getByLabel('Correo electrónico (obligatorio)').fill(email); await page.getByLabel('Contraseña (obligatoria)').fill(password); await page.getByRole('button', { name: 'Entrar', exact: true }).click(); await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
}

test('dos sesiones resuelven 412 mediante comparación sin perder el borrador local', async ({ page, browser, environment, gestor }) => {
  const response = await gestor.request.post('/api/v1/families', { headers: { 'X-XSRF-TOKEN': gestor.csrfToken, 'Idempotency-Key': randomUUID() }, data: { code: `CONFLICT-${randomUUID()}`, name: 'Original' } });
  expect(response.status()).toBe(201); const id = (await response.json()).resource.id;
  const otherContext = await browser.newContext(); const other = await otherContext.newPage();
  try {
    await login(page, environment.accounts.gestor.email, environment.accounts.gestor.password); await login(other, environment.accounts.gestor.email, environment.accounts.gestor.password);
    await page.goto(`/maestros/familias/${id}`); await other.goto(`/maestros/familias/${id}`);
    await page.getByLabel('Nombre (obligatorio)').fill('Mi borrador local'); await other.getByLabel('Nombre (obligatorio)').fill('Versión de otra sesión');
    const first = other.waitForResponse(value => value.url().endsWith(`/api/v1/families/${id}`) && value.request().method() === 'PUT' && value.status() === 200);
    await other.getByRole('button', { name: 'Guardar familia' }).click(); await first;
    await page.getByRole('button', { name: 'Guardar familia' }).click();
    await expect(page.getByRole('region', { name: 'Conflicto de versión' })).toBeVisible();
    await expect(page.getByLabel('Nombre (obligatorio)')).toHaveValue('Mi borrador local');
    await page.getByRole('button', { name: 'Cargar versión vigente para comparar' }).click();
    await expect(page.getByRole('table')).toContainText('Versión de otra sesión');
    await expect(page.getByLabel('Nombre (obligatorio)')).toHaveValue('Mi borrador local');
    await page.getByRole('button', { name: 'Aceptar revisión y conservar los valores elegidos' }).click();
    await page.getByRole('button', { name: 'Guardar familia' }).click();
    await expect(page.getByText('Familia guardada y confirmada.')).toBeVisible();
    expect((await (await gestor.request.get(`/api/v1/families/${id}`)).json()).name).toBe('Mi borrador local');
  } finally { await otherContext.close(); }
});

test('respuesta perdida después del commit se recupera tras recargar sin repetir el alta', async ({ page, environment, gestor }) => {
  await login(page, environment.accounts.gestor.email, environment.accounts.gestor.password); await page.goto('/maestros/familias/nuevo');
  const code = `LOST-${randomUUID()}`.toUpperCase(); let key = '';
  await page.route('**/api/v1/families', async route => {
    if (route.request().method() !== 'POST') { await route.continue(); return; }
    key = route.request().headers()['idempotency-key']; const committed = await route.fetch(); expect(committed.status()).toBe(201); await route.abort('failed');
  });
  await page.getByLabel('Código (obligatorio)').fill(code); await page.getByLabel('Nombre (obligatorio)').fill('Petición original'); await page.getByRole('button', { name: 'Guardar familia' }).click();
  await expect(page.getByRole('button', { name: 'Comprobar resultado' })).toBeVisible();
  await page.reload();
  await expect(page.getByLabel('Código (obligatorio)')).toHaveValue(code);
  await expect(page.getByRole('button', { name: 'Guardar familia' })).toBeDisabled();
  await page.getByRole('button', { name: 'Comprobar resultado' }).click();
  await expect(page).toHaveURL(/\/maestros\/familias\/[0-9a-f-]+$/);
  await expect(page.getByLabel('Código (obligatorio)')).toHaveValue(code);
  const list = await (await gestor.request.get(`/api/v1/families?q=${code}&state=all`)).json(); expect(list.totalCount).toBe(1);
  expect((await gestor.request.get(`/api/v1/operations/${key}`)).status()).toBe(200);
});
