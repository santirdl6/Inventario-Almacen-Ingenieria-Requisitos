import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { mkdir, writeFile } from 'node:fs/promises';
import { platform, arch, totalmem, cpus } from 'node:os';
import { performance } from 'node:perf_hooks';
import { prepareRoute } from './hu07-browser-profile.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(resolve(root, 'frontend/package.json'));
const { chromium } = require('@playwright/test');
// A new project on every run prevents accidental reuse of a partially seeded database.
const project = `inventario-hu07-e2e-performance-${randomUUID().replaceAll('-', '')}`;
const output = resolve(root, 'frontend/test-results/hu07-performance', project);
await mkdir(output, { recursive: true });
const env = { ...process.env, DB_NAME: 'InventoryHu07Performance', APP_BIND_ADDRESS: '127.0.0.1', APP_PORT: '8086', MAILPIT_PORT: '8028',
  SMTP_HOST: 'smtp', SMTP_PORT: '1025', SMTP_FROM: 'inventario@performance.test', SMTP_SECURITY: 'None', SMTP_USERNAME: '', SMTP_PASSWORD: '',
  PURCHASING_CURRENCY: 'EUR', PURCHASE_DELIVERY_ENABLED: 'false', HU07_PERFORMANCE: '7007',
  SQL_APP_USER: 'performance_app', SQL_APP_PASSWORD: `Perf_${randomUUID()}!aA9`, SQL_SA_PASSWORD: `Perf_${randomUUID()}!aA9` };
const compose = (args, options = {}) => execFileSync('docker', ['compose', '--project-name', project, '--file', resolve(root, 'docker-compose.yml'),
  '--file', resolve(root, 'scripts/hu07-performance.compose.yml'), ...args],
  { cwd: root, env, stdio: 'inherit', timeout: 3_900_000, maxBuffer: 32 * 1024 * 1024, ...options });
function fixture(mode = '') {
  const prepared = compose(['--profile', 'test', 'run', '--no-deps', '--rm', '--volume', `${root}:/workspace`, '--workdir', '/workspace',
    '--env', 'HU07_PERFORMANCE', '--env', 'SQL_APP_USER', '--env', 'SQL_APP_PASSWORD', '--entrypoint', 'sh', 'tests', '-c',
    `dotnet run --file scripts/prepare-hu07-performance.cs -- ${project} ${mode}`], { stdio: ['ignore', 'pipe', 'inherit'], encoding: 'utf8' });
  const line = prepared.split('\n').find(value => value.startsWith('HU07_PERFORMANCE_JSON:'));
  if (!line) throw new Error('No se recibió el manifiesto de preparación.');
  return JSON.parse(line.slice('HU07_PERFORMANCE_JSON:'.length));
}
const samples = []; let browser; let profile; let fatal; let started = false;
const report = { profile: 'SC-007', seed: 7007, project, startedAt: new Date().toISOString(), result: 'INCONCLUSIVE',
  host: { platform: platform(), arch: arch(), memoryBytes: totalmem(), cpu: cpus()[0]?.model, cpuCount: cpus().length, node: process.version },
  protocol: { sessions: 25, articles: 10_000, orders: 10_000, history: 1_000_000, warmupSeconds: 120, measuredSeconds: 600, minimumPerOperation: 1_000,
    mix: '50/25/25', boundary: 'Node monotonic clock: UI action to matching rendered result', pageSize: 25, pageRotation: 'first/middle/final', sizes: [1, 10, 20], smtp: 'disabled', conflicts: 'not induced; reported as errors', efCommandLogLevel: 'Warning' } };
const rng = seed => { let state = seed; return max => { state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state % max; }; };
const isList = response => response.request().method() === 'GET' && new URL(response.url()).pathname === '/api/v1/purchase-orders';
async function listReady(page, body) {
  // Wait for the matching response's rows AND for loading to finish, never accept the old table.
  await page.waitForFunction(body => {
    const rows = [...document.querySelectorAll('table tbody th a')].map(link => link.textContent.trim());
    return !document.querySelector('form fieldset')?.disabled
      && rows.length === body.items.length && rows.every((number, index) => number === body.items[index].number)
      && [...document.querySelectorAll('p')].some(p => p.textContent === `${body.totalCount} pedidos coincidentes. Página ${body.page}.`);
  }, body);
}
async function metric(page, operation, action, predicate, start, detail) {
  if (performance.now() >= start.end) return;
  const clock = performance.now(); const measured = clock >= start.measure;
  let waiting; let status; let problemCode; let traceId;
  try {
    waiting = page.waitForResponse(predicate, { timeout: 30_000 });
    // Attach rejection immediately, including when action fails before awaiting the response.
    waiting.catch(() => {});
    await action(); const actionMilliseconds = performance.now() - clock; const response = await waiting;
    status = response.status();
    if (!response.ok()) {
      const problem = await response.json().catch(() => ({})); problemCode = problem.code; traceId = problem.traceId;
      throw new Error(`HTTP ${status}`);
    }
    const body = await response.json(); await response.finished();
    const responseMilliseconds = performance.now() - clock;
    if (operation === 'query') await listReady(page, body);
    else {
      await page.waitForURL(url => url.pathname === `/compras/${body.resource.id}`);
      await page.getByRole('heading', { name: body.resource.number, exact: true }).waitFor();
    }
    await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
    const sample = { ...detail, operation, ok: true, measured, milliseconds: performance.now() - clock,
      actionMilliseconds, responseMilliseconds, renderMilliseconds: performance.now() - clock - responseMilliseconds,
      httpMilliseconds: response.request().timing().responseEnd };
    samples.push(sample);
  } catch (error) {
    samples.push({ ...detail, operation, ok: false, measured, milliseconds: performance.now() - clock, status, problemCode, traceId, error: error.message });
  }
}

async function lane(index, start) {
  const random = rng(7007 + index); const account = profile.buyers[index];
  const context = await browser.newContext({ baseURL: 'http://localhost:8086' });
  try {
    const csrf = await context.request.get('/api/v1/auth/csrf'); const anonymous = await csrf.json();
    const login = await context.request.post('/api/v1/auth/login', { headers: { 'X-XSRF-TOKEN': anonymous.requestToken }, data: { email: account.email, password: account.password } });
    if (!login.ok()) throw new Error('No se pudo autenticar una sesión de carga.');
    const page = await context.newPage(); page.setDefaultTimeout(30_000);
    await page.goto('/compras'); await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).waitFor({ state: 'visible' });
    await page.waitForFunction(() => !document.querySelector('form fieldset')?.disabled);
    await start.ready();
    let iteration = 0;
    while (performance.now() < start.end) {
      const current = iteration++; const kind = current % 8; const cycle = Math.floor(current / 8);
      const operation = kind < 4 ? 'query' : kind < 6 ? 'create' : 'edit';
      const detail = { lane: index, iteration: current };
      try {
        if (kind < 4) {
          const params = new URLSearchParams();
          if (kind === 1) params.set('supplierId', profile.suppliers[random(profile.suppliers.length)].id);
          if (kind === 2) params.set('q', profile.orders[random(profile.orders.length)].number.slice(0, -2));
          if (kind === 3) { params.set('state', 'Draft'); params.set('from', report.filters.from); params.set('to', report.filters.to); }
          const rotation = cycle % 3;
          let target = 1;
          if (rotation === 1) {
            // Only the middle page needs a separate count to choose its preceding page.
            const response = await context.request.get(`/api/v1/purchase-orders?${params}`);
            try {
              if (!response.ok()) throw new Error(`Preparación de filtro HTTP ${response.status()}`);
              target = Math.max(1, Math.ceil(Math.ceil((await response.json()).totalCount / 25) / 2));
            } finally { await response.dispose(); }
            if (target > 2) params.set('page', String(target - 1));
          }
          await prepareRoute(page, `/compras${params.size ? '?' + params : ''}`, isList);
          await page.getByRole('button', { name: 'Buscar pedidos', exact: true }).waitFor();
          await page.waitForFunction(() => !document.querySelector('form fieldset')?.disabled);
          const last = page.getByRole('button', { name: 'Última página válida', exact: true });
          const useLast = rotation === 2 && await last.isEnabled();
          if (useLast) {
            const summary = await page.getByText(/^\d+ pedidos coincidentes/).textContent();
            target = Math.ceil(Number(summary.split(' ')[0]) / 25);
          }
          detail.filterKind = kind; detail.targetPage = target;
          const trigger = useLast ? last : page.getByRole('button', { name: target > 1 ? 'Página siguiente' : 'Buscar pedidos', exact: true });
          await trigger.scrollIntoViewIfNeeded();
          await metric(page, operation, () => trigger.click(),
            response => isList(response) && Number(new URL(response.url()).searchParams.get('page') ?? 1) === target, start, detail);
        } else {
          const creating = operation === 'create'; const sizeIndex = (cycle * 4 + kind - 4) % 3; const count = [1, 10, 20][sizeIndex];
          const slot = [0, 160, 280][sizeIndex] + random([160, 120, 120][sizeIndex]);
          const order = profile.orders[index + 25 * slot];
          detail.lineCount = count;
          await prepareRoute(page, creating ? '/compras/nuevo' : `/compras/${order.id}/editar`, response =>
            response.request().method() === 'GET' && new URL(response.url()).pathname === (creating ? '/api/v1/purchase-orders/settings' : `/api/v1/purchase-orders/${order.id}`));
          await page.getByRole('button', { name: 'Buscar proveedores', exact: true }).waitFor();
          if (creating) {
            const supplier = profile.suppliers[random(profile.suppliers.length)];
            await page.getByLabel('Buscar proveedor', { exact: true }).fill(supplier.code);
            await page.getByRole('button', { name: 'Buscar proveedores', exact: true }).click();
            await page.getByLabel('Proveedor (obligatorio)', { exact: true }).selectOption(supplier.id);
            const group = random(100) * 100;
            await page.getByLabel('Buscar artículo por', { exact: true }).selectOption('skuPrefix');
            await page.getByLabel('Buscar artículo', { exact: true }).fill(profile.articles[group].sku.slice(0, -2));
            await page.getByRole('button', { name: 'Buscar artículos', exact: true }).click();
            await page.locator('#line-0-article option').filter({ hasText: profile.articles[group].sku }).waitFor({ state: 'attached' });
            await page.getByRole('button', { name: 'Añadir línea', exact: true }).evaluate((button, count) => {
              for (let line = 1; line < count; line++) button.click();
            }, count);
            await page.locator(`#line-${count - 1}-article`).waitFor();
            await page.evaluate(({ ids, count }) => {
              const set = (id, value, event = 'input') => { const input = document.getElementById(id); input.value = value; input.dispatchEvent(new Event(event, { bubbles: true })); };
              set('purchase-date', '2026-12-01');
              for (let i = 0; i < count; i++) { set(`line-${i}-article`, ids[i], 'change'); set(`line-${i}-quantity`, '10'); set(`line-${i}-price`, '12.50'); }
            }, { ids: profile.articles.slice(group, group + count).map(article => article.id), count });
          } else {
            await page.locator(`#line-${count - 1}-quantity`).waitFor();
            await page.evaluate(count => { for (let i = 0; i < count; i++) { const input = document.getElementById(`line-${i}-quantity`); input.value = /^11(?:\.0+)?$/.test(input.value) ? '10' : '11'; input.dispatchEvent(new Event('input', { bubbles: true })); } }, count);
          }
          // Form preparation is outside the measurement; wait until Guardar is actually enabled.
          await page.waitForFunction(() => document.querySelector('form button[type="submit"]')?.disabled === false);
          const trigger = page.getByRole('button', { name: 'Guardar pedido', exact: true });
          await trigger.scrollIntoViewIfNeeded();
          await metric(page, operation, () => trigger.click(),
            response => response.request().method() === (creating ? 'POST' : 'PUT') && new URL(response.url()).pathname === (creating ? '/api/v1/purchase-orders' : `/api/v1/purchase-orders/${order.id}`), start, detail);
        }
      } catch (error) {
        samples.push({ ...detail, operation, ok: false, measured: performance.now() >= start.measure, stage: 'preparation', error: error.message });
      }
    }
  } catch (error) { start.abort(error); throw error; }
  finally { await context.close(); }
}

try {
  report.docker = execFileSync('docker', ['info', '--format', '{{json .}}'], { encoding: 'utf8' });
  report.image = execFileSync('docker', ['image', 'inspect', 'inventario-app:local', '--format', '{{.Id}} {{.Architecture}}'], { encoding: 'utf8' }).trim();
  started = true;
  compose(['up', '--no-build', '-d', '--wait', '--wait-timeout', '300']);
  const ids = compose(['ps', '--quiet', 'app', 'sqlserver'], { stdio: 'pipe', encoding: 'utf8' }).trim().split('\n');
  report.containers = JSON.parse(execFileSync('docker', ['inspect', ...ids, '--format', '{{json .}}'], { encoding: 'utf8' })
    .trim().split('\n').map(line => { const value = JSON.parse(line); return JSON.stringify({ name: value.Name, image: value.Image, platform: value.Platform,
      memoryLimit: value.HostConfig.Memory, nanoCpus: value.HostConfig.NanoCpus }); }).join(',').replace(/^/, '[').replace(/$/, ']'));
  console.log(`SC-007 ${project}: preparando datos mediante comandos auditados.`);
  profile = fixture();
  await writeFile(resolve(output, 'private-manifest.json'), JSON.stringify(profile), { mode: 0o600 });
  report.beforeWarmup = profile.counts;
  report.filters = { from: report.startedAt.slice(0, 16) + ':00Z', to: new Date(Math.ceil(Date.now() / 60_000) * 60_000).toISOString() };
  browser = await chromium.launch(process.env.HU08_E2E_BROWSER_CHANNEL === 'chrome' ? { channel: 'chrome' } : {});
  report.browserVersion = browser.version();
  let arrived = 0; let release; let reject;
  const allReady = new Promise((done, failed) => { release = done; reject = failed; });
  allReady.catch(() => {});
  const start = { measure: Infinity, end: Infinity, abort: error => { start.end = 0; reject(error); }, ready: async () => {
    if (++arrived === 25) {
      const now = performance.now(); start.measure = now + 120_000; start.end = start.measure + 600_000;
      report.measureStartsAt = new Date(Date.now() + 120_000).toISOString();
      release(); console.log('25 sesiones listas: calentamiento 2 min + medición 10 min.');
    }
    await allReady;
  } };
  let interruption;
  let lastWall = Date.now(); let lastTick = performance.now();
  // A suspended host can advance wall time without advancing the browser's workload.
  // Do not accept a window spanning a suspend, clock jump or abnormally long event-loop stall.
  const heartbeat = setInterval(() => {
    const wall = Date.now(); const tick = performance.now();
    const gap = wall - lastWall; const monotonicGap = tick - lastTick;
    lastWall = wall; lastTick = tick;
    if (gap > 15_000 || Math.abs(gap - monotonicGap) > 5_000) {
      interruption = new Error(`Ventana interrumpida: intervalo de reloj ${gap} ms (monotónico ${Math.round(monotonicGap)} ms).`);
      start.abort(interruption);
      clearInterval(heartbeat);
    }
  }, 1_000);
  const watchdog = setTimeout(() => {
    interruption = new Error('Plazo global de navegador agotado.'); start.abort(interruption);
  }, 900_000);
  try {
    const results = await Promise.allSettled(Array.from({ length: 25 }, (_, index) => lane(index, start)));
    const failed = results.find(result => result.status === 'rejected'); if (failed) throw failed.reason;
  } finally { clearTimeout(watchdog); clearInterval(heartbeat); }
  report.afterMeasurement = fixture('inspect');
  const percentile = values => values.length ? values.sort((a, b) => a - b)[Math.ceil(values.length * 0.95) - 1] : null;
  report.operations = Object.fromEntries(['query', 'create', 'edit'].map(operation => {
    const entries = samples.filter(value => value.measured && value.operation === operation); const success = entries.filter(value => value.ok);
    return [operation, { attempts: entries.length, success: success.length, errors: entries.length - success.length,
      timeouts: entries.filter(value => !value.ok && /timeout|timed out/i.test(value.error)).length,
      conflicts: entries.filter(value => !value.ok && /HTTP (409|412)/.test(value.error)).length,
      p95Milliseconds: percentile(success.map(value => value.milliseconds)), httpP95Milliseconds: percentile(success.map(value => value.httpMilliseconds)) }];
  }));
  report.warmup = { samples: samples.filter(value => !value.measured).length, creates: samples.filter(value => !value.measured && value.operation === 'create' && value.ok).length,
    errors: samples.filter(value => !value.measured && !value.ok).length };
  const values = Object.values(report.operations);
  report.result = values.some(value => value.attempts < 1_000) ? 'INCONCLUSIVE'
    : values.every(value => value.errors === 0 && value.p95Milliseconds <= 2_000) && report.warmup.errors === 0 ? 'PASS' : 'FAIL';
  if (interruption) { report.result = 'INCONCLUSIVE'; report.interruption = interruption.message; }
} catch (error) { fatal = error; report.error = error.message; }
finally {
  if (browser) await browser.close();
  if (started) {
    try { report.finalCounts = fixture('inspect'); } catch (error) { report.inspectionError = error.message; }
    try { report.cleanup = fixture('retire'); } catch (error) { report.cleanupError = error.message; report.result = 'INCONCLUSIVE'; }
    try { compose(['stop', '--timeout', '60'], { timeout: 120_000 }); } catch (error) { report.stopError = error.message; }
  }
  report.finishedAt = new Date().toISOString();
  await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));
  await writeFile(resolve(output, 'samples.json'), JSON.stringify(samples));
  console.log(JSON.stringify({ result: report.result, report: resolve(output, 'report.json'), operations: report.operations, error: report.error }, null, 2));
}
if (fatal || report.result !== 'PASS') process.exitCode = 1;
