import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/master-data.fixture';

// HU-02 · Recorrido principal por la interfaz (T063): login de A, alta, duplicado, búsqueda,
// detalle, desactivar y reactivar, todo con teclado. Las existencias se prueban por API
// (L-05), porque HU-01 aún no ofrece recepciones por HTTP.

async function login(page: Page, email: string, password: string, target: string): Promise<void> {
  await page.goto(target);
  await expect(page).toHaveURL(/\/login\?returnUrl=/);
  await page.getByLabel('Correo electrónico (obligatorio)').fill(email);
  await page.getByLabel('Contraseña (obligatoria)').fill(password);
  await page.getByLabel('Contraseña (obligatoria)').press('Enter');
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
}

async function createLocation(page: Page, code: string, aisle: string, kind: 'Almacenamiento' | 'Zona de recepción'): Promise<void> {
  await page.getByRole('link', { name: 'Nueva ubicación' }).press('Enter');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Nueva ubicación');
  await page.getByLabel(/^Pasillo/).fill(aisle);
  await page.getByLabel(/^Estantería/).fill('E01');
  await page.getByLabel(/^Nivel/).fill('N01');
  await page.getByLabel(/^Código/).fill(code);
  await page.getByLabel(/^Tipo/).selectOption({ label: kind });
  await page.getByRole('button', { name: 'Crear ubicación' }).press('Enter');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Ubicación ${code}`);
  await expect(page.getByRole('status').filter({ hasText: `Ubicación ${code} guardada.` })).toBeVisible();
}

test('un operario da de alta, busca, revisa y retira ubicaciones con el teclado', async ({ page, environment }) => {
  const run = Date.now().toString(36).toUpperCase().slice(-6);
  const receiving = `REC-${run}`;
  const storage = `E2E-${run}-N01`;
  const account = environment.accounts.lector;
  await login(page, account.email, account.password, '/ubicaciones');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Ubicaciones');

  // US2.1 (T055): «Localizar un artículo» abre la búsqueda por código o nombre, con clic y con teclado.
  await page.getByRole('link', { name: 'Localizar un artículo' }).click();
  await expect(page).toHaveURL('/ubicaciones/articulo');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Localizar un artículo');
  await expect(page.getByLabel('Código o nombre del artículo')).toBeVisible();
  await page.goto('/ubicaciones');
  await page.getByRole('link', { name: 'Localizar un artículo' }).press('Enter');
  await expect(page).toHaveURL('/ubicaciones/articulo');
  await page.goto('/ubicaciones');

  await createLocation(page, receiving, `R${run}`, 'Zona de recepción');
  await expect(page.getByText('Esta ubicación está vacía.')).toBeVisible();
  await page.goto('/ubicaciones');
  await createLocation(page, storage, `E2E${run}`, 'Almacenamiento');

  // Duplicado: se rechaza sin perder lo escrito.
  await page.goto('/ubicaciones');
  await page.getByRole('link', { name: 'Nueva ubicación' }).press('Enter');
  await page.getByLabel(/^Código/).fill(storage.toLowerCase());
  await page.getByLabel(/^Pasillo/).fill(`OTRO${run}`);
  await page.getByLabel(/^Estantería/).fill('E01');
  await page.getByLabel(/^Nivel/).fill('N01');
  await page.getByRole('button', { name: 'Crear ubicación' }).press('Enter');
  await expect(page.locator('#code-error')).toBeVisible();
  await expect(page.getByLabel(/^Pasillo/)).toHaveValue(`OTRO${run}`);
  await expect(page.locator('#code')).toBeFocused();

  // Búsqueda por código o pasillo.
  await page.goto('/ubicaciones');
  await page.getByLabel('Código o pasillo').fill(`e2e${run}`.toLowerCase());
  await page.getByLabel('Código o pasillo').press('Enter');
  const row = page.getByRole('row').filter({ hasText: storage });
  await expect(row).toContainText('Almacenamiento');
  await expect(row).toContainText('Activa');
  await row.getByRole('link', { name: storage }).press('Enter');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Ubicación ${storage}`);

  // Desactivar y reactivar tras revisar; nunca hay acción de borrado.
  await expect(page.getByRole('button', { name: /eliminar|borrar/i })).toHaveCount(0);
  await page.getByTestId('state-button').getByRole('button', { name: 'Desactivar' }).press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Desactivar ubicación' });
  await expect(dialog).toContainText('Dejará de ofrecerse para nuevas recepciones');
  await dialog.getByRole('button', { name: 'Desactivar' }).press('Enter');
  await expect(page.getByRole('status').filter({ hasText: `Ubicación ${storage} desactivada.` })).toBeVisible();
  await expect(page.locator('dl.facts').first()).toContainText('Inactiva');
  await page.getByTestId('state-button').getByRole('button', { name: 'Reactivar' }).press('Enter');
  await page.getByRole('dialog', { name: 'Reactivar ubicación' }).getByRole('button', { name: 'Reactivar' }).press('Enter');
  await expect(page.getByRole('status').filter({ hasText: `Ubicación ${storage} reactivada.` })).toBeVisible();
  await expect(page.locator('dl.facts').first()).toContainText('Activa');
});

test('una cuenta de solo lectura consulta ubicaciones pero no puede gestionarlas', async ({ page, environment }) => {
  const account = environment.accounts.gestor;
  await login(page, account.email, account.password, '/ubicaciones');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Ubicaciones');
  await expect(page.getByRole('link', { name: 'Nueva ubicación' })).toHaveCount(0);
  await page.goto('/ubicaciones/nueva');
  await expect(page).toHaveURL('/sin-permiso');
});

test('un alta con respuesta perdida se recupera tras recargar sin otra intención ni duplicado', async ({ page, environment, lector }) => {
  const run = randomUUID().replaceAll('-', '').slice(0, 10).toUpperCase();
  const code = `RECOVER-${run}`;
  await login(page, environment.accounts.lector.email, environment.accounts.lector.password, '/ubicaciones/nueva');
  await page.getByLabel(/^Código/).fill(code); await page.getByLabel(/^Pasillo/).fill(`P-${run}`);
  await page.getByLabel(/^Estantería/).fill('E01'); await page.getByLabel(/^Nivel/).fill('N01');
  let sends = 0; let id = ''; let key = '';
  await page.route('**/api/v1/locations', async route => {
    if (route.request().method() !== 'POST') { await route.continue(); return; }
    sends++; key = route.request().headers()['idempotency-key'];
    const response = await route.fetch(); expect(response.status()).toBe(201);
    id = (await response.json()).resource.id; await route.abort('failed');
  });
  await page.getByRole('button', { name: 'Crear ubicación', exact: true }).click();
  await expect(page.getByTestId('pending')).toContainText('Resultado pendiente de comprobar');
  await page.reload();
  await expect(page.getByLabel(/^Código/)).toHaveValue(code);
  await expect(page.getByTestId('pending')).toContainText(key);
  await expect(page.getByRole('button', { name: 'Crear ubicación', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Comprobar', exact: true }).click();
  await expect(page).toHaveURL(`/ubicaciones/${id}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(`Ubicación ${code}`);
  expect(sends).toBe(1);
  const result = await lector.request.get(`/api/v1/locations?search=${code}&status=all`);
  expect(result.status()).toBe(200); expect((await result.json()).totalCount).toBe(1);
  const outcome = await lector.request.get(`/api/v1/operations/${key}`);
  expect(outcome.status()).toBe(200); expect((await outcome.json()).status).toBe(201);
});
