import { ChangeDetectorRef, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { apiProblem } from '../../../core/http/api-problem';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { SessionService } from '../../../core/session/session.service';
import { LocationsApi } from '../../locations/data/locations-api.service';
import { LocationSummary } from '../../locations/data/location.models';
import { PurchaseOrdersApiService } from '../../purchasing/data/purchase-orders-api.service';
import { PurchaseLine, PurchasePage, exactDecimal } from '../../purchasing/data/purchase-order.models';
import { Receipt, ReceiptWrite, ReceiptsApiService } from '../data/receipts-api.service';
import { ReceiptDiscrepanciesComponent } from './receipt-discrepancies.component';

@Component({ standalone: true, imports: [FormsModule, RouterLink, OperationStatusComponent, ReceiptDiscrepanciesComponent], template: `
  <h1>{{ id() ? 'Recepción' : 'Nueva recepción' }}</h1><a routerLink="/recepciones">Volver a recepciones</a>
  <app-operation-status [resource]="resource()" />
  @if (error()) { <p role="alert" tabindex="-1" #failure>{{error()}}</p> }
  @if (id()) { <p>Identificador: {{id()}}</p> }
  @if (draft.purchaseOrderId) { <a [routerLink]="['/compras', draft.purchaseOrderId]">Consultar pedido {{orderNumber}}</a> }
  @if (receipt()?.state === 'Confirmed') { <p role="status">Recepción confirmada. Entrada {{receipt()?.movementId}}.</p> }
  @else if (id()) { <p>Borrador · Sin efectos en existencias.</p> }
  @if (id()) { <button type="button" (click)="load(id())" [disabled]="busy()">Actualizar recepción</button> }
  @if (!id()) {
    <fieldset [disabled]="busy() || pending()"><legend>Selecciona un pedido enviado</legend>
      <label for="purchase-state">Estado del pedido</label><select id="purchase-state" [(ngModel)]="purchaseState" (ngModelChange)="searchPurchases(1)"><option value="Sent">Enviado</option><option value="PartiallyReceived">Recibido parcialmente</option></select>
      <label for="purchase-number">Número de pedido</label><input id="purchase-number" [(ngModel)]="numberQuery">
      <button type="button" (click)="searchPurchases(1)">Buscar pedidos</button>
      @for (purchase of orders()?.items; track purchase.id) { <button type="button" (click)="selectPurchase(purchase.id)">{{purchase.number}} · {{purchase.supplierSnapshot.name}}</button> }
      <button type="button" [disabled]="(orders()?.page ?? 1) <= 1" (click)="searchPurchases((orders()?.page ?? 1)-1)">Pedidos anteriores</button>
      <button type="button" [disabled]="!orders() || orders()!.page * orders()!.pageSize >= orders()!.totalCount" (click)="searchPurchases(orders()!.page+1)">Más pedidos</button>
    </fieldset>
  }
  @if (draft.purchaseOrderId && canWrite()) {
    <fieldset [disabled]="!editable() || pending() || reviewed()"><legend>Datos de recepción</legend>
      <label for="occurred">Fecha y hora del hecho</label><input id="occurred" type="datetime-local" [(ngModel)]="occurred">
      <label for="support">Albarán o soporte</label><input id="support" [(ngModel)]="draft.supplierDocumentReference">
      <label for="absence">Motivo de ausencia de soporte</label><input id="absence" [(ngModel)]="draft.supportNotApplicableReason">
      <label for="reason">Motivo de entrada</label><input id="reason" [(ngModel)]="draft.reason">
      @for (source of purchaseLines; track source.id) {
        @if (source.pendingQuantity !== '0.000' && !hasLine(source.id)) { <button type="button" (click)="addLine(source.id)">Añadir {{source.articleSnapshot.sku}} · línea {{source.position}}</button> }
      }
      @for (line of draft.lines; track line.id; let i = $index) {
        <fieldset><legend>Línea {{i+1}}: {{label(line.purchaseOrderLineId)}}</legend>
          <p>Pedido {{source(line.purchaseOrderLineId)?.orderedQuantity}}; recibido {{source(line.purchaseOrderLineId)?.receivedQuantity}}; pendiente {{source(line.purchaseOrderLineId)?.pendingQuantity}}.</p>
          <label [for]="'reported-'+i">Cantidad reportada · línea {{i+1}}</label><input [id]="'reported-'+i" inputmode="decimal" [(ngModel)]="line.reportedQuantity">
          <label [for]="'accepted-'+i">Cantidad aceptada · línea {{i+1}}</label><input [id]="'accepted-'+i" inputmode="decimal" [(ngModel)]="line.acceptedQuantity">
          <label [for]="'difference-'+i">Motivo de diferencia · línea {{i+1}}</label><input [id]="'difference-'+i" [(ngModel)]="line.discrepancyReason">
          @for (allocation of line.allocations; track allocation.id; let j = $index) {
            <label [for]="'place-'+i+'-'+j">Ubicación · línea {{i+1}}, asignación {{j+1}}</label>
            <select [id]="'place-'+i+'-'+j" [(ngModel)]="allocation.locationId"><option value="">Selecciona</option>
              @if (allocation.locationId && !hasDestination(allocation.locationId)) { <option [value]="allocation.locationId">{{allocation.locationSnapshot?.code ?? allocation.locationId}}</option> }
              @for (place of destinations(); track place.id) { <option [value]="place.id">{{place.code}} · {{place.aisle}}/{{place.rack}}/{{place.level}}</option> }
            </select>
            @if (source(line.purchaseOrderLineId)?.articleSnapshot?.tracksLot || allocation.lotCode) {
              <label [for]="'lot-'+i+'-'+j">Lote · línea {{i+1}}, asignación {{j+1}}</label><input [id]="'lot-'+i+'-'+j" [(ngModel)]="allocation.lotCode">
            }
            <label [for]="'qty-'+i+'-'+j">Cantidad asignada · línea {{i+1}}, asignación {{j+1}}</label><input [id]="'qty-'+i+'-'+j" inputmode="decimal" [(ngModel)]="allocation.quantity">
            <button type="button" (click)="line.allocations.splice(j,1)">Retirar asignación {{j+1}}</button>
          }
          <button type="button" (click)="line.allocations.push({id: uuid(), locationId: '', quantity:'0'})">Añadir asignación · línea {{i+1}}</button>
          <button type="button" (click)="draft.lines.splice(i,1)">Retirar línea {{i+1}}</button>
        </fieldset>
      }
      <app-receipt-discrepancies [incidents]="draft.incidents" [readonly]="!editable()"
        (add)="draft.incidents.push({id:uuid(),kind:'Mercancía ajena',observedDescription:'',reason:''})"
        (remove)="removeIncident($event)" />
      <label for="location-search">Buscar ubicación</label><input id="location-search" [(ngModel)]="locationSearch">
      <button type="button" (click)="loadLocations(1)">Buscar ubicaciones</button>
      <button type="button" [disabled]="locationPage <= 1" (click)="loadLocations(locationPage-1)">Ubicaciones anteriores</button>
      <button type="button" [disabled]="locationPage*25 >= locationTotal" (click)="loadLocations(locationPage+1)">Más ubicaciones</button>
      <button type="button" (click)="save()">Guardar borrador</button>
      @if (id()) { <button type="button" (click)="save(true)">Guardar y revisar</button> }
    </fieldset>
  } @else if (receipt(); as saved) {
    <p>Fecha del hecho: {{saved.occurredAt}}. Soporte: {{saved.supplierDocumentReference ?? saved.supportNotApplicableReason}}.</p>
    <p>Motivo: {{saved.reason}}. Contraparte: {{saved.counterpartyReference}}.</p>
    @if (saved.recordedAt) { <p>Confirmada por {{saved.actorId}} el {{saved.recordedAt}}.</p> }
    @for (line of saved.lines; track line.id) {
      <h2>{{label(line.purchaseOrderLineId)}}</h2>
      <p>Reportado {{line.reportedQuantity}} · Aceptado {{line.acceptedQuantity}} @if (line.discrepancyReason) { · {{line.discrepancyReason}} }</p>
      @for (assignment of line.allocations; track assignment.id) {
        <p>{{assignment.locationSnapshot?.code ?? destination(assignment.locationId)}} · {{assignment.lotCode ?? 'Sin lote'}}: {{assignment.quantity}}</p>
      }
    }
    @for (incident of saved.incidents; track incident.id) { <p>{{incident.kind}}: {{incident.observedDescription}} · {{incident.reason}}</p> }
    @for (correction of saved.corrections ?? []; track correction.id) {
      <p>Compensación {{correction.id}} · {{correction.reason}} · {{correction.reference ?? 'Sin referencia adicional'}} · {{correction.recordedAt}}</p>
      @for (item of correction.lines; track item.receiptAllocationId) { <p>Asignación {{item.receiptAllocationId}}: {{item.quantity}}</p> }
    }
    @if (saved.state === 'Confirmed' && session.has('Inventory.Adjust')) {
      <section aria-label="Corregir recepción"><h2>Compensar una entrada</h2>
        <label for="correction-allocation">Asignación original</label><select id="correction-allocation" [(ngModel)]="correctionAllocation">
          <option value="">Selecciona</option>
          @for (line of saved.lines; track line.id) { @for (assignment of line.allocations; track assignment.id) {
            <option [value]="assignment.id">{{label(line.purchaseOrderLineId)}} · {{assignment.locationSnapshot?.code ?? assignment.locationId}} · {{assignment.quantity}}</option>
          } }
        </select>
        <label for="correction-quantity">Cantidad a compensar</label><input id="correction-quantity" [(ngModel)]="correctionQuantity" inputmode="decimal">
        <label for="correction-reason">Motivo de compensación</label><input id="correction-reason" [(ngModel)]="correctionReason">
        <label for="correction-reference">Referencia de compensación (opcional)</label><input id="correction-reference" [(ngModel)]="correctionReference">
        <button type="button" (click)="correct()" [disabled]="busy() || correctionPending() || !correctionAllocation || !correctionReason">Confirmar compensación</button>
        <app-operation-status [resource]="correctionResource()" />
      </section>
    }
  }
  @if (reviewed(); as review) {
    <section aria-label="Revisar recepción"><h2>Revisar recepción</h2>
      @for (line of draft.lines; track line.id) {
        <p>{{label(line.purchaseOrderLineId)}}: reportado {{line.reportedQuantity}}, aceptado {{line.acceptedQuantity}}, pendiente previsto {{projected(line.purchaseOrderLineId, review.context)}}.</p>
        @for (allocation of line.allocations; track allocation.id) { <p>{{destination(allocation.locationId)}} · {{allocation.lotCode ?? 'Sin lote'}}: {{allocation.quantity}}</p> }
      }
      <button type="button" (click)="reviewed.set(null)" [disabled]="busy()">Volver al borrador</button>
      <button type="button" (click)="confirm()" [disabled]="busy() || pending() || !positive()">Confirmar recepción</button>
    </section>
  }
  ` })
export class ReceiptFormPage implements OnInit {
  private readonly api = inject(ReceiptsApiService);
  private readonly purchases = inject(PurchaseOrdersApiService);
  private readonly locations = inject(LocationsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly session = inject(SessionService);
  private readonly destroy = inject(DestroyRef);
  private readonly changeDetector = inject(ChangeDetectorRef);
  readonly receipt = signal<Receipt | null>(null);
  readonly id = signal<string | undefined>(undefined);
  readonly orders = signal<PurchasePage | null>(null);
  readonly destinations = signal<readonly LocationSummary[]>([]);
  readonly reviewed = signal<Awaited<ReturnType<ReceiptsApiService['detail']>> | null>(null);
  readonly error = signal(''); readonly busy = signal(false);
  #epoch = 0; #etag?: string;
  locationPage = 1; locationTotal = 0; locationSearch = '';
  readonly uuid = () => crypto.randomUUID();
  purchaseState = 'Sent'; numberQuery = ''; orderNumber = '';
  purchaseLines: PurchaseLine[] = [];
  correctionAllocation = ''; correctionQuantity = ''; correctionReason = ''; correctionReference = '';
  occurred = this.localDate(new Date().toISOString());
  draft: ReceiptWrite = this.empty();

  ngOnInit(): void {
    this.destroy.onDestroy(() => this.#epoch++);
    this.session.loggedOut.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.#epoch++; this.draft = this.empty(); this.reviewed.set(null); this.receipt.set(null); this.orders.set(null); this.purchaseLines = []; this.orderNumber = ''; this.#etag = undefined;
      this.correctionAllocation = ''; this.correctionQuantity = ''; this.correctionReason = ''; this.correctionReference = '';
    });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params => void this.load(params.get('id') ?? undefined));
    this.api.mutations.recovered.pipe(takeUntilDestroyed(this.destroy)).subscribe(({ operation, outcome }) => {
      if (operation.resource === this.correctionResource() && this.id()) { void this.load(this.id()); return; }
      if (operation.resource !== this.resource() && operation.resource !== `${this.resource()}/confirm`) return;
      const id = (outcome.body as { resource?: Receipt } | null)?.resource?.id;
      if (id) void this.go(id);
    });
  }
  resource() { return this.id() ? `${this.api.path}/${this.id()}` : this.api.path; }
  pending() { return !!this.api.mutations.pending.forResource(this.resource()); }
  correctionResource() { return this.receipt()?.movementId ? `/api/v1/movements/${this.receipt()!.movementId}/corrections` : ''; }
  correctionPending() { return !!this.api.mutations.pending.forResource(this.correctionResource()); }
  canWrite() { return this.receipt()?.state !== 'Confirmed' && this.session.has('Receiving.Record'); }
  editable() { return !this.busy() && this.canWrite(); }
  source(id: string) { return this.purchaseLines.find(value => value.id === id); }
  label(id: string) { const value = this.source(id); return value ? `${value.articleSnapshot.sku} · ${value.articleSnapshot.name} · ${value.articleSnapshot.baseUnit}` : id; }
  projected(id: string, context: Awaited<ReturnType<ReceiptsApiService['detail']>>['context']) { return context.lines.find(value => value.purchaseOrderLineId === id)?.projectedPendingQuantity; }
  destination(id: string) { return this.destinations().find(value => value.id === id)?.code ?? id; }
  hasDestination(id: string) { return this.destinations().some(value => value.id === id); }
  hasLine(id: string) { return this.draft.lines.some(line => line.purchaseOrderLineId === id); }
  positive() { return this.draft.lines.some(line => /[1-9]/.test(line.acceptedQuantity)); }
  addLine(id: string) { this.draft.lines.push({ id: this.uuid(), purchaseOrderLineId: id, reportedQuantity: '0', acceptedQuantity: '0', allocations: [] }); }
  removeIncident(id: string) { this.draft.incidents = this.draft.incidents.filter(item => item.id !== id); }
  async load(id: string | undefined) {
    this.id.set(id); this.reviewed.set(null); this.draft = this.empty(); this.receipt.set(null); this.#etag = undefined;
    await this.run(async epoch => {
      if (!id) { await this.searchPurchases(1, epoch); return; }
      const result = await this.api.detail(id);
      if (epoch !== this.#epoch) return;
      this.receipt.set(result.receipt); this.#etag = result.etag;
      const { purchaseOrderId, occurredAt, supplierDocumentReference, supportNotApplicableReason, reason, lines, incidents } = result.receipt;
      this.draft = { purchaseOrderId, occurredAt, supplierDocumentReference, supportNotApplicableReason, reason, lines: structuredClone(lines), incidents: structuredClone(incidents) };
      this.occurred = this.localDate(occurredAt);
      await Promise.all([this.selectPurchase(purchaseOrderId, epoch), this.loadLocations(1, epoch)]);
    });
  }
  async searchPurchases(page: number, epoch = this.#epoch) {
    try {
      const result = await this.purchases.list({ q: this.numberQuery, state: this.purchaseState, supplierId: '', from: '', to: '' }, page);
      if (epoch === this.#epoch) this.orders.set(result);
    } catch (error) { if (epoch === this.#epoch) this.error.set(apiProblem(error).detail); }
  }
  async selectPurchase(id: string, epoch = this.#epoch) {
    try {
      const { resource } = await this.purchases.detail(id);
      if (epoch !== this.#epoch) return;
      if (resource.sendStatus !== 'Accepted') throw new Error('El envío del pedido debe estar aceptado.');
      if (this.draft.purchaseOrderId !== id) { this.draft = { ...this.empty(), purchaseOrderId: id }; await this.loadLocations(1, epoch); }
      if (epoch === this.#epoch) {
        this.purchaseLines = resource.lines; this.orderNumber = resource.number;
        this.changeDetector.markForCheck();
      }
    } catch (error) { if (epoch === this.#epoch) this.error.set(apiProblem(error).detail); }
  }
  async loadLocations(page: number, epoch = this.#epoch) {
    try {
      const result = await firstValueFrom(this.locations.list({ search: this.locationSearch, status: 'active', page }));
      if (epoch === this.#epoch) { this.destinations.set(result.items); this.locationPage = result.page; this.locationTotal = result.totalCount; }
    } catch (error) { if (epoch === this.#epoch) this.error.set(apiProblem(error).detail); }
  }
  async save(review = false) {
    if (!this.editable() || this.pending()) return;
    await this.run(async epoch => {
      const body: ReceiptWrite = { ...this.draft, occurredAt: new Date(this.occurred).toISOString(),
        lines: this.draft.lines.map(line => ({ id: line.id, purchaseOrderLineId: line.purchaseOrderLineId,
          reportedQuantity: exactDecimal(line.reportedQuantity, 3), acceptedQuantity: exactDecimal(line.acceptedQuantity, 3),
          discrepancyReason: line.discrepancyReason, allocations: line.allocations.map(value => ({ id: value.id, locationId: value.locationId, lotCode: value.lotCode, quantity: exactDecimal(value.quantity, 3) })) })),
        incidents: this.draft.incidents.map(({id, receiptLineId, kind, observedDescription, quantity, observedUnit, reason}) => ({id, receiptLineId, kind, observedDescription, quantity: quantity?.trim() ? exactDecimal(quantity, 3) : null, observedUnit, reason})) };
      const result = await this.api.save(this.id(), body, this.#etag);
      if (epoch !== this.#epoch) return;
      this.#etag = result.etag; this.receipt.set(result.receipt);
      if (!this.id()) await this.go(result.receipt.id);
      else if (review) {
        const next = await this.api.detail(result.receipt.id);
        if (epoch === this.#epoch && next.etag === result.etag) this.reviewed.set(next);
        else if (epoch === this.#epoch) throw new Error('La recepción cambió. Actualiza antes de confirmar.');
      }
    });
  }
  async confirm() {
    const review = this.reviewed(); const id = this.id();
    if (!id || !review || !this.editable() || this.pending() || !this.positive()) return;
    await this.run(async epoch => {
      const result = await this.api.confirm(id, review.etag);
      if (epoch !== this.#epoch) return;
      this.receipt.set(result.receipt); this.reviewed.set(null); this.#etag = result.etag;
      await this.selectPurchase(result.receipt.purchaseOrderId, epoch);
    });
  }
  async correct() {
    const id = this.id(); const movement = this.receipt()?.movementId; const tag = this.#etag;
    if (!id || !movement || !tag || !this.session.has('Inventory.Adjust') || this.busy() || this.correctionPending()) return;
    await this.run(async epoch => {
      await this.api.correct(movement, tag, this.uuid(), this.correctionReason, this.correctionReference || null, [{ receiptAllocationId: this.correctionAllocation,
        quantity: exactDecimal(this.correctionQuantity, 3) }]);
      if (epoch === this.#epoch) { this.correctionAllocation = ''; this.correctionQuantity = ''; this.correctionReason = ''; this.correctionReference = ''; await this.load(id); }
    });
  }
  private async go(id: string) { if (this.id() === id) await this.load(id); else await this.router.navigate(['/recepciones', id]); }
  private async run(work: (epoch: number) => Promise<void>) {
    const epoch = ++this.#epoch; this.busy.set(true); this.error.set('');
    try { await work(epoch); } catch (error) { if (epoch === this.#epoch) this.error.set(apiProblem(error).detail); }
    finally { if (epoch === this.#epoch) this.busy.set(false); }
  }
  private empty(): ReceiptWrite { return { purchaseOrderId: '', occurredAt: '', lines: [], incidents: [] }; }
  private localDate(value: string) { const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset()*60_000).toISOString().slice(0, 16); }
}
