import type { Browser, Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { test, expect, type Session } from './fixtures/master-data.fixture';

// HU-05 T019 · E2E-01 (specs/004-consultar-existencias/quickstart.md): consulta de existencias,
// alta de pedido, reserva y liberación por la interfaz, contra el entorno Docker real. Las
// existencias se preparan con el endpoint de research D-17 (activo solo en el proyecto E2E),
// porque HU-01 aún no ofrece recepciones por HTTP. Nada más se automatiza en E2E.

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

async function openStock(page: Page, sku: string): Promise<void> {
  await page.goto('/existencias');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Existencias');
  await expect(page.getByText('Elige un artículo para ver sus existencias.')).toBeVisible();
  await page.getByLabel('Artículo (SKU o nombre)').fill(sku);
  await page.getByLabel('Artículo (SKU o nombre)').press('Enter');
  await page.getByRole('button', { name: new RegExp(`^${sku} ·`) }).click();
  await expect(page.getByTestId('totals')).toContainText('Totales de todo el filtro');
}

test('E2E-01: el responsable consulta, el operario crea el pedido y el responsable reserva y libera', async ({ browser, gestor, lector, environment }) => {
  const run = Date.now().toString(36).toUpperCase().slice(-6);
  const sku = `E2E05-${run}`;
  const code = `N01-${run}`;
  const reference = `PED-${run}`;

  // 1. Preparación: artículo con lote (gestor), ubicación N01 (operario) y 10 de L-001 en N01
  //    con la siembra D-17 (Receiving.Record), que deja un movimiento normal en el diario.
  const family = await post(gestor, '/api/v1/families', { code: `F05-${run}`, name: 'Familia E2E-01' });
  const article = await post(gestor, '/api/v1/articles', {
    sku, name: 'Tornillo E2E-01', familyId: (family['resource'] as { id: string }).id, baseUnit: 'UD',
    allowsFractional: false, tracksLot: true, isActive: true
  });
  const articleId = (article['resource'] as { id: string }).id;
  const location = await post(lector, '/api/v1/locations', {
    code, aisle: `P${run}`, rack: 'E01', level: 'N01', kind: 'Storage', isActive: true
  });
  const locationId = (location['resource'] as { id: string }).id;
  await post(lector, '/api/v1/testing/stock-receipts', { articleId, locationId, lotCode: 'L-001', quantity: '10' });

  // 2. El responsable abre /existencias, elige el artículo y ve totales y filas.
  const responsable = environment.accounts.responsable;
  const manager = await login(browser, responsable.email, responsable.password, '/existencias');
  await openStock(manager, sku);
  const totals = manager.getByTestId('totals');
  await expect(totals).toContainText('Físico 10.000 UD');
  await expect(totals).toContainText('Reservado 0.000 UD');
  await expect(totals).toContainText('Disponible 10.000 UD');
  const row = manager.getByRole('row').filter({ hasText: code });
  await expect(row).toContainText('L-001');
  await expect(row).toContainText('10.000 UD');

  // 3. El operario crea el pedido en /existencias/pedidos/nuevo, con revisión antes de confirmar.
  const operario = await login(browser, environment.accounts.lector.email, environment.accounts.lector.password,
    '/existencias/pedidos/nuevo');
  await expect(operario.getByRole('heading', { level: 1 })).toHaveText('Nuevo pedido de cliente');
  await operario.getByLabel('Referencia del pedido').fill(reference);
  await operario.getByLabel('Cliente').fill('Cliente Norte');
  await operario.getByLabel('Añadir línea: artículo (SKU o nombre)').fill(sku);
  await operario.getByLabel('Añadir línea: artículo (SKU o nombre)').press('Enter');
  await operario.getByRole('button', { name: new RegExp(`^${sku} ·`) }).click();
  await operario.getByLabel('Cantidad (UD)').fill('7');
  await operario.getByRole('button', { name: 'Revisar' }).click();
  await expect(operario.getByTestId('order-review')).toContainText(`${sku} · Tornillo E2E-01: 7 UD`);
  await operario.getByRole('button', { name: 'Confirmar' }).click();
  await expect(operario).toHaveURL(/\/existencias\/pedidos\/[0-9a-f-]{36}$/);
  const orderPath = new URL(operario.url()).pathname;
  await expect(operario.getByRole('heading', { level: 1 })).toHaveText(`Pedido ${reference}`);

  // 4. El responsable abre el pedido por URL (la búsqueda de pedidos queda en «Si hay tiempo»),
  //    reserva 7 en N01 y en /existencias ve disponible 3.
  await manager.goto(orderPath);
  await expect(manager.getByRole('heading', { level: 1 })).toHaveText(`Pedido ${reference}`);
  await manager.getByRole('button', { name: 'Reservar' }).click();
  const reserve = manager.getByRole('dialog', { name: `Reservar ${sku} · Tornillo E2E-01` });
  await expect(reserve).toContainText('Máximo reservable 7.000 UD');
  await reserve.getByLabel(`Cantidad en ${code} (UD)`).fill('7');
  await reserve.getByRole('button', { name: 'Revisar' }).click();
  await reserve.getByRole('button', { name: 'Confirmar reserva' }).click();
  await expect(manager.getByRole('status').filter({ hasText: 'Reserva confirmada' })).toBeVisible();
  await expect(manager.getByTestId('line')).toContainText('Reservado 7.000 UD');
  await expect(manager.getByTestId('reservation')).toContainText(code);
  await openStock(manager, sku);
  await expect(manager.getByTestId('totals')).toContainText('Reservado 7.000 UD');
  await expect(manager.getByTestId('totals')).toContainText('Disponible 3.000 UD');

  // 5. Libera 2 con motivo y ve reservado 5 y disponible 5.
  await manager.goto(orderPath);
  await manager.getByRole('button', { name: 'Liberar' }).click();
  const release = manager.getByRole('dialog', { name: 'Liberar reserva' });
  await expect(release).toContainText('Remanente 7.000 UD');
  await release.getByLabel('Cantidad a liberar (UD)').fill('2');
  await release.getByLabel('Motivo').fill('El cliente reduce el pedido');
  await release.getByRole('button', { name: 'Revisar' }).click();
  await release.getByRole('button', { name: 'Confirmar liberación' }).click();
  await expect(manager.getByRole('status').filter({ hasText: 'Liberación confirmada' })).toBeVisible();
  await expect(manager.getByTestId('line')).toContainText('Reservado 5.000 UD');
  await openStock(manager, sku);
  await expect(manager.getByTestId('totals')).toContainText('Reservado 5.000 UD');
  await expect(manager.getByTestId('totals')).toContainText('Disponible 5.000 UD');

  await manager.context().close();
  await operario.context().close();
});
