import { Component, DestroyRef, DOCUMENT, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { visiblePurchasePolls } from '../data/purchase-polling';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { PurchaseDelivery, PurchaseDetail, PurchaseSendPreview, purchaseSendLabel, purchaseStateLabel } from '../data/purchase-order.models';
import { SessionService } from '../../../core/session/session.service';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { apiProblem, ApiProblem } from '../../../core/http/api-problem';

@Component({ standalone: true, imports: [RouterLink, OperationStatusComponent], template: `
  <h1>Revisar envío al proveedor</h1><a [routerLink]="['/compras', id]" queryParamsHandling="preserve">Volver al pedido</a>
  <app-operation-status [resource]="resource" />
  @if (problem(); as issue) { <p role="alert" id="send-error" tabindex="-1">{{ issue.detail }}</p> }
  @if (notice()) { <p role="status">{{ notice() }}</p> }
  @if (loading()) { <p role="status">Cargando revisión del envío…</p> }
  <button type="button" [disabled]="loading() || busy()" (click)="load()">Actualizar estado y revisión</button>
  @if (current(); as value) {
    <h2>{{ value.resource.number }}</h2>
    <p>Estado del pedido: {{ stateLabel(value.resource.state) }}. Envío del correo: {{ sendLabel(progress()?.sendStatus ?? (knownRequested() ? 'Pending' : value.resource.sendStatus)) }}.</p>
    @if (review(); as preview) {
      <p>Destinatario que se confirmará: {{ preview.recipientEmail ?? 'Sin correo válido' }}</p><p>Remitente: {{ preview.senderEmail }}</p>
      @for (reason of preview.blockReasons; track reason) { <p role="alert">{{ reason }}</p> }
    }
    @if (progress(); as delivery) {
      <p>Destinatario conservado: {{ delivery.recipientEmail ?? 'Sin solicitud' }}. Remitente conservado: {{ delivery.senderEmail ?? 'Sin solicitud' }}.</p>
      <p>Intentos: {{ delivery.attempts }}. Solicitud: {{ delivery.requestedAt }}. Aceptación SMTP: {{ delivery.acceptedAt ?? 'No confirmada' }}.</p>
      @if (delivery.lastErrorSummary) { <p role="alert">{{ delivery.lastErrorSummary }}</p> }
      @if (delivery.acceptanceUncertain) { <p>La aceptación de algún intento no pudo comprobarse. Podría existir una copia del mismo pedido; no es otra compra.</p> }
      @if (delivery.deliveryId) { <a [href]="documentUrl">Descargar documento conservado</a> }
      @if (delivery.sendStatus === 'Failed') {
        <p>Reintentar conserva exactamente este documento, destinatario y MessageId; la acción queda registrada por separado.</p>
        <button type="button" [disabled]="!canRetry()" (click)="retry()">Reintentar envío</button>
      }
    }
    <section aria-label="Documento revisado"><h3>Documento del pedido</h3>
      <p>Proveedor: {{ value.resource.supplierSnapshot.code }} — {{ value.resource.supplierSnapshot.name }}. Moneda: {{ value.resource.currency }}. Fecha prevista general: {{ value.resource.expectedDeliveryDate }}.</p>
      <table><thead><tr><th>Línea</th><th>Artículo</th><th>Cantidad</th><th>Unidad</th><th>Precio unitario</th><th>Fecha prevista</th></tr></thead><tbody>
        @for (line of (review()?.document ?? value.resource).lines; track line.id) {
          <tr><td>{{ line.position }}</td><th>{{ line.articleSnapshot.sku }} — {{ line.articleSnapshot.name }}</th><td>{{ line.orderedQuantity }}</td><td>{{ line.articleSnapshot.baseUnit }}</td><td>{{ line.unitPrice }} {{ value.resource.currency }}</td><td>{{ line.expectedDeliveryDate }}</td></tr>
        }
      </tbody></table>
    </section>
    @if (value.resource.sendStatus === 'None' && !knownRequested()) {
      <p>Confirmar conserva el documento y destinatario de esta revisión. El envío queda pendiente hasta la aceptación del servicio de correo y el pedido dejará de admitir cambios.</p>
      <button type="button" [disabled]="!canConfirm()" (click)="confirm()">Confirmar solicitud de envío</button>
    } @else { <p>El documento y destinatario ya están congelados, también ante un error.</p> }
    <p>Aceptado por el servicio de correo no acredita entrega final, lectura ni aceptación comercial del proveedor.</p>
  }
` })
export class PurchaseOrderSendPage implements OnInit {
  private readonly api = inject(PurchaseOrdersApiService); private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionService); private readonly mutations = inject(MasterDataMutationsService);
  private readonly destroy = inject(DestroyRef); private generation = 0; private loadSequence = 0;
  readonly loading = signal(true); readonly busy = signal(false); readonly problem = signal<ApiProblem | null>(null);
  readonly notice = signal(''); readonly current = signal<PurchaseDetail | null>(null);
  readonly knownRequested = signal(false);
  readonly review = signal<PurchaseSendPreview | null>(null); readonly progress = signal<PurchaseDelivery | null>(null);
  readonly stateLabel = purchaseStateLabel; readonly sendLabel = purchaseSendLabel;
  get id(): string { return this.route.snapshot.paramMap.get('id')!; }
  get resource(): string { return `/api/v1/purchase-orders/${this.id}/send`; }
  get documentUrl(): string { return `/api/v1/purchase-orders/${this.id}/delivery/document`; }
  constructor() {
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => {
      this.generation++; this.current.set(null); this.review.set(null); this.progress.set(null);
      this.knownRequested.set(false); this.notice.set(''); this.problem.set(null); this.busy.set(false); this.loading.set(false);
    });
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation }) => {
      if (operation.actorId === this.session.user()?.id && operation.resource === this.resource) { this.knownRequested.set(true); void this.load(); }
    });
    visiblePurchasePolls(inject(DOCUMENT)).pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.session.user() && (this.progress()?.sendStatus === 'Pending' || this.knownRequested() && !this.progress()) && !this.loading() && !this.busy()) void this.load();
    });
    this.destroy.onDestroy(() => this.generation++);
  }
  ngOnInit(): void {
    if (this.route.paramMap) this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.generation++; this.current.set(null); this.review.set(null); this.progress.set(null); this.knownRequested.set(false);
      this.notice.set(''); this.busy.set(false); this.problem.set(null); void this.load();
    });
    else void this.load();
  }
  canConfirm(): boolean {
    const value = this.current(); const preview = this.review();
    return !!value && !!preview && this.session.has('Purchasing.Send') && value.resource.state === 'Draft' && value.resource.sendStatus === 'None'
      && preview.purchaseOrderVersion === value.etag && preview.blockReasons.length === 0
      && !this.mutations.pending.forResource(this.resource) && !this.loading() && !this.busy();
  }
  canRetry(): boolean {
    return this.session.has('Purchasing.Send') && this.current()?.resource.sendStatus === 'Failed' && !!this.progress()?.canRetry
      && !!this.progress()?.deliveryId && !this.loading() && !this.busy() && !this.mutations.pending.forResource(this.resource);
  }
  async load(): Promise<void> {
    const generation = this.generation; const id = this.id; const sequence = ++this.loadSequence;
    const isCurrent = () => generation === this.generation && sequence === this.loadSequence;
    this.loading.set(true); this.problem.set(null); this.review.set(null);
    try {
      let detail = await this.api.detail(id); if (!isCurrent()) return;
      if (detail.resource.sendStatus === 'None') {
        if (this.knownRequested()) throw new Error('La solicitud quedó registrada; actualiza para comprobar su estado vigente.');
        const preview = await this.api.preview(id); if (!isCurrent()) return;
        if (preview.purchaseOrderVersion !== detail.etag) throw new Error('El pedido cambió durante la revisión. Actualiza antes de confirmar.');
        this.current.set(detail); this.review.set(preview); this.progress.set(null);
      } else {
        const progress = await this.api.delivery(id); if (!isCurrent()) return;
        if (progress.purchaseOrderVersion !== detail.etag) detail = await this.api.detail(id);
        if (!isCurrent()) return;
        if (progress.purchaseOrderVersion !== detail.etag) throw new Error('El estado cambió durante la consulta. Actualiza para comprobarlo.');
        this.current.set(detail); this.progress.set(progress); this.knownRequested.set(!!progress.deliveryId);
      }
    } catch (error) { if (isCurrent()) this.problem.set(apiProblem(error)); }
    finally { if (isCurrent()) this.loading.set(false); }
  }
  async confirm(): Promise<void> {
    if (!this.canConfirm()) return;
    const generation = this.generation; const id = this.id; const detail = this.current()!; const preview = this.review()!;
    this.busy.set(true); this.problem.set(null);
    try {
      await this.api.requestSend(id, { kind: 'initial', reviewToken: preview.reviewToken }, detail.etag);
      if (generation !== this.generation) return;
      this.knownRequested.set(true); this.review.set(null); this.notice.set('Solicitud de envío registrada. Comprueba su estado vigente.');
      await this.load();
    } catch (error) {
      if (generation === this.generation) {
        this.problem.set(apiProblem(error)); this.review.set(null);
        setTimeout(() => { if (generation === this.generation) document.getElementById('send-error')?.focus(); });
      }
    } finally { if (generation === this.generation) this.busy.set(false); }
  }
  async retry(): Promise<void> {
    if (!this.canRetry()) return;
    const generation = this.generation; const current = this.current()!; const delivery = this.progress()!;
    this.busy.set(true); this.problem.set(null);
    try {
      await this.api.requestSend(current.resource.id, { kind: 'retry', deliveryId: delivery.deliveryId! }, current.etag);
      if (generation !== this.generation) return;
      this.knownRequested.set(true); this.notice.set('Reintento registrado sobre la misma entrega y documento.'); await this.load();
    } catch (error) { if (generation === this.generation) this.problem.set(apiProblem(error)); }
    finally { if (generation === this.generation) this.busy.set(false); }
  }
}
