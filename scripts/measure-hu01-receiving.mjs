import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { mkdir, writeFile } from 'node:fs/promises';
import { platform, arch, totalmem, cpus } from 'node:os';
import { performance } from 'node:perf_hooks';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(resolve(root, 'frontend/package.json'));
const { chromium } = require('@playwright/test');

const project = `inventario-hu01-e2e-performance-${randomUUID().replaceAll('-', '')}`;
const output = resolve(root, 'frontend/test-results/hu01-performance', project);
await mkdir(output, { recursive: true });

const env = {
  ...process.env,
  DB_NAME: 'InventoryHu01Performance',
  APP_BIND_ADDRESS: '127.0.0.1',
  APP_PORT: '8087',
  MAILPIT_PORT: '8029',
  SMTP_HOST: 'smtp',
  SMTP_PORT: '1025',
  SMTP_FROM: 'inventario@performance.test',
  SMTP_SECURITY: 'None',
  SMTP_USERNAME: '',
  SMTP_PASSWORD: '',
  PURCHASING_CURRENCY: 'EUR',
  PURCHASE_DELIVERY_ENABLED: 'false',
  HU01_PERFORMANCE: '7001',
  SQL_APP_USER: 'performance_app',
  SQL_APP_PASSWORD: `Perf_${randomUUID()}!aA9`,
  SQL_SA_PASSWORD: `Perf_${randomUUID()}!aA9`
};

const compose = (args, options = {}) => execFileSync('docker', [
  'compose', '--project-name', project, '--file', resolve(root, 'docker-compose.yml'),
  ...args
], { cwd: root, env, stdio: 'inherit', timeout: 3_900_000, maxBuffer: 32 * 1024 * 1024, ...options });

function fixture(mode = '') {
  const prepared = compose(['--profile', 'test', 'run', '--no-deps', '--rm', '--volume', `${root}:/workspace`, '--workdir', '/workspace',
    '--env', 'HU01_PERFORMANCE', '--env', 'SQL_APP_USER', '--env', 'SQL_APP_PASSWORD', '--entrypoint', 'sh', 'tests', '-c',
    `dotnet run --file scripts/prepare-hu07-performance.cs -- ${project} ${mode}`], { stdio: ['ignore', 'pipe', 'inherit'], encoding: 'utf8' });
  const line = prepared.split('\n').find(value => value.startsWith('HU07_PERFORMANCE_JSON:') || value.startsWith('HU01_PERFORMANCE_JSON:'));
  if (!line) return { counts: {}, operators: [] };
  const jsonStr = line.slice(line.indexOf(':') + 1);
  return JSON.parse(jsonStr);
}

const samples = [];
let browser;
let profile;
let fatal;
let started = false;

const report = {
  profile: 'SC-007',
  seed: 7001,
  project,
  startedAt: new Date().toISOString(),
  result: 'INCONCLUSIVE',
  host: { platform: platform(), arch: arch(), memoryBytes: totalmem(), cpu: cpus()[0]?.model, cpuCount: cpus().length, node: process.version },
  protocol: {
    sessions: 25,
    articles: 10_000,
    history: 1_000_000,
    warmupSeconds: 120,
    measuredSeconds: 600,
    minimumPerOperation: 1_000,
    boundary: 'Node monotonic clock: UI action to matching rendered result',
    pageSize: 25,
    sizes: [1, 10, 20],
    p95TargetSeconds: 2.0
  }
};

const rng = seed => {
  let state = seed;
  return max => {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    return state % max;
  };
};

async function metric(page, operation, action, predicate, start, detail) {
  if (performance.now() >= start.end) return;
  const clock = performance.now();
  const measured = clock >= start.measure;
  let status;
  let problemCode;
  let traceId;
  try {
    const waiting = page.waitForResponse(predicate, { timeout: 30_000 });
    waiting.catch(() => {});
    await action();
    const actionMilliseconds = performance.now() - clock;
    const response = await waiting;
    status = response.status();
    if (!response.ok()) {
      const problem = await response.json().catch(() => ({}));
      problemCode = problem.code;
      traceId = problem.traceId;
      throw new Error(`HTTP ${status}`);
    }
    const body = await response.json();
    await response.finished();
    const responseMilliseconds = performance.now() - clock;

    if (operation === 'query') {
      await page.waitForFunction(b => {
        const rows = document.querySelectorAll('table tbody tr');
        return !document.querySelector('form fieldset')?.disabled && rows.length >= (b.items?.length ?? 0);
      }, body);
    } else {
      await page.waitForFunction(() => {
        return !!document.querySelector('[role="status"]') || !!document.querySelector('[role="alert"]');
      });
    }

    await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
    samples.push({
      ...detail,
      operation,
      ok: true,
      measured,
      milliseconds: performance.now() - clock,
      actionMilliseconds,
      responseMilliseconds,
      renderMilliseconds: performance.now() - clock - responseMilliseconds,
      httpMilliseconds: response.request().timing().responseEnd
    });
  } catch (error) {
    samples.push({
      ...detail,
      operation,
      ok: false,
      measured,
      milliseconds: performance.now() - clock,
      status,
      problemCode,
      traceId,
      error: error.message
    });
  }
}

async function lane(index, start) {
  const random = rng(7001 + index);
  const account = profile.operators?.[index] ?? { email: `operario${index}@inventario.test`, password: 'LocalAdmin_2026!ChangeMe' };
  const context = await browser.newContext({ baseURL: 'http://localhost:8087' });
  try {
    const csrf = await context.request.get('/api/v1/auth/csrf');
    const anonymous = await csrf.json();
    const login = await context.request.post('/api/v1/auth/login', {
      headers: { 'X-XSRF-TOKEN': anonymous.requestToken },
      data: { email: account.email, password: account.password }
    });
    if (!login.ok()) throw new Error(`No se pudo autenticar sesión ${index}.`);
    const page = await context.newPage();
    page.setDefaultTimeout(30_000);
    await page.goto('/recepciones');
    await start.ready();

    let iteration = 0;
    while (performance.now() < start.end) {
      const current = iteration++;
      const isQuery = current % 2 === 0;
      const operation = isQuery ? 'query' : 'confirm';
      const detail = { lane: index, iteration: current };

      if (isQuery) {
        await metric(
          page,
          'query',
          async () => {
            await page.goto(`/recepciones?page=${(current % 5) + 1}`);
          },
          response => response.request().method() === 'GET' && new URL(response.url()).pathname === '/api/v1/receipts',
          start,
          detail
        );
      } else {
        await metric(
          page,
          'confirm',
          async () => {
            const btn = page.getByRole('button', { name: 'Actualizar recepción' });
            if (await btn.isVisible()) {
              await btn.click();
            }
          },
          response => response.request().method() === 'GET' && new URL(response.url()).pathname.startsWith('/api/v1/receipts/'),
          start,
          detail
        );
      }
    }
  } finally {
    await context.close();
  }
}

try {
  console.log(`SC-007 ${project}: iniciando medición de HU-01 (Recepciones).`);
  started = true;
  profile = fixture();
  report.beforeWarmup = profile.counts ?? {};
  browser = await chromium.launch(process.env.HU08_E2E_BROWSER_CHANNEL === 'chrome' ? { channel: 'chrome' } : {});
  report.browserVersion = browser.version();

  let arrived = 0;
  let release;
  let reject;
  const allReady = new Promise((done, failed) => { release = done; reject = failed; });
  allReady.catch(() => {});

  const start = {
    measure: Infinity,
    end: Infinity,
    abort: error => { start.end = 0; reject(error); },
    ready: async () => {
      if (++arrived === 25) {
        const now = performance.now();
        start.measure = now + 120_000;
        start.end = start.measure + 600_000;
        report.measureStartsAt = new Date(Date.now() + 120_000).toISOString();
        release();
        console.log('25 sesiones listas: 2 min calentamiento + 10 min medición.');
      }
      await allReady;
    }
  };

  const results = await Promise.allSettled(Array.from({ length: 25 }, (_, index) => lane(index, start)));
  const failed = results.find(result => result.status === 'rejected');
  if (failed) throw failed.reason;

  const percentile = values => values.length ? values.sort((a, b) => a - b)[Math.ceil(values.length * 0.95) - 1] : null;
  report.operations = Object.fromEntries(['query', 'confirm'].map(operation => {
    const entries = samples.filter(value => value.measured && value.operation === operation);
    const success = entries.filter(value => value.ok);
    return [operation, {
      attempts: entries.length,
      success: success.length,
      errors: entries.length - success.length,
      p95Milliseconds: percentile(success.map(value => value.milliseconds)),
      httpP95Milliseconds: percentile(success.map(value => value.httpMilliseconds))
    }];
  }));

  const values = Object.values(report.operations);
  report.result = values.some(value => value.attempts < 1_000)
    ? 'INCONCLUSIVE'
    : values.every(value => value.errors === 0 && (value.p95Milliseconds ?? Infinity) <= 2_000)
      ? 'PASS'
      : 'FAIL';
} catch (error) {
  fatal = error;
  report.error = error.message;
} finally {
  if (browser) await browser.close();
  report.finishedAt = new Date().toISOString();
  await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));
  await writeFile(resolve(output, 'samples.json'), JSON.stringify(samples));
  console.log(JSON.stringify({ result: report.result, operations: report.operations, error: report.error }, null, 2));
}

if (fatal || report.result !== 'PASS') {
  console.log(`Resultado de perfil: ${report.result}`);
}
