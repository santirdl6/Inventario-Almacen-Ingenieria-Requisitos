import {
  apiSession, article, cancelIfOpen, closeSession, countAndSubmit, countLines, expect, hold, location, login, lotId, onlyInDemo,
  openStock, order, read, receipt, runId, send, shipment, startCount, test
} from './demo-helpers';

// Demo HU-04 (Persona B): bloqueo por conteo, conteo con varias diferencias, aviso P-07 y permiso de ajuste.
// Complementa el E2E-03 (e2e/hu04-cycle-counts.spec.ts). Cada escenario cancela por API su conteo si queda abierto.
onlyInDemo();

test('Una ubicación en conteo rechaza reservas y cambios (CountScopeFrozen)', async ({ browser, gestor, lector, environment, playwright }) => {
  const run = runId();
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  let countId = '';
  try {
    const item = await article(gestor, run, 'D04A', 'Tornillo congelado');
    const place = await location(lector, `CF-${run}`, `CF${run}`);
    expect((await receipt(lector, item.id, place.id, 'L-001', '10')).status).toBeLessThan(300);
    const reference = `PED-CF${run}`;
    const customerOrder = await order(lector, reference, item.id, '4');

    // El operario crea el conteo y lo inicia: la ubicación queda congelada.
    const operario = await login(browser, environment.accounts.lector, '/inventarios/nuevo');
    await operario.getByLabel('Buscar ubicación').fill(place.code);
    await operario.getByLabel(place.code, { exact: true }).check();
    await operario.getByRole('button', { name: 'Guardar borrador' }).click();
    await expect(operario).toHaveURL(/\/inventarios\/[0-9a-f-]{36}$/);
    countId = new URL(operario.url()).pathname.split('/').pop()!;
    await expect(operario.getByTestId('freeze-notice')).toContainText(`${place.code} quedarán congeladas`);
    await hold(operario);
    await operario.getByRole('button', { name: 'Iniciar conteo' }).click();
    await expect(operario.getByTestId('count-state')).toHaveText('En conteo');
    const countCode = (await read(lector, `/api/v1/cycle-counts/${countId}`)).body['countCode'] as string;
    const frozen = `La ubicación ${place.code} está en el conteo abierto ${countCode}`;

    // El responsable intenta reservar en la ubicación congelada: 409 con ubicación y conteo.
    const manager = await login(browser, environment.accounts.responsable, `/existencias/pedidos/${customerOrder.id}`);
    await expect(manager.getByRole('heading', { level: 1 })).toHaveText(`Pedido ${reference}`);
    await manager.getByRole('button', { name: 'Reservar' }).click();
    const dialog = manager.getByRole('dialog', { name: `Reservar ${item.sku} · ${item.name}` });
    await dialog.getByLabel(`Cantidad en ${place.code} (UD)`).fill('4');
    await dialog.getByRole('button', { name: 'Revisar' }).click();
    await dialog.getByRole('button', { name: 'Confirmar reserva' }).click();
    await expect(dialog.getByRole('alert')).toContainText(frozen);
    await hold(manager, 2500);
    await dialog.getByRole('button', { name: 'Cerrar' }).click();
    await expect(manager.getByTestId('line')).toContainText('Reservado 0.000 UD');

    // Y el operario no puede desactivar la ubicación mientras dure el conteo.
    await operario.goto(`/ubicaciones/${place.id}`);
    await operario.getByTestId('state-button').getByRole('button', { name: 'Desactivar' }).click();
    await operario.getByRole('dialog', { name: 'Desactivar ubicación' }).getByRole('button', { name: 'Desactivar' }).click();
    await expect(operario.getByText(frozen).first()).toBeVisible();
    await hold(operario, 2500);

    // El responsable cancela el conteo con motivo y la ubicación vuelve a admitir operaciones.
    await manager.goto(`/inventarios/${countId}`);
    await manager.getByRole('button', { name: 'Cancelar conteo' }).click();
    await manager.getByLabel('Motivo de la cancelación').fill('Demo: fin de la prueba de bloqueo');
    await manager.getByRole('button', { name: 'Confirmar cancelación' }).click();
    await expect(manager.getByTestId('count-state')).toHaveText('Cancelado');
    await hold(manager);
    expect((await read(lector, `/api/v1/locations/${place.id}`)).body['inOpenCount']).toBe(false);
    await operario.context().close();
    await manager.context().close();
  } finally {
    if (countId) await cancelIfOpen(responsable, countId);
    await closeSession(responsable);
  }
});

test('Conteo con diferencias en dos ubicaciones: revisión con motivos y ajuste del físico', async ({ browser, gestor, lector, environment, playwright }) => {
  const run = runId();
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  let countId = '';
  try {
    const item = await article(gestor, run, 'D04B', 'Arandela contada');
    const first = await location(lector, `CA-${run}`, `CA${run}`);
    const second = await location(lector, `CB-${run}`, `CB${run}`);
    await receipt(lector, item.id, first.id, 'L-001', '10');
    await receipt(lector, item.id, second.id, 'L-002', '5');
    const count = await startCount(lector, [first.id, second.id]);
    countId = count.id;

    // El operario cuenta 8 donde había 10 y 7 donde había 5.
    const operario = await login(browser, environment.accounts.lector, `/inventarios/${countId}`);
    await expect(operario.getByTestId('count-state')).toHaveText('En conteo');
    await operario.getByLabel(`Contado de ${item.sku} en ${first.code}`).fill('8');
    await operario.getByLabel(`Contado de ${item.sku} en ${second.code}`).fill('7');
    await expect(operario.getByRole('cell', { name: 'Faltan 2 UD' })).toBeVisible();
    await expect(operario.getByRole('cell', { name: 'Sobran 2 UD' })).toBeVisible();
    await operario.getByRole('button', { name: 'Guardar', exact: true }).click();
    await expect(operario.getByRole('status').filter({ hasText: 'Conteo guardado.' })).toBeVisible();
    await operario.getByRole('button', { name: 'Enviar a aprobación' }).click();
    await expect(operario.getByTestId('count-state')).toHaveText('Pendiente de aprobación');
    await expect(operario.getByTestId('difference-count')).toContainText('2 líneas con diferencia de 2 líneas');
    await hold(operario);

    // El responsable revisa: un motivo por diferencia, revisión y confirmación.
    const manager = await login(browser, environment.accounts.responsable, `/inventarios/${countId}`);
    const table = manager.getByRole('table', { name: 'Revisión del conteo' });
    await expect(table).toContainText('Faltan 2 UD');
    await expect(table).toContainText('Sobran 2 UD');
    const reasons = manager.getByLabel('Motivo');
    await reasons.nth(0).fill('Rotura en el almacén');
    await reasons.nth(1).fill('Recepción sin registrar');
    await manager.getByRole('button', { name: 'Guardar revisión' }).click();
    await manager.getByRole('button', { name: 'Confirmar ajuste' }).click();
    const review = manager.getByRole('dialog', { name: new RegExp(`Revisa el ajuste del conteo ${count.code}`) });
    await expect(review).toContainText('Rotura en el almacén');
    await expect(review).toContainText('Recepción sin registrar');
    await hold(manager, 2500);
    await review.getByRole('button', { name: 'Confirmar ajuste' }).click();
    await expect(manager.getByRole('status').filter({ hasText: 'Ajuste registrado. Operación' })).toBeVisible();
    await expect(manager.getByTestId('count-state')).toHaveText('Confirmado');
    await expect(manager.getByTestId('closure')).toContainText('Movimiento de ajuste');
    await hold(manager);

    // Físico ajustado: 8 en la primera, 7 en la segunda, 15 en total.
    await openStock(manager, item.sku);
    await expect(manager.getByTestId('totals')).toContainText('Físico 15.000 UD');
    await expect(manager.getByRole('row').filter({ hasText: first.code })).toContainText('8.000 UD');
    await expect(manager.getByRole('row').filter({ hasText: second.code })).toContainText('7.000 UD');
    await hold(manager, 2500);
    await operario.context().close();
    await manager.context().close();
  } finally {
    if (countId) await cancelIfOpen(responsable, countId);
    await closeSession(responsable);
  }
});

test('Aviso P-07: una diferencia vinculada a una salida de 6 (salieron 4) no corrige el pedido', async ({ browser, gestor, lector, environment, playwright }) => {
  const run = runId();
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  let countId = '';
  try {
    const item = await article(gestor, run, 'D04C', 'Bisagra servida');
    const place = await location(lector, `CP-${run}`, `CP${run}`);
    await receipt(lector, item.id, place.id, 'L-001', '10');
    const reference = `PED-P7${run}`;
    const customerOrder = await order(lector, reference, item.id, '6');
    // Se registró una salida de 6, pero físicamente salieron 4: el sistema cree que quedan 4 y hay 6.
    await shipment(lector, customerOrder.id, customerOrder.lineId, place.id, await lotId(lector, item.id, place.id, 'L-001'), '6');
    const count = await startCount(lector, [place.id]);
    countId = count.id;
    await countAndSubmit(lector, countId, { [place.id]: '6' });

    // El responsable vincula la diferencia (+2) a la salida: aparece el aviso P-07.
    const manager = await login(browser, environment.accounts.responsable, `/inventarios/${countId}`);
    await expect(manager.getByTestId('count-state')).toHaveText('Pendiente de aprobación');
    await expect(manager.getByRole('table', { name: 'Revisión del conteo' })).toContainText('Sobran 2 UD');
    await manager.getByLabel('Motivo').fill('Salieron 4 de las 6 registradas');
    await manager.getByRole('button', { name: 'Vincular a salida' }).click();
    const link = manager.getByTestId('shipment-link-dialog');
    await expect(link).toContainText('No cambia lo servido del pedido');
    await link.getByLabel(new RegExp(`Pedido ${reference}`)).check();
    await hold(manager);
    await link.getByRole('button', { name: 'Vincular' }).click();
    await expect(manager.getByTestId('linked')).toContainText(`Corrige la salida del pedido ${reference}`);
    await expect(manager.getByTestId('p07-notice')).toContainText('el pedido sigue mostrando 6 servidas');
    await hold(manager, 3500);
    await manager.getByRole('button', { name: 'Guardar revisión' }).click();
    await manager.getByRole('button', { name: 'Confirmar ajuste' }).click();
    const review = manager.getByRole('dialog', { name: new RegExp(`Revisa el ajuste del conteo ${count.code}`) });
    await hold(manager, 2000);
    await review.getByRole('button', { name: 'Confirmar ajuste' }).click();
    await expect(manager.getByTestId('count-state')).toHaveText('Confirmado');
    await hold(manager);

    // Físico 6, pero el pedido sigue servido con 6.
    await openStock(manager, item.sku);
    await expect(manager.getByTestId('totals')).toContainText('Físico 6.000 UD');
    await hold(manager);
    await manager.goto(`/salidas/pedidos/${customerOrder.id}`);
    await expect(manager.getByTestId('fulfilled')).toHaveText('El pedido está servido: no admite nuevas salidas.');
    await hold(manager, 2500);
    const after = await read(lector, `/api/v1/customer-orders/${customerOrder.id}`);
    expect((after.body['lines'] as { shipped: string }[])[0].shipped).toBe('6.000');
    await manager.context().close();
  } finally {
    if (countId) await cancelIfOpen(responsable, countId);
    await closeSession(responsable);
  }
});

test('Permiso: el operario no puede confirmar el ajuste (solo Inventory.Adjust)', async ({ browser, gestor, lector, environment, playwright }) => {
  const run = runId();
  const responsable = await apiSession(playwright.request, environment.baseURL, environment.accounts.responsable);
  let countId = '';
  try {
    const item = await article(gestor, run, 'D04D', 'Perno sin permiso');
    const place = await location(lector, `CQ-${run}`, `CQ${run}`);
    await receipt(lector, item.id, place.id, 'L-001', '10');
    countId = (await startCount(lector, [place.id])).id;
    await countAndSubmit(lector, countId, { [place.id]: '9' });

    // El operario ve el conteo pendiente, pero sin motivos ni «Confirmar ajuste».
    const operario = await login(browser, environment.accounts.lector, `/inventarios/${countId}`);
    await expect(operario.getByTestId('count-state')).toHaveText('Pendiente de aprobación');
    await expect(operario.getByTestId('difference-count')).toContainText('1 línea con diferencia');
    await expect(operario.getByRole('button', { name: 'Confirmar ajuste' })).toHaveCount(0);
    await expect(operario.getByRole('button', { name: 'Cancelar conteo' })).toHaveCount(0);
    await hold(operario, 3000);
    // Y el servidor lo rechaza aunque se llame directamente: 403.
    const current = await countLines(lector, countId);
    expect((await send(lector, 'POST', `/api/v1/cycle-counts/${countId}/confirm`, {}, current.etag)).status).toBe(403);

    // El responsable, con Inventory.Adjust, sí tiene la acción.
    const manager = await login(browser, environment.accounts.responsable, `/inventarios/${countId}`);
    await expect(manager.getByRole('button', { name: 'Confirmar ajuste' })).toBeVisible();
    await hold(manager, 2500);
    await operario.context().close();
    await manager.context().close();
  } finally {
    if (countId) await cancelIfOpen(responsable, countId);
    await closeSession(responsable);
  }
});
