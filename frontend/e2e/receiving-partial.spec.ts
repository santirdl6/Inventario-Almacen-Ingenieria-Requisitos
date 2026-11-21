import { purchasingProfile } from './fixtures/master-data.fixture';
import { test, expect } from './fixtures/receiving.fixture';

test.skip(!purchasingProfile || !/^inventario-hu07-e2e-hu01(?:-|$)/.test(process.env['HU08_E2E_PROJECT'] ?? ''),
  'Requiere el proyecto HU-01 aislado.');

test('localiza 6 unidades, mantiene 4 pendientes y completa el pedido con otra entrega', async ({ page, lector, receivingPurchase }) => {
  const purchase = receivingPurchase;
  await page.context().addCookies((await lector.request.storageState()).cookies);
  async function receive(amount: number, pieces: number[]) {
    await page.goto('/recepciones');
    await page.getByRole('link', { name: 'Nueva recepción' }).click();
    if (amount === 4) await page.getByLabel('Estado del pedido').selectOption('PartiallyReceived');
    await page.getByLabel('Número de pedido').fill(purchase.orderNumber);
    await page.getByRole('button', { name: 'Buscar pedidos' }).click();
    await page.getByRole('button', { name: new RegExp(purchase.orderNumber) }).click();
    await page.getByLabel('Albarán o soporte').fill(`ALB-${amount}`);
    await page.getByLabel('Motivo de entrada', { exact: true }).fill('Recepción del proveedor');
    await page.getByRole('button', { name: /Añadir .* línea 1/ }).click();
    await page.getByLabel('Cantidad reportada · línea 1').fill(String(amount));
    await page.getByLabel('Cantidad aceptada · línea 1').fill(String(amount));
    for (const [index, piece] of pieces.entries()) {
      await page.getByRole('button', { name: 'Añadir asignación · línea 1' }).click();
      await page.getByLabel(`Ubicación · línea 1, asignación ${index+1}`).selectOption(purchase.locations[index].id);
      await page.getByLabel(`Cantidad asignada · línea 1, asignación ${index+1}`).fill(String(piece));
    }
    await page.getByRole('button', { name: 'Guardar borrador' }).click();
    await expect(page).toHaveURL(/\/recepciones\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Borrador · Sin efectos en existencias.')).toBeVisible();
    await page.getByRole('button', { name: 'Guardar y revisar' }).click();
    await expect(page.getByRole('region', { name: 'Revisar recepción' })).toBeVisible();
    await page.getByRole('button', { name: 'Confirmar recepción' }).click();
    await expect(page.getByRole('main').getByRole('status').filter({ hasText: 'Recepción confirmada.' })).toBeVisible();
  }
  await receive(6, [2, 4]);
  const partial = await lector.request.get(`/api/v1/purchase-orders/${purchase.orderId}`);
  expect(await partial.json()).toMatchObject({ state: 'PartiallyReceived', lines: [{ receivedQuantity: '6.000', pendingQuantity: '4.000' }] });
  await receive(4, [4]);
  const complete = await lector.request.get(`/api/v1/purchase-orders/${purchase.orderId}`);
  expect(await complete.json()).toMatchObject({ state: 'Received', lines: [{ receivedQuantity: '10.000', pendingQuantity: '0.000' }] });
  expect(complete.headers()['etag']).not.toBe(partial.headers()['etag']);
});
