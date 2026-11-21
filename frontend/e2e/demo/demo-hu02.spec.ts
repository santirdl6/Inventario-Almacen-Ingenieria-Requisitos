import { expect, hold, location, login, onlyInDemo, read, runId, send, test, type Session } from './demo-helpers';

// Demo HU-02 (Persona B): código con caracteres no válidos, retirada de la última zona de recepción activa y
// cuenta sin Locations.Manage. Complementa e2e/locations.spec.ts.
onlyInDemo();

type LocationRow = { id: string; code: string; aisle: string; rack: string; level: string; kind: string; isActive: boolean };

async function activeReceiving(session: Session): Promise<LocationRow[]> {
  const rows: LocationRow[] = [];
  for (let page = 1; ; page++) {
    const result = await read(session, `/api/v1/locations?kind=Receiving&status=active&page=${page}&pageSize=100`);
    rows.push(...(result.body['items'] as LocationRow[]));
    if (rows.length >= (result.body['totalCount'] as number) || !(result.body['items'] as unknown[]).length) return rows;
  }
}

async function setActive(session: Session, locationId: string, isActive: boolean): Promise<number> {
  const current = await read(session, `/api/v1/locations/${locationId}`);
  const { code, aisle, rack, level, kind } = current.body as unknown as LocationRow;
  return (await send(session, 'PUT', `/api/v1/locations/${locationId}`, { code, aisle, rack, level, kind, isActive }, current.etag)).status;
}

test('Código con caracteres no válidos: error de validación sin perder lo escrito', async ({ page, environment }) => {
  const run = runId();
  await page.goto('/ubicaciones/nueva');
  await page.getByLabel('Correo electrónico (obligatorio)').fill(environment.accounts.lector.email);
  await page.getByLabel('Contraseña (obligatoria)').fill(environment.accounts.lector.password);
  await page.getByLabel('Contraseña (obligatoria)').press('Enter');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Nueva ubicación');
  await page.getByLabel(/^Pasillo/).fill(`V${run}`);
  await page.getByLabel(/^Estantería/).fill('E01');
  await page.getByLabel(/^Nivel/).fill('N01');
  await page.getByLabel(/^Código/).fill(`AÑO 1/${run}`);
  await page.getByRole('button', { name: 'Crear ubicación', exact: true }).click();
  await expect(page.locator('#code-error')).toContainText('Solo letras A–Z sin acentos ni Ñ, dígitos y guion');
  await expect(page.getByLabel(/^Pasillo/)).toHaveValue(`V${run}`);
  await expect(page.locator('#code')).toBeFocused();
  await hold(page, 3000);
  // Con un código válido se guarda.
  await page.getByLabel(/^Código/).fill(`ANO-1-${run}`);
  await page.getByRole('button', { name: 'Crear ubicación', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Ubicación ANO-1-${run}`);
  await hold(page);
});

test('No se puede retirar la última zona de recepción activa', async ({ browser, lector, environment }) => {
  const run = runId();
  const zone = await location(lector, `REC-U${run}`, `RU${run}`, 'Receiving');
  // Se retiran por API las demás zonas de recepción activas de la base E2E; al final se reactivan.
  const others = (await activeReceiving(lector)).filter(row => row.id !== zone.id);
  const retired: string[] = [];
  try {
    for (const other of others) {
      const status = await setActive(lector, other.id, false);
      test.skip(status >= 300, `No se pudo retirar ${other.code} (${status}); escenario no preparable`);
      retired.push(other.id);
    }
    const page = await login(browser, environment.accounts.lector, `/ubicaciones/${zone.id}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Ubicación ${zone.code}`);
    await page.getByTestId('state-button').getByRole('button', { name: 'Desactivar' }).click();
    const dialog = page.getByRole('dialog', { name: 'Desactivar ubicación' });
    await expect(dialog.getByTestId('no-receiving')).toContainText('No se puede retirar la única zona de recepción activa');
    await expect(dialog.getByRole('button', { name: 'Desactivar' })).toHaveCount(0);
    await hold(page, 3500);
    await dialog.getByRole('button', { name: 'Cerrar' }).click();
    // El servidor también lo rechaza aunque se llame directamente: 409 NoActiveReceivingLeft.
    const current = await read(lector, `/api/v1/locations/${zone.id}`);
    const rejected = await send(lector, 'PUT', `/api/v1/locations/${zone.id}`, { ...zone.body, isActive: false }, current.etag);
    expect(rejected.status).toBe(409);
    expect(rejected.body['code']).toBe('NoActiveReceivingLeft');
    await page.reload();
    await expect(page.locator('dl.facts').first()).toContainText('Activa');
    await hold(page);
    await page.context().close();
  } finally {
    for (const other of retired) await setActive(lector, other, true);
  }
});

test('Permiso: sin Locations.Manage no se crea ni se retira una ubicación', async ({ browser, gestor, lector, environment }) => {
  const run = runId();
  const place = await location(lector, `PM-${run}`, `PM${run}`);
  expect(gestor.me.permissions).not.toContain('Locations.Manage');
  const page = await login(browser, environment.accounts.gestor, `/ubicaciones/${place.id}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Ubicación ${place.code}`);
  await expect(page.getByTestId('state-button').getByRole('button', { name: 'Desactivar' })).toHaveCount(0);
  await hold(page, 3000);
  await page.goto('/ubicaciones/nueva');
  await expect(page).toHaveURL('/sin-permiso');
  await hold(page, 2500);
  // El servidor lo rechaza aunque se llame directamente: 403.
  const denied = await send(gestor, 'POST', '/api/v1/locations', { code: `PX-${run}`, aisle: `PX${run}`, rack: 'E01', level: 'N01', kind: 'Storage', isActive: true });
  expect(denied.status).toBe(403);
  expect(await setActive(gestor, place.id, false)).toBe(403);
  await page.context().close();
});
