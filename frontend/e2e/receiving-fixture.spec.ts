import { purchasingProfile } from './fixtures/master-data.fixture';
import { test, expect } from './fixtures/receiving.fixture';

test.skip(!purchasingProfile || !/^inventario-hu07-e2e-hu01(?:-|$)/.test(process.env['HU08_E2E_PROJECT'] ?? ''),
  'Requiere perfil de correo y proyecto HU-01 aislado.');

test('prepara una compra enviada y dos destinos mediante los flujos existentes', async ({ lector, receivingPurchase }) => {
  expect(lector.me.permissions).toContain('Receiving.Record');
  const order = await lector.request.get(`/api/v1/purchase-orders/${receivingPurchase.orderId}`);
  expect(order.status()).toBe(200);
  expect(await order.json()).toMatchObject({ state: 'Sent', sendStatus: 'Accepted',
    lines: [{ id: receivingPurchase.lineId, pendingQuantity: '10.000' }] });
});
