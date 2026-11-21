import { purchasingProfile } from './fixtures/master-data.fixture';
import { test, expect } from './fixtures/receiving.fixture';

test.skip(!purchasingProfile || !/^inventario-hu07-e2e-hu01(?:-|$)/.test(process.env['HU08_E2E_PROJECT'] ?? ''), 'Requiere el proyecto HU-01 aislado.');

test('reabre una incidencia sin aceptación y no la convierte en una entrada', async ({ page, lector, receivingPurchase }) => {
  await page.context().addCookies((await lector.request.storageState()).cookies);
  await page.goto('/recepciones/nueva');
  await page.getByLabel('Número de pedido').fill(receivingPurchase.orderNumber);
  await page.getByRole('button', { name: 'Buscar pedidos' }).click();
  await page.getByRole('button', { name: new RegExp(receivingPurchase.orderNumber) }).click();
  await page.getByRole('button', { name: 'Añadir incidencia' }).click();
  await page.getByLabel('Descripción observada').fill('Pieza no pedida');
  await page.getByLabel('Motivo de la incidencia').fill('Sustitución rechazada');
  await page.getByRole('button', { name: 'Guardar borrador' }).click();
  await expect(page).toHaveURL(/\/recepciones\/[0-9a-f-]{36}$/);
  await page.reload();
  await expect(page.getByLabel('Descripción observada')).toHaveValue('Pieza no pedida');
  await page.getByRole('button', { name: 'Guardar y revisar' }).click();
  await expect(page.getByRole('button', { name: 'Confirmar recepción' })).toBeDisabled();
  const order = await lector.request.get(`/api/v1/purchase-orders/${receivingPurchase.orderId}`);
  expect(await order.json()).toMatchObject({ state: 'Sent', lines: [{ pendingQuantity: '10.000' }] });
});
