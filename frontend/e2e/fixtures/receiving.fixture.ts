import { randomUUID } from 'node:crypto';
import { test as base, expect, purchasingProfile } from './master-data.fixture';

type Id = { id: string };
type ReadyPurchase = { orderId: string; orderNumber: string; lineId: string; articleId: string; locations: [Id, Id] };

export const test = base.extend<{ receivingPurchase: ReadyPurchase }>({
  receivingPurchase: async ({ gestor, comprador, lector }, use) => {
    if (!purchasingProfile || !/^inventario-hu07-e2e-hu01(?:-|$)/.test(process.env['HU08_E2E_PROJECT'] ?? ''))
      throw new Error('Usa INVENTORY_E2E_PROFILE=hu07 y un proyecto inventario-hu07-e2e-hu01 dedicado.');
    const headers = (csrf: string) => ({ 'X-XSRF-TOKEN': csrf, 'Idempotency-Key': randomUUID() });
    const suffix = randomUUID().slice(0, 8).toUpperCase();
    async function master(path: string, data: object): Promise<Id> {
      const result = await gestor.request.post(`/api/v1/${path}`, { headers: headers(gestor.csrfToken), data });
      expect(result.status()).toBe(201);
      return (await result.json()).resource;
    }
    const family = await master('families', { code: `RF-${suffix}`, name: 'Recepciones' });
    const supplier = await master('suppliers', { code: `RP-${suffix}`, name: 'Proveedor de recepción', email: 'recepcion@proveedor.test' });
    const article = await master('articles', { sku: `RA-${suffix}`, name: 'Artículo de recepción', familyId: family.id,
      baseUnit: 'UD', allowsFractional: false, tracksLot: false, isActive: true });
    async function location(index: number, kind: string): Promise<Id> {
      const result = await lector.request.post('/api/v1/locations', { headers: headers(lector.csrfToken),
        data: { code: `R-${suffix}-${index}`, aisle: `${suffix}-${index}`, rack: 'E01', level: 'N01', kind, isActive: true } });
      expect(result.status()).toBe(201);
      return (await result.json()).resource;
    }
    const locations: [Id, Id] = [await location(1, 'Storage'), await location(2, 'Receiving')];
    const lineId = randomUUID();
    const created = await comprador.request.post('/api/v1/purchase-orders', { headers: headers(comprador.csrfToken),
      data: { supplierId: supplier.id, expectedDeliveryDate: '2026-12-01', lines: [{ id: lineId, articleId: article.id, quantity: '10', unitPrice: '12.50' }] } });
    expect(created.status()).toBe(201);
    const { id, number } = (await created.json()).resource;
    const path = `/api/v1/purchase-orders/${id}`;
    const preview = await comprador.request.get(`${path}/send-preview`);
    expect(preview.ok()).toBe(true);
    const sent = await comprador.request.post(`${path}/send`, { headers: { ...headers(comprador.csrfToken), 'If-Match': created.headers()['etag'] },
      data: { kind: 'initial', reviewToken: (await preview.json()).reviewToken } });
    expect(sent.status()).toBe(202);
    await expect.poll(async () => (await (await comprador.request.get(path)).json()).sendStatus, { timeout: 20_000 }).toBe('Accepted');
    await use({ orderId: id, orderNumber: number, lineId, articleId: article.id, locations });
  }
});

export { expect };
