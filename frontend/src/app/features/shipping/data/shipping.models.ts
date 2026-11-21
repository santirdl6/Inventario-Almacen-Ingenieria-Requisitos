// HU-03 · Contratos de specs/005-registrar-salida-mercancia/contracts/shipping-api.md. Las cantidades
// llegan como cadenas con tres decimales y se muestran tal cual, siempre con la unidad.
import { CustomerOrderContext } from '../../stock/data/stock.models';

export interface CustomerOrderLookup { id: string; externalReference: string; state: string }

export interface ShipmentConsumption { reservationAllocationId: string; reservationId: string; quantity: string }
export interface ShipmentLineView {
  id: string; customerOrderLineId: string; articleId: string; locationId: string; lotId: string;
  quantity: string; ownReservationConsumed: string; consumptions: ShipmentConsumption[];
}
/** GET /shipments/{id}: solo datos propios, con ETag. */
export interface ShipmentView {
  id: string; customerOrderId: string; state: string; occurredAt: string; recordedAt: string; actorId: string;
  operationId: string | null; lines: ShipmentLineView[];
}

export interface ShipmentReviewLine {
  lineId: string; pending: string; physical: string; reserved: string; ownReserved: string; otherReserved: string; possible: string;
}
/** GET /shipments/{id}/context: datos actuales sin ETag, con shipmentVersion y orderVersion. */
export interface ShipmentContext {
  shipmentVersion: string; orderVersion: string; asOf: string; actorName: string;
  order: { id: string; externalReference: string; customerReference: string; state: string };
  articles: CustomerOrderContext['articles'];
  locations: CustomerOrderContext['locations'];
  lots: CustomerOrderContext['lots'];
  review: ShipmentReviewLine[];
}

/** Detalle combinado: las dos lecturas pertenecen a la misma revisión (shipmentVersion === etag). */
export interface ShipmentDetail { shipment: ShipmentView; context: ShipmentContext; etag: string }

export interface ShipmentSummary {
  id: string; state: string; occurredAt: string; recordedAt: string; actorName: string; lineCount: number; totalQuantity: string;
}
export interface ShipmentPage { items: ShipmentSummary[]; page: number; pageSize: number; totalCount: number }

export interface ShipmentLineWrite { customerOrderLineId: string; locationId: string; lotId: string; quantity: string }
export interface ShipmentWrite { customerOrderId?: string; occurredAt: string; lines: ShipmentLineWrite[] }
export interface ShipmentMutation { operationId: string; shipment: ShipmentView }
export interface ShipmentConfirmation {
  operationId: string; shipmentId: string; movementId: string; customerOrderId: string; orderState: string;
  lines: { id: string; quantity: string; ownReservationConsumed: string }[];
}

export const SHIPMENTS = '/api/v1/shipments';
export const shipmentResource = (id: string) => `${SHIPMENTS}/${id}`;
export const confirmResource = (id: string) => `${SHIPMENTS}/${id}/confirm`;
export const orderShipmentsRoute = (orderId: string) => `/salidas/pedidos/${orderId}`;
export const shipmentRoute = (id: string) => `/salidas/${id}`;

export const SHIPMENT_STATE_LABELS: Record<string, string> = { Draft: 'Borrador', Confirmed: 'Confirmada' };
