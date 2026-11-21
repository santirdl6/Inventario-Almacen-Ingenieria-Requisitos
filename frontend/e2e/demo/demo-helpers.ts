import type { APIRequestContext, Browser, Page, TestInfo } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { basename } from 'node:path';
import { test, expect, type Session } from '../fixtures/master-data.fixture';

// Ayudas de la demo de Persona B (HU-02, HU-05, HU-03, HU-04). Reutilizan el fixture de A y la siembra
// D-17 de los E2E existentes; los conteos, pedidos y salidas que solo preparan un escenario se crean por
// API para que el vídeo muestre lo que se demuestra. Solo para frontend/playwright.demo.config.ts.

export { test, expect };
export type { Session };

type Account = { email: string; password: string };
export type Reply = { status: number; body: Record<string, unknown>; etag: string };

/** Los specs de e2e/demo solo se ejecutan con playwright.demo.config.ts (outputDir test-results/demo). */
export function onlyInDemo(): void {
  test.beforeEach(({}, testInfo: TestInfo) => {
    test.skip(basename(testInfo.project.outputDir) !== 'demo', 'Escenario de la demo: usa playwright.demo.config.ts');
  });
}

export const runId = (): string => Date.now().toString(36).toUpperCase().slice(-6);
export const id = (reply: Reply): string => {
  const holder = [reply.body['resource'], reply.body['count'], reply.body['shipment'], reply.body]
    .find(value => typeof (value as { id?: unknown } | undefined)?.id === 'string') as { id: string } | undefined;
  expect(holder, `id en ${JSON.stringify(reply.body)}`).toBeTruthy();
  return holder!.id;
};

/** Petición con CSRF, clave de idempotencia e If-Match opcional; devuelve estado, cuerpo y ETag sin exigir éxito. */
export async function send(session: Session, method: 'POST' | 'PUT', path: string, data: unknown, ifMatch?: string): Promise<Reply> {
  const headers: Record<string, string> = { 'X-XSRF-TOKEN': session.csrfToken, 'Idempotency-Key': randomUUID() };
  if (ifMatch) headers['If-Match'] = ifMatch;
  const response = await session.request.fetch(path, { method, headers, data });
  const text = await response.text();
  return { status: response.status(), body: text ? JSON.parse(text) as Record<string, unknown> : {}, etag: response.headers()['etag'] ?? '' };
}

/** Como send, pero exige 2xx. */
export async function ok(session: Session, method: 'POST' | 'PUT', path: string, data: unknown, ifMatch?: string): Promise<Reply> {
  const reply = await send(session, method, path, data, ifMatch);
  expect(reply.status, `${method} ${path}: ${JSON.stringify(reply.body)}`).toBeLessThan(300);
  return reply;
}

export async function read(session: Session, path: string): Promise<Reply> {
  const response = await session.request.get(path);
  expect(response.status(), `GET ${path}`).toBe(200);
  return { status: 200, body: await response.json() as Record<string, unknown>, etag: response.headers()['etag'] ?? '' };
}

/** Sesión API de una cuenta sin fixture propio (la del responsable). Cerrar con closeSession. */
export async function apiSession(factory: { newContext(options: { baseURL: string }): Promise<APIRequestContext> },
  baseURL: string, account: Account): Promise<Session> {
  const request = await factory.newContext({ baseURL });
  const csrf = await (await request.get('/api/v1/auth/csrf')).json() as { requestToken: string };
  const login = await request.post('/api/v1/auth/login', { headers: { 'X-XSRF-TOKEN': csrf.requestToken }, data: account });
  expect(login.status(), 'Inicio de sesión API de la demo').toBe(200);
  const token = (await login.json() as { requestToken: string }).requestToken;
  const me = await (await request.get('/api/v1/auth/me')).json() as Session['me'];
  return { request, csrfToken: token, me };
}

export async function closeSession(session: Session): Promise<void> {
  await session.request.post('/api/v1/auth/logout', { headers: { 'X-XSRF-TOKEN': session.csrfToken }, data: {} }).catch(() => undefined);
  await session.request.dispose();
}

export async function login(browser: Browser, account: Account, target: string): Promise<Page> {
  const page = await (await browser.newContext()).newPage();
  await page.goto(target);
  await expect(page).toHaveURL(/\/login\?returnUrl=/);
  await page.getByLabel('Correo electrónico (obligatorio)').fill(account.email);
  await page.getByLabel('Contraseña (obligatoria)').fill(account.password);
  await page.getByLabel('Contraseña (obligatoria)').press('Enter');
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
  return page;
}

/** Pausa visible en el vídeo para que se lea un mensaje (no sustituye a ninguna espera de estado). */
export const hold = (page: Page, ms = 1500): Promise<void> => page.waitForTimeout(ms);

export type Article = { id: string; sku: string; name: string; body: Record<string, unknown> };

/** Familia y artículo con lote (cuenta gestor), como en los E2E de HU-05/HU-03/HU-04. */
export async function article(gestor: Session, run: string, prefix: string, name: string): Promise<Article> {
  const family = await ok(gestor, 'POST', '/api/v1/families', { code: `F${prefix}-${run}`, name: `Familia ${name}` });
  const sku = `${prefix}-${run}`;
  const body = { sku, name, familyId: id(family), baseUnit: 'UD', allowsFractional: false, tracksLot: true, isActive: true };
  return { id: id(await ok(gestor, 'POST', '/api/v1/articles', body)), sku, name, body };
}

export type Location = { id: string; code: string; body: Record<string, unknown> };

export async function location(lector: Session, code: string, aisle: string, kind: 'Storage' | 'Receiving' = 'Storage'): Promise<Location> {
  const body = { code, aisle, rack: 'E01', level: 'N01', kind, isActive: true };
  return { id: id(await ok(lector, 'POST', '/api/v1/locations', body)), code, body };
}

/** Siembra D-17 (Receiving.Record). */
export const receipt = (lector: Session, articleId: string, locationId: string, lotCode: string, quantity: string): Promise<Reply> =>
  send(lector, 'POST', '/api/v1/testing/stock-receipts', { articleId, locationId, lotCode, quantity });

export async function order(lector: Session, reference: string, articleId: string, quantity: string): Promise<{ id: string; lineId: string }> {
  const created = await ok(lector, 'POST', '/api/v1/customer-orders', {
    externalReference: reference, customerReference: 'Cliente Demo', lines: [{ articleId, quantity }]
  });
  const orderId = id(created);
  const detail = await read(lector, `/api/v1/customer-orders/${orderId}`);
  return { id: orderId, lineId: (detail.body['lines'] as { id: string }[])[0].id };
}

/** Lote de una ubicación según GET /stock. */
export async function lotId(session: Session, articleId: string, locationId: string, lotCode: string): Promise<string> {
  const stock = await read(session, `/api/v1/stock?articleId=${articleId}&locationId=${locationId}`);
  const row = (stock.body['items'] as { lot: { id: string; code: string } }[]).find(item => item.lot.code === lotCode);
  expect(row, `lote ${lotCode}`).toBeTruthy();
  return row!.lot.id;
}

export async function reserve(responsable: Session, orderId: string, lineId: string, locationId: string, lot: string, quantity: string): Promise<Reply> {
  const current = await read(responsable, `/api/v1/customer-orders/${orderId}`);
  return send(responsable, 'POST', '/api/v1/reservations', {
    customerOrderLineId: lineId, allocations: [{ locationId, lotId: lot, quantity }]
  }, current.etag);
}

/** Salida confirmada por API (Shipping.Record), para preparar escenarios de HU-04. */
export async function shipment(lector: Session, orderId: string, lineId: string, locationId: string, lot: string, quantity: string): Promise<string> {
  const draft = await ok(lector, 'POST', '/api/v1/shipments', {
    customerOrderId: orderId, occurredAt: new Date().toISOString(), lines: [{ customerOrderLineId: lineId, locationId, lotId: lot, quantity }]
  });
  const shipmentId = id(draft);
  const current = await read(lector, `/api/v1/shipments/${shipmentId}`);
  const context = await read(lector, `/api/v1/shipments/${shipmentId}/context`);
  await ok(lector, 'POST', `/api/v1/shipments/${shipmentId}/confirm`, { orderVersion: context.body['orderVersion'] }, current.etag);
  return shipmentId;
}

/** Conteo creado e iniciado por API (Inventory.Count): las ubicaciones quedan congeladas. */
export async function startCount(lector: Session, locationIds: string[]): Promise<{ id: string; code: string }> {
  const created = await ok(lector, 'POST', '/api/v1/cycle-counts', { locationIds });
  const countId = id(created);
  const draft = await read(lector, `/api/v1/cycle-counts/${countId}`);
  await ok(lector, 'POST', `/api/v1/cycle-counts/${countId}/start`, {}, draft.etag);
  const started = await read(lector, `/api/v1/cycle-counts/${countId}`);
  return { id: countId, code: started.body['countCode'] as string };
}

export type CountLine = { id: string; locationId: string; lotCode: string; theoreticalPhysical: string };

export async function countLines(session: Session, countId: string): Promise<{ lines: CountLine[]; etag: string; state: string }> {
  const detail = await read(session, `/api/v1/cycle-counts/${countId}`);
  return { lines: detail.body['lines'] as CountLine[], etag: detail.etag, state: detail.body['state'] as string };
}

/** Registra lo contado por ubicación y envía a aprobación (por API). */
export async function countAndSubmit(lector: Session, countId: string, counted: Record<string, string>): Promise<void> {
  const before = await countLines(lector, countId);
  await ok(lector, 'PUT', `/api/v1/cycle-counts/${countId}/lines`, {
    counted: before.lines.map(line => ({ lineId: line.id, countedPhysical: counted[line.locationId] ?? line.theoreticalPhysical })), found: []
  }, before.etag);
  const after = await countLines(lector, countId);
  await ok(lector, 'POST', `/api/v1/cycle-counts/${countId}/submit`, {}, after.etag);
}

/** Cancela un conteo que siga abierto (limpieza, como M1 del E2E-03). */
export async function cancelIfOpen(responsable: Session, countId: string): Promise<void> {
  const response = await responsable.request.get(`/api/v1/cycle-counts/${countId}`);
  if (!response.ok()) return;
  const state = (await response.json() as { state: string }).state;
  if (['Draft', 'Counting', 'AwaitingApproval'].includes(state))
    await send(responsable, 'POST', `/api/v1/cycle-counts/${countId}/cancel`, { reason: 'Limpieza de la demo' }, response.headers()['etag'] ?? '');
}

export async function openStock(page: Page, sku: string): Promise<void> {
  await page.goto('/existencias');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Existencias');
  await page.getByLabel('Artículo (SKU o nombre)').fill(sku);
  await page.getByLabel('Artículo (SKU o nombre)').press('Enter');
  await page.getByRole('button', { name: new RegExp(`^${sku} ·`) }).click();
  await expect(page.getByTestId('totals')).toContainText('Totales de todo el filtro');
}
