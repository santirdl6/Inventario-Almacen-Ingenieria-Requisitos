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
import { SuppliersApiService } from './suppliers-api.service';
import { Supplier } from './supplier.models';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { VersionConflictComponent, VersionReview } from '../../../shared/version-conflict.component';

@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, ConfirmDialogModule, FieldErrorsComponent, OperationStatusComponent, VersionConflictComponent], providers: [ConfirmationService],
  template: `
    <h1>{{ id ? 'Detalle de proveedor' : 'Nuevo proveedor' }}</h1><a routerLink="/maestros/proveedores">Volver a proveedores</a>
    @if (!canManage()) { <p>Solo consulta: tu cuenta no puede modificar datos maestros.</p> }
    @if (problem(); as issue) { <p role="alert" tabindex="-1" id="supplier-error">{{ issue.detail }}</p> }
    @if (loading()) { <p role="status">Cargando proveedor…</p> }
    @if (saved()) { <p role="status">Proveedor guardado y confirmado.</p> }
    <app-operation-status [resource]="resourcePath" />
    @if (problem()?.status === 412) { <app-version-conflict [draft]="form.getRawValue()" [loadCurrent]="readCurrent" (reviewed)="reviewVersion($event)" /> }
    <form [formGroup]="form" (ngSubmit)="submit()"><fieldset disabled [disabled]="loading() || busy() || !!pending() || !canManage()"><legend>Datos del proveedor</legend>
      <p><label for="supplier-code">Código de proveedor (obligatorio)</label><br><input pInputText id="supplier-code" formControlName="code" maxlength="64" required aria-describedby="code-errors"><app-field-errors fieldId="code" [errors]="errors('code')" /></p>
      <p><label for="supplier-name">Nombre del proveedor (obligatorio)</label><br><input pInputText id="supplier-name" formControlName="name" maxlength="200" required aria-describedby="name-errors"><app-field-errors fieldId="name" [errors]="errors('name')" /></p>
      <p><label for="supplier-email">Correo del proveedor (opcional)</label><br><input pInputText id="supplier-email" type="email" formControlName="email" maxlength="256" aria-describedby="email-errors"><app-field-errors fieldId="email" [errors]="errors('email')" /></p>
      @if (!form.controls.email.value.trim()) { <p>Sin correo: puedes guardar el proveedor, pero deberás completarlo antes de enviar pedidos.</p> }
      <p><label for="supplier-tax">Identificador fiscal (opcional)</label><br><input pInputText id="supplier-tax" formControlName="taxIdentifier" maxlength="64" aria-describedby="taxIdentifier-errors"><app-field-errors fieldId="taxIdentifier" [errors]="errors('taxIdentifier')" /></p>
      @if (id) { <p><label><input type="checkbox" formControlName="isActive"> Proveedor activo</label></p> } @else { <p>Estado inicial: Activo</p> }
      @if (canManage()) { <p-button type="submit" label="Guardar proveedor" [disabled]="loading() || busy() || form.invalid || form.disabled || !!pending() || problem()?.status === 412" /> }
    </fieldset></form><p-confirmdialog />
  ` })
export class SupplierFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router); private readonly api = inject(SuppliersApiService);
  private readonly session = inject(SessionService); private readonly confirmation = inject(ConfirmationService);
  private readonly mutations = inject(MasterDataMutationsService);
  readonly id = this.route.snapshot.paramMap.get('id'); readonly loading = signal(true); readonly busy = signal(false); readonly saved = signal(false);
  readonly resourcePath = this.id ? `/api/v1/suppliers/${this.id}` : '/api/v1/suppliers';
  pending() { return this.mutations.pending.forResource(this.resourcePath); }
  readonly problem = signal<ApiProblem | null>(null);
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(256)] }),
    taxIdentifier: new FormControl('', { nonNullable: true, validators: Validators.maxLength(64) }), isActive: new FormControl(true, { nonNullable: true })
  });
  private original: Supplier | null = null; private etag?: string;
  constructor() {
    this.session.loggedOut.pipe(takeUntilDestroyed()).subscribe(() => { this.form.reset(); this.form.disable(); this.original = null; this.etag = undefined; this.problem.set(null); this.saved.set(false); });
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation, outcome }) => {
      if (operation.resource !== this.resourcePath) return;
      const id = (outcome.body as { resource?: Supplier })?.resource?.id;
      if (id && id !== this.id) void this.router.navigateByUrl(`/maestros/proveedores/${id}`); else void this.load();
      this.saved.set(true);
    });
  }
  readonly readCurrent = () => this.api.detail(this.id!);
  reviewVersion(review: VersionReview): void {
    this.original = review.snapshot.resource as Supplier; this.etag = review.snapshot.etag;
    this.form.patchValue({ ...review.values, email: (review.values['email'] as string | null) ?? '', taxIdentifier: (review.values['taxIdentifier'] as string | null) ?? '' }); this.problem.set(null);
  }
  canManage(): boolean { return this.session.has('MasterData.Manage'); }
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.loading.set(true);
    try {
      if (this.id) {
        const detail = await this.api.detail(this.id); this.original = detail.resource; this.etag = detail.etag;
        this.form.patchValue({ ...detail.resource, email: detail.resource.email ?? '', taxIdentifier: detail.resource.taxIdentifier ?? '' });
      }
      const pending = this.pending(); if (pending?.payload) {
        const value = pending.payload as Partial<Supplier>; this.form.patchValue({ ...value, email: value.email ?? '', taxIdentifier: value.taxIdentifier ?? '' });
      }
      if (this.canManage()) this.form.enable(); else this.form.disable();
    } catch (error) { this.problem.set(apiProblem(error)); this.form.disable(); }
    finally { this.loading.set(false); }
  }
  errors(field: 'code' | 'name' | 'email' | 'taxIdentifier'): string[] {
    return this.problem()?.errors[field] ?? (this.form.controls[field].touched && this.form.controls[field].invalid ? ['Completa un valor válido.'] : []);
  }
  async submit(): Promise<void> {
    if (!this.canManage() || this.busy() || this.loading() || this.form.invalid || this.form.disabled || this.pending() || this.problem()?.status === 412) return;
    this.busy.set(true); this.problem.set(null); this.saved.set(false);
    try {
      const value = this.form.getRawValue();
      if (this.original && this.original.isActive !== value.isActive) {
        const accepted = await new Promise<boolean>(resolve => this.confirmation.confirm({ header: 'Revisar estado del proveedor',
          message: `${value.isActive ? 'Reactivar' : 'Desactivar'} ${this.original!.code} — ${this.original!.name}. Se conservan los documentos existentes; un proveedor inactivo no admite nuevas compras.`,
          acceptLabel: 'Confirmar', rejectLabel: 'Volver', accept: () => resolve(true), reject: () => resolve(false) }));
        if (!accepted) return;
      }
      const saved = await this.api.save(this.id, { ...value, email: value.email.trim() || null, taxIdentifier: value.taxIdentifier.trim() || null }, this.etag);
      this.saved.set(true);
      if (this.id === saved.id) await this.load(); else await this.router.navigateByUrl(`/maestros/proveedores/${saved.id}`);
    } catch (error) { this.problem.set(apiProblem(error)); setTimeout(() => document.getElementById('supplier-error')?.focus()); }
    finally { this.busy.set(false); }
  }
}
