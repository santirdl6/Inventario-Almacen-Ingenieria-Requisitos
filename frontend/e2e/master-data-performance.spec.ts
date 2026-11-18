import { randomUUID } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import type { APIRequestContext, BrowserContext } from '@playwright/test';
import { test, expect } from './fixtures/master-data.fixture';

// La traza de las 10.000 altas y 25 navegadores añade carga ajena a la búsqueda medida.
// Conservar las 200 muestras y errores en el informe, tanto si pasa como si falla.
test.use({ trace: 'off' });

test('SC-005: 10.000 artículos auditados, 25 sesiones y 200 búsquedas hasta mostrar resultados', async ({ browser, playwright, environment, gestor }, testInfo) => {
  test.setTimeout(600_000);
  const run = randomUUID().replaceAll('-', '').toUpperCase();
  const headers = { 'X-XSRF-TOKEN': gestor.csrfToken, 'Idempotency-Key': randomUUID() };
  const family = await gestor.request.post('/api/v1/families', { headers, data: { code: `PERF-${run}`, name: 'Familia de rendimiento' } });
  expect(family.status()).toBe(201); const familyId = (await family.json()).resource.id as string;
  const sku = (index: number) => `PERF-${run}-${String(index).padStart(5, '0')}`;
  const name = (index: number) => `Rendimiento ${run} artículo ${String(index).padStart(5, '0')}`;
  // Datos de preparación mediante las mismas altas autenticadas y auditadas; ningún DML de stock.
  const prepareStart = performance.now();
  await Promise.all(Array.from({ length: 25 }, async (_, lane) => {
    for (let index = lane; index < 10_000; index += 25) {
      const body = { sku: sku(index), name: name(index), familyId, baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true };
      const created = await gestor.request.post('/api/v1/articles', { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: body });
      expect(created.status(), `Alta auditada ${index}`).toBe(201);
      if (index < 50) {
        const id = (await created.json()).resource.id;
        const inactive = await gestor.request.put(`/api/v1/articles/${id}`, { headers: { ...headers, 'Idempotency-Key': randomUUID(), 'If-Match': created.headers()['etag'] }, data: { ...body, isActive: false } });
        expect(inactive.status()).toBe(200);
      }
      await created.dispose();
    }
  }));
  const prepared = await gestor.request.get(`/api/v1/articles?q=${run}&state=all&familyId=${familyId}`);
  expect((await prepared.json()).totalCount).toBe(10_000);
  const prepareMs = performance.now() - prepareStart;
  const requests: APIRequestContext[] = []; const contexts: BrowserContext[] = [];
  const samples: { session: number; query: number; elapsedMs: number; success: boolean; error?: string }[] = [];
  try {
    const pages = await Promise.all(Array.from({ length: 25 }, async () => {
      const request = await playwright.request.newContext({ baseURL: environment.baseURL }); requests.push(request);
      const token = (await (await request.get('/api/v1/auth/csrf')).json()).requestToken as string;
      const authenticated = await request.post('/api/v1/auth/login', { headers: { 'X-XSRF-TOKEN': token }, data: { email: environment.accounts.lector.email, password: environment.accounts.lector.password } });
      expect(authenticated.status()).toBe(200);
      const context = await browser.newContext({ storageState: await request.storageState() }); contexts.push(context);
      const page = await context.newPage(); await page.goto(`${environment.baseURL}/maestros/articulos`);
      await expect(page.getByRole('button', { name: 'Buscar artículos', exact: true })).toBeEnabled();
      // Preparar la familia buscable antes de medir; la medición empieza al confirmar cada búsqueda.
      await page.getByLabel('Buscar familia del filtro', { exact: true }).fill(`PERF-${run}`);
      await page.getByRole('button', { name: 'Buscar familias del filtro', exact: true }).click();
      await expect(page.getByLabel('Familia del artículo', { exact: true })).toContainText(`PERF-${run}`);
      await page.getByLabel('Familia del artículo', { exact: true }).selectOption(familyId);
      return page;
    }));
    // Las 25 sesiones ejecutan ocho búsquedas cada una, con solapamiento real de navegador y API.
    await Promise.all(pages.map(async (page, session) => {
      for (let query = 0; query < 8; query++) {
        const kind = query % 4; const index = 100 + session * 8 + query;
        await page.getByLabel('Código o nombre del artículo').fill(kind === 0 ? sku(index) : kind === 1 ? name(index) : run);
        await page.getByLabel('Estado del artículo', { exact: true }).selectOption(kind === 3 ? 'inactive' : kind === 2 ? 'all' : 'active');
        const started = performance.now();
        try {
          const response = page.waitForResponse(value => new URL(value.url()).pathname === '/api/v1/articles' && value.request().method() === 'GET');
          await page.getByRole('button', { name: 'Buscar artículos', exact: true }).click();
          const result = await response; expect(result.status()).toBe(200);
          await expect(page.getByRole('button', { name: 'Buscar artículos', exact: true })).toBeEnabled();
          const rows = page.getByRole('row'); await expect(rows.nth(1)).toBeVisible();
          if (kind < 2) await expect(rows.nth(1)).toContainText(sku(index));
          if (kind === 3) await expect(rows.nth(1)).toContainText('Inactivo');
          samples.push({ session, query, elapsedMs: performance.now() - started, success: true });
        } catch (error) { samples.push({ session, query, elapsedMs: performance.now() - started, success: false, error: String(error) }); }
      }
    }));
  } finally {
    await Promise.all(contexts.map(context => context.close())); await Promise.all(requests.map(request => request.dispose()));
    const sorted = samples.map(sample => sample.elapsedMs).sort((a, b) => a - b);
    const report = { articles: 10_000, sessions: 25, samples: samples.length, failures: samples.filter(sample => !sample.success).length,
      p95Ms: sorted[Math.ceil(sorted.length * .95) - 1] ?? null, within2s: samples.filter(sample => sample.success && sample.elapsedMs <= 2000).length,
      prepareMs, platform: process.platform, arch: process.arch, node: process.version, measurements: samples };
    console.info('HU08_PERFORMANCE', JSON.stringify({ ...report, measurements: undefined }));
    const reportPath = testInfo.outputPath('hu08-performance.json');
    await writeFile(reportPath, JSON.stringify(report, null, 2));
    await testInfo.attach('hu08-performance.json', { path: reportPath, contentType: 'application/json' });
  }
  expect(samples).toHaveLength(200); expect(samples.filter(sample => !sample.success)).toHaveLength(0);
  expect(samples.filter(sample => sample.success && sample.elapsedMs <= 2000).length).toBeGreaterThanOrEqual(190);
});
