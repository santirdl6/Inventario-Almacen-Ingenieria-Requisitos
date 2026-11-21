import { TestRequest } from '@angular/common/http/testing';
import { httpMock } from '../../locations/testing/locations-testing';

// HU-04 · Datos y utilidades comunes de las specs de /inventarios (T014–T016).
export async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 6; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

export const COUNT = '40000000-0000-4000-8000-000000000004';
export const U1 = '50000000-0000-4000-8000-000000000001';
export const U2 = '50000000-0000-4000-8000-000000000002';
export const ART_A = 'a0000000-0000-4000-8000-00000000000a';
export const ART_B = 'b0000000-0000-4000-8000-00000000000b';
export const L1 = 'c0000000-0000-4000-8000-000000000001';
export const L2 = 'c0000000-0000-4000-8000-000000000002';
export const FOUND = 'c0000000-0000-4000-8000-0000000000f1';
export const SHIP = '30000000-0000-4000-8000-000000000003';
export const ORDER = '10000000-0000-4000-8000-000000000001';
export const USER = 'e0000000-0000-4000-8000-000000000001';
export const RECORD = ['Inventory.Read', 'Counts.Record'];
export const ADJUST = ['Inventory.Read', 'Counts.Record', 'Inventory.Adjust'];

export function location(id: string, code: string, isActive = true) {
  return { id, code, aisle: 'P01', rack: 'E01', level: code, description: '', kind: 'Storage', kindLabel: 'Almacenaje',
    isActive, statusLabel: isActive ? 'Activa' : 'Inactiva' };
}

export interface LineOverrides {
  id?: string; articleId?: string; locationId?: string; lotCode?: string; isFound?: boolean; theoreticalPhysical?: string;
  countedPhysical?: string | null; difference?: string | null; reason?: string | null; correctsShipmentId?: string | null;
  shipmentCorrectionNotice?: string | null; countedBy?: string | null; countedAt?: string | null;
}

export function line(overrides: LineOverrides = {}) {
  return {
    id: L1, articleId: ART_A, locationId: U1, lotCode: 'SIN-LOTE', isFound: false, theoreticalPhysical: '10.000',
    countedPhysical: null, difference: null, countedBy: null, countedAt: null, reason: null, reasonBy: null,
    correctsShipmentId: null, shipmentCorrectionNotice: null, ...overrides
  };
}

export function count(state = 'Draft', lines: unknown[] = [], extra: Record<string, unknown> = {}) {
  return {
    id: COUNT, countCode: 'CC-400000000000', state, createdBy: USER, createdAt: '2026-11-21T08:00:00Z',
    startedBy: state === 'Draft' ? null : USER, startedAt: state === 'Draft' ? null : '2026-11-21T08:10:00Z',
    submittedBy: null, submittedAt: null, approvedBy: null, approvedAt: null, cancelledBy: null, cancelledAt: null,
    cancelReason: null, operationId: null, adjustmentMovementId: null,
    locations: [{ locationId: U1, isFrozen: state === 'Counting' || state === 'AwaitingApproval' },
      { locationId: U2, isFrozen: state === 'Counting' || state === 'AwaitingApproval' }],
    lines, ...extra
  };
}

export function context(version = '"c1"', extra: Record<string, unknown> = {}) {
  return {
    countVersion: version,
    articles: [
      { id: ART_A, sku: 'ART-A', name: 'Tornillo', unit: 'UD', allowsFractions: false, tracksLot: false, isActive: true },
      { id: ART_B, sku: 'ART-B', name: 'Cable', unit: 'M', allowsFractions: true, tracksLot: true, isActive: true }
    ],
    locations: [{ id: U1, code: 'U1', isActive: true }, { id: U2, code: 'U2', isActive: true }],
    reservedByBucket: [], shipments: [], users: [{ id: USER, name: 'operario@h0.test' }], ...extra
  };
}

/** Atiende GET /cycle-counts/{id} y su contexto con la misma versión. */
export async function flushDetail(fixture: { detectChanges(): void }, body: object, ctx: object = context(),
  etag = '"c1"'): Promise<void> {
  await settle(fixture);
  httpMock().expectOne(r => r.method === 'GET' && r.url === `/api/v1/cycle-counts/${COUNT}`).flush(body, { headers: { ETag: etag } });
  await settle(fixture);
  httpMock().expectOne(`/api/v1/cycle-counts/${COUNT}/context`).flush(ctx);
  await settle(fixture);
}

export function expectWrite(method: string, url: string): TestRequest {
  const request = httpMock().expectOne(r => r.method === method && r.url === url);
  return request;
}
