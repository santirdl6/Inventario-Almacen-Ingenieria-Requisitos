import {
  apiSession, article, closeSession, expect, hold, location, login, lotId, ok, onlyInDemo, openStock, order, read, receipt, reserve,
  runId, send, shipment, test
} from './demo-helpers';

// Demo HU-05 (Persona B): consulta con y sin filtros, reserva por encima de lo disponible, liberación parcial con
// motivo y operario sin permiso de reservar. Complementa el E2E-01 (e2e/stock.spec.ts).
onlyInDemo();

test('Consulta con y sin filtros: ubicaciones con físico 0 ocultas y artículo inactivo marcado', async ({ browser, gestor, lector, environment }) => {
  const run = runId();
  const item = await article(gestor, run, 'D05A', 'Clavo consultado');
  const first = await location(lector, `QA-${run}`, `QA${run}`);
  const second = await location(lector, `QB-${run}`, `QB${run}`);
  const empty = await location(lector, `QZ-${run}`, `QZ${run}`);
  await receipt(lector, item.id, first.id, 'L-001', '10');
  await receipt(lector, item.id, second.id, 'L-002', '5');
  await receipt(lector, item.id, empty.id, 'L-003', '2');
  // La ubicación QZ se vacía con una salida de sus 2: queda una fila con físico 0.
  const emptied = await order(lector, `PED-QZ${run}`, item.id, '2');
  await shipment(lector, emptied.id, emptied.lineId, empty.id, await lotId(lector, item.id, empty.id, 'L-003'), '2');
  // Y el artículo se da de baja en maestros (sigue teniendo existencias).
  const current = await read(gestor, `/api/v1/articles/${item.id}`);
  const deactivated = await send(gestor, 'PUT', `/api/v1/articles/${item.id}`, { ...item.body, isActive: false }, current.etag);
  expect(deactivated.status, JSON.stringify(deactivated.body)).toBeLessThan(300);

  const page = await login(browser, environment.accounts.responsable, '/existencias');
  await page.getByLabel('Artículo (SKU o nombre)').fill(item.sku);
  await page.getByLabel('Artículo (SKU o nombre)').press('Enter');
  await expect(page.getByTestId('article-option').filter({ hasText: item.sku })).toContainText('(Inactivo)');
  await hold(page);
  await page.getByRole('button', { name: new RegExp(`^${item.sku} ·`) }).click();
  const totals = page.getByTestId('totals');
  await expect(totals).toContainText('Físico 15.000 UD');
  await expect(page.locator('.badge-inactive').filter({ hasText: 'Inactivo' })).toBeVisible();
  const table = page.getByRole('table', { name: 'Existencias por ubicación y lote' });
  await expect(table).toContainText(first.code);
  await expect(table).toContainText(second.code);
  await expect(table).not.toContainText(empty.code);
  await hold(page, 2500);

  // Filtro por ubicación: solo QB y los totales del filtro.
  await page.locator('#stock-location').selectOption({ label: second.code });
  await expect(table).not.toContainText(first.code);
  await expect(totals).toContainText('Físico 5.000 UD');
  await hold(page, 2500);
  await page.locator('#stock-location').selectOption({ label: 'Todas' });
  // Filtro por lote: solo L-001 (en QA).
  await page.locator('#stock-lot').selectOption({ label: 'L-001' });
  await expect(table).not.toContainText(second.code);
  await expect(totals).toContainText('Físico 10.000 UD');
  await hold(page, 2500);
  await page.locator('#stock-lot').selectOption({ label: 'Todos' });
  await expect(table).toContainText(first.code);

  // Sin el filtro de vacías: aparece QZ con físico 0.
  await page.getByLabel('Mostrar ubicaciones sin existencias').check();
  await expect(page.getByRole('row').filter({ hasText: empty.code })).toContainText('0.000 UD');
  await hold(page, 3000);
  await page.context().close();
});

test('Reservar más de lo disponible: el servidor lo rechaza y no reserva nada', async ({ browser, gestor, lector, environment }) => {
  const run = runId();
  const item = await article(gestor, run, 'D05B', 'Clavo escaso');
  const place = await location(lector, `RA-${run}`, `RA${run}`);
  await receipt(lector, item.id, place.id, 'L-001', '5');
  const reference = `PED-RA${run}`;
  const customerOrder = await order(lector, reference, item.id, '8');

  const page = await login(browser, environment.accounts.responsable, `/existencias/pedidos/${customerOrder.id}`);
  await page.getByRole('button', { name: 'Reservar' }).click();
  const dialog = page.getByRole('dialog', { name: `Reservar ${item.sku} · ${item.name}` });
  await expect(dialog).toContainText('Máximo reservable 5.000 UD');
  await dialog.getByLabel(`Cantidad en ${place.code} (UD)`).fill('8');
  await dialog.getByRole('button', { name: 'Revisar' }).click();
  await expect(dialog).toContainText('Vas a reservar 8');
  await dialog.getByRole('button', { name: 'Confirmar reserva' }).click();
  await expect(dialog.getByRole('alert')).not.toBeEmpty();
  await expect(dialog.getByRole('link', { name: 'Ver existencias actuales' })).toBeVisible();
  await hold(page, 3500);
  await dialog.getByRole('button', { name: 'Cerrar' }).click();
  await expect(page.getByTestId('line')).toContainText('Reservado 0.000 UD');
  await hold(page);
  await page.context().close();
});

test('Liberar parte de una reserva con motivo obligatorio', async ({ browser, gestor, lector, environment, playwright }) => {
  const run = runId();
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  try {
    const item = await article(gestor, run, 'D05C', 'Clavo liberado');
    const place = await location(lector, `LA-${run}`, `LA${run}`);
    await receipt(lector, item.id, place.id, 'L-001', '10');
    const reference = `PED-LA${run}`;
    const customerOrder = await order(lector, reference, item.id, '6');
    await ok(responsable, 'POST', '/api/v1/reservations', {
      customerOrderLineId: customerOrder.lineId,
      allocations: [{ locationId: place.id, lotId: await lotId(lector, item.id, place.id, 'L-001'), quantity: '6' }]
    }, (await read(responsable, `/api/v1/customer-orders/${customerOrder.id}`)).etag);

    const page = await login(browser, environment.accounts.responsable, `/existencias/pedidos/${customerOrder.id}`);
    await expect(page.getByTestId('line')).toContainText('Reservado 6.000 UD');
    await page.getByRole('button', { name: 'Liberar' }).click();
    const release = page.getByRole('dialog', { name: 'Liberar reserva' });
    await expect(release).toContainText('Remanente 6.000 UD');
    await release.getByLabel('Cantidad a liberar (UD)').fill('2');
    // Sin motivo no se puede revisar.
    await release.getByRole('button', { name: 'Revisar' }).click();
    await expect(release.getByLabel('Motivo')).toHaveAttribute('aria-invalid', 'true');
    await hold(page, 2500);
    await release.getByLabel('Motivo').fill('El cliente aplaza parte del pedido');
    await release.getByRole('button', { name: 'Revisar' }).click();
    await hold(page);
    await release.getByRole('button', { name: 'Confirmar liberación' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Liberación confirmada' })).toBeVisible();
    await expect(page.getByTestId('line')).toContainText('Reservado 4.000 UD');
    await hold(page);
    await openStock(page, item.sku);
    await expect(page.getByTestId('totals')).toContainText('Reservado 4.000 UD');
    await expect(page.getByTestId('totals')).toContainText('Disponible 6.000 UD');
    await hold(page, 2500);
    await page.context().close();
  } finally {
    await closeSession(responsable);
  }
});

test('Permiso: el operario ve el pedido pero no puede reservar (Reservations.Manage)', async ({ browser, gestor, lector, environment }) => {
  const run = runId();
  const item = await article(gestor, run, 'D05D', 'Clavo sin permiso');
  const place = await location(lector, `PA-${run}`, `PA${run}`);
  await receipt(lector, item.id, place.id, 'L-001', '10');
  const reference = `PED-PA${run}`;
  const customerOrder = await order(lector, reference, item.id, '3');

  const page = await login(browser, environment.accounts.lector, `/existencias/pedidos/${customerOrder.id}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Pedido ${reference}`);
  await expect(page.getByTestId('line')).toContainText('Reservado 0.000 UD');
  await expect(page.getByRole('button', { name: 'Reservar' })).toHaveCount(0);
  await hold(page, 3000);
  expect(lector.me.permissions).not.toContain('Reservations.Manage');
  const denied = await reserve(lector, customerOrder.id, customerOrder.lineId, place.id, await lotId(lector, item.id, place.id, 'L-001'), '3');
  expect(denied.status).toBe(403);
  await page.context().close();
});
