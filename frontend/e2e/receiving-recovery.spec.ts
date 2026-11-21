import { randomUUID } from 'node:crypto';
import { purchasingProfile } from './fixtures/master-data.fixture';
import { test, expect } from './fixtures/receiving.fixture';

test.skip(!purchasingProfile || !/^inventario-hu07-e2e-hu01(?:-|$)/.test(process.env['HU08_E2E_PROJECT'] ?? ''), 'Requiere el proyecto HU-01 aislado.');

test('recupera la confirmación cuyo resultado se perdió sin otra entrada', async ({ page, lector, receivingPurchase }) => {
  const purchase = receivingPurchase;
  const created = await lector.request.post('/api/v1/receipts', { headers: { 'X-XSRF-TOKEN': lector.csrfToken, 'Idempotency-Key': randomUUID() }, data: {
    purchaseOrderId: purchase.orderId, occurredAt: new Date().toISOString(), supplierDocumentReference: 'ALB-RECUPERACIÓN', reason: 'Recepción',
    lines: [{ id: randomUUID(), purchaseOrderLineId: purchase.lineId, reportedQuantity: '6', acceptedQuantity: '6',
      allocations: [{ id: randomUUID(), locationId: purchase.locations[0].id, quantity: '6' }] }]
  } });
  expect(created.status()).toBe(201);
  const id = (await created.json()).resource.id;
  await page.context().addCookies((await lector.request.storageState()).cookies);
  await page.goto(`/recepciones/${id}`);
  await page.getByRole('button', { name: 'Guardar y revisar' }).click();
  await page.route(`**/api/v1/receipts/${id}/confirm`, async route => {
    const response = await route.fetch();
    expect(response.status()).toBe(200);
    await route.abort();
  }, { times: 1 });
  await page.getByRole('button', { name: 'Confirmar recepción' }).click();
  await expect(page.getByRole('main').getByRole('alert')).toBeVisible();
  await page.reload();
  await page.getByRole('main').getByRole('button', { name: 'Comprobar resultado' }).click();
  await expect(page.getByRole('main').getByText(/Recepción confirmada\. Entrada/)).toBeVisible();
  const order = await lector.request.get(`/api/v1/purchase-orders/${purchase.orderId}`);
  expect(await order.json()).toMatchObject({ state: 'PartiallyReceived', lines: [{ receivedQuantity: '6.000', pendingQuantity: '4.000' }] });
});
