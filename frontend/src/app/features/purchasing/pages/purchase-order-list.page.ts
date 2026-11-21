import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { InputTextModule } from 'primeng/inputtext';
import { SessionService } from '../../../core/session/session.service';
import { PendingOperationStore } from '../../../core/operations/pending-operation.store';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { apiProblem, ApiProblem } from '../../../core/http/api-problem';
import { SuppliersApiService } from '../../master-data/suppliers/suppliers-api.service';
import { Supplier } from '../../master-data/suppliers/supplier.models';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { PurchasePage, purchaseStateLabel, purchaseSendLabel } from '../data/purchase-order.models';

const control = () => new FormControl('', { nonNullable: true });
@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink, InputTextModule, OperationStatusComponent], template: `
  <h1>Compras</h1>
  @if (session.has('Purchasing.Manage')) { <a routerLink="/compras/nuevo" [queryParams]="navigationQuery()">Nuevo pedido</a> }
  @for (operation of pending.operations(); track operation.operationId) {
    @if (operation.actorId === session.user()?.id && operation.resource.startsWith('/api/v1/purchase-orders')) {
      <app-operation-status [resource]="operation.resource" />
    }
  }
  <form [formGroup]="filters" (ngSubmit)="search()"><fieldset [disabled]="loading()"><legend>Buscar pedidos</legend>
    <p><label for="purchase-number">Número de pedido (prefijo)</label><input pInputText id="purchase-number" formControlName="q"></p>
    <label for="supplier-filter-query">Buscar proveedor de filtro</label><input pInputText id="supplier-filter-query" [formControl]="supplierQuery">
    <button type="button" (click)="searchSuppliers(1)">Buscar proveedores</button>
    <label for="purchase-supplier-filter">Proveedor (incluye inactivos)</label><select id="purchase-supplier-filter" formControlName="supplierId">
      <option value="">Todos</option>@for (supplier of suppliers(); track supplier.id) { <option [value]="supplier.id">{{ supplier.code }} — {{ supplier.name }}{{ supplier.isActive ? '' : ' (Inactivo)' }}</option> }
    </select>
    <button type="button" [disabled]="supplierPage()<=1" (click)="searchSuppliers(supplierPage()-1)">Proveedores anteriores</button>
    <button type="button" [disabled]="supplierPage()*25>=supplierCount()" (click)="searchSuppliers(supplierPage()+1)">Más proveedores</button>
    <p><label for="purchase-state">Estado logístico</label><select id="purchase-state" formControlName="state"><option value="">Todos</option>
      <option value="Draft">Borrador</option><option value="Sent">Enviado</option><option value="PartiallyReceived">Recibido parcialmente</option><option value="Received">Recibido</option>
    </select></p>
    <label for="purchase-from">Desde creación (UTC, inclusivo)</label><input type="datetime-local" id="purchase-from" formControlName="from">
    <label for="purchase-to">Hasta creación (UTC, exclusivo)</label><input type="datetime-local" id="purchase-to" formControlName="to">
    <button type="submit">Buscar pedidos</button>
  </fieldset></form>
  <button type="button" [disabled]="loading()" (click)="load()">Actualizar listado</button>
  @if (loading()) { <p role="status">Consultando pedidos…</p> }
  @if (problem(); as error) { <p role="alert">{{ error.detail }} Los datos anteriores no se han actualizado.</p> }
  @if (result(); as page) {
    <p>{{ page.totalCount }} pedidos coincidentes. Página {{ page.page }}.</p>
    @if (page.items.length) {
      <table><caption>Pedidos a proveedores</caption><thead><tr><th>Número</th><th>Proveedor histórico</th><th>Creado (UTC)</th><th>Fecha prevista</th><th>Estado logístico</th><th>Envío del correo</th></tr></thead><tbody>
        @for (order of page.items; track order.id) { <tr><th><a [routerLink]="['/compras', order.id]" [queryParams]="navigationQuery()">{{ order.number }}</a></th>
          <td>{{ order.supplierSnapshot.code }} — {{ order.supplierSnapshot.name }}</td><td>{{ order.createdAt }}</td><td>{{ order.expectedDeliveryDate }}</td>
          <td>{{ stateLabel(order.state) }}</td><td>{{ sendLabel(order.sendStatus) }}</td></tr> }
      </tbody></table>
    } @else if (!problem()) {
      @if (page.totalCount === 0) { <p>No hay pedidos para estos filtros.</p> }
      @else { <p>Esta página ya no tiene filas. Vuelve a una página válida conservando los filtros.</p> }
    }
    <nav aria-label="Páginas de compras"><button type="button" aria-label="Primera página" [disabled]="loading() || page.page<=1" (click)="go(1)">Primera página</button>
      <button type="button" [disabled]="loading() || page.page<=1" (click)="go(page.page-1)">Página anterior</button>
      <button type="button" [disabled]="loading() || page.page*page.pageSize>=page.totalCount" (click)="go(page.page+1)">Página siguiente</button>
      <button type="button" [disabled]="loading() || page.totalCount===0 || page.page===lastPage()" (click)="go(lastPage())">Última página válida</button>
    </nav>
  }
` })
export class PurchaseOrderListPage implements OnInit {
  readonly session = inject(SessionService); readonly pending = inject(PendingOperationStore);
  private readonly api = inject(PurchaseOrdersApiService); private readonly supplierApi = inject(SuppliersApiService);
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router); private readonly destroy = inject(DestroyRef);
  readonly filters = new FormGroup({ q: control(), supplierId: control(), state: control(), from: control(), to: control() });
  readonly supplierQuery = control(); readonly suppliers = signal<Supplier[]>([]); readonly supplierPage = signal(1); readonly supplierCount = signal(0);
  readonly result = signal<PurchasePage | null>(null); readonly loading = signal(false); readonly problem = signal<ApiProblem | null>(null);
  readonly stateLabel = purchaseStateLabel; readonly sendLabel = purchaseSendLabel;
  private page = 1; private generation = 0;
  constructor() {
    this.destroy.onDestroy(() => this.generation++);
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => { this.generation++; this.result.set(null); this.suppliers.set([]); this.problem.set(null); this.filters.reset(); this.loading.set(false); });
  }
  ngOnInit(): void {
    const restore = () => {
      const query = this.route.snapshot.queryParamMap;
      for (const key of ['q', 'supplierId', 'state', 'from', 'to'] as const) {
        const raw = query.get(key) ?? '';
        this.filters.controls[key].setValue(key === 'from' || key === 'to' ? raw.replace(/Z$/, '') : raw);
      }
      const number = Number(query.get('page') ?? '1'); this.page = Number.isInteger(number) && number >= 1 ? number : 1;
      void this.load(); void this.searchSuppliers(1);
    };
    if (this.route.queryParamMap) this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(restore);
    else restore();
  }
  navigationQuery(page = this.result()?.page ?? this.page): Record<string, string | number> {
    const query: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(this.filters.getRawValue())) if (value) query[key] = value;
    if (page > 1) query['page'] = page;
    return query;
  }
  lastPage(): number { const result = this.result(); return result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1; }
  async search(): Promise<void> { this.page = 1; await this.navigate(); }
  async go(page: number): Promise<void> { this.page = page; await this.navigate(); }
  private async navigate(): Promise<void> {
    try { const changed = await this.router.navigate([], { relativeTo: this.route, queryParams: this.navigationQuery(this.page) }); if (!changed || !this.route.queryParamMap) await this.load(); }
    catch (error) { this.problem.set(apiProblem(error)); }
  }
  async load(): Promise<void> {
    const generation = ++this.generation; this.loading.set(true); this.problem.set(null);
    try { const result = await this.api.list(this.filters.getRawValue(), this.page); if (generation === this.generation) { this.result.set(result); this.page = result.page; } }
    catch (error) { if (generation === this.generation) this.problem.set(apiProblem(error)); }
    finally { if (generation === this.generation) this.loading.set(false); }
  }
  async searchSuppliers(page: number): Promise<void> {
    const actor = this.session.user()?.id;
    try {
      const result = await this.supplierApi.list(this.supplierQuery.value, 'all', page);
      if (!actor || actor !== this.session.user()?.id || this.destroy.destroyed) return;
      const values = [...result.items]; const selected = this.filters.controls.supplierId.value;
      if (selected && !values.some(value => value.id === selected)) {
        const current = await this.supplierApi.detail(selected); if (actor !== this.session.user()?.id || this.destroy.destroyed) return; values.unshift(current.resource);
      }
      this.suppliers.set(values); this.supplierPage.set(page); this.supplierCount.set(result.totalCount);
    } catch (error) { if (actor === this.session.user()?.id && !this.destroy.destroyed) this.problem.set(apiProblem(error)); }
  }
}
