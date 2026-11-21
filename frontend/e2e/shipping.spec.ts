import type { Browser, Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { test, expect, type Session } from './fixtures/master-data.fixture';

// HU-03 T017 · E2E-02 (specs/005-registrar-salida-mercancia/quickstart.md): el responsable reserva 4
// de 10 por la interfaz de HU-05; el operario busca el pedido en /salidas, prepara la salida con la
// propuesta, cambia su cantidad a 6 (analyze-1 H1: otra fila del mismo bucket repetiría línea,
// ubicación y lote), revisa (propio 4, posible 6) y confirma. Datos únicos por ejecución; las
// existencias se preparan con la siembra D-17 de HU-05, activa solo en el proyecto E2E. Sin cambios
// en el fixture de A: las cuentas lector (Operario) y responsable ya existen.

async function post(session: Session, path: string, data: unknown): Promise<Record<string, unknown>> {
  const response = await session.request.post(path, {
    headers: { 'X-XSRF-TOKEN': session.csrfToken, 'Idempotency-Key': randomUUID() }, data
  });
  expect(response.status(), `POST ${path}: ${await response.text()}`).toBeLessThan(300);
  return await response.json() as Record<string, unknown>;
}

async function login(browser: Browser, email: string, password: string, target: string): Promise<Page> {
  const page = await (await browser.newContext()).newPage();
  await page.goto(target);
  await expect(page).toHaveURL(/\/login\?returnUrl=/);
  await page.getByLabel('Correo electrónico (obligatorio)').fill(email);
  await page.getByLabel('Contraseña (obligatoria)').fill(password);
  await page.getByLabel('Contraseña (obligatoria)').press('Enter');
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
  return page;
}

test('E2E-02: el operario sirve un pedido consumiendo la reserva propia y lo deja servido', async ({ browser, gestor, lector, environment }) => {
  const run = Date.now().toString(36).toUpperCase().slice(-6);
  const sku = `E2E03-${run}`;
  const code = `S01-${run}`;
  const reference = `PED-S${run}`;

  // 1. Preparación por API: artículo con lote, ubicación, 10 de L-001 sembradas y pedido de 6.
  const family = await post(gestor, '/api/v1/families', { code: `F03-${run}`, name: 'Familia E2E-02' });
  const article = await post(gestor, '/api/v1/articles', {
    sku, name: 'Tornillo E2E-02', familyId: (family['resource'] as { id: string }).id, baseUnit: 'UD',
    allowsFractional: false, tracksLot: true, isActive: true
  });
  const articleId = (article['resource'] as { id: string }).id;
  const location = await post(lector, '/api/v1/locations', {
    code, aisle: `P${run}`, rack: 'E01', level: 'N01', kind: 'Storage', isActive: true
  });
  const locationId = (location['resource'] as { id: string }).id;
  await post(lector, '/api/v1/testing/stock-receipts', { articleId, locationId, lotCode: 'L-001', quantity: '10' });
  const order = await post(lector, '/api/v1/customer-orders', {
    externalReference: reference, customerReference: 'Cliente Sur', lines: [{ articleId, quantity: '6' }]
  });
  const orderId = (order['resource'] as { id: string }).id;

  // 2. El responsable reserva 4 por la interfaz de HU-05.
  const responsable = environment.accounts.responsable;
  const manager = await login(browser, responsable.email, responsable.password, `/existencias/pedidos/${orderId}`);
  await expect(manager.getByRole('heading', { level: 1 })).toHaveText(`Pedido ${reference}`);
  await manager.getByRole('button', { name: 'Reservar' }).click();
  const reserve = manager.getByRole('dialog', { name: `Reservar ${sku} · Tornillo E2E-02` });
  await reserve.getByLabel(`Cantidad en ${code} (UD)`).fill('4');
  await reserve.getByRole('button', { name: 'Revisar' }).click();
  await reserve.getByRole('button', { name: 'Confirmar reserva' }).click();
  await expect(manager.getByTestId('line')).toContainText('Reservado 4.000 UD');

  // 3. El operario busca la referencia con espacios y minúsculas y abre el pedido en /salidas.
  const operario = await login(browser, environment.accounts.lector.email, environment.accounts.lector.password, '/salidas');
  await expect(operario.getByRole('heading', { level: 1 })).toHaveText('Salidas');
  await operario.getByLabel('Referencia del pedido').fill(`  ${reference.toLowerCase()} `);
  await operario.getByRole('button', { name: 'Buscar' }).click();
  await expect(operario).toHaveURL(new RegExp(`/salidas/pedidos/${orderId}$`));
  await expect(operario.getByTestId('order-line')).toContainText('Pendiente 6.000 UD');

  // Prepara la salida con la propuesta (4) y cambia su cantidad a 6 en la misma fila (H1).
  await operario.getByRole('link', { name: 'Nueva salida' }).click();
  await expect(operario.getByRole('heading', { level: 1 })).toHaveText(`Nueva salida del pedido ${reference}`);
  await operario.getByRole('button', { name: 'Añadir', exact: true }).click();
  const line = operario.getByTestId('shipment-line');
  await expect(line).toHaveCount(1);
  await expect(line).toContainText(`${code} · L-001`);
  await expect(line).toContainText('Propuesto desde la reserva');
  await expect(line.getByLabel('Cantidad (UD)')).toHaveValue('4.000');
  await line.getByLabel('Cantidad (UD)').fill('6');
  await operario.getByRole('button', { name: 'Guardar borrador' }).click();
  await expect(operario).toHaveURL(/\/salidas\/[0-9a-f-]{36}$/);
  const shipmentPath = new URL(operario.url()).pathname;

  // Revisa (propio 4, posible 6) y confirma.
  await operario.getByRole('button', { name: 'Revisar y confirmar' }).click();
  const review = operario.getByRole('dialog', { name: `Revisa la salida del pedido ${reference}` });
  await expect(review).toContainText('Cantidad 6.000 UD');
  await expect(review).toContainText('Reservado propio 4.000 UD');
  await expect(review).toContainText('Posible 6.000 UD');
  await review.getByRole('button', { name: 'Confirmar salida' }).click();

  // 4. Ve «Salida registrada», el pedido servido y en /existencias físico 4 y reservado 0.
  await expect(operario.getByRole('status').filter({ hasText: 'Salida registrada. Operación' })).toBeVisible();
  await expect(operario.getByTestId('confirmed-operation')).toContainText('Salida confirmada con la operación');
  await expect(operario.getByRole('button', { name: 'Guardar borrador' })).toHaveCount(0);
  await operario.goto(`/salidas/pedidos/${orderId}`);
  await expect(operario.getByTestId('fulfilled')).toHaveText('El pedido está servido: no admite nuevas salidas.');
  await expect(operario.getByTestId('shipment-row')).toContainText('Confirmada');

  await operario.goto('/existencias');
  await operario.getByLabel('Artículo (SKU o nombre)').fill(sku);
  await operario.getByLabel('Artículo (SKU o nombre)').press('Enter');
  await operario.getByRole('button', { name: new RegExp(`^${sku} ·`) }).click();
  await expect(operario.getByTestId('totals')).toContainText('Físico 4.000 UD');
  await expect(operario.getByTestId('totals')).toContainText('Reservado 0.000 UD');

  // Al reabrir la salida en otra visita se sigue viendo su operación (analyze-1 H2).
  await operario.goto(shipmentPath);
  await expect(operario.getByTestId('confirmed-operation')).toContainText('Salida confirmada con la operación');

  await manager.context().close();
  await operario.context().close();
});
