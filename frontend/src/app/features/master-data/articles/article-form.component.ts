import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
import { SessionService } from '../../../core/session/session.service';
import { apiProblem, ApiProblem } from '../../../core/http/api-problem';
import { FieldErrorsComponent } from '../../../shared/field-errors.component';
import { FamiliesApiService } from '../families/families-api.service';
import { Family } from '../families/family.models';
import { ArticlesApiService } from './articles-api.service';
import { Article, ArticleContext } from './article.models';
import { ArticleLifecycleComponent } from './article-lifecycle.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { VersionConflictComponent, VersionReview } from '../../../shared/version-conflict.component';
import { StockPolicyComponent } from '../../replenishment/stock-policy.component';

@Component({
  standalone: true, imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, ConfirmDialogModule, FieldErrorsComponent, ArticleLifecycleComponent, OperationStatusComponent, VersionConflictComponent, StockPolicyComponent],
  providers: [ConfirmationService],
  template: `
    <h1>{{ id ? 'Detalle de artículo' : 'Nuevo artículo' }}</h1><a routerLink="/maestros/articulos">Volver a artículos</a>
    @if (!canManage()) { <p>Solo consulta: tu cuenta no puede modificar datos maestros.</p> }
    @if (problem(); as issue) { <p id="article-error" tabindex="-1" role="alert">{{ issue.detail }}</p>@if (issue.status !== 412 && !pending()) { <button type="button" (click)="load()">Volver a cargar artículo</button> } }
    @if (loading()) { <p role="status">Cargando datos del artículo…</p> }
    @if (saved()) { <p role="status">Artículo guardado y confirmado.</p> }
    <app-operation-status [resource]="resourcePath" />
    @if (problem()?.status === 412) { <app-version-conflict [draft]="form.getRawValue()" [loadCurrent]="readCurrent" (reviewed)="reviewVersion($event)" /> }
    <form [formGroup]="form" (ngSubmit)="submit()"><fieldset disabled [disabled]="loading() || busy() || !!pending() || !canManage()"><legend>Datos del artículo</legend>
      <p><label for="sku">Código de artículo (obligatorio)</label><br><input pInputText id="sku" formControlName="sku" maxlength="64" required aria-describedby="sku-errors"><app-field-errors fieldId="sku" [errors]="errors('sku')" /></p>
      <p><label for="article-name">Nombre del artículo (obligatorio)</label><br><input pInputText id="article-name" formControlName="name" maxlength="200" required aria-describedby="name-errors"><app-field-errors fieldId="name" [errors]="errors('name')" /></p>
      @if (canManage()) {
        <p><label for="family-query">Buscar familia por código o nombre</label><input pInputText id="family-query" [formControl]="familyQuery" maxlength="200">
        <p-button type="button" label="Buscar familias" (onClick)="searchFamilies()" /></p>
      }
      <p><label for="family">Familia (obligatoria)</label><br><select id="family" formControlName="familyId" required aria-describedby="familyId-errors">
        <option value="">Selecciona una familia</option>
        @for (family of families(); track family.id) { <option [value]="family.id">{{ family.code }} — {{ family.name }}{{ family.isActive ? '' : ' (inactiva; asociación actual)' }}</option> }
      </select><app-field-errors fieldId="familyId" [errors]="errors('familyId')" /></p>
      <p><label for="unit">Unidad base (obligatoria)</label><br><input pInputText id="unit" formControlName="baseUnit" maxlength="32" required aria-describedby="baseUnit-errors"><app-field-errors fieldId="baseUnit" [errors]="errors('baseUnit')" /></p>
      <p><label><input type="checkbox" formControlName="allowsFractional"> Admite fracciones de la unidad base</label></p>
      <p><label><input type="checkbox" formControlName="tracksLot"> Seguimiento por lote</label></p>
      @if (context() && !context()!.capabilities.canEditDefinition) { <p>La primera referencia bloquea unidad, lotes y fracciones, aunque después se retire.</p> }
      @if (id) { <p><label><input type="checkbox" formControlName="isActive"> Artículo activo</label></p> }
      @else { <p>Estado inicial: Activo. La política de reposición se inicia a cero.</p> }
      @if (canManage()) { <p-button type="submit" label="Guardar artículo" [disabled]="form.invalid || form.disabled || busy() || loading() || !!pending() || problem()?.status === 412" /> }
    </fieldset></form><p-confirmdialog />
    @if (id) { <app-stock-policy [articleId]="id" [unit]="original?.baseUnit ?? ''" [allowsFractional]="original?.allowsFractional ?? true" /><p><a [routerLink]="['/maestros/articulos', id, 'movimientos']">Consultar historial de movimientos</a></p> }
    @if (original && context() && etag) { <app-article-lifecycle [article]="original" [context]="context()!" [etag]="etag" [disabled]="busy() || loading() || !!pending() || problem()?.status === 412" (working)="busy.set($event)" (deleted)="afterDelete()" (deactivate)="deactivate()" /> }
  `
})
export class ArticleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(ArticlesApiService); private readonly familyApi = inject(FamiliesApiService);
  private readonly session = inject(SessionService); private readonly confirmation = inject(ConfirmationService);
  private readonly mutations = inject(MasterDataMutationsService);
  readonly id = this.route.snapshot.paramMap.get('id'); readonly loading = signal(true); readonly busy = signal(false);
  readonly resourcePath = this.id ? `/api/v1/articles/${this.id}` : '/api/v1/articles';
  pending() { return this.mutations.pending.forResource(this.resourcePath); }
  readonly problem = signal<ApiProblem | null>(null); readonly families = signal<Family[]>([]); readonly context = signal<ArticleContext | null>(null);
  readonly familyQuery = new FormControl('', { nonNullable: true });
  readonly saved = signal(false);
  readonly form = new FormGroup({
    sku: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    familyId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    baseUnit: new FormControl('UD', { nonNullable: true, validators: [Validators.required, Validators.maxLength(32)] }),
    allowsFractional: new FormControl(false, { nonNullable: true }), tracksLot: new FormControl(false, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true })
  });
  protected original: Article | null = null; protected etag?: string;
  constructor() {
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => { this.form.reset(); this.form.disable(); this.original = null; this.etag = undefined; this.problem.set(null); this.saved.set(false); this.context.set(null); this.families.set([]); });
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation, outcome }) => {
      if (operation.resource !== this.resourcePath) return;
      if (operation.method === 'DELETE') { void this.afterDelete(); return; }
      const id = (outcome.body as { resource?: Article })?.resource?.id;
      if (id && id !== this.id) void this.router.navigateByUrl(`/maestros/articulos/${id}`); else void this.load();
      this.saved.set(true);
    });
  }
  readonly readCurrent = () => this.api.detail(this.id!);
  reviewVersion(review: VersionReview): void {
    const article = review.snapshot.resource as Article; const context = review.snapshot.context as ArticleContext;
    if (!context.capabilities.canEditDefinition && ['baseUnit', 'allowsFractional', 'tracksLot'].some(field => review.values[field] !== article[field as keyof Article])) {
      this.problem.set({ status: 412, code: 'VersionConflict', detail: 'La definición quedó bloqueada. Elige los valores vigentes de unidad, lotes y fracciones en la comparación.', errors: {} }); return;
    }
    this.original = article; this.etag = review.snapshot.etag; this.context.set(context); this.form.patchValue(review.values); this.problem.set(null);
    if (!context.capabilities.canEditDefinition) { this.form.controls.baseUnit.disable(); this.form.controls.allowsFractional.disable(); this.form.controls.tracksLot.disable(); }
  }
  canManage(): boolean { return this.session.has('MasterData.Manage'); }
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.loading.set(true); this.problem.set(null);
    try {
      if (this.id) {
        const detail = await this.api.detail(this.id);
        this.original = detail.resource; this.etag = detail.etag; this.context.set(detail.context);
        this.form.patchValue(detail.resource);
      }
      const pending = this.pending(); if (pending?.payload) this.form.patchValue(pending.payload);
      await this.loadFamilies();
      this.form.enable();
      if (!this.canManage()) this.form.disable();
      else if (this.context()?.capabilities.canEditDefinition === false) {
        this.form.controls.baseUnit.disable(); this.form.controls.allowsFractional.disable(); this.form.controls.tracksLot.disable();
      }
    } catch (error) { this.problem.set(apiProblem(error)); this.form.disable(); }
    finally { this.loading.set(false); }
  }
  private async loadFamilies(): Promise<void> {
    const result = await this.familyApi.list(this.familyQuery.value, 'active', 1);
    const current = this.context()?.family;
    const selected = this.families().find(value => value.id === this.form.controls.familyId.value);
    const options = [...result.items];
    for (const family of [current, selected])
      if (family && !options.some(value => value.id === family.id)) options.unshift(family);
    this.families.set(options);
  }
  async searchFamilies(): Promise<void> { try { await this.loadFamilies(); } catch (error) { this.problem.set(apiProblem(error)); } }
  errors(field: string): string[] { return this.problem()?.errors[field] ?? []; }
  async afterDelete(): Promise<void> { await this.router.navigateByUrl('/maestros/articulos'); }
  async deactivate(): Promise<void> { this.form.controls.isActive.setValue(false); await this.submit(); }
  async submit(): Promise<void> {
    if (!this.canManage() || this.busy() || this.loading() || this.form.invalid || this.form.disabled || this.pending() || this.problem()?.status === 412) return;
    this.busy.set(true); this.problem.set(null); this.saved.set(false);
    try {
      const value = this.form.getRawValue();
      if (this.original && this.original.isActive !== value.isActive) {
        const accepted = await new Promise<boolean>(resolve => this.confirmation.confirm({ header: 'Revisar estado del artículo',
          message: `${value.isActive ? 'Reactivar' : 'Desactivar'} ${this.original!.sku} — ${this.original!.name}. Se conserva el historial; un inactivo no admite nuevos pedidos, reservas ni recepciones.`,
          acceptLabel: 'Confirmar', rejectLabel: 'Volver', accept: () => resolve(true), reject: () => resolve(false) }));
        if (!accepted) return;
      }
      const saved = await this.api.save(this.id, value, this.etag);
      this.saved.set(true);
      if (this.id === saved.id) await this.load(); else await this.router.navigateByUrl(`/maestros/articulos/${saved.id}`);
    } catch (error) { this.problem.set(apiProblem(error)); setTimeout(() => document.getElementById('article-error')?.focus()); }
    finally { this.busy.set(false); }
  }
}
