import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/master-data.fixture';
import type { Page } from '@playwright/test';

async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Correo electrónico (obligatorio)').fill(email);
  await page.getByLabel('Contraseña (obligatoria)').fill(password);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
}

test('gestor mantiene una familia, revisa estados y conserva datos ante duplicados', async ({ page, environment, gestor }) => {
  await login(page, environment.accounts.gestor.email, environment.accounts.gestor.password);
  await page.getByRole('link', { name: 'Familias', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Familias', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Nueva familia', exact: true }).click();
  const code = `UI-${randomUUID()}`.toUpperCase();
  await page.getByLabel('Código (obligatorio)').fill(code);
  await page.getByLabel('Nombre (obligatorio)').fill('Familia de validación');
  await page.getByRole('button', { name: 'Guardar familia' }).click();
  await expect(page).toHaveURL(/\/maestros\/familias\/[0-9a-f-]+$/);
  await expect(page.getByLabel('Código (obligatorio)')).toHaveValue(code);
  const id = new URL(page.url()).pathname.split('/').at(-1)!;

  await page.getByLabel('Nombre (obligatorio)').fill('Nombre actualizado');
  let saved = page.waitForResponse(response => response.url().endsWith(`/api/v1/families/${id}`)
    && response.request().method() === 'PUT' && response.status() === 200);
  await page.getByRole('button', { name: 'Guardar familia' }).click(); await saved;
  expect((await (await gestor.request.get(`/api/v1/families/${id}`)).json()).name).toBe('Nombre actualizado');
  await expect(page.getByRole('button', { name: 'Guardar familia' })).toBeEnabled();
  await page.getByLabel('Familia activa').uncheck();
  await page.getByRole('button', { name: 'Guardar familia' }).click();
  await expect(page.getByText('Revisar cambio de estado', { exact: true })).toBeVisible();
  await expect(page.getByText(/asociaciones existentes se conservan/)).toBeVisible();
  saved = page.waitForResponse(response => response.url().endsWith(`/api/v1/families/${id}`)
    && response.request().method() === 'PUT' && response.status() === 200);
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click(); await saved;
  expect((await (await gestor.request.get(`/api/v1/families/${id}`)).json()).isActive).toBe(false);

  await page.getByRole('link', { name: 'Volver a familias' }).click();
  await page.getByLabel('Código o nombre').fill(code);
  await page.getByLabel('Estado', { exact: true }).selectOption('inactive');
  await expect(page.getByLabel('Código o nombre')).toHaveValue(code);
  await expect(page.getByLabel('Estado', { exact: true })).toHaveValue('inactive');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: code });
  await expect(row).toContainText('Inactivo');
  await row.getByRole('link', { name: 'Consultar' }).click();
  await expect(page.getByLabel('Familia activa')).not.toBeChecked();
  await page.getByLabel('Familia activa').check();
  await page.getByRole('button', { name: 'Guardar familia' }).click();
  saved = page.waitForResponse(response => response.url().endsWith(`/api/v1/families/${id}`)
    && response.request().method() === 'PUT' && response.status() === 200);
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click(); await saved;
  expect((await (await gestor.request.get(`/api/v1/families/${id}`)).json()).isActive).toBe(true);

  await page.goto('/maestros/familias/nuevo');
  await page.getByLabel('Código (obligatorio)').fill(` ${code.toLowerCase()} `);
  await page.getByLabel('Nombre (obligatorio)').fill('Nombre que debe conservarse');
  await page.getByRole('button', { name: 'Guardar familia' }).click();
  await expect(page.getByText('Introduce un código libre.', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Nombre (obligatorio)')).toHaveValue('Nombre que debe conservarse');
});

test('lector consulta la familia pero no obtiene controles ni permiso de escritura', async ({ page, environment, gestor, lector }) => {
  const code = `READ-${randomUUID()}`.toUpperCase();
  const created = await gestor.request.post('/api/v1/families', {
    headers: { 'X-XSRF-TOKEN': gestor.csrfToken, 'Idempotency-Key': randomUUID() },
    data: { code, name: 'Familia de consulta', isActive: true }
  });
  expect(created.status()).toBe(201);
  const id = (await created.json()).resource.id as string;
  await login(page, environment.accounts.lector.email, environment.accounts.lector.password);
  await page.goto(`/maestros/familias/${id}`);
  await expect(page.getByLabel('Código (obligatorio)')).toHaveValue(code);
  await expect(page.getByLabel('Código (obligatorio)')).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Guardar familia' })).toHaveCount(0);
  const denied = await lector.request.post('/api/v1/families', {
    headers: { 'X-XSRF-TOKEN': lector.csrfToken, 'Idempotency-Key': randomUUID() },
    data: { code: `DENIED-${randomUUID()}`, name: 'No se crea' }
  });
  expect(denied.status()).toBe(403);
});
