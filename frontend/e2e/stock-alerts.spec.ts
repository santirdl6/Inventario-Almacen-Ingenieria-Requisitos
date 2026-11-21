import { randomUUID } from 'node:crypto';
import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';

test.skip(!purchasingProfile, 'Requiere SQL Server y Mailpit del perfil E2E aislado.');

test('configura el mínimo y el comprador revisa una propuesta sin generar un pedido ni correo', async ({ page, gestor, responsable, comprador, lector, smtp }) => {
  const suffix = randomUUID().slice(0, 8).toUpperCase();
  const headers = () => ({ 'X-XSRF-TOKEN': gestor.csrfToken, 'Idempotency-Key': randomUUID() });
  const family = await gestor.request.post('/api/v1/families', { headers: headers(), data: { code: `F-${suffix}`, name: 'Familia de alertas' } });
  expect(family.status()).toBe(201);
  const body = { sku: `AL-${suffix}`, name: 'Artículo de alertas', familyId: (await family.json()).resource.id,
    baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true };
  const created = await gestor.request.post('/api/v1/articles', { headers: headers(), data: body });
  expect(created.status()).toBe(201);
  const { id } = (await created.json()).resource;
  const count = async () => (await (await comprador.request.get('/api/v1/purchase-orders')).json()).totalCount;
  const originalOrders = await count(); const originalMail = await smtp.messages();
  const useSession = async (request: typeof gestor.request) => {
    await page.context().clearCookies();
    await page.context().addCookies((await request.storageState()).cookies);
  };
  await useSession(responsable.request);
  await page.goto(`/maestros/articulos/${id}`);
  await page.getByLabel('Nuevo mínimo (UD)').fill('5');
  await page.getByRole('button', { name: 'Guardar mínimo' }).click();
  await expect(page.getByText('Mínimo guardado y confirmado.')).toBeVisible();
  await expect(page.getByText('Mínimo vigente: 5.000 UD')).toBeVisible();

  await useSession(lector.request);
  await page.goto('/alertas');
  await expect(page).toHaveURL(/\/sin-permiso$/);
  await useSession(comprador.request);
  await page.goto('/alertas');
  await page.getByLabel('Código o nombre').fill(body.sku);
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: body.sku });
  await expect(row).toContainText('5.000 UD');
  await row.getByRole('link', { name: 'Crear pedido' }).click();
  await expect(page).toHaveURL(/\/compras\/nuevo\?/);
  await expect(page.locator('#line-0-article')).toHaveValue(id);
  await expect(page.locator('#purchase-supplier')).toHaveValue('');
  await expect(page.locator('#line-0-quantity')).toHaveValue('5.000');
  await page.locator('#line-0-quantity').fill('7');
  await expect(page.locator('#line-0-quantity')).toHaveValue('7');
  const proposalUrl = page.url();
  const disabled = await gestor.request.put(`/api/v1/articles/${id}`, { headers: { ...headers(), 'If-Match': created.headers()['etag'] }, data: { ...body, isActive: false } });
  expect(disabled.status()).toBe(200);
  await page.goto(proposalUrl);
  await expect(page.getByRole('alert')).toContainText('el artículo está inactivo');
  await expect(page.locator('#line-0-article')).toHaveValue('');
  expect(await count()).toBe(originalOrders);
  expect(await smtp.messages()).toEqual(originalMail);
});
