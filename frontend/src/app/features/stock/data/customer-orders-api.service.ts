import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TimeoutError, firstValueFrom } from 'rxjs';
import { MasterDataMutationsService, MutationResult } from '../../../core/http/master-data-mutations.service';
import {
  CustomerOrderContext, CustomerOrderView, CustomerOrderWrite, NEW_ORDER_ROUTE, ORDERS, OrderDetail, RESERVATIONS,
  ReleaseResult, ReleaseWrite, ReservationResult, ReservationView, ReservationWrite, orderRoute, releaseResource
} from './stock.models';

/** El detalle y el contexto se leyeron en revisiones distintas y no coincidieron tras reintentar. */
export class OrderChangedError extends Error {
  constructor() { super('El pedido cambió durante la consulta. Vuelve a cargarlo.'); }
}

/** Red, timeout o 5xx: el servidor pudo confirmar la operación o no (estado «pendiente de comprobar»). */
export function isUncertain(error: unknown): boolean {
  if (error instanceof TimeoutError) return true;
  if (error instanceof HttpErrorResponse) return error.status === 0 || error.status >= 500;
  return false;
}

/**
 * HU-05 T016 · Pedidos, reservas y liberaciones. Las lecturas siguen el patrón de
 * articles-api.service.ts de HU-08 (detalle con ETag + contexto sin ETag). Las mutaciones usan el
 * transporte común de A (PendingOperationStore.begin + MasterDataMutationsService.send, que es lo
 * que hace mutate() añadiendo appRoute): clave, cuerpo congelado e If-Match se conservan en la
 * intención, que sobrevive a la recarga. No se usa idempotent-intent.ts (research D-15).
 */
@Injectable({ providedIn: 'root' })
export class CustomerOrdersApiService {
  private readonly http = inject(HttpClient);
  private readonly mutations = inject(MasterDataMutationsService);

  /** Dos lecturas de la misma revisión; si no coinciden, vuelve a leer ambas (hasta 3 veces). */
  async detail(id: string): Promise<OrderDetail> {
    for (let attempt = 0; attempt < 3; attempt++) {
      const response = await firstValueFrom(this.http.get<CustomerOrderView>(`${ORDERS}/${id}`, { observe: 'response' }));
      const context = await firstValueFrom(this.http.get<CustomerOrderContext>(`${ORDERS}/${id}/context`));
      const etag = response.headers.get('ETag');
      if (response.body && etag && context.orderVersion === etag) return { order: response.body, context, etag };
    }
    throw new OrderChangedError();
  }

  /** Solo para obtener el ETag de la reserva antes de liberar. */
  async reservation(id: string): Promise<{ reservation: ReservationView; etag: string }> {
    const response = await firstValueFrom(this.http.get<ReservationView>(`${RESERVATIONS}/${id}`, { observe: 'response' }));
    const etag = response.headers.get('ETag');
    if (!response.body || !etag) throw new Error('No se pudo leer la versión de la reserva.');
    return { reservation: response.body, etag };
  }

  create(body: CustomerOrderWrite): Promise<MutationResult<{ operationId: string; resource: CustomerOrderView }>> {
    return this.send('POST', ORDERS, body, undefined, NEW_ORDER_ROUTE);
  }

  reserve(orderId: string, body: ReservationWrite, orderEtag: string): Promise<MutationResult<ReservationResult>> {
    return this.send('POST', RESERVATIONS, body, orderEtag, orderRoute(orderId));
  }

  release(orderId: string, reservationId: string, body: ReleaseWrite, reservationEtag: string): Promise<MutationResult<ReleaseResult>> {
    return this.send('POST', releaseResource(reservationId), body, reservationEtag, orderRoute(orderId));
  }

  private send<T>(method: 'POST', resource: string, body: unknown, etag: string | undefined, appRoute: string): Promise<MutationResult<T>> {
    try {
      return this.mutations.send<T>(this.mutations.pending.begin(method, resource, body, etag, appRoute));
    } catch (error) {
      return Promise.reject(error);
    }
  }
}
