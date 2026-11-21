import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';

test.skip(!purchasingProfile, 'Los permisos de compras se prueban en HU-07 aislada.');

test('logout retira el payload y otra cuenta no reutiliza la intención pendiente', async ({ page, comprador, gestor, lector }) => {
  const suffix = randomUUID().replaceAll('-', '').toUpperCase();
  const headers = () => ({ 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': gestor.csrfToken });
  const familyResponse = await gestor.request.post('/api/v1/families', { headers: headers(), data: { code: `F-${suffix}`, name: 'Familia permisos' } });
  expect(familyResponse.status()).toBe(201); const family = (await familyResponse.json()).resource;
  const supplierResponse = await gestor.request.post('/api/v1/suppliers', { headers: headers(), data: { code: `P-${suffix}`, name: 'Proveedor permisos', email: null } });
  expect(supplierResponse.status()).toBe(201); const supplier = (await supplierResponse.json()).resource;
  const articleResponse = await gestor.request.post('/api/v1/articles', { headers: headers(), data: {
    sku: `A-${suffix}`, name: 'Artículo permisos', familyId: family.id, baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true
  } });
  expect(articleResponse.status()).toBe(201); const article = (await articleResponse.json()).resource;
  const draft = await comprador.request.post('/api/v1/purchase-orders', { headers: { 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': comprador.csrfToken }, data: {
    supplierId: supplier.id, expectedDeliveryDate: '2026-12-01', lines: [{ id: randomUUID(), articleId: article.id, quantity: '10', unitPrice: '0' }]
  } });
  expect(draft.status()).toBe(201); const order = (await draft.json()).resource; const path = `/api/v1/purchase-orders/${order.id}`;
  await page.context().addCookies((await comprador.request.storageState()).cookies); await page.goto(`/compras/${order.id}/editar`);
  await page.getByLabel('Cantidad (obligatoria) UD', { exact: true }).fill('8');
  let key: string | undefined;
  await page.route('**' + path, async route => {
    expect(route.request().method()).toBe('PUT'); const result = await route.fetch(); expect(result.status()).toBe(200);
    key = route.request().headers()['idempotency-key']; await route.abort('failed');
  }, { times: 1 });
  await page.getByRole('button', { name: 'Guardar pedido', exact: true }).click();
  await expect.poll(() => key).toBeTruthy();
  await expect(page.getByRole('main').getByText(/Resultado pendiente de comprobar:/)).toBeVisible();
  await page.getByRole('button', { name: 'Cerrar sesión', exact: true }).click(); await expect(page).toHaveURL(/\/login$/);
  const references = await page.evaluate(() => JSON.parse(sessionStorage.getItem('inventario.pending-operations') ?? '[]'));
  const reference = references.find((value: { operationId: string }) => value.operationId === key);
  expect(reference).toBeDefined(); expect(reference).not.toHaveProperty('payload'); expect(reference).not.toHaveProperty('etag');
  await page.context().addCookies((await lector.request.storageState()).cookies); await page.goto('/compras');
  await expect(page.getByRole('heading', { name: 'Compras', exact: true })).toBeVisible();
  await expect(page.getByText(key!)).toHaveCount(0); await expect(page.getByRole('link', { name: 'Nuevo pedido', exact: true })).toHaveCount(0);
  await page.goto(`/compras/${order.id}/editar`); await expect(page).toHaveURL(/\/sin-permiso$/);
  await page.context().addCookies((await comprador.request.storageState()).cookies); await page.goto(`/compras/${order.id}/editar`);
  await expect(page.getByText(/La petición original se retiró al cerrar sesión/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Repetir la misma intención', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Comprobar resultado', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Comprobar resultado', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Cantidad (obligatoria) UD', { exact: true })).toHaveValue('8.000');
});
