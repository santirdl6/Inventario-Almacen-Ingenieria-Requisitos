import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SessionService } from '../../../core/session/session.service';
import { apiProblem } from '../../../core/http/api-problem';
import { ReceiptsApiService } from '../data/receipts-api.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';

@Component({ standalone: true, imports: [FormsModule, RouterLink, OperationStatusComponent], template: `
  <h1>Recepciones</h1>
  @if (session.has('Receiving.Record')) { <a routerLink="/recepciones/nueva">Nueva recepción</a> }
  @if (session.has('Inventory.Adjust')) { <a routerLink="/recepciones/contingencia">Conciliar contingencia</a> }
  @for (operation of api.mutations.pending.operations(); track operation.operationId) {
    @if (operation.actorId === session.user()?.id && (operation.resource.startsWith('/api/v1/receipts') || operation.resource.startsWith('/api/v1/movements/') || operation.resource.startsWith('/api/v1/contingencies/'))) {
      <app-operation-status [resource]="operation.resource" [summaryOnly]="true" />
    }
  }
  <fieldset [disabled]="loading()"><legend>Buscar recepciones</legend>
    <label for="receipt-order">Identificador del pedido</label><input id="receipt-order" [(ngModel)]="filters.purchaseOrderId">
    <label for="receipt-state">Estado</label><select id="receipt-state" [(ngModel)]="filters.state"><option value="">Todos</option><option value="Draft">Borrador</option><option value="Confirmed">Confirmada</option></select>
    <button type="button" (click)="load(1)">Buscar recepciones</button>
  </fieldset>
  @if (loading()) { <p role="status">Consultando recepciones…</p> }
  @if (error()) { <p role="alert">{{error()}}</p> }
  @if (result(); as page) {
    @if (!page.items.length) { <p>No hay recepciones en esta página.</p> }
    @else { <table><caption>Recepciones registradas</caption><thead><tr><th>Recepción</th><th>Fecha</th><th>Estado</th></tr></thead><tbody>
      @for (item of page.items; track item.id) { <tr><td><a [routerLink]="['/recepciones',item.id]">{{item.supplierDocumentReference || item.id}}</a></td><td>{{item.occurredAt}}</td><td>{{item.state === 'Confirmed' ? 'Confirmada' : 'Borrador'}}</td></tr> }
    </tbody></table> }
    <button type="button" [disabled]="loading() || page.page <= 1" (click)="load(page.page-1)">Página anterior</button>
    <button type="button" [disabled]="loading() || page.page * page.pageSize >= page.totalCount" (click)="load(page.page+1)">Página siguiente</button>
  }
  ` })
export class ReceiptListPage implements OnInit {
  readonly api = inject(ReceiptsApiService);
  readonly session = inject(SessionService);
  readonly result = signal<Awaited<ReturnType<ReceiptsApiService['list']>> | null>(null);
  readonly loading = signal(false); readonly error = signal('');
  filters = { purchaseOrderId: '', state: '' };
  ngOnInit() { void this.load(1); }
  async load(page: number) {
    this.loading.set(true); this.error.set('');
    try { this.result.set(await this.api.list(this.filters, page)); }
    catch (error) { this.error.set(apiProblem(error).detail); }
    finally { this.loading.set(false); }
  }
}
