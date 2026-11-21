import { test as base, expect, type APIRequestContext, type PlaywrightWorkerArgs } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { resolve } from 'node:path';

type AccountKey = 'gestor' | 'lector' | 'administrador' | 'comprador';
type Account = { email: string; password: string; role: string };
type Me = { id: string; email: string; roles: string[]; permissions: string[] };
export type Session = { request: APIRequestContext; csrfToken: string; me: Me };
type Environment = { baseURL: string; accounts: Record<Exclude<AccountKey, 'comprador'>, Account> & { comprador?: Account } };
type WorkerFixtures = { environment: Environment };
type HistorySnapshot = { actorId: string; actorName: string; balances: unknown[]; entries: unknown[] };
type TestFixtures = { gestor: Session; lector: Session; administrador: Session;
  comprador: Session;
  historyData: (mode: 'prepare' | 'snapshot', articleId: string) => HistorySnapshot };
type RequestFactory = PlaywrightWorkerArgs['playwright']['request'];

const repositoryRoot = resolve(__dirname, '../../..');
const project = process.env['HU08_E2E_PROJECT'] ?? 'inventario-hu08-e2e';
export const purchasingProfile = process.env['INVENTORY_E2E_PROFILE'] === 'hu07';
if (!/^inventario-(?:hu08|hu07|hu02|pr6)-e2e(?:-[a-z0-9-]+)?$/.test(project)
  || purchasingProfile !== /^inventario-hu07-e2e(?:-|$)/.test(project))
  throw new Error('El proyecto Compose E2E debe tener un nombre dedicado de pruebas.');

function configuration(): { baseURL: string; env: NodeJS.ProcessEnv } {
  const url = new URL(process.env['HU08_E2E_BASE_URL'] ?? 'http://localhost:8082');
  if (url.protocol !== 'http:' || !['localhost', '127.0.0.1'].includes(url.hostname)
    || url.username || url.password || url.pathname !== '/' || url.search || url.hash || !url.port)
    throw new Error('E2E requiere una URL HTTP local con puerto explícito para su proyecto dedicado.');
  if (url.port === '8081')
    throw new Error('El fixture E2E no utiliza el puerto de InventoryDev.');
  return {
    baseURL: url.origin,
    env: {
      ...process.env,
      DB_NAME: purchasingProfile ? 'InventoryHu07E2E' : 'InventoryHu08E2E',
      APP_BIND_ADDRESS: '127.0.0.1',
      APP_PORT: url.port,
      MAILPIT_PORT: purchasingProfile ? '8027' : '8026',
      ...(purchasingProfile ? { SMTP_HOST: 'smtp', SMTP_PORT: '1025', SMTP_USERNAME: '', SMTP_PASSWORD: '', SMTP_SECURITY: 'None', SMTP_FROM: 'inventario@hu07.e2e.test',
        PURCHASING_CURRENCY: 'EUR', PURCHASE_DELIVERY_ENABLED: 'true', PURCHASE_DELIVERY_MAX_ATTEMPTS: '1',
        PURCHASE_DELIVERY_POLL_SECONDS: '1', PURCHASE_DELIVERY_TIMEOUT_SECONDS: '5', PURCHASE_DELIVERY_LEASE_SECONDS: '40' } : {})
    }
  };
}

function compose(env: NodeJS.ProcessEnv, args: string[], timeout = 720_000): void {
  execFileSync('docker', ['compose', '--project-name', project, '--file',
    resolve(repositoryRoot, 'docker-compose.yml'), ...args], {
    cwd: repositoryRoot, env, stdio: 'inherit', timeout
  });
}

function provision(env: NodeJS.ProcessEnv, accounts: Environment['accounts'], mode: 'provision' | 'retire'): void {
  compose({
    ...env,
    HU08_E2E_PROVISION: '1',
    HU08_E2E_DATABASE: env['DB_NAME'],
    INVENTORY_E2E_PROFILE: purchasingProfile ? 'hu07' : 'hu08',
    HU08_E2E_USERS_JSON: JSON.stringify(Object.values(accounts))
  }, ['--profile', 'test', 'run', '--no-deps', '--rm',
    '--volume', `${repositoryRoot}:/workspace`, '--workdir', '/workspace',
    '--env', 'HU08_E2E_PROVISION', '--env', 'HU08_E2E_DATABASE', '--env', 'HU08_E2E_USERS_JSON',
    '--env', 'INVENTORY_E2E_PROFILE',
    '--entrypoint', 'sh', 'tests', '-c',
    `dotnet restore backend/Inventario.slnx --force && dotnet run --file scripts/provision-e2e-users.cs -- ${mode}`]);
}

async function authenticate(factory: RequestFactory, environment: Environment, key: AccountKey): Promise<Session> {
  const request = await factory.newContext({ baseURL: environment.baseURL });
  try {
    const csrf = await request.get('/api/v1/auth/csrf');
    expect(csrf.status(), 'El emisor CSRF debe estar disponible').toBe(200);
    const anonymous = await csrf.json() as { requestToken: string };
    const account = environment.accounts[key];
    if (!account) throw new Error('La cuenta requiere el perfil explícito HU-07.');
    const login = await request.post('/api/v1/auth/login', {
      headers: { 'X-XSRF-TOKEN': anonymous.requestToken },
      data: { email: account.email, password: account.password }
    });
    expect(login.status(), `Inicio de sesión E2E de ${key}`).toBe(200);
    const token = await login.json() as { requestToken: string };
    expect(token.requestToken).not.toBe(anonymous.requestToken);
    const response = await request.get('/api/v1/auth/me');
    expect(response.status()).toBe(200);
    return { request, csrfToken: token.requestToken, me: await response.json() as Me };
  } catch (error) {
    await request.dispose();
    throw error;
  }
}

async function useSession(factory: RequestFactory, environment: Environment, key: AccountKey,
  use: (session: Session) => Promise<void>): Promise<void> {
  const session = await authenticate(factory, environment, key);
  try {
    await use(session);
  } finally {
    try {
      const logout = await session.request.post('/api/v1/auth/logout', {
        headers: { 'X-XSRF-TOKEN': session.csrfToken }, data: {}
      });
      expect(logout.status()).toBe(204);
    } finally {
      await session.request.dispose();
    }
  }
}

export const test = base.extend<TestFixtures, WorkerFixtures>({
  historyData: async ({ environment }, use) => {
    await use((mode, articleId) => {
      if (purchasingProfile) throw new Error('El auxiliar de historial HU-08 no usa la base HU-07.');
      if (!/^[0-9a-f-]{36}$/i.test(articleId)) throw new Error('Artículo E2E inválido.');
      const { env } = configuration();
      const output = execFileSync('docker', ['compose', '--project-name', project, '--file',
        resolve(repositoryRoot, 'docker-compose.yml'), '--profile', 'test', 'run', '--no-deps', '--rm',
        '--volume', `${repositoryRoot}:/workspace`, '--workdir', '/workspace',
        '--env', 'HU08_E2E_PROVISION', '--env', 'HU08_E2E_DATABASE', '--env', 'HU08_E2E_USERS_JSON',
        '--env', 'SQL_APP_USER', '--env', 'SQL_APP_PASSWORD', '--entrypoint', 'sh', 'tests', '-c',
        `dotnet run --file scripts/prepare-hu08-history.cs -- ${mode} ${articleId}`], {
        cwd: repositoryRoot, env: { ...env, HU08_E2E_PROVISION: '1', HU08_E2E_DATABASE: 'InventoryHu08E2E',
          HU08_E2E_USERS_JSON: JSON.stringify(Object.values(environment.accounts)) },
        encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'], timeout: 180_000, maxBuffer: 8 * 1024 * 1024
      });
      const line = output.split('\n').find(value => value.startsWith('HU08_HISTORY_JSON:'));
      if (!line) throw new Error('No se recibió la evidencia del núcleo de stock.');
      return JSON.parse(line.slice('HU08_HISTORY_JSON:'.length)) as HistorySnapshot;
    });
  },
  environment: [async ({ playwright }, use) => {
    const { baseURL, env } = configuration();
    const run = randomUUID().replaceAll('-', '');
    const account = (role: string): Account => ({
      email: `${run}.${role.toLowerCase()}@${purchasingProfile ? 'hu07' : 'hu08'}.e2e.test`,
      password: `Test_${randomUUID()}!aA9`, role
    });
    const accounts: Environment['accounts'] = {
      gestor: account('GestorMaestros'), lector: account('Operario'), administrador: account('Administrador')
    };
    if (purchasingProfile) accounts.comprador = account('Comprador');
    let provisioningAttempted = false;
    try {
      compose(env, ['up', process.env['HU08_E2E_SKIP_BUILD'] === '1' ? '--no-build' : '--build',
        '-d', '--wait', '--wait-timeout', '300']);
      provisioningAttempted = true;
      provision(env, accounts, 'provision');
      await use({ baseURL, accounts });
    } finally {
      try {
        if (provisioningAttempted) {
          provision(env, accounts, 'retire');
          const request = await playwright.request.newContext({ baseURL });
          try {
            const csrf = await request.get('/api/v1/auth/csrf');
            const token = await csrf.json() as { requestToken: string };
            const rejected = await request.post('/api/v1/auth/login', {
              headers: { 'X-XSRF-TOKEN': token.requestToken },
              data: { email: accounts.gestor.email, password: accounts.gestor.password }
            });
            expect(rejected.status(), 'La cuenta temporal debe quedar bloqueada al limpiar').toBe(401);
          } finally {
            await request.dispose();
          }
        }
      } finally {
        if (process.env['HU08_E2E_KEEP_RUNNING'] !== '1')
          compose(env, ['stop', '--timeout', '60'], 120_000);
      }
    }
  }, { scope: 'worker', timeout: 900_000 }],
  gestor: async ({ playwright, environment }, use) => useSession(playwright.request, environment, 'gestor', use),
  lector: async ({ playwright, environment }, use) => useSession(playwright.request, environment, 'lector', use),
  administrador: async ({ playwright, environment }, use) => useSession(playwright.request, environment, 'administrador', use),
  comprador: async ({ playwright, environment }, use) => useSession(playwright.request, environment, 'comprador', use)
});

export { expect };
