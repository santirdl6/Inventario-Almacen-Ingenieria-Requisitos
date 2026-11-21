import { Component, DestroyRef, DOCUMENT, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { visiblePurchasePolls } from '../data/purchase-polling';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { PurchaseDetail, PurchaseDelivery, purchaseStateLabel, purchaseSendLabel } from '../data/purchase-order.models';
import { SessionService } from '../../../core/session/session.service';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { apiProblem, ApiProblem } from '../../../core/http/api-problem';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';

@Component({ standalone: true, imports: [RouterLink, OperationStatusComponent], template: `
  <h1>Pedido de compra</h1><a routerLink="/compras" queryParamsHandling="preserve">Volver a compras</a>
  <app-operation-status [resource]="resource" />
  @if (problem(); as error) { <p role="alert">{{ error.detail }}</p> }
  <button type="button" [disabled]="loading()" (click)="load()">Actualizar pedido</button>
  @if (loading()) { <p role="status">Cargando pedido…</p> }
  @if (detail(); as value) {
    <h2>{{ value.resource.number }}</h2><p>Proveedor: {{ value.resource.supplierSnapshot.code }} — {{ value.resource.supplierSnapshot.name }}</p>
    @if (!value.context.supplier.isActive) { <p>Proveedor Inactivo; se conserva el documento.</p> }
    <p>Estado del pedido: {{ stateLabel(value.resource.state) }}. Envío del correo: {{ sendLabel(value.resource.sendStatus) }}.</p>
    <p>Fecha prevista general: {{ value.resource.expectedDeliveryDate }}. Moneda: {{ value.resource.currency }}.</p>
    @for (reason of value.context.sendBlockReasons; track reason) { <p>{{ reason }}</p> }
    @if (session.has('Purchasing.Manage') && value.resource.state === 'Draft' && value.resource.sendStatus === 'None') {
      <a [routerLink]="['/compras', value.resource.id, 'editar']" queryParamsHandling="preserve">Corregir borrador</a>
    }
    @if (session.has('Purchasing.Send')) { <a [routerLink]="['/compras', value.resource.id, 'enviar']" queryParamsHandling="preserve">Revisar envío al proveedor</a> }
    @if (progress(); as delivery) {
      <p>Destinatario conservado: {{ delivery.recipientEmail }}. Intentos de correo: {{ delivery.attempts }}. Último intento: {{ delivery.lastAttemptAt ?? 'Todavía no iniciado' }}.</p>
      @if (delivery.lastErrorSummary) { <p role="alert">{{ delivery.lastErrorSummary }}</p> }
      @if (delivery.acceptanceUncertain) { <p>Hubo una aceptación incierta: podría existir una copia del mismo pedido.</p> }
      <a [href]="resource+'/delivery/document'">Descargar documento conservado</a>
      <p>La aceptación SMTP no acredita entrega final ni lectura del correo.</p>
    }
    <table><caption>Líneas del pedido</caption><thead><tr><th>Artículo</th><th>Unidad</th><th>Pedido</th><th>Recibido</th><th>Pendiente</th><th>Precio unitario</th><th>Fecha prevista</th></tr></thead><tbody>
      @for (line of value.resource.lines; track line.id) {
        <tr><th>{{ line.articleSnapshot.sku }} — {{ line.articleSnapshot.name }} @if (inactive(line.articleId)) { (Inactivo) }</th>
          <td>{{ line.articleSnapshot.baseUnit }}</td><td>{{ line.orderedQuantity }}</td><td>{{ line.receivedQuantity }}</td><td>{{ line.pendingQuantity }}</td><td>{{ line.unitPrice }} {{ value.resource.currency }}</td><td>{{ line.expectedDeliveryDate }}</td></tr>
      }
    </tbody></table>
  }
` })
export class PurchaseOrderDetailPage implements OnInit {
  readonly session = inject(SessionService); private readonly api = inject(PurchaseOrdersApiService);
  private readonly mutations = inject(MasterDataMutationsService);
  private readonly route = inject(ActivatedRoute); private readonly destroy = inject(DestroyRef);
  get id(): string { return this.route.snapshot.paramMap.get('id')!; }
  get resource(): string { return `/api/v1/purchase-orders/${this.id}`; }
  readonly detail = signal<PurchaseDetail | null>(null); readonly loading = signal(false); readonly problem = signal<ApiProblem | null>(null);
  readonly progress = signal<PurchaseDelivery | null>(null);
  readonly stateLabel = purchaseStateLabel; readonly sendLabel = purchaseSendLabel; private generation = 0;
  constructor() {
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => { this.generation++; this.detail.set(null); this.progress.set(null); this.problem.set(null); this.loading.set(false); });
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation }) => {
      if (operation.actorId === this.session.user()?.id && (operation.resource === this.resource || operation.resource === this.resource + '/send')) void this.load();
    });
    this.destroy.onDestroy(() => this.generation++);
    visiblePurchasePolls(inject(DOCUMENT)).pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.session.user() && this.detail()?.resource.sendStatus === 'Pending' && !this.loading()) void this.load();
    });
  }
  ngOnInit(): void {
    if (this.route.paramMap) this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.generation++; this.detail.set(null); this.progress.set(null); void this.load();
    });
    else void this.load();
  }
  inactive(id: string): boolean { return this.detail()?.context.articles.some(value => value.id === id && !value.isActive) ?? false; }
  async load(): Promise<void> {
    const generation = ++this.generation; this.loading.set(true); this.problem.set(null);
    try {
      let value = await this.api.detail(this.id); if (generation !== this.generation) return;
      let progress: PurchaseDelivery | null = null;
      if (value.resource.sendStatus !== 'None') {
        progress = await this.api.delivery(this.id); if (generation !== this.generation) return;
        if (progress.purchaseOrderVersion !== value.etag) value = await this.api.detail(this.id);
        if (generation !== this.generation) return;
        if (progress.purchaseOrderVersion !== value.etag) throw new Error('El pedido cambió durante la consulta. Actualiza para comprobar su estado.');
      }
      this.detail.set(value); this.progress.set(progress);
    }
    catch (error) { if (generation === this.generation) this.problem.set(apiProblem(error)); }
    finally { if (generation === this.generation) this.loading.set(false); }
  }
}
