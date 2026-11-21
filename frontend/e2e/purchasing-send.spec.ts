import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';
import { PurchaseDelivery } from '../src/app/features/purchasing/data/purchase-order.models';

test.skip(!purchasingProfile, 'El envío se prueba solo contra el Mailpit dedicado HU-07.');

test('revisa un cambio de contacto y Mailpit recibe la entrega congelada con su documento original', async ({ page, comprador, gestor, smtp }) => {
  const suffix = randomUUID().replaceAll('-', '').toUpperCase();
  const oldEmail = `antes-${suffix.toLowerCase()}@proveedor.test`; const reviewedEmail = `revisado-${suffix.toLowerCase()}@proveedor.test`;
  const laterEmail = `posterior-${suffix.toLowerCase()}@proveedor.test`;
  const headers = () => ({ 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': gestor.csrfToken });
  const familyResponse = await gestor.request.post('/api/v1/families', { headers: headers(), data: { code: `F-${suffix}`, name: 'Familia envío' } });
  expect(familyResponse.status()).toBe(201); const family = (await familyResponse.json()).resource;
  const supplierResponse = await gestor.request.post('/api/v1/suppliers', { headers: headers(), data: { code: `P-${suffix}`, name: 'Proveedor envío', email: oldEmail } });
  expect(supplierResponse.status()).toBe(201); const supplier = (await supplierResponse.json()).resource;
  const articleResponse = await gestor.request.post('/api/v1/articles', { headers: headers(), data: {
    sku: `A-${suffix}`, name: 'Artículo enviado', familyId: family.id, baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true
  } });
  expect(articleResponse.status()).toBe(201); const article = (await articleResponse.json()).resource;
  const draft = await comprador.request.post('/api/v1/purchase-orders', {
    headers: { 'Idempotency-Key': randomUUID(), 'X-XSRF-TOKEN': comprador.csrfToken }, data: {
      supplierId: supplier.id, expectedDeliveryDate: '2026-12-01', lines: [{ id: randomUUID(), articleId: article.id, quantity: '10', unitPrice: '12.50' }]
    }
  });
  expect(draft.status()).toBe(201); const order = (await draft.json()).resource; const path = `/api/v1/purchase-orders/${order.id}`;
  await page.context().addCookies((await comprador.request.storageState()).cookies);
  await page.goto(`/compras/${order.id}`);
  await page.getByRole('link', { name: 'Revisar envío al proveedor', exact: true }).click();
  await expect(page.getByText(`Destinatario que se confirmará: ${oldEmail}`, { exact: true })).toBeVisible();
  const changed = await gestor.request.put(`/api/v1/suppliers/${supplier.id}`, {
    headers: { ...headers(), 'If-Match': supplierResponse.headers()['etag'] }, data: { ...supplier, id: undefined, email: reviewedEmail }
  });
  expect(changed.status()).toBe(200);
  const oldReply = page.waitForResponse(response => response.url().endsWith(path + '/send') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Confirmar solicitud de envío', exact: true }).click();
  expect((await oldReply).status()).toBe(409);
  await expect(page.getByText(/cambió desde la revisión/)).toBeVisible();
  await page.getByRole('button', { name: 'Actualizar estado y revisión', exact: true }).click();
  await expect(page.getByText(`Destinatario que se confirmará: ${reviewedEmail}`, { exact: true })).toBeVisible();
  const response = page.waitForResponse(reply => reply.url().endsWith(path + '/send') && reply.request().method() === 'POST');
  await page.getByRole('button', { name: 'Confirmar solicitud de envío', exact: true }).focus(); await page.keyboard.press('Enter');
  const accepted = await response; expect(accepted.status()).toBe(202);
  const initial = await accepted.json(); expect(initial.state).toBe('Draft'); expect(initial.sendStatus).toBe('Pending');
  await expect(page.getByText(/Envío del correo: Aceptado por el servicio de correo/)).toBeVisible({ timeout: 15_000 });
  const deliveryResponse = await comprador.request.get(path + '/delivery'); expect(deliveryResponse.status()).toBe(200);
  const progress = await deliveryResponse.json() as PurchaseDelivery;
  expect(progress.deliveryId).toBe(initial.deliveryId); expect(progress.recipientEmail).toBe(reviewedEmail); expect(progress.sendStatus).toBe('Accepted');
  const after = await gestor.request.put(`/api/v1/suppliers/${supplier.id}`, {
    headers: { ...headers(), 'If-Match': changed.headers()['etag'] }, data: { ...supplier, id: undefined, email: laterEmail }
  });
  expect(after.status()).toBe(200);
  await page.getByRole('button', { name: 'Actualizar estado y revisión', exact: true }).click();
  await expect(page.getByText(`Destinatario conservado: ${reviewedEmail}`, { exact: false })).toBeVisible();
  type Mail = { ID: string; Subject: string; To: { Address: string }[] };
  let message: Mail | undefined;
  await expect.poll(async () => {
    const list = await smtp.messages() as { messages: Mail[] };
    const matching = list.messages.filter(mail => mail.Subject === `Pedido ${order.number}` && mail.To.some(address => address.Address === reviewedEmail));
    message = matching[0]; return matching.length;
  }, { timeout: 15_000 }).toBe(1);
  const raw = (await smtp.raw(message!.ID)).toString('utf8'); expect(raw).toContain(progress.messageId!);
  const captured = await smtp.message(message!.ID) as { Attachments: { PartID: string; FileName: string }[] };
  const attachment = captured.Attachments.find(part => part.FileName === `pedido-${order.number}.html`); expect(attachment).toBeDefined();
  const original = await comprador.request.get(path + '/delivery/document'); expect(original.status()).toBe(200);
  expect(await smtp.part(message!.ID, attachment!.PartID)).toEqual(await original.body());
  await page.getByRole('link', { name: 'Volver al pedido', exact: true }).click();
  await expect(page.getByText(/Estado del pedido: Enviado/)).toBeVisible();
  await expect(page.getByRole('link', { name: 'Corregir borrador', exact: true })).toHaveCount(0);
});
