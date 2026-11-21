import type { Page } from '@playwright/test';
import {
  apiSession, article, closeSession, cancelIfOpen, expect, hold, location, login, lotId, onlyInDemo, openStock, order, read,
  receipt, reserve, runId, send, startCount, test, type Session
} from './demo-helpers';

// Demo HU-03 (Persona B): stock insuficiente, salida que consume la reserva, origen en conteo abierto y permiso
// Shipping.Record. Complementa el E2E-02 (e2e/shipping.spec.ts). La reserva previa se prepara por API (HU-05).
onlyInDemo();

type Prepared = { sku: string; code: string; locationId: string; articleId: string; reference: string; orderId: string; lineId: string };

async function prepare(gestor: Session, lector: Session, responsable: Session, prefix: string, name: string,
  stock: string, ordered: string, reserved: string): Promise<Prepared> {
  const run = runId();
  const item = await article(gestor, run, prefix, name);
  const place = await location(lector, `${prefix.slice(-1)}S-${run}`, `${prefix}${run}`);
  expect((await receipt(lector, item.id, place.id, 'L-001', stock)).status).toBeLessThan(300);
  const reference = `PED-${prefix}${run}`;
  const customerOrder = await order(lector, reference, item.id, ordered);
  const lot = await lotId(lector, item.id, place.id, 'L-001');
  expect((await reserve(responsable, customerOrder.id, customerOrder.lineId, place.id, lot, reserved)).status).toBeLessThan(300);
  return { sku: item.sku, code: place.code, locationId: place.id, articleId: item.id, reference, orderId: customerOrder.id, lineId: customerOrder.lineId };
}

/** El operario abre el pedido en /salidas, prepara la salida con la propuesta y deja la cantidad indicada en borrador. */
async function draft(operario: Page, data: Prepared, quantity: string): Promise<void> {
  await operario.getByLabel('Referencia del pedido').fill(data.reference);
  await operario.getByRole('button', { name: 'Buscar' }).click();
  await expect(operario).toHaveURL(new RegExp(`/salidas/pedidos/${data.orderId}$`));
  await operario.getByRole('link', { name: 'Nueva salida' }).click();
  await expect(operario.getByRole('heading', { level: 1 })).toHaveText(`Nueva salida del pedido ${data.reference}`);
  await operario.getByRole('button', { name: 'Añadir', exact: true }).click();
  const line = operario.getByTestId('shipment-line');
  await expect(line).toContainText('Propuesto desde la reserva');
  await line.getByLabel('Cantidad (UD)').fill(quantity);
  await operario.getByRole('button', { name: 'Guardar borrador' }).click();
  await expect(operario).toHaveURL(/\/salidas\/[0-9a-f-]{36}$/);
}

test('Salida con stock insuficiente: la revisión avisa con el detalle y el servidor la rechaza', async ({ browser, gestor, lector, environment, playwright }) => {
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  try {
    const data = await prepare(gestor, lector, responsable, 'D03A', 'Tuerca escasa', '3', '6', '3');
    const operario = await login(browser, environment.accounts.lector, '/salidas');
    await draft(operario, data, '6');
    const shipmentId = new URL(operario.url()).pathname.split('/').pop()!;
    await operario.getByRole('button', { name: 'Revisar y confirmar' }).click();
    const review = operario.getByRole('dialog', { name: `Revisa la salida del pedido ${data.reference}` });
    await expect(review).toContainText('Cantidad 6.000 UD');
    await expect(review.getByTestId('over-possible')).toContainText('La cantidad supera la posible ahora (3.000 UD)');
    await hold(operario, 3500);

    // Aunque se llame directamente, el servidor rechaza la salida y nada cambia en existencias.
    const current = await read(lector, `/api/v1/shipments/${shipmentId}`);
    const context = await read(lector, `/api/v1/shipments/${shipmentId}/context`);
    const rejected = await send(lector, 'POST', `/api/v1/shipments/${shipmentId}/confirm`, { orderVersion: context.body['orderVersion'] }, current.etag);
    expect(rejected.status).toBeGreaterThanOrEqual(400);
    expect(String(rejected.body['detail'] ?? '')).not.toBe('');
    await openStock(operario, data.sku);
    await expect(operario.getByTestId('totals')).toContainText('Físico 3.000 UD');
    await hold(operario, 2500);
    await operario.context().close();
  } finally {
    await closeSession(responsable);
  }
});

test('Salida que consume la reserva del pedido: reservado a 0 y pedido servido', async ({ browser, gestor, lector, environment, playwright }) => {
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  try {
    const data = await prepare(gestor, lector, responsable, 'D03B', 'Tuerca reservada', '10', '6', '6');
    const operario = await login(browser, environment.accounts.lector, '/salidas');
    await draft(operario, data, '6');
    await operario.getByRole('button', { name: 'Revisar y confirmar' }).click();
    const review = operario.getByRole('dialog', { name: `Revisa la salida del pedido ${data.reference}` });
    await expect(review).toContainText('Reservado propio 6.000 UD');
    await hold(operario, 2500);
    await review.getByRole('button', { name: 'Confirmar salida' }).click();
    await expect(operario.getByRole('status').filter({ hasText: 'Salida registrada. Operación' })).toBeVisible();
    await hold(operario);
    await operario.goto(`/salidas/pedidos/${data.orderId}`);
    await expect(operario.getByTestId('fulfilled')).toHaveText('El pedido está servido: no admite nuevas salidas.');
    await hold(operario);
    await openStock(operario, data.sku);
    await expect(operario.getByTestId('totals')).toContainText('Físico 4.000 UD');
    await expect(operario.getByTestId('totals')).toContainText('Reservado 0.000 UD');
    await hold(operario, 2500);
    await operario.context().close();
  } finally {
    await closeSession(responsable);
  }
});

test('Salida cuyo origen entra en un conteo abierto: confirmar se rechaza (CountScopeFrozen)', async ({ browser, gestor, lector, environment, playwright }) => {
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  let countId = '';
  try {
    const data = await prepare(gestor, lector, responsable, 'D03C', 'Tuerca congelada', '10', '4', '4');
    const operario = await login(browser, environment.accounts.lector, '/salidas');
    await draft(operario, data, '4');
    // Mientras el borrador espera, empieza un conteo de su ubicación.
    const count = await startCount(lector, [data.locationId]);
    countId = count.id;
    await operario.getByRole('button', { name: 'Revisar y confirmar' }).click();
    const review = operario.getByRole('dialog', { name: `Revisa la salida del pedido ${data.reference}` });
    await review.getByRole('button', { name: 'Confirmar salida' }).click();
    await expect(operario.getByRole('alert')).toContainText(`La ubicación ${data.code} está en el conteo abierto ${count.code}`);
    await hold(operario, 3500);
    await openStock(operario, data.sku);
    await expect(operario.getByTestId('totals')).toContainText('Físico 10.000 UD');
    await hold(operario);
    await operario.context().close();
  } finally {
    if (countId) await cancelIfOpen(responsable, countId);
    await closeSession(responsable);
  }
});

test('Permiso Shipping.Record: una cuenta sin él no prepara salidas', async ({ browser, gestor, lector, environment, playwright }) => {
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  try {
    const data = await prepare(gestor, lector, responsable, 'D03D', 'Tuerca sin permiso', '10', '2', '2');
    // El gestor de maestros no tiene Shipping.Record: ve el pedido, pero sin «Nueva salida».
    const session = gestor;
    expect(session.me.permissions).not.toContain('Shipping.Record');
    const page = await login(browser, environment.accounts.gestor, `/salidas/pedidos/${data.orderId}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Salidas del pedido ${data.reference}`);
    await expect(page.getByText('Pendiente 2.000 UD')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Nueva salida' })).toHaveCount(0);
    await hold(page, 2500);
    await page.goto(`/salidas/pedidos/${data.orderId}/nueva`);
    await expect(page).toHaveURL('/sin-permiso');
    await hold(page);
    const lot = await lotId(lector, data.articleId, data.locationId, 'L-001');
    const denied = await send(session, 'POST', '/api/v1/shipments', {
      customerOrderId: data.orderId, occurredAt: new Date().toISOString(),
      lines: [{ customerOrderLineId: data.lineId, locationId: data.locationId, lotId: lot, quantity: '2' }]
    });
    expect(denied.status).toBe(403);
    await page.context().close();
  } finally {
    await closeSession(responsable);
  }
});
