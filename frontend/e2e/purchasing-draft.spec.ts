import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';

test.skip(!purchasingProfile, 'El recorrido de compras requiere el proyecto HU-07 aislado.');

test('comprador entra desde el menú, corrige un dato inválido y reabre su borrador', async ({ page, comprador, gestor }) => {
  const suffix = randomUUID().slice(0, 8).toUpperCase();
  const headers = () => ({ 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': gestor.csrfToken });
  const familyResponse = await gestor.request.post('/api/v1/families', { headers: headers(), data: { code: `F-${suffix}`, name: 'Familia de compras' } });
  expect(familyResponse.status()).toBe(201);
  const family = (await familyResponse.json()).resource;
  const supplierResponse = await gestor.request.post('/api/v1/suppliers', { headers: headers(), data: { code: `P-${suffix}`, name: `Proveedor ${suffix}`, email: null } });
  expect(supplierResponse.status()).toBe(201);
  const supplier = (await supplierResponse.json()).resource;
  const articleResponse = await gestor.request.post('/api/v1/articles', { headers: headers(), data: {
    sku: `ART-${suffix}`, name: 'Artículo de compras', familyId: family.id, baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true
  } });
  expect(articleResponse.status()).toBe(201);
  const article = (await articleResponse.json()).resource;
  await page.context().addCookies((await comprador.request.storageState()).cookies);
  await page.goto('/');
  await page.getByRole('link', { name: 'Compras', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Compras', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Nuevo pedido', exact: true }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel('Proveedor (obligatorio)', { exact: true }).selectOption(supplier.id);
  await page.getByLabel('Fecha prevista general (obligatoria)', { exact: true }).fill('2026-12-01');
  await page.getByLabel('Artículo (obligatorio)', { exact: true }).selectOption(article.id);
  await expect(page.getByText('Precios en EUR.', { exact: false })).toBeVisible();
  const quantity = page.getByLabel('Cantidad (obligatoria) UD', { exact: true });
  await quantity.fill('1.5');
  await page.getByLabel('Precio unitario (obligatorio)', { exact: true }).fill('0.00');
  await page.getByRole('button', { name: 'Guardar pedido', exact: true }).click();
  await expect(page.getByText(/unidades enteras de UD/)).toBeVisible();
  await expect(quantity).toHaveValue('1.5');
  await quantity.fill('10');
  await page.getByRole('button', { name: 'Guardar pedido', exact: true }).click();
  await expect(page).toHaveURL(/\/compras\/[0-9a-f-]{36}$/);
  await expect(page.getByText(/Estado del pedido: Borrador/)).toBeVisible();
  await expect(page.getByText(/Envío del correo: Sin solicitar/)).toBeVisible();
  const originalUrl = page.url();
  await page.getByRole('link', { name: 'Corregir borrador', exact: true }).click();
  await page.getByLabel('Cantidad (obligatoria) UD', { exact: true }).fill('6');
  await page.getByRole('button', { name: 'Guardar pedido', exact: true }).click();
  await expect(page).toHaveURL(originalUrl);
  const row = page.getByRole('row').filter({ hasText: article.sku });
  await expect(row.locator('td').nth(1)).toHaveText('6.000');
  await expect(row.locator('td').nth(2)).toHaveText('0.000');
  await expect(row.locator('td').nth(3)).toHaveText('6.000');
});
