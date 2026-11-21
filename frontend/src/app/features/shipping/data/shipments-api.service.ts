import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService, MutationResult } from '../../../core/http/master-data-mutations.service';
import {
  CustomerOrderLookup, SHIPMENTS, ShipmentConfirmation, ShipmentContext, ShipmentDetail, ShipmentMutation, ShipmentPage,
  ShipmentView, ShipmentWrite, confirmResource, orderShipmentsRoute, shipmentResource, shipmentRoute
} from './shipping.models';

/** El detalle y el contexto se leyeron en revisiones distintas y no coincidieron tras reintentar. */
export class ShipmentChangedError extends Error {
  constructor() { super('La salida cambió durante la consulta. Vuelve a cargarla.'); }
}

/**
 * HU-03 T014 · Salidas. Las lecturas siguen el patrón de HU-05 (detalle con ETag + contexto sin
 * ETag, releídos hasta coincidir). Las mutaciones usan el transporte común de A
 * (PendingOperationStore.begin + MasterDataMutationsService.send) con appRoute, sin
 * idempotent-intent.ts: clave, cuerpo congelado e If-Match se conservan en la intención (D-14).
 */
@Injectable({ providedIn: 'root' })
export class ShipmentsApiService {
  private readonly http = inject(HttpClient);
  private readonly mutations = inject(MasterDataMutationsService);

  lookup(externalReference: string): Promise<CustomerOrderLookup> {
    const params = new HttpParams().set('externalReference', externalReference);
    return firstValueFrom(this.http.get<CustomerOrderLookup>('/api/v1/customer-orders/lookup', { params }));
  }

  /** Dos lecturas de la misma revisión; si no coinciden, vuelve a leer ambas (hasta 3 veces). */
  async detail(id: string): Promise<ShipmentDetail> {
    for (let attempt = 0; attempt < 3; attempt++) {
      const response = await firstValueFrom(this.http.get<ShipmentView>(shipmentResource(id), { observe: 'response' }));
      const context = await firstValueFrom(this.http.get<ShipmentContext>(`${shipmentResource(id)}/context`));
      const etag = response.headers.get('ETag');
      if (response.body && etag && context.shipmentVersion === etag) return { shipment: response.body, context, etag };
    }
    throw new ShipmentChangedError();
  }

  listByOrder(orderId: string, page = 1): Promise<ShipmentPage> {
    const params = new HttpParams().set('customerOrderId', orderId).set('page', page).set('pageSize', 25);
    return firstValueFrom(this.http.get<ShipmentPage>(SHIPMENTS, { params }));
  }

  create(orderId: string, body: ShipmentWrite): Promise<MutationResult<ShipmentMutation>> {
    return this.send('POST', SHIPMENTS, body, undefined, orderShipmentsRoute(orderId));
  }

  update(id: string, body: ShipmentWrite, etag: string): Promise<MutationResult<ShipmentMutation>> {
    return this.send('PUT', shipmentResource(id), body, etag, shipmentRoute(id));
  }

  remove(id: string, etag: string): Promise<MutationResult<null>> {
    return this.send('DELETE', shipmentResource(id), {}, etag, shipmentRoute(id));
  }

  confirm(id: string, orderVersion: string, etag: string): Promise<MutationResult<ShipmentConfirmation>> {
    return this.send('POST', confirmResource(id), { orderVersion }, etag, shipmentRoute(id));
  }

  private send<T>(method: 'POST' | 'PUT' | 'DELETE', resource: string, body: unknown, etag: string | undefined,
    appRoute: string): Promise<MutationResult<T>> {
    try {
      return this.mutations.send<T>(this.mutations.pending.begin(method, resource, body, etag, appRoute));
    } catch (error) {
      return Promise.reject(error);
    }
  }
}
