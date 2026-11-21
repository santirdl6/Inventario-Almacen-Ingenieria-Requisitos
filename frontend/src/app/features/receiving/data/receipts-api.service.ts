import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';

export interface AllocationWrite { id: string; locationId: string; lotCode?: string | null; quantity: string; locationSnapshot?: { code: string } }
export interface ReceiptLineWrite { id: string; purchaseOrderLineId: string; reportedQuantity: string; acceptedQuantity: string;
  discrepancyReason?: string | null; allocations: AllocationWrite[] }
export interface IncidentWrite { id: string; receiptLineId?: string | null; kind: string; observedDescription: string;
  quantity?: string | null; observedUnit?: string | null; reason: string }
export interface ReceiptWrite { purchaseOrderId: string; occurredAt: string; supplierDocumentReference?: string | null;
  supportNotApplicableReason?: string | null; reason?: string | null; lines: ReceiptLineWrite[]; incidents: IncidentWrite[] }
export interface ReceiptSummary { id: string; purchaseOrderId: string; state: 'Draft' | 'Confirmed'; occurredAt: string;
  supplierDocumentReference?: string | null }
export interface Receipt extends ReceiptSummary, ReceiptWrite { movementId: string | null; counterpartyReference: string | null;
  recordedAt: string | null; actorId: string | null; corrections?: { id: string; movementId: string; reason: string; reference: string | null; recordedAt: string;
    lines: { receiptAllocationId: string; quantity: string }[] }[] }
export interface ReceiptContext { receiptVersion: string; purchaseOrderVersion: string; orderNumber: string;
  lines: { purchaseOrderLineId: string; sku: string; name: string; baseUnit: string; tracksLot: boolean;
    orderedQuantity: string; receivedQuantity: string; pendingQuantity: string; projectedPendingQuantity: string | null }[] }

@Injectable({ providedIn: 'root' })
export class ReceiptsApiService {
  private readonly http = inject(HttpClient);
  readonly mutations = inject(MasterDataMutationsService);
  readonly path = '/api/v1/receipts';

  list(filters: Record<string, string> = {}, page = 1) {
    const params = new HttpParams({ fromObject: { ...Object.fromEntries(Object.entries(filters).filter(([, value]) => value)), page } });
    return firstValueFrom(this.http.get<{ items: ReceiptSummary[]; totalCount: number; page: number; pageSize: number }>(this.path, { params }));
  }
  async detail(id: string) {
    const path = `${this.path}/${id}`;
    const [detail, context] = await Promise.all([
      firstValueFrom(this.http.get<Receipt>(path, { observe: 'response' })), firstValueFrom(this.http.get<ReceiptContext>(`${path}/context`))
    ]);
    const etag = detail.headers.get('ETag');
    if (!detail.body || !etag || context.receiptVersion !== etag) throw new Error('La recepción cambió de revisión. Vuelve a consultarla.');
    return { receipt: detail.body, context, etag };
  }
  async save(id: string | undefined, value: ReceiptWrite, etag?: string) {
    const result = await this.mutations.mutate<{ resource: Receipt }>(id ? 'PUT' : 'POST', id ? `${this.path}/${id}` : this.path, value, etag);
    if (!result.body?.resource || !result.etag) throw new Error('Comprueba el resultado del guardado.');
    return { receipt: result.body.resource, etag: result.etag };
  }
  async confirm(id: string, etag: string) {
    const result = await this.mutations.mutate<{ resource: Receipt }>('POST', `${this.path}/${id}/confirm`, {}, etag);
    if (!result.body?.resource || !result.etag) throw new Error('Comprueba el resultado de la confirmación.');
    return { receipt: result.body.resource, etag: result.etag };
  }
  async correct(movementId: string, etag: string, correctionId: string, reason: string, reference: string | null, lines: { receiptAllocationId: string; quantity: string }[]) {
    return this.mutations.mutate<{ receiptId: string }>('POST', `/api/v1/movements/${movementId}/corrections`,
      { correctionId, reason, reference, lines }, etag);
  }
}
