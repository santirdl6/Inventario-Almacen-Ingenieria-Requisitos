import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';

test.skip(!purchasingProfile, 'La consulta de compras usa únicamente el proyecto aislado HU-07.');

test('consulta histórica, páginas vacías y fallo de consulta conservan navegación y datos', async ({ page, comprador, gestor, lector }) => {
  const suffix = randomUUID().replaceAll('-', '').toUpperCase();
  const managerHeaders = () => ({ 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': gestor.csrfToken });
  const familyResponse = await gestor.request.post('/api/v1/families', { headers: managerHeaders(), data: { code: `F-${suffix}`, name: 'Familia consulta' } });
  expect(familyResponse.status()).toBe(201); const family = (await familyResponse.json()).resource;
  const supplierResponse = await gestor.request.post('/api/v1/suppliers', { headers: managerHeaders(), data: { code: `P-${suffix}`, name: `Proveedor histórico ${suffix}`, email: 'consulta@proveedor.test' } });
  expect(supplierResponse.status()).toBe(201); const supplier = (await supplierResponse.json()).resource;
  const articleResponse = await gestor.request.post('/api/v1/articles', { headers: managerHeaders(), data: {
    sku: `A-${suffix}`, name: 'Artículo consulta', familyId: family.id, baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true
  } });
  expect(articleResponse.status()).toBe(201); const article = (await articleResponse.json()).resource;
  let last: { id: string; number: string } | null = null;
  for (let index = 0; index < 26; index++) {
    const created = await comprador.request.post('/api/v1/purchase-orders', {
      headers: { 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': comprador.csrfToken }, data: {
        supplierId: supplier.id, expectedDeliveryDate: '2026-12-01', lines: [{ id: randomUUID(), articleId: article.id, quantity: '1', unitPrice: '0' }]
      }
    });
    expect(created.status()).toBe(201); last = (await created.json()).resource;
  }
  const inactive = await gestor.request.put(`/api/v1/suppliers/${supplier.id}`, {
    headers: { ...managerHeaders(), 'If-Match': supplierResponse.headers()['etag'] }, data: { ...supplier, id: undefined, name: `Nombre corregido ${suffix}`, isActive: false }
  });
  expect(inactive.status()).toBe(200);
  await page.context().addCookies((await lector.request.storageState()).cookies);
  await page.goto('/compras');
  await expect(page.getByRole('link', { name: 'Nuevo pedido', exact: true })).toHaveCount(0);
  await page.getByLabel('Buscar proveedor de filtro', { exact: true }).fill(supplier.code);
  await page.getByRole('button', { name: 'Buscar proveedores', exact: true }).click();
  await page.getByLabel('Proveedor (incluye inactivos)', { exact: true }).selectOption(supplier.id);
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByText('26 pedidos coincidentes. Página 1.', { exact: true })).toBeVisible();
  await expect(page.getByRole('table')).toContainText(supplier.name);
  await page.getByRole('button', { name: 'Página siguiente', exact: true }).click();
  await expect(page.getByText('26 pedidos coincidentes. Página 2.', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: last!.number, exact: true }).click();
  await expect(page.getByText(/Proveedor Inactivo; se conserva el documento/)).toBeVisible();
  await expect(page.getByText(`Proveedor: ${supplier.code} — ${supplier.name}`, { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Corregir borrador', exact: true })).toHaveCount(0);
  await page.getByRole('link', { name: 'Volver a compras', exact: true }).click();
  await expect(page.getByText('26 pedidos coincidentes. Página 2.', { exact: true })).toBeVisible();
  await page.goto(`/compras?supplierId=${supplier.id}&page=99`);
  await expect(page.getByText(/Esta página ya no tiene filas/)).toBeVisible();
  await expect(page.getByText(/No hay pedidos para estos filtros/)).toHaveCount(0);
  await page.getByRole('button', { name: 'Última página válida', exact: true }).click();
  await expect(page.getByRole('link', { name: last!.number, exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('supplierId')).toBe(supplier.id);
  const matcher = '**/api/v1/purchase-orders?**';
  await page.route(matcher, route => route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'DependencyUnavailable', detail: 'Consulta temporalmente no disponible' }) }));
  await page.getByRole('button', { name: 'Actualizar listado', exact: true }).click();
  await expect(page.getByText(/Consulta temporalmente no disponible/)).toBeVisible();
  await expect(page.getByRole('link', { name: last!.number, exact: true })).toBeVisible();
  await page.unroute(matcher);
  await page.getByLabel('Número de pedido (prefijo)', { exact: true }).fill(last!.number);
  await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).click();
  await expect(page.getByText('1 pedidos coincidentes. Página 1.', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: last!.number, exact: true })).toBeVisible();
});
