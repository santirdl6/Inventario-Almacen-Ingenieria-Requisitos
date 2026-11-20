// HU-05 · Contratos de specs/004-consultar-existencias/contracts/stock-api.md. Las cantidades
// llegan como cadenas con tres decimales y se muestran tal cual, siempre con la unidad.

/** Artículo elegible en el selector propio (GET /articles?q=&state=all), incluidos inactivos. */
export interface StockArticleOption {
  id: string; sku: string; name: string; baseUnit: string; isActive: boolean; tracksLot: boolean;
}

export interface StockLocation {
  id: string; code: string; aisle: string; rack: string; level: string; kind: string; isActive: boolean;
}

/** En artículos sin seguimiento, isUntracked: la UI muestra «sin lote», nunca SIN-LOTE. */
export interface StockLot { id: string; code: string; isUntracked: boolean }

export interface StockRow { location: StockLocation; lot: StockLot; physical: string; reserved: string; available: string }

export interface StockQueryResult {
  asOf: string;
  article: { id: string; sku: string; name: string; baseUnit: string; isActive: boolean; tracksLot: boolean };
  totals: { physical: string; reserved: string; available: string };
  items: StockRow[];
  page: number; pageSize: number; totalCount: number;
}

export interface StockFilter {
  articleId: string; locationId?: string; lotId?: string; includeEmpty: boolean; page: number;
}

export function lotText(lot: { code: string; isUntracked: boolean }): string {
  return lot.isUntracked ? 'sin lote' : lot.code;
}

export function isZeroQuantity(value: string): boolean {
  return Number(value) === 0;
}

// --- Pedido de cliente, reservas y liberaciones (T016) ---

export interface OrderAllocation {
  id: string; articleId: string; locationId: string; lotId: string;
  allocated: string; consumed: string; released: string; remaining: string;
}
export interface OrderReservation { id: string; state: string; allocations: OrderAllocation[] }
export interface OrderLine {
  id: string; articleId: string; ordered: string; shipped: string; pending: string; reservedRemaining: string;
  reservations: OrderReservation[];
}
/** GET /customer-orders/{id}: solo datos propios, con ETag. */
export interface CustomerOrderView { id: string; externalReference: string; customerReference: string; state: string; lines: OrderLine[] }

/** GET /customer-orders/{id}/context: datos actuales, sin ETag, con orderVersion. */
export interface CustomerOrderContext {
  orderVersion: string; asOf: string;
  articles: { id: string; sku: string; name: string; baseUnit: string; isActive: boolean; tracksLot: boolean; allowsFractional: boolean }[];
  locations: { id: string; code: string; aisle: string; rack: string; level: string; isActive: boolean }[];
  lots: { id: string; articleId: string; code: string; isUntracked: boolean }[];
}

/** Detalle combinado: las dos lecturas pertenecen a la misma revisión (orderVersion === etag). */
export interface OrderDetail { order: CustomerOrderView; context: CustomerOrderContext; etag: string }

export interface ReservationView {
  id: string; state: string; customerOrderId: string; customerOrderLineId: string; articleId: string;
  allocations: OrderAllocation[];
}

export interface CustomerOrderWrite {
  externalReference: string; customerReference: string; lines: { articleId: string; quantity: string }[];
}
export interface ReservationWrite {
  customerOrderLineId: string; allocations: { locationId: string; lotId: string | null; quantity: string }[];
}
export interface ReleaseWrite { releaseId: string; reason: string; allocations: { allocationId: string; quantity: string }[] }

export interface ReservationResult { operationId: string; reservationId: string; customerOrderId: string; movementId: string }
export interface ReleaseResult { operationId: string; reservationId: string; releaseId: string; movementId: string; state: string }

export const ORDERS = '/api/v1/customer-orders';
export const RESERVATIONS = '/api/v1/reservations';
export const NEW_ORDER_ROUTE = '/existencias/pedidos/nuevo';
export const orderRoute = (id: string) => `/existencias/pedidos/${id}`;
export const releaseResource = (reservationId: string) => `${RESERVATIONS}/${reservationId}/release`;

export const STATE_LABELS: Record<string, string> = {
  Open: 'Abierto', Fulfilled: 'Servido', Cancelled: 'Cancelado', Active: 'Activa', Consumed: 'Consumida', Released: 'Liberada'
};
