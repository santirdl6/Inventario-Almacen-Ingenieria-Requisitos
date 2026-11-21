import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { apiProblem } from '../../../core/http/api-problem';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { SessionService } from '../../../core/session/session.service';
import { exactDecimal } from '../../purchasing/data/purchase-order.models';

interface Scope { id: string; state: string; expectedVouchers: string[]; purchaseOrderIds: string[]; locationIds: string[];
  differences: { id: string; description: string; resolved: boolean; resolutionReference: string | null }[];
  journalEvidence: string | null; stockEvidence: string | null; purchaseEvidence: string | null }
@Component({ standalone: true, imports: [FormsModule, RouterLink, OperationStatusComponent], template: `
  <h1>Conciliar contingencia</h1><a routerLink="/recepciones">Volver a recepciones</a>
  @if (error()) { <p role="alert">{{error()}}</p> }
  @if (message()) { <p role="status">{{message()}}</p> }
  @if (session.has('Receiving.Record') && session.has('Inventory.Adjust')) {
    <fieldset [disabled]="busy()"><legend>Registrar un comprobante de recepción</legend>
      <label for="voucher">Comprobante</label><input id="voucher" [(ngModel)]="voucher">
      <label for="responsible">Responsable declarado</label><input id="responsible" [(ngModel)]="responsible">
      <label for="purchase">Identificador de compra</label><input id="purchase" [(ngModel)]="purchaseId">
      <label for="purchase-line">Identificador de línea de compra</label><input id="purchase-line" [(ngModel)]="purchaseLineId">
      <label for="place">Identificador de ubicación</label><input id="place" [(ngModel)]="locationId">
      <label for="lot">Lote, cuando corresponda</label><input id="lot" [(ngModel)]="lot">
      <label for="quantity">Cantidad aceptada y reportada</label><input id="quantity" [(ngModel)]="quantity" inputmode="decimal">
      <label for="happened">Fecha y hora del hecho</label><input id="happened" type="datetime-local" [(ngModel)]="occurred">
      <label for="support">Documento de soporte</label><input id="support" [(ngModel)]="support">
      <label for="reason">Motivo</label><input id="reason" [(ngModel)]="reason">
      <button type="button" (click)="reconcile()" [disabled]="pending('/api/v1/contingencies/reconcile')">Incorporar comprobante</button>
      <app-operation-status resource="/api/v1/contingencies/reconcile" />
    </fieldset>
  }
  <fieldset [disabled]="busy()"><legend>Alcance suspendido</legend>
    <p>Registra las compras, ubicaciones y comprobantes separados por comas. La reapertura se decide por separado.</p>
    <label for="scope-purchases">Identificadores de compras</label><input id="scope-purchases" [(ngModel)]="scopePurchases">
    <label for="scope-locations">Identificadores de ubicaciones</label><input id="scope-locations" [(ngModel)]="scopeLocations">
    <label for="scope-vouchers">Comprobantes esperados</label><input id="scope-vouchers" [(ngModel)]="scopeVouchers">
    <label for="journal">Evidencia del diario</label><input id="journal" [(ngModel)]="journal">
    <label for="stock">Evidencia de existencias</label><input id="stock" [(ngModel)]="stock">
    <label for="purchase-evidence">Evidencia de compra</label><input id="purchase-evidence" [(ngModel)]="purchaseEvidence">
    <label for="difference">Diferencia identificada</label><input id="difference" [(ngModel)]="difference">
    <label for="resolution">Referencia de resolución</label><input id="resolution" [(ngModel)]="resolution">
    <label for="resolved">Diferencia resuelta</label><input id="resolved" type="checkbox" [(ngModel)]="resolved">
    @for (item of otherDifferences; track item.id; let index = $index) {
      <fieldset><legend>Diferencia adicional {{index+1}}</legend>
        <label [for]="'other-description-'+index">Descripción</label><input [id]="'other-description-'+index" [(ngModel)]="item.description">
        <label [for]="'other-resolution-'+index">Referencia de resolución</label><input [id]="'other-resolution-'+index" [(ngModel)]="item.resolutionReference">
        <label [for]="'other-resolved-'+index">Diferencia resuelta</label><input [id]="'other-resolved-'+index" type="checkbox" [(ngModel)]="item.resolved">
      </fieldset>
    }
    <button type="button" (click)="addDifference()">Añadir diferencia</button>
    <button type="button" (click)="saveScope()" [disabled]="pending(scopeResource())">{{scope() ? 'Guardar revisión del alcance' : 'Registrar alcance'}}</button>
    @if (scope(); as current) {
      <p>Alcance {{current.id}} · {{current.state === 'Suspended' ? 'Suspendido' : 'Reapertura autorizada'}}</p>
      <button type="button" (click)="reopen()" [disabled]="current.state !== 'Suspended' || pending(scopeResource())">Autorizar reapertura</button>
    }
    <app-operation-status [resource]="scopeResource()" />
  </fieldset>
  ` })
export class ReceiptContingencyPage implements OnInit {
  private readonly http = inject(HttpClient); private readonly mutations = inject(MasterDataMutationsService);
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router); private readonly destroy = inject(DestroyRef);
  readonly session = inject(SessionService); readonly busy = signal(false); readonly error = signal(''); readonly message = signal('');
  readonly scope = signal<Scope | null>(null); private etag?: string; private differenceId: string = crypto.randomUUID(); private generation = 0;
  voucher = ''; responsible = ''; purchaseId = ''; purchaseLineId = ''; locationId = ''; lot = ''; quantity = ''; occurred = '';
  support = ''; reason = ''; scopePurchases = ''; scopeLocations = ''; scopeVouchers = ''; journal = ''; stock = ''; purchaseEvidence = '';
  difference = ''; resolution = ''; resolved = false;
  otherDifferences: Scope['differences'] = [];
  ngOnInit() {
    this.destroy.onDestroy(() => this.generation++);
    this.session.loggedOut.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.generation++; this.scope.set(null); this.etag = undefined; this.error.set(''); this.message.set('');
      this.voucher = this.responsible = this.purchaseId = this.purchaseLineId = this.locationId = this.lot = this.quantity = this.occurred = '';
      this.support = this.reason = this.scopePurchases = this.scopeLocations = this.scopeVouchers = this.journal = this.stock = this.purchaseEvidence = '';
      this.difference = this.resolution = ''; this.resolved = false; this.otherDifferences = [];
    });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params => {
      const id = params.get('id'); if (id) void this.load(id);
    });
    this.mutations.recovered.pipe(takeUntilDestroyed(this.destroy)).subscribe(({ operation, outcome }) => {
      if (operation.resource === '/api/v1/contingencies/reconcile') {
        const id = (outcome.body as { resource?: { id?: string } } | null)?.resource?.id;
        if (id) this.message.set(`Comprobante incorporado a la recepción ${id}. La reapertura se revisa por separado.`);
        return;
      }
      if (!operation.resource.startsWith('/api/v1/contingencies/receiving-scopes')) return;
      const id = (outcome.body as { resource?: Scope } | null)?.resource?.id;
      if (id) void this.router.navigate(['/recepciones/contingencia', id]).then(() => this.load(id));
    });
  }
  scopeResource() { return this.scope() ? `/api/v1/contingencies/receiving-scopes/${this.scope()!.id}` : '/api/v1/contingencies/receiving-scopes'; }
  addDifference() { this.otherDifferences.push({ id: crypto.randomUUID(), description: '', resolved: false, resolutionReference: null }); }
  pending(path: string) { return !!this.mutations.pending.forResource(path); }
  async reconcile() {
    await this.run(async () => {
      const amount = exactDecimal(this.quantity, 3);
      const result = await this.mutations.mutate<{ resource: { id: string } }>('POST', '/api/v1/contingencies/reconcile', {
        type: 'Receive', voucherId: this.voucher, responsibleReference: this.responsible,
        receipt: { purchaseOrderId: this.purchaseId, occurredAt: new Date(this.occurred).toISOString(), supplierDocumentReference: this.support,
          reason: this.reason, incidents: [], lines: [{ id: crypto.randomUUID(), purchaseOrderLineId: this.purchaseLineId,
            reportedQuantity: amount, acceptedQuantity: amount, allocations: [{ id: crypto.randomUUID(), locationId: this.locationId, lotCode: this.lot || null, quantity: amount }] }] }
      });
      if (!result.body?.resource.id) throw new Error('Comprueba el resultado del comprobante.');
      this.message.set(`Comprobante incorporado a la recepción ${result.body.resource.id}. La reapertura se revisa por separado.`);
    });
  }
  async saveScope() {
    await this.run(async () => {
      const list = (value: string) => value.split(',').map(item => item.trim()).filter(Boolean);
      const path = this.scopeResource(); const existing = this.scope();
      const result = await this.mutations.mutate<{ resource: Scope }>(existing ? 'PUT' : 'POST', path, {
        purchaseOrderIds: list(this.scopePurchases), locationIds: list(this.scopeLocations), expectedVouchers: list(this.scopeVouchers),
        journalEvidence: this.journal || null, stockEvidence: this.stock || null, purchaseEvidence: this.purchaseEvidence || null,
        differences: [...(this.difference ? [{ id: this.differenceId, description: this.difference, resolved: this.resolved, resolutionReference: this.resolution || null }] : []), ...this.otherDifferences]
      }, this.etag);
      if (!result.body?.resource || !result.etag) throw new Error('Comprueba el resultado del alcance.');
      this.scope.set(result.body.resource); this.etag = result.etag; this.message.set('Alcance guardado.');
      if (!existing) await this.router.navigate(['/recepciones/contingencia', result.body.resource.id]);
    });
  }
  async reopen() {
    await this.run(async () => {
      const result = await this.mutations.mutate<{ resource: Scope }>('POST', `${this.scopeResource()}/reopen`, {}, this.etag);
      if (!result.body?.resource || !result.etag) throw new Error('Comprueba la autorización antes de continuar.');
      this.scope.set(result.body.resource); this.etag = result.etag; this.message.set('Autorización registrada. Comprueba las demás restricciones antes de reanudar.');
    });
  }
  private async run(work: () => Promise<void>) {
    if (this.busy()) return; const generation = this.generation; this.busy.set(true); this.error.set(''); this.message.set('');
    try { await work(); } catch (error) { if (generation === this.generation) this.error.set(apiProblem(error).detail); }
    finally { if (generation === this.generation) this.busy.set(false); }
  }
  private async load(id: string) {
    const generation = this.generation;
    try {
      const response = await firstValueFrom(this.http.get<Scope>(`/api/v1/contingencies/receiving-scopes/${id}`, { observe: 'response' }));
      if (generation !== this.generation || !response.body || !response.headers.get('ETag')) return;
      const scope = response.body; this.scope.set(scope); this.etag = response.headers.get('ETag')!;
      this.scopePurchases = scope.purchaseOrderIds.join(', '); this.scopeLocations = scope.locationIds.join(', '); this.scopeVouchers = scope.expectedVouchers.join(', ');
      this.journal = scope.journalEvidence ?? ''; this.stock = scope.stockEvidence ?? ''; this.purchaseEvidence = scope.purchaseEvidence ?? '';
      const first = scope.differences[0]; this.otherDifferences = scope.differences.slice(1);
      if (first) { this.differenceId = first.id; this.difference = first.description; this.resolved = first.resolved; this.resolution = first.resolutionReference ?? ''; }
      else { this.difference = ''; this.resolved = false; this.resolution = ''; this.differenceId = crypto.randomUUID(); }
    } catch (error) { if (generation === this.generation) this.error.set(apiProblem(error).detail); }
  }
}
