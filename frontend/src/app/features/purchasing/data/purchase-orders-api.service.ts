import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { PurchaseDetail, PurchaseOrder, PurchaseOrderContext, PurchaseOrderWrite, PurchaseSendPreview, PurchaseDelivery, PurchaseSendWrite, PurchaseSendResult, PurchasePage, PurchaseListFilter } from './purchase-order.models';

@Injectable({ providedIn: 'root' })
export class PurchaseOrdersApiService {
  private readonly http = inject(HttpClient);
  private readonly mutations = inject(MasterDataMutationsService);

  settings(): Promise<{ currency: string }> {
    return firstValueFrom(this.http.get<{ currency: string }>('/api/v1/purchase-orders/settings'));
  }

  list(filter: PurchaseListFilter, page = 1, pageSize = 25): Promise<PurchasePage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    for (const key of ['q', 'supplierId', 'state'] as const) if (filter[key].trim()) params = params.set(key, filter[key].trim());
    for (const key of ['from', 'to'] as const) if (filter[key]) {
      const raw = filter[key]; const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(raw) ? raw : raw + 'Z');
      if (!Number.isFinite(date.getTime())) return Promise.reject(new Error('Indica fechas de creación UTC válidas.'));
      params = params.set(key, date.toISOString());
    }
    return firstValueFrom(this.http.get<PurchasePage>('/api/v1/purchase-orders', { params }));
  }

  preview(id: string): Promise<PurchaseSendPreview> { return firstValueFrom(this.http.get<PurchaseSendPreview>(`/api/v1/purchase-orders/${id}/send-preview`)); }
  delivery(id: string): Promise<PurchaseDelivery> { return firstValueFrom(this.http.get<PurchaseDelivery>(`/api/v1/purchase-orders/${id}/delivery`)); }
  async requestSend(id: string, body: PurchaseSendWrite, etag: string): Promise<PurchaseSendResult> {
    const result = await this.mutations.mutate<PurchaseSendResult>('POST', `/api/v1/purchase-orders/${id}/send`, body, etag);
    if (!result.body?.deliveryId) throw new Error('Comprueba la solicitud antes de emitir otra intención de envío.');
    return result.body;
  }

  async detail(id: string): Promise<PurchaseDetail> {
    const [response, context] = await Promise.all([
      firstValueFrom(this.http.get<PurchaseOrder>(`/api/v1/purchase-orders/${id}`, { observe: 'response' })),
      firstValueFrom(this.http.get<PurchaseOrderContext>(`/api/v1/purchase-orders/${id}/context`))
    ]);
    const etag = response.headers.get('ETag');
    if (!response.body || !etag || context.purchaseOrderVersion !== etag)
      throw new Error('El pedido cambió de revisión. Vuelve a consultarlo antes de confirmar.');
    return { resource: response.body, context, etag };
  }

  async save(id: string | null, body: PurchaseOrderWrite, etag?: string): Promise<PurchaseOrder> {
    const result = await this.mutations.mutate<{ operationId: string; resource: PurchaseOrder }>(id ? 'PUT' : 'POST',
      id ? `/api/v1/purchase-orders/${id}` : '/api/v1/purchase-orders', body, etag);
    if (!result.body?.resource) throw new Error('Comprueba el resultado antes de repetir el guardado.');
    return result.body.resource;
  }
}
