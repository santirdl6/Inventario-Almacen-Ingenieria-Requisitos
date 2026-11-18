import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { test, expect, type Session } from './fixtures/master-data.fixture';

async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Correo electrónico (obligatorio)').fill(email);
  await page.getByLabel('Contraseña (obligatoria)').fill(password);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
}
async function family(session: Session) {
  const code = `ART-F-${randomUUID()}`.toUpperCase();
  const response = await session.request.post('/api/v1/families', {
    headers: { 'X-XSRF-TOKEN': session.csrfToken, 'Idempotency-Key': randomUUID() }, data: { code, name: 'Familia de artículos E2E' }
  });
  expect(response.status()).toBe(201);
  return { ...(await response.json()).resource as { id: string; code: string; name: string; isActive: boolean }, etag: response.headers()['etag'] };
}

test('gestor crea y edita artículo sin stock, conserva familia inactiva y filtra el catálogo', async ({ page, environment, gestor }) => {
  const destination = await family(gestor);
  const sku = `ART-UI-${randomUUID()}`.toUpperCase();
  await login(page, environment.accounts.gestor.email, environment.accounts.gestor.password);
  await page.getByRole('link', { name: 'Artículos', exact: true }).click();
  await page.getByRole('link', { name: 'Nuevo artículo', exact: true }).click();
  await page.getByLabel('Código de artículo (obligatorio)').fill(sku);
  await page.getByLabel('Nombre del artículo (obligatorio)').fill('Artículo Chromium');
  await expect(page.getByRole('button', { name: 'Guardar artículo' })).toBeDisabled();
  await page.getByLabel('Buscar familia por código o nombre').fill(destination.code);
  await page.getByRole('button', { name: 'Buscar familias', exact: true }).click();
  await expect(page.getByLabel('Familia (obligatoria)')).toContainText(destination.code);
  await page.getByLabel('Familia (obligatoria)').selectOption(destination.id);
  await page.getByLabel('Unidad base (obligatoria)').fill('UD');
  await page.getByLabel('Seguimiento por lote').check();
  await page.getByRole('button', { name: 'Guardar artículo' }).click();
  await expect(page).toHaveURL(/\/maestros\/articulos\/[0-9a-f-]+$/);
  await expect(page.getByLabel('Código de artículo (obligatorio)')).toHaveValue(sku);
  await expect(page.getByText('Mínimo disponible configurado (consulta): 0.000 UD')).toBeVisible();
  const id = new URL(page.url()).pathname.split('/').at(-1)!;
  const deactivated = await gestor.request.put(`/api/v1/families/${destination.id}`, {
    headers: { 'X-XSRF-TOKEN': gestor.csrfToken, 'Idempotency-Key': randomUUID(), 'If-Match': destination.etag },
    data: { code: destination.code, name: destination.name, isActive: false }
  });
  expect(deactivated.status()).toBe(200);
  await page.reload();
  await expect(page.getByLabel('Familia (obligatoria)')).toHaveValue(destination.id);
  await expect(page.getByLabel('Familia (obligatoria)')).toContainText('inactiva; asociación actual');
  await page.getByLabel('Nombre del artículo (obligatorio)').fill('Descripción corregida');
  await page.getByLabel('Unidad base (obligatoria)').fill('KG');
  await page.getByLabel('Admite fracciones de la unidad base').check();
  await page.getByLabel('Seguimiento por lote').uncheck();
  let saved = page.waitForResponse(response => response.url().endsWith(`/api/v1/articles/${id}`) && response.request().method() === 'PUT' && response.status() === 200);
  await page.getByRole('button', { name: 'Guardar artículo' }).click(); await saved;
  await expect(page.getByText('Artículo guardado y confirmado.')).toBeVisible();
  const resource = await (await gestor.request.get(`/api/v1/articles/${id}`)).json();
  expect(resource).toMatchObject({ id, familyId: destination.id, baseUnit: 'KG', allowsFractional: true, tracksLot: false, isActive: true });
  await expect(page.getByRole('button', { name: 'Guardar artículo' })).toBeEnabled();
  await page.getByLabel('Artículo activo', { exact: true }).uncheck();
  await page.getByRole('button', { name: 'Guardar artículo' }).click();
  await expect(page.getByText('Revisar estado del artículo', { exact: true })).toBeVisible();
  await expect(page.getByText(/Se conserva el historial;/)).toContainText(sku);
  saved = page.waitForResponse(response => response.url().endsWith(`/api/v1/articles/${id}`) && response.request().method() === 'PUT' && response.status() === 200);
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click(); await saved;
  await page.getByRole('link', { name: 'Volver a artículos' }).click();
  await page.getByLabel('Código o nombre del artículo').fill(sku);
  await page.getByLabel('Estado del artículo', { exact: true }).selectOption('inactive');
  await page.getByLabel('Buscar familia del filtro', { exact: true }).fill(destination.code);
  await page.getByRole('button', { name: 'Buscar familias del filtro', exact: true }).click();
  await expect(page.getByLabel('Familia del artículo', { exact: true })).toContainText(destination.code);
  await page.getByLabel('Familia del artículo', { exact: true }).selectOption(destination.id);
  await page.getByRole('button', { name: 'Buscar artículos', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: sku });
  await expect(row).toContainText('Inactivo'); await expect(row).toContainText('KG'); await expect(row).toContainText(destination.code);

  const active = await family(gestor);
  await page.getByRole('link', { name: 'Nuevo artículo', exact: true }).click();
  await page.getByLabel('Código de artículo (obligatorio)').fill(` ${sku.toLowerCase()} `);
  await page.getByLabel('Nombre del artículo (obligatorio)').fill('Conservar este nombre');
  await page.getByLabel('Buscar familia por código o nombre').fill(active.code);
  await page.getByRole('button', { name: 'Buscar familias', exact: true }).click();
  await expect(page.getByLabel('Familia (obligatoria)')).toContainText(active.code);
  await page.getByLabel('Familia (obligatoria)').selectOption(active.id);
  await page.getByRole('button', { name: 'Guardar artículo' }).click();
  await expect(page.getByText('Introduce un código libre.', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Nombre del artículo (obligatorio)')).toHaveValue('Conservar este nombre');
});

test('lector consulta unidad, familia y política sin controles de escritura', async ({ page, environment, gestor, lector }) => {
  const destination = await family(gestor); const sku = `ART-READ-${randomUUID()}`.toUpperCase();
  const response = await gestor.request.post('/api/v1/articles', {
    headers: { 'X-XSRF-TOKEN': gestor.csrfToken, 'Idempotency-Key': randomUUID() },
    data: { sku, name: 'Artículo solo consulta', familyId: destination.id, baseUnit: 'UD', allowsFractional: false, tracksLot: true }
  });
  expect(response.status()).toBe(201); const id = (await response.json()).resource.id as string;
  await login(page, environment.accounts.lector.email, environment.accounts.lector.password);
  await page.goto(`/maestros/articulos/${id}`);
  await expect(page.getByLabel('Código de artículo (obligatorio)')).toHaveValue(sku);
  await expect(page.getByLabel('Código de artículo (obligatorio)')).toBeDisabled();
  await expect(page.getByLabel('Unidad base (obligatoria)')).toHaveValue('UD');
  await expect(page.getByLabel('Familia (obligatoria)')).toHaveValue(destination.id);
  await expect(page.getByRole('button', { name: 'Guardar artículo' })).toHaveCount(0);
  const denied = await lector.request.put(`/api/v1/articles/${id}`, {
    headers: { 'X-XSRF-TOKEN': lector.csrfToken, 'Idempotency-Key': randomUUID(), 'If-Match': response.headers()['etag'] },
    data: { sku, name: 'No autorizado', familyId: destination.id, baseUnit: 'UD', allowsFractional: false, tracksLot: true, isActive: true }
  });
  expect(denied.status()).toBe(403);
});
