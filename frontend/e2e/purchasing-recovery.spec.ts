import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';
import { PurchaseDelivery, PurchaseOrder } from '../src/app/features/purchasing/data/purchase-order.models';

test.skip(!purchasingProfile, 'La recuperación requiere el perfil HU-07 y su SMTP dedicado.');

test('recupera respuestas perdidas de alta, edición, solicitud y retry sin otra compra', async ({ page, comprador, gestor, smtp }) => {
  const suffix = randomUUID().replaceAll('-', '').toUpperCase();
  const email = `original-${suffix.toLowerCase()}@proveedor.test`;
  const headers = () => ({ 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': gestor.csrfToken });
  const familyResponse = await gestor.request.post('/api/v1/families', { headers: headers(), data: { code: `F-${suffix}`, name: 'Familia recuperación' } });
  expect(familyResponse.status()).toBe(201); const family = (await familyResponse.json()).resource;
  const supplierResponse = await gestor.request.post('/api/v1/suppliers', { headers: headers(), data: { code: `P-${suffix}`, name: 'Proveedor recuperación', email } });
  expect(supplierResponse.status()).toBe(201); const supplier = (await supplierResponse.json()).resource;
  const articleResponse = await gestor.request.post('/api/v1/articles', { headers: headers(), data: {
    sku: `A-${suffix}`, name: 'Artículo recuperación', familyId: family.id, baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true
  } });
  expect(articleResponse.status()).toBe(201); const article = (await articleResponse.json()).resource;
  await page.context().addCookies((await comprador.request.storageState()).cookies);
  await page.goto('/compras/nuevo');
  await page.getByLabel('Buscar proveedor', { exact: true }).fill(supplier.code);
  await page.getByRole('button', { name: 'Buscar proveedores', exact: true }).click();
  await page.getByLabel('Proveedor (obligatorio)', { exact: true }).selectOption(supplier.id);
  await page.getByLabel('Fecha prevista general (obligatoria)', { exact: true }).fill('2026-12-01');
  await page.getByLabel('Buscar artículo', { exact: true }).fill(article.sku);
  await page.getByRole('button', { name: 'Buscar artículos', exact: true }).click();
  await page.getByLabel('Artículo (obligatorio)', { exact: true }).selectOption(article.id);
  const captures: { key: string; body: { resource?: PurchaseOrder; deliveryId?: string }; request: Record<string, unknown> }[] = [];
  const loseNext = async (method: string, path: string) => page.route('**' + path, async route => {
    expect(route.request().method()).toBe(method);
    const response = await route.fetch(); expect(response.ok()).toBe(true);
    captures.push({ key: route.request().headers()['idempotency-key'], body: await response.json(), request: route.request().postDataJSON() });
    await route.abort('failed');
  }, { times: 1 });
  await loseNext('POST', '/api/v1/purchase-orders');
  await page.getByRole('button', { name: 'Guardar pedido', exact: true }).click();
  await expect.poll(() => captures.length).toBe(1);
  await expect(page.getByRole('main').getByText(/Resultado pendiente de comprobar:/)).toBeVisible();
  const order = captures[0].body.resource!; const path = `/api/v1/purchase-orders/${order.id}`;
  await page.reload(); await page.getByRole('button', { name: 'Comprobar resultado', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/compras/${order.id}$`));
  await page.getByRole('link', { name: 'Corregir borrador', exact: true }).click();
  await page.getByLabel('Cantidad (obligatoria) UD', { exact: true }).fill('7');
  await loseNext('PUT', path);
  await page.getByRole('button', { name: 'Guardar pedido', exact: true }).click(); await expect.poll(() => captures.length).toBe(2);
  await expect(page.getByRole('main').getByText(/Resultado pendiente de comprobar:/)).toBeVisible();
  await page.reload(); await page.getByRole('button', { name: 'Comprobar resultado', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Comprobar resultado', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Cantidad (obligatoria) UD', { exact: true })).toHaveValue('7.000');
  await page.goto(`/compras/${order.id}/enviar`);
  await expect(page.getByRole('button', { name: 'Confirmar solicitud de envío', exact: true })).toBeEnabled();
  smtp.stop();
  await loseNext('POST', path + '/send');
  await page.getByRole('button', { name: 'Confirmar solicitud de envío', exact: true }).click(); await expect.poll(() => captures.length).toBe(3);
  await page.reload();
  await expect(page.getByText(/Envío del correo: Error/)).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('button', { name: 'Reintentar envío', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Comprobar resultado', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Reintentar envío', exact: true })).toBeEnabled();
  const initial = await comprador.request.get(path + '/delivery'); expect(initial.ok()).toBe(true); const failed = await initial.json() as PurchaseDelivery;
  const document = await comprador.request.get(path + '/delivery/document'); const originalBytes = await document.body();
  const changed = await gestor.request.put(`/api/v1/suppliers/${supplier.id}`, {
    headers: { ...headers(), 'If-Match': supplierResponse.headers()['etag'] }, data: { ...supplier, id: undefined, email: `posterior-${suffix.toLowerCase()}@proveedor.test` }
  });
  expect(changed.status()).toBe(200);
  await smtp.start(); await loseNext('POST', path + '/send');
  await page.getByRole('button', { name: 'Reintentar envío', exact: true }).click(); await expect.poll(() => captures.length).toBe(4);
  expect(captures[3].key).not.toBe(captures[2].key); expect(captures[3].request['kind']).toBe('retry');
  expect(captures[3].body.deliveryId).toBe(failed.deliveryId);
  await page.reload(); await page.getByRole('button', { name: 'Comprobar resultado', exact: true }).click();
  await expect(page.getByText(/Envío del correo: Aceptado por el servicio de correo/)).toBeVisible({ timeout: 15_000 });
  const finalResponse = await comprador.request.get(path + '/delivery'); const final = await finalResponse.json() as PurchaseDelivery;
  expect(final.deliveryId).toBe(failed.deliveryId); expect(final.messageId).toBe(failed.messageId); expect(final.recipientEmail).toBe(email);
  expect(await (await comprador.request.get(path + '/delivery/document')).body()).toEqual(originalBytes);
  const list = await comprador.request.get(`/api/v1/purchase-orders?supplierId=${supplier.id}`); expect((await list.json()).totalCount).toBe(1);
  const messages = await smtp.messages() as { messages: { Subject: string; To: { Address: string }[] }[] };
  expect(messages.messages.filter(message => message.Subject === `Pedido ${order.number}` && message.To.some(to => to.Address === email))).toHaveLength(1);
});
