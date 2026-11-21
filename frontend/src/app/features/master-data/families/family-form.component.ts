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
import { FamiliesApiService } from './families-api.service';
import { Family } from './family.models';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { VersionConflictComponent, VersionReview } from '../../../shared/version-conflict.component';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, ConfirmDialogModule, FieldErrorsComponent, OperationStatusComponent, VersionConflictComponent],
  providers: [ConfirmationService],
  template: `
    <h1>{{ id ? 'Detalle de familia' : 'Nueva familia' }}</h1>
    <a routerLink="/maestros/familias">Volver a familias</a>
    @if (!canManage()) { <p>Solo consulta: tu cuenta no puede modificar datos maestros.</p> }
    @if (problem(); as issue) { <p role="alert" tabindex="-1" id="family-error">{{ issue.detail }}</p> }
    @if (loading()) { <p role="status">Cargando familia…</p> }
    @if (saved()) { <p role="status">Familia guardada y confirmada.</p> }
    <app-operation-status [resource]="resourcePath" />
    @if (problem()?.status === 412) { <app-version-conflict [draft]="form.getRawValue()" [loadCurrent]="readCurrent" (reviewed)="reviewVersion($event)" /> }
    <form [formGroup]="form" (ngSubmit)="submit()">
      <fieldset disabled [disabled]="busy() || loading() || !!pending() || !canManage()"><legend>Datos de familia</legend>
      <p><label for="code">Código (obligatorio)</label><br>
      <input pInputText id="code" formControlName="code" maxlength="64" required aria-describedby="code-errors">
      <app-field-errors fieldId="code" [errors]="errors('code')" /></p>
      <p><label for="name">Nombre (obligatorio)</label><br>
      <input pInputText id="name" formControlName="name" maxlength="200" required aria-describedby="name-errors">
      <app-field-errors fieldId="name" [errors]="errors('name')" /></p>
      @if (id) { <p><label><input type="checkbox" formControlName="isActive"> Familia activa</label></p> }
      @else { <p>Estado inicial: Activo</p> }
      @if (canManage()) { <p-button type="submit" label="Guardar familia" [disabled]="busy() || loading() || form.invalid || form.disabled || !!pending() || problem()?.status === 412" /> }
      </fieldset>
    </form>
    <p-confirmdialog />
  `
})
export class FamilyFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router);
  private readonly api = inject(FamiliesApiService); private readonly session = inject(SessionService);
  private readonly confirmations = inject(ConfirmationService);
  private readonly mutations = inject(MasterDataMutationsService);
  readonly id = this.route.snapshot.paramMap.get('id');
  readonly resourcePath = this.id ? `/api/v1/families/${this.id}` : '/api/v1/families';
  readonly saved = signal(false);
  pending() { return this.mutations.pending.forResource(this.resourcePath); }
  readonly busy = signal(false); readonly loading = signal(true); readonly problem = signal<ApiProblem | null>(null);
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    isActive: new FormControl(true, { nonNullable: true })
  });
  private original: Family | null = null; private etag?: string;
  canManage(): boolean { return this.session.has('MasterData.Manage'); }
  constructor() {
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => { this.form.reset(); this.form.disable(); this.original = null; this.etag = undefined; this.problem.set(null); this.saved.set(false); });
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation, outcome }) => {
      if (operation.resource !== this.resourcePath) return;
      const id = (outcome.body as { resource?: Family })?.resource?.id;
      if (id && id !== this.id) void this.router.navigateByUrl(`/maestros/familias/${id}`); else void this.load();
      this.saved.set(true);
    });
  }
  ngOnInit(): void { void this.load(); }
  readonly readCurrent = () => this.api.detail(this.id!);
  reviewVersion(review: VersionReview): void { this.original = review.snapshot.resource as Family; this.etag = review.snapshot.etag; this.form.patchValue(review.values); this.problem.set(null); }
  async load(): Promise<void> {
    if (!this.canManage()) this.form.disable();
    this.loading.set(true);
    try {
      if (this.id) {
      const detail = await this.api.detail(this.id);
      this.original = detail.resource; this.etag = detail.etag; this.form.patchValue(detail.resource);
      }
      const pending = this.pending(); if (pending?.payload) this.form.patchValue(pending.payload);
    } catch (error) { this.problem.set(apiProblem(error)); this.form.disable(); }
    finally { this.loading.set(false); }
  }
  errors(field: 'code' | 'name'): string[] {
    return this.problem()?.errors[field] ?? (this.form.controls[field].touched && this.form.controls[field].invalid ? ['Completa un valor válido.'] : []);
  }
  async submit(): Promise<void> {
    if (!this.canManage() || this.busy() || this.loading() || this.form.invalid || this.form.disabled || this.pending() || this.problem()?.status === 412) return;
    this.busy.set(true); this.problem.set(null); this.saved.set(false);
    try {
      const value = this.form.getRawValue();
      if (this.original && this.original.isActive !== value.isActive) {
        const confirmed = await new Promise<boolean>(resolve => this.confirmations.confirm({
          header: 'Revisar cambio de estado',
          message: `${value.isActive ? 'Reactivar' : 'Desactivar'} ${this.original!.code} — ${this.original!.name}. Las asociaciones existentes se conservan; una familia inactiva no admite nuevas asignaciones.`,
          acceptLabel: 'Confirmar', rejectLabel: 'Volver', accept: () => resolve(true), reject: () => resolve(false)
        }));
        if (!confirmed) return;
      }
      const saved = await this.api.save(this.id, value, this.etag);
      this.saved.set(true);
      if (this.id === saved.id) {
        const detail = await this.api.detail(saved.id);
        this.original = detail.resource; this.etag = detail.etag; this.form.patchValue(detail.resource);
      } else await this.router.navigateByUrl(`/maestros/familias/${saved.id}`);
    } catch (error) {
      this.problem.set(apiProblem(error));
      setTimeout(() => document.getElementById('family-error')?.focus());
    } finally { this.busy.set(false); }
  }
}
