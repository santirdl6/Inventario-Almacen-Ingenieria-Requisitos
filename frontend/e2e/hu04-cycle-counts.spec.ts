import type { APIRequestContext, Browser, Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { test, expect, type Session } from './fixtures/master-data.fixture';

// HU-04 T017 · E2E-03 (specs/006-realizar-inventarios-ciclicos/quickstart.md): el operario crea, inicia y
// cuenta un conteo con una diferencia (10 → 8); el responsable revisa con motivo y confirma; las
// existencias quedan en 8 y la ubicación sale del conteo. Datos únicos por ejecución y existencias de la
// siembra D-17 de HU-05. Sin cambios en el fixture de A: cuentas lector (Operario) y responsable.
// M1 (analyze-1): si falla a mitad, el bloque finally cancela por API su conteo abierto para no dejar
// la ubicación congelada.

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

/** M1: cancela el conteo si quedó abierto (con la cuenta del responsable, que tiene Inventory.Adjust). */
async function cancelIfOpen(request: APIRequestContext, email: string, password: string, countId: string): Promise<void> {
  const csrf = await (await request.get('/api/v1/auth/csrf')).json() as { requestToken: string };
  const signedIn = await request.post('/api/v1/auth/login', { headers: { 'X-XSRF-TOKEN': csrf.requestToken }, data: { email, password } });
  if (signedIn.status() !== 200) return;
  const token = (await signedIn.json() as { requestToken: string }).requestToken;
  const detail = await request.get(`/api/v1/cycle-counts/${countId}`);
  const state = detail.ok() ? (await detail.json() as { state: string }).state : null;
  if (state === 'Draft' || state === 'Counting' || state === 'AwaitingApproval') {
    await request.post(`/api/v1/cycle-counts/${countId}/cancel`, {
      headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': randomUUID(), 'If-Match': detail.headers()['etag'] ?? '' },
      data: { reason: 'Limpieza del E2E-03 tras un fallo' }
    });
  }
  await request.post('/api/v1/auth/logout', { headers: { 'X-XSRF-TOKEN': token }, data: {} });
}

test('E2E-03: el operario cuenta una diferencia y el responsable confirma el ajuste', async ({ browser, gestor, lector, environment, playwright }) => {
  const run = Date.now().toString(36).toUpperCase().slice(-6);
  const sku = `E2E04-${run}`;
  const code = `C01-${run}`;
  const responsable = environment.accounts.responsable;
  let countId: string | null = null;
  const pages: Page[] = [];

  try {
    // 1. Preparación por API: artículo con lote, ubicación y 10 de L-001 sembradas.
    const family = await post(gestor, '/api/v1/families', { code: `F04-${run}`, name: 'Familia E2E-03' });
    const article = await post(gestor, '/api/v1/articles', {
      sku, name: 'Tornillo E2E-03', familyId: (family['resource'] as { id: string }).id, baseUnit: 'UD',
      allowsFractional: false, tracksLot: true, isActive: true
    });
    const articleId = (article['resource'] as { id: string }).id;
    const location = await post(lector, '/api/v1/locations', {
      code, aisle: `P${run}`, rack: 'E01', level: 'N01', kind: 'Storage', isActive: true
    });
    const locationId = (location['resource'] as { id: string }).id;
    await post(lector, '/api/v1/testing/stock-receipts', { articleId, locationId, lotCode: 'L-001', quantity: '10' });

    // 2. El operario crea el borrador con la ubicación y lo inicia (aviso de congelación visible).
    const operario = await login(browser, environment.accounts.lector.email, environment.accounts.lector.password, '/inventarios/nuevo');
    pages.push(operario);
    await expect(operario.getByRole('heading', { level: 1 })).toHaveText('Nuevo conteo');
    await operario.getByLabel('Buscar ubicación').fill(code);
    await operario.getByLabel(code, { exact: true }).check();
    await operario.getByRole('button', { name: 'Guardar borrador' }).click();
    await expect(operario).toHaveURL(/\/inventarios\/[0-9a-f-]{36}$/);
    countId = new URL(operario.url()).pathname.split('/').pop()!;
    await expect(operario.getByTestId('count-state')).toHaveText('Borrador');
    await expect(operario.getByTestId('freeze-notice')).toContainText(`${code} quedarán congeladas`);
    await operario.getByRole('button', { name: 'Iniciar conteo' }).click();
    await expect(operario.getByTestId('count-state')).toHaveText('En conteo');
    const inCount = await lector.request.get(`/api/v1/locations/${locationId}`);
    expect((await inCount.json() as { inOpenCount: boolean }).inOpenCount).toBe(true);

    // 3. Cuenta 8 donde había 10: la diferencia se dice en texto; guarda y envía a aprobación.
    const counted = operario.getByLabel(`Contado de ${sku} en ${code}`);
    await expect(counted).toHaveValue('');
    await counted.fill('8');
    await expect(operario.getByRole('cell', { name: 'Faltan 2 UD' })).toBeVisible();
    await operario.getByRole('button', { name: 'Guardar', exact: true }).click();
    await expect(operario.getByRole('status').filter({ hasText: 'Conteo guardado.' })).toBeVisible();
    await operario.getByRole('button', { name: 'Enviar a aprobación' }).click();
    await expect(operario.getByTestId('count-state')).toHaveText('Pendiente de aprobación');

    // 4. El responsable revisa con motivo y confirma: un ajuste con su operación visible.
    const manager = await login(browser, responsable.email, responsable.password, `/inventarios/${countId}`);
    pages.push(manager);
    await expect(manager.getByTestId('count-state')).toHaveText('Pendiente de aprobación');
    await expect(manager.getByRole('table', { name: 'Revisión del conteo' })).toContainText('Faltan 2 UD');
    await manager.getByLabel('Motivo').fill('Rotura en el almacén');
    await manager.getByRole('button', { name: 'Confirmar ajuste' }).click();
    const review = manager.getByRole('dialog', { name: /Revisa el ajuste del conteo CC-/ });
    await expect(review).toContainText('Faltan 2 UD');
    await expect(review).toContainText('Rotura en el almacén');
    await review.getByRole('button', { name: 'Confirmar ajuste' }).click();
    await expect(manager.getByRole('status').filter({ hasText: 'Ajuste registrado. Operación' })).toBeVisible();
    await expect(manager.getByTestId('count-state')).toHaveText('Confirmado');
    await expect(manager.getByTestId('closure')).toContainText('Movimiento de ajuste');

    // 5. Existencias ajustadas a 8 y la ubicación fuera de conteo.
    await manager.goto('/existencias');
    await manager.getByLabel('Artículo (SKU o nombre)').fill(sku);
    await manager.getByLabel('Artículo (SKU o nombre)').press('Enter');
    await manager.getByRole('button', { name: new RegExp(`^${sku} ·`) }).click();
    await expect(manager.getByTestId('totals')).toContainText('Físico 8.000 UD');
    const released = await lector.request.get(`/api/v1/locations/${locationId}`);
    expect((await released.json() as { inOpenCount: boolean }).inOpenCount).toBe(false);
  } finally {
    for (const page of pages) await page.context().close();
    if (countId) {
      const request = await playwright.request.newContext({ baseURL: environment.baseURL });
      try { await cancelIfOpen(request, responsable.email, responsable.password, countId); }
      finally { await request.dispose(); }
    }
  }
});
