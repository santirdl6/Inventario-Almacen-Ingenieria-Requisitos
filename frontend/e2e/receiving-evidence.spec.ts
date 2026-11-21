import { randomUUID } from 'node:crypto';
import { purchasingProfile } from './fixtures/master-data.fixture';
import { test, expect } from './fixtures/receiving.fixture';

test.skip(!purchasingProfile || !/^inventario-hu07-e2e-hu01(?:-|$)/.test(process.env['HU08_E2E_PROJECT'] ?? ''),
  'Requiere el proyecto HU-01 aislado.');

test('consulta evidencia, compensa una entrada y revisa alcance de contingencia', async ({ page, lector, gestor, receivingPurchase }) => {
  const purchase = receivingPurchase;
  const headers = (csrf: string) => ({ 'X-XSRF-TOKEN': csrf, 'Idempotency-Key': randomUUID() });

  // 1. Confirmar una recepción de 6 unidades vía API
  const allocationId = randomUUID();
  const created = await lector.request.post('/api/v1/receipts', {
    headers: headers(lector.csrfToken),
    data: {
      purchaseOrderId: purchase.orderId,
      occurredAt: new Date().toISOString(),
      supplierDocumentReference: 'ALB-EVIDENCIA-01',
      reason: 'Entrega para control de evidencia',
      lines: [{
        id: randomUUID(),
        purchaseOrderLineId: purchase.lineId,
        reportedQuantity: '6',
        acceptedQuantity: '6',
        allocations: [{ id: allocationId, locationId: purchase.locations[0].id, quantity: '6' }]
      }]
    }
  });
  expect(created.status()).toBe(201);
  const receiptId = (await created.json()).resource.id;

  const confirmed = await lector.request.post(`/api/v1/receipts/${receiptId}/confirm`, {
    headers: { ...headers(lector.csrfToken), 'If-Match': created.headers()['etag'] },
    data: {}
  });
  expect(confirmed.status()).toBe(200);

  // 2. Consulta en la UI y verificación de evidencia
  await page.context().addCookies((await lector.request.storageState()).cookies);
  await page.goto(`/recepciones/${receiptId}`);
  await expect(page.getByText('Recepción confirmada.')).toBeVisible();
  await expect(page.getByText('ALB-EVIDENCIA-01')).toBeVisible();

  // 3. Compensación de 2 unidades como usuario con permiso de ajuste (gestor)
  await page.context().clearCookies();
  await page.context().addCookies((await gestor.request.storageState()).cookies);
  await page.goto(`/recepciones/${receiptId}`);

  await expect(page.getByRole('heading', { name: 'Compensar una entrada' })).toBeVisible();
  await page.getByLabel('Asignación original').selectOption(allocationId);
  await page.getByLabel('Cantidad a compensar').fill('2');
  await page.getByLabel('Motivo de la compensación').fill('Diferencia de pesaje detectada en almacén');
  await page.getByRole('button', { name: 'Registrar compensación' }).click();

  await expect(page.getByText(/Compensación .* Diferencia de pesaje/)).toBeVisible();

  // 4. Verificación de compra tras compensación: recibido 4, pendiente 6
  const orderAfter = await lector.request.get(`/api/v1/purchase-orders/${purchase.orderId}`);
  expect(await orderAfter.json()).toMatchObject({
    state: 'PartiallyReceived',
    lines: [{ receivedQuantity: '4.000', pendingQuantity: '6.000' }]
  });

  // 5. Revisión del alcance de contingencia y rechazo de reapertura incompleta
  await page.goto('/recepciones/contingencia');
  await page.getByLabel('Identificadores de compras').fill(purchase.orderId);
  await page.getByLabel('Identificadores de ubicaciones').fill(purchase.locations[0].id);
  await page.getByLabel('Comprobantes esperados').fill('CP-001, CP-002');
  await page.getByRole('button', { name: 'Registrar alcance' }).click();

  await expect(page.getByText(/Alcance .* Suspendido/)).toBeVisible();
  await page.getByRole('button', { name: 'Autorizar reapertura' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
});
