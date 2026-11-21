import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { SessionService } from '../../../core/session/session.service';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { apiProblem, ApiProblem } from '../../../core/http/api-problem';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { FieldErrorsComponent } from '../../../shared/field-errors.component';
import { SuppliersApiService } from '../../master-data/suppliers/suppliers-api.service';
import { ArticlesApiService } from '../../master-data/articles/articles-api.service';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { exactDecimal, PurchaseDetail, PurchaseOrder, PurchaseOrderContext, PurchaseLineWrite, PurchaseOrderWrite } from '../data/purchase-order.models';

type SupplierChoice = { id: string; code: string; name: string; email: string | null; isActive: boolean };
type ArticleChoice = { id: string; sku: string; name: string; baseUnit: string; allowsFractional: boolean; tracksLot: boolean; isActive: boolean };
const text = (value = '') => new FormControl(value, { nonNullable: true });

@Component({
  standalone: true, imports: [ReactiveFormsModule, RouterLink, InputTextModule, ButtonModule, OperationStatusComponent, FieldErrorsComponent],
  template: `
    <h1>{{ id ? 'Corregir pedido de compra' : 'Nuevo pedido de compra' }}</h1><a routerLink="/compras" queryParamsHandling="preserve">Volver a compras</a>
    @if (original(); as document) { <h2>{{ document.number }}</h2> }
    <app-operation-status [resource]="resourcePath" />
    @if (loading()) { <p role="status">Cargando compra…</p> }
    @if (problem(); as issue) { <p id="purchase-error" role="alert" tabindex="-1">{{ issue.detail }}</p> }
    @if (!canEdit()) { <p>El pedido no admite cambios o tu cuenta no puede gestionarlo.</p> }
    @if (context()?.sendBlockReasons?.length) { @for (reason of context()!.sendBlockReasons; track reason) { <p>{{ reason }}</p> } }
    @if (selectedSupplier() && !selectedSupplier()!.email) { <p>El proveedor no tiene correo: puedes guardar, pero no solicitar el envío.</p> }
    @if (problem()?.status === 412) { <button type="button" (click)="review()">Comparar con la revisión actual</button> }
    @if (conflict(); as current) {
      <section aria-label="Comparar revisiones"><h2>Revisión actual {{ current.resource.number }}</h2>
      <p>Proveedor vigente del pedido: {{ current.resource.supplierSnapshot.name }}. Fecha: {{ current.resource.expectedDeliveryDate }}.</p>
      <table><caption>Cambios por identidad de línea</caption><thead><tr><th>Artículo</th><th>Mi cantidad / precio / fecha</th><th>Actual cantidad / precio / fecha</th></tr></thead><tbody>
        @for (row of comparison(); track row.id) { <tr><th>{{ row.label }}</th><td>{{ row.local }}</td><td>{{ row.current }}</td></tr> }
      </tbody></table>
      <button type="button" [disabled]="current.resource.state !== 'Draft' || current.resource.sendStatus !== 'None'" (click)="acceptReview()">Conservar mi borrador sobre esta revisión</button>
      </section>
    }
    <form [formGroup]="form" (ngSubmit)="submit()"><fieldset [disabled]="loading() || busy() || !!pending() || !canEdit()"><legend>Datos del pedido</legend>
      <label for="supplier-query">Buscar proveedor</label><input pInputText id="supplier-query" [formControl]="supplierQuery">
      <button type="button" (click)="searchSuppliers(1)">Buscar proveedores</button>
      <label for="purchase-supplier">Proveedor (obligatorio)</label><select id="purchase-supplier" formControlName="supplierId" required>
        <option value="">Selecciona proveedor</option>@for (supplier of suppliers(); track supplier.id) { <option [value]="supplier.id">{{ supplier.code }} — {{ supplier.name }}{{ supplier.isActive ? '' : ' (Inactivo; referencia actual)' }}</option> }
      </select><app-field-errors fieldId="supplierId" [errors]="errors('supplierId')" />
      <button type="button" [disabled]="supplierPage() <= 1" (click)="searchSuppliers(supplierPage()-1)">Proveedores anteriores</button>
      <button type="button" [disabled]="supplierPage()*25 >= supplierCount()" (click)="searchSuppliers(supplierPage()+1)">Más proveedores</button>
      <p><label for="purchase-date">Fecha prevista general (obligatoria)</label><input id="purchase-date" type="date" formControlName="expectedDeliveryDate" required><app-field-errors fieldId="expectedDeliveryDate" [errors]="errors('expectedDeliveryDate')" /></p>
      <p>Precios en {{ currency() ?? 'cargando moneda…' }}. El precio cero es válido.</p>
      <label for="article-match">Buscar artículo por</label><select id="article-match" [formControl]="articleMatch">
        <option value="contains">Código o nombre (contiene)</option><option value="skuPrefix">Prefijo del código SKU</option>
      </select>
      <label for="article-query">Buscar artículo</label><input pInputText id="article-query" [formControl]="articleQuery"><button type="button" (click)="searchArticles(1)">Buscar artículos</button>
      <button type="button" [disabled]="articlePage() <= 1" (click)="searchArticles(articlePage()-1)">Artículos anteriores</button>
      <button type="button" [disabled]="articlePage()*25 >= articleCount()" (click)="searchArticles(articlePage()+1)">Más artículos</button>
      <div formArrayName="lines">@for (line of form.controls.lines.controls; track line.controls.id.value; let i = $index) {
        <fieldset [formGroupName]="i"><legend>Línea {{ i+1 }}</legend>
          <label [for]="'line-'+i+'-article'">Artículo (obligatorio)</label><select [id]="'line-'+i+'-article'" formControlName="articleId" required (change)="articleChanged(i)">
            <option value="">Selecciona artículo</option>@for (article of articles(); track article.id) { <option [value]="article.id">{{ article.sku }} — {{ article.name }}{{ article.isActive ? '' : ' (Inactivo; referencia actual)' }}</option> }
          </select><app-field-errors [fieldId]="'line-'+i+'-article'" [errors]="errors('lines['+i+'].articleId')" />
          <label [for]="'line-'+i+'-quantity'">Cantidad (obligatoria) {{ unit(line.controls.articleId.value) }}</label><input pInputText inputmode="decimal" [id]="'line-'+i+'-quantity'" formControlName="quantity" required>
          <app-field-errors [fieldId]="'line-'+i+'-quantity'" [errors]="errors('lines['+i+'].quantity')" />
          <label [for]="'line-'+i+'-price'">Precio unitario (obligatorio)</label><input pInputText inputmode="decimal" [id]="'line-'+i+'-price'" formControlName="unitPrice" required>
          <app-field-errors [fieldId]="'line-'+i+'-price'" [errors]="errors('lines['+i+'].unitPrice')" />
          <label [for]="'line-'+i+'-date'">Fecha propia (vacía: hereda general)</label><input type="date" [id]="'line-'+i+'-date'" formControlName="expectedDeliveryDate">
          <app-field-errors [fieldId]="'line-'+i+'-date'" [errors]="errors('lines['+i+'].expectedDeliveryDate')" />
          <button type="button" [disabled]="form.controls.lines.length <= 1" (click)="removeLine(i)">Retirar línea {{ i+1 }}</button>
        </fieldset>
      }</div><app-field-errors fieldId="lines" [errors]="errors('lines')" />
      <button type="button" (click)="addLine()">Añadir línea</button>
      <p-button type="submit" label="Guardar pedido" [disabled]="form.invalid || busy() || loading() || !!pending() || !canEdit() || problem()?.status === 412 || !!conflict()" />
    </fieldset></form>
  `
})
export class PurchaseOrderFormPage implements OnInit {
  private readonly api = inject(PurchaseOrdersApiService); private readonly supplierApi = inject(SuppliersApiService);
  private readonly articleApi = inject(ArticlesApiService); private readonly session = inject(SessionService);
  private readonly router = inject(Router); private readonly mutations = inject(MasterDataMutationsService);
  private readonly route = inject(ActivatedRoute); private readonly destroy = inject(DestroyRef);
  get id(): string | null { return this.route.snapshot.paramMap.get('id'); }
  get resourcePath(): string { return this.id ? `/api/v1/purchase-orders/${this.id}` : '/api/v1/purchase-orders'; }
  readonly loading = signal(true); readonly busy = signal(false); readonly problem = signal<ApiProblem | null>(null);
  readonly original = signal<PurchaseOrder | null>(null); readonly context = signal<PurchaseOrderContext | null>(null);
  readonly currency = signal<string | null>(null);
  readonly conflict = signal<PurchaseDetail | null>(null); private etag?: string; private generation = 0; private loadSequence = 0;
  readonly suppliers = signal<SupplierChoice[]>([]); readonly articles = signal<ArticleChoice[]>([]);
  readonly supplierPage = signal(1); readonly supplierCount = signal(0); readonly articlePage = signal(1); readonly articleCount = signal(0);
  readonly supplierQuery = text(); readonly articleQuery = text();
  readonly articleMatch = text('contains');
  readonly form = new FormGroup({ supplierId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    expectedDeliveryDate: new FormControl('', { nonNullable: true, validators: Validators.required }), lines: new FormArray([this.line()]) });

  constructor() {
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => {
      this.generation++; this.form.reset(); this.form.controls.lines.clear(); this.form.disable();
      this.original.set(null); this.currency.set(null); this.context.set(null); this.conflict.set(null); this.suppliers.set([]); this.articles.set([]); this.etag = undefined;
      this.busy.set(false); this.loading.set(false); this.problem.set(null);
    });
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation, outcome }) => {
      if (operation.actorId !== this.session.user()?.id || operation.resource !== this.resourcePath) return;
      const id = (outcome.body as { resource?: PurchaseOrder })?.resource?.id;
      if (id && !this.id) void this.router.navigate(['/compras', id], { queryParamsHandling: 'preserve' }); else void this.load();
    });
    this.destroy.onDestroy(() => this.generation++);
  }
  ngOnInit(): void {
    if (this.route.paramMap) this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.generation++; this.original.set(null); this.currency.set(null); this.context.set(null); this.conflict.set(null); this.etag = undefined;
      this.suppliers.set([]); this.articles.set([]); this.busy.set(false);
      this.form.reset(); this.form.controls.lines.clear(); this.addLine();
      void this.load();
    });
    else void this.load();
  }
  private line(value?: PurchaseLineWrite) { return new FormGroup({ id: text(value?.id ?? crypto.randomUUID()),
    articleId: new FormControl(value?.articleId ?? '', { nonNullable: true, validators: Validators.required }),
    quantity: new FormControl(value?.quantity ?? '1', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d+(?:[.,]\d{1,3})?$/)] }),
    unitPrice: new FormControl(value?.unitPrice ?? '0.00', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d+(?:[.,]\d{1,2})?$/)] }),
    expectedDeliveryDate: text(value?.expectedDeliveryDate ?? '') }); }
  addLine(): void { this.form.controls.lines.push(this.line()); }
  removeLine(index: number): void { if (this.form.controls.lines.length > 1) this.form.controls.lines.removeAt(index); }
  articleChanged(index: number): void {
    const row = this.form.controls.lines.at(index); const old = this.original()?.lines.find(line => line.id === row.controls.id.value);
    if (old && old.articleId !== row.controls.articleId.value) row.controls.id.setValue(crypto.randomUUID());
  }
  pending() { return this.mutations.pending.forResource(this.resourcePath); }
  canEdit(): boolean { return this.session.has('Purchasing.Manage') && !!this.currency() && (!this.original() || this.original()!.state === 'Draft' && this.original()!.sendStatus === 'None'); }
  errors(field: string): string[] { return this.problem()?.errors[field] ?? []; }
  selectedSupplier() { return this.suppliers().find(value => value.id === this.form.controls.supplierId.value); }
  unit(id: string): string { return this.articles().find(value => value.id === id)?.baseUnit ?? ''; }
  private patch(value: PurchaseOrderWrite): void {
    this.form.patchValue({ supplierId: value.supplierId, expectedDeliveryDate: value.expectedDeliveryDate });
    this.form.controls.lines.clear(); for (const line of value.lines) this.form.controls.lines.push(this.line(line));
  }
  async load(): Promise<void> {
    const generation = this.generation; const sequence = ++this.loadSequence;
    const isCurrent = () => generation === this.generation && sequence === this.loadSequence;
    this.loading.set(true); this.problem.set(null);
    try {
      if (this.id) {
        const current = await this.api.detail(this.id); if (!isCurrent()) return;
        this.original.set(current.resource); this.context.set(current.context); this.etag = current.etag;
        this.currency.set(current.resource.currency);
        this.patch({ ...current.resource, lines: current.resource.lines.map(line => ({ ...line, quantity: line.orderedQuantity })) });
      }
      else {
        const settings = await this.api.settings(); if (!isCurrent()) return;
        this.currency.set(settings.currency);
      }
      const pending = this.pending(); if (pending?.payload) this.patch(pending.payload as PurchaseOrderWrite);
      await Promise.all([this.searchSuppliers(1), this.searchArticles(1)]);
      if (!isCurrent()) return;
      this.form.enable(); if (!this.canEdit()) this.form.disable();
    } catch (error) { if (isCurrent()) { this.problem.set(apiProblem(error)); this.form.disable(); } }
    finally { if (isCurrent()) this.loading.set(false); }
  }
  async searchSuppliers(page: number): Promise<void> {
    const generation = this.generation;
    try {
      const result = await this.supplierApi.list(this.supplierQuery.value, 'active', page); if (generation !== this.generation) return;
      const values: SupplierChoice[] = [...result.items]; const current = this.context()?.supplier;
      if (current && !values.some(value => value.id === current.id)) values.unshift(current);
      const selected = this.form.controls.supplierId.value;
      if (selected && !values.some(value => value.id === selected)) {
        const chosen = await this.supplierApi.detail(selected); if (generation !== this.generation) return; values.unshift(chosen.resource);
      }
      this.suppliers.set(values); this.supplierPage.set(page); this.supplierCount.set(result.totalCount);
    } catch (error) { if (generation === this.generation) this.problem.set(apiProblem(error)); }
  }
  async searchArticles(page: number): Promise<void> {
    const generation = this.generation;
    try {
      const prefix = this.articleMatch.value === 'skuPrefix';
      const result = await this.articleApi.list(prefix ? '' : this.articleQuery.value, 'active', '', page,
        prefix ? this.articleQuery.value : undefined); if (generation !== this.generation) return;
      const values: ArticleChoice[] = [...result.items];
      for (const row of this.form.controls.lines.controls) {
        const id = row.controls.articleId.value; if (!id || values.some(value => value.id === id)) continue;
        const original = this.original()?.lines.find(line => line.articleId === id);
        if (original) values.unshift({ ...original.articleSnapshot, isActive: this.context()?.articles.find(article => article.id === id)?.isActive ?? false });
        else { const detail = await this.articleApi.detail(id); if (generation !== this.generation) return; values.unshift(detail.resource); }
      }
      this.articles.set(values); this.articlePage.set(page); this.articleCount.set(result.totalCount);
    } catch (error) { if (generation === this.generation) this.problem.set(apiProblem(error)); }
  }
  async submit(): Promise<void> {
    if (this.loading() || this.busy() || this.pending() || !this.canEdit() || this.form.invalid || this.problem()?.status === 412 || this.conflict()) return;
    const generation = this.generation; this.busy.set(true); this.problem.set(null);
    try {
      const value = this.form.getRawValue();
      const body: PurchaseOrderWrite = { ...value, lines: value.lines.map(line => ({ ...line, quantity: exactDecimal(line.quantity, 3),
        unitPrice: exactDecimal(line.unitPrice, 2), expectedDeliveryDate: line.expectedDeliveryDate || null })) };
      const saved = await this.api.save(this.id, body, this.etag); if (generation !== this.generation) return;
      await this.router.navigate(['/compras', saved.id], { queryParamsHandling: 'preserve' });
    } catch (error) {
      if (generation === this.generation) {
        this.problem.set(apiProblem(error)); const first = Object.keys(this.problem()?.errors ?? {})[0];
        const target = first?.replace(/^lines\[(\d+)\]\./, 'line-$1-').replace('unitPrice', 'price').replace('articleId', 'article');
        setTimeout(() => { if (generation === this.generation) (document.getElementById(target ?? '') ?? document.getElementById('purchase-error'))?.focus(); });
      }
    } finally { if (generation === this.generation) this.busy.set(false); }
  }
  async review(): Promise<void> { const generation = this.generation; try { const current = await this.api.detail(this.id!); if (generation === this.generation) this.conflict.set(current); } catch (error) { if (generation === this.generation) this.problem.set(apiProblem(error)); } }
  comparison() {
    const current = this.conflict()?.resource.lines ?? []; const local = this.form.getRawValue().lines;
    return [...new Set([...local.map(line => line.id), ...current.map(line => line.id)])].map(id => {
      const one = local.find(line => line.id === id); const two = current.find(line => line.id === id);
      return { id, label: two?.articleSnapshot.sku ?? this.articles().find(article => article.id === one?.articleId)?.sku ?? 'Línea nueva',
        local: one ? `${one.quantity} / ${one.unitPrice} / ${one.expectedDeliveryDate || this.form.controls.expectedDeliveryDate.value}` : 'Retirada',
        current: two ? `${two.orderedQuantity} / ${two.unitPrice} / ${two.expectedDeliveryDate}` : 'No existe' };
    });
  }
  acceptReview(): void {
    const current = this.conflict(); if (!current || current.resource.state !== 'Draft' || current.resource.sendStatus !== 'None') return;
    this.etag = current.etag; this.original.set(current.resource); this.context.set(current.context); this.conflict.set(null); this.problem.set(null);
  }
}
