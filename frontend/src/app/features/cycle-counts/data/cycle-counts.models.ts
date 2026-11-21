/** HU-04 · Modelos de /api/v1/cycle-counts (specs/006-realizar-inventarios-ciclicos/contracts/cycle-counts-api.md). */
export const CYCLE_COUNTS = '/api/v1/cycle-counts';

export type CountState = 'Draft' | 'Counting' | 'AwaitingApproval' | 'Confirmed' | 'Cancelled';

export const COUNT_STATE_LABELS: Record<string, string> = {
  Draft: 'Borrador', Counting: 'En conteo', AwaitingApproval: 'Pendiente de aprobación', Confirmed: 'Confirmado', Cancelled: 'Cancelado'
};

export interface CountSummary {
  id: string; countCode: string; state: CountState; locationIds: string[]; locationCodes: string[];
  createdBy: string; createdByName: string; createdAt: string;
  startedBy: string | null; startedByName: string | null; startedAt: string | null;
  closedBy: string | null; closedByName: string | null; closedAt: string | null;
}

export interface CountPage { items: CountSummary[]; page: number; pageSize: number; totalCount: number }

export interface CountLineView {
  id: string; articleId: string; locationId: string; lotCode: string; isFound: boolean; theoreticalPhysical: string;
  countedPhysical: string | null; difference: string | null; countedBy: string | null; countedAt: string | null;
  reason: string | null; reasonBy: string | null; correctsShipmentId: string | null; shipmentCorrectionNotice: string | null;
}

export interface CountView {
  id: string; countCode: string; state: CountState; createdBy: string; createdAt: string;
  startedBy: string | null; startedAt: string | null; submittedBy: string | null; submittedAt: string | null;
  approvedBy: string | null; approvedAt: string | null; cancelledBy: string | null; cancelledAt: string | null;
  cancelReason: string | null; operationId: string | null; adjustmentMovementId: string | null;
  locations: { locationId: string; isFrozen: boolean }[]; lines: CountLineView[];
}

export interface CountArticle { id: string; sku: string; name: string; unit: string; allowsFractions: boolean; tracksLot: boolean; isActive: boolean }

export interface CountContext {
  countVersion: string; articles: CountArticle[]; locations: { id: string; code: string; isActive: boolean }[];
  reservedByBucket: { articleId: string; locationId: string; lotCode: string; reserved: string }[];
  shipments: { shipmentId: string; customerOrderId: string; externalReference: string }[];
  users: { id: string; name: string }[];
}

/** Detalle y contexto de la misma revisión (D-12), con el ETag para la siguiente escritura. */
export interface CountDetail { count: CountView; context: CountContext; etag: string }

export interface CountMutation { operationId: string; count: CountView }

export interface CountLinesWrite {
  counted: { lineId: string; countedPhysical: string }[];
  found: { articleId: string; locationId: string; lotCode?: string; countedPhysical: string }[];
}

export interface CountReviewWrite { lines: { lineId: string; reason: string | null; correctsShipmentId?: string }[] }

export interface ShipmentCorrection { lineId: string; shipmentId: string; customerOrderId: string; orderChanged: false; notice: string }

export interface CountConfirmation {
  operationId: string; countId: string; movementId: string | null; adjustedLines: { lineId: string; difference: string }[];
  shipmentCorrections: ShipmentCorrection[];
}

export interface CountCancellation { operationId: string; countId: string }

export interface ShipmentCandidate {
  shipmentId: string; customerOrderId: string; externalReference: string; confirmedAt: string; quantity: string; linkedCountLineIds: string[];
}

export interface CandidatePage { items: ShipmentCandidate[]; page: number; pageSize: number; totalCount: number }

export const countResource = (id: string) => `${CYCLE_COUNTS}/${id}`;
export const countRoute = (id: string) => `/inventarios/${id}`;
export const NEW_COUNT_ROUTE = '/inventarios/nuevo';

/** Máximo de ubicaciones de un conteo (FR-001; contrato API § Escrituras). */
export const MAX_LOCATIONS = 50;

/** Diferencia en texto, no solo color (contracts/cycle-counts-ui.md): «Faltan 2», «Sobran 3», «Sin diferencia». */
export function differenceText(difference: string | null, unit: string): string {
  if (difference === null) return 'Sin contar';
  const value = Number(difference);
  if (!value) return 'Sin diferencia';
  return `${value < 0 ? 'Faltan' : 'Sobran'} ${trimQuantity(Math.abs(value).toString())} ${unit}`.trim();
}

/** «10.000» → «10»; «2.500» → «2.5». */
export function trimQuantity(value: string | null): string {
  if (value === null) return '';
  return value.includes('.') ? value.replace(/\.?0+$/, '') : value;
}
