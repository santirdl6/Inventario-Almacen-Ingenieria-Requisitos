import { Component, ElementRef, Injector, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { timeout } from 'rxjs';
import { ApiProblem } from '../../../core/http/api-problem';
import { completeIntent, intentKey } from '../data/idempotent-intent';
import {
  CODE_MAX_LENGTH,
  KIND_OPTIONS,
  LocationDetail,
  LocationFields,
  LocationKind,
  PART_MAX_LENGTH,
  Versioned
} from '../data/location.models';
import { LocationsApi } from '../data/locations-api.service';
import {
  FORBIDDEN_MESSAGE,
  MUTATION_TIMEOUT_MS,
  OperationStatus,
  PENDING_CHECK_MESSAGE,
  asProblem,
  fieldMessages,
  isUncertain
} from '../shared/operation';

type TextField = 'code' | 'aisle' | 'rack' | 'level';
type Field = TextField | 'kind';

interface FieldConfig {
  readonly name: TextField;
  readonly label: string;
  readonly max: number;
}

const IDENTITY_FIELDS: readonly TextField[] = ['code', 'aisle', 'rack', 'level'];

/** Rechaza valores vacíos o compuestos solo por espacios (US1.4). */
function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length === 0 ? { blank: true } : null;
}

/**
 * FR-002/FR-003 (clarify 2026-11-19): solo letras A–Z sin acentos ni Ñ, dígitos y guion, sin
 * espacios interiores. Lista ASCII explícita, igual que el servidor; las minúsculas se admiten
 * porque se guardan en mayúsculas. Los espacios extremos se recortan al enviar.
 */
export const ALLOWED_CHARACTERS = /^[A-Za-z0-9-]*$/;
export const ALLOWED_CHARACTERS_MESSAGE = 'Solo letras A–Z sin acentos ni Ñ, dígitos y guion, sin espacios interiores.';

function allowedCharacters(control: AbstractControl<string>): ValidationErrors | null {
  return ALLOWED_CHARACTERS.test(control.value.trim()) ? null : { characters: true };
}

function textControl(max: number): FormControl<string> {
  return new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(max), allowedCharacters] });
}

/**
 * HU-02 · Alta (US1) y corrección (US4) de una ubicación, sobre la base de A: errores con
 * apiProblem(), parámetro `id` leído de la ruta y avisos en la propia página.
 * Conserva lo escrito ante cualquier error (FR-019), bloquea la identificación física de una
 * ubicación con movimientos (FR-011), gestiona la versión obsoleta (412, FR-017) y el resultado
 * incierto reutilizando la misma clave de idempotencia (FR-018, FR-020).
 */
@Component({
  selector: 'app-location-form-page',
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule],
  styleUrl: '../locations.css',
  template: `
    <h1>{{ isEdit() ? 'Corregir ubicación' : 'Nueva ubicación' }}</h1>

    @if (loadError()) {
      <p class="error-text" role="alert">{{ loadError() }}</p>
    }

    @if (identityLocked()) {
      <p class="notice" role="note" data-testid="identity-locked">
        Esta ubicación tiene movimientos: su código, pasillo, estantería y nivel forman parte del
        historial y no se pueden cambiar. Si el lugar físico ha cambiado,
        <a routerLink="/ubicaciones/nueva">crea otra ubicación</a> y desactiva esta cuando quede vacía.
      </p>
    }

    @if (conflict()) {
      <div class="conflict" role="alert" data-testid="conflict">
        <p>Otra persona ha cambiado esta ubicación después de que la abrieras. Tus cambios no se han guardado.</p>
        <div class="actions">
          <p-button label="Ver datos vigentes" (onClick)="viewCurrent()" />
          <p-button label="Descartar mis cambios" [outlined]="true" (onClick)="discard()" />
        </div>
        @if (current(); as now) {
          <dl class="facts" data-testid="current">
            <dt>Código</dt><dd>{{ now.code }}</dd>
            <dt>Ubicación</dt><dd>{{ now.description }}</dd>
            <dt>Tipo</dt><dd>{{ now.kindLabel }}</dd>
            <dt>Estado</dt><dd>{{ now.statusLabel }}</dd>
          </dl>
          <p>Revisa tus cambios, que se conservan en el formulario, y guarda de nuevo si siguen siendo correctos.</p>
        }
      </div>
    }

    @if (status() === 'pending-check') {
      <div class="pending" role="status" data-testid="pending">
        <p>{{ pendingMessage }}</p>
        <p-button label="Comprobar" (onClick)="checkResult()" />
      </div>
    }

    @if (generalError()) {
      <p class="error-text" role="alert" data-testid="general-error">{{ generalError() }}</p>
    }
    @if (duplicateSearch(); as search) {
      <p><a routerLink="/ubicaciones" [queryParams]="{ search: search, status: 'all' }" data-testid="duplicate-link">Buscar la ubicación ya registrada</a></p>
    }

    @if (!isEdit() || loaded()) {
      <form class="location-form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <p class="field-help">Los campos marcados con * son obligatorios.</p>
        @for (field of textFields; track field.name) {
          <div class="field">
            <label [for]="field.name">{{ field.label }} <span aria-hidden="true">*</span><span class="visually-hidden">(obligatorio)</span></label>
            <input pInputText [id]="field.name" [formControlName]="field.name" [attr.maxlength]="field.max"
              autocomplete="off" [attr.aria-required]="true" [attr.aria-invalid]="errorFor(field.name) ? 'true' : null"
              [attr.aria-describedby]="field.name + '-help' + (errorFor(field.name) ? ' ' + field.name + '-error' : '')" />
            <small class="field-help" [id]="field.name + '-help'">Máximo {{ field.max }} caracteres: letras A–Z, dígitos y guion.</small>
            @if (errorFor(field.name); as message) {
              <small class="field-error" [id]="field.name + '-error'">{{ message }}</small>
            }
          </div>
          @if (field.name === 'level' && !identityLocked()) {
            <div class="actions">
              <p-button type="button" label="Proponer código" [text]="true" [disabled]="!canPropose()" (onClick)="proposeCode()" />
              <small class="field-help">Rellena el código a partir de pasillo, estantería y nivel (p. ej. P01-E01-N01). Puedes editarlo.</small>
            </div>
          }
        }
        <div class="field">
          <label for="kind">Tipo <span aria-hidden="true">*</span><span class="visually-hidden">(obligatorio)</span></label>
          <select id="kind" formControlName="kind" [attr.aria-invalid]="errorFor('kind') ? 'true' : null"
            [attr.aria-describedby]="errorFor('kind') ? 'kind-error' : null">
            @for (option of kindOptions; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
          @if (errorFor('kind'); as message) {
            <small class="field-error" id="kind-error">{{ message }}</small>
          }
          @if (contentLink() && id()) {
            <a [routerLink]="['/ubicaciones', id()]" fragment="contenido-ubicacion" data-testid="content-link">Ver el contenido de la ubicación</a>
          }
        </div>
        <div class="actions">
          <p-button type="submit" [label]="isEdit() ? 'Guardar cambios' : 'Crear ubicación'"
            [disabled]="status() !== 'idle'" [loading]="status() === 'confirming'" />
          <a [routerLink]="isEdit() ? ['/ubicaciones', id()] : ['/ubicaciones']">Cancelar</a>
        </div>
      </form>
    }
  `
})
export class LocationFormPage implements OnInit {
  /** Identificador de la ruta `:id/editar`; sin él, la página da de alta. */
  readonly id = signal<string | undefined>(inject(ActivatedRoute).snapshot.paramMap.get('id') ?? undefined);

  private readonly api = inject(LocationsApi);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly kindOptions = KIND_OPTIONS;
  protected readonly pendingMessage = PENDING_CHECK_MESSAGE;
  protected readonly textFields: readonly FieldConfig[] = [
    { name: 'code', label: 'Código', max: CODE_MAX_LENGTH },
    { name: 'aisle', label: 'Pasillo', max: PART_MAX_LENGTH },
    { name: 'rack', label: 'Estantería', max: PART_MAX_LENGTH },
    { name: 'level', label: 'Nivel', max: PART_MAX_LENGTH }
  ];

  readonly form = new FormGroup({
    code: textControl(CODE_MAX_LENGTH),
    aisle: textControl(PART_MAX_LENGTH),
    rack: textControl(PART_MAX_LENGTH),
    level: textControl(PART_MAX_LENGTH),
    kind: new FormControl<LocationKind>('Storage', { nonNullable: true, validators: [Validators.required] })
  });

  readonly isEdit = computed(() => !!this.id());
  readonly loaded = signal<Versioned<LocationDetail> | null>(null);
  readonly identityLocked = computed(() => this.loaded()?.value.hasMovements ?? false);
  readonly loadError = signal<string | null>(null);
  readonly status = signal<OperationStatus>('idle');
  readonly serverErrors = signal<Partial<Record<Field, string>>>({});
  readonly generalError = signal<string | null>(null);
  readonly duplicateSearch = signal<string | null>(null);
  readonly contentLink = signal(false);
  readonly conflict = signal(false);
  readonly current = signal<LocationDetail | null>(null);
  private readonly revision = signal(0);
  private pendingBody: LocationFields | null = null;

  constructor() {
    for (const name of [...IDENTITY_FIELDS, 'kind'] as Field[]) {
      this.form.controls[name].valueChanges.subscribe(() => {
        this.revision.update(value => value + 1);
        if (this.serverErrors()[name]) {
          const { [name]: _removed, ...rest } = this.serverErrors();
          this.serverErrors.set(rest);
        }
      });
    }
    this.form.statusChanges.subscribe(() => this.revision.update(value => value + 1));
  }

  ngOnInit(): void {
    const id = this.id();
    if (id) this.load(id);
  }

  /** Mensaje de error de un campo: primero el del servidor, después el de validación local. */
  errorFor(name: Field): string | null {
    this.revision();
    const server = this.serverErrors()[name];
    if (server) return server;
    const control = this.form.controls[name];
    if (!control.touched || control.valid || control.disabled) return null;
    if (control.hasError('required') || control.hasError('blank')) return 'Este campo es obligatorio.';
    if (control.hasError('maxlength')) return `Máximo ${control.getError('maxlength').requiredLength} caracteres.`;
    if (control.hasError('characters')) return ALLOWED_CHARACTERS_MESSAGE;
    return 'El valor no es válido.';
  }

  canPropose(): boolean {
    this.revision();
    const { aisle, rack, level } = this.form.getRawValue();
    return [aisle, rack, level].every(part => part.trim().length > 0);
  }

  proposeCode(): void {
    const { aisle, rack, level } = this.form.getRawValue();
    this.form.controls.code.setValue([aisle, rack, level].map(part => part.trim().toUpperCase()).join('-'));
    this.form.controls.code.markAsTouched();
  }

  submit(): void {
    if (this.status() !== 'idle') return;
    this.clearMessages();
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.revision.update(value => value + 1);
      this.focusFirstError();
      return;
    }
    this.send(this.body());
  }

  /** Repite la misma petición con la misma clave para conocer el resultado sin duplicarlo. */
  checkResult(): void {
    if (this.pendingBody && this.status() === 'pending-check') {
      this.status.set('idle');
      this.send(this.pendingBody);
    }
  }

  viewCurrent(): void {
    const id = this.id();
    if (!id) return;
    this.api.get(id).subscribe({
      next: latest => {
        this.current.set(latest.value);
        // La siguiente intención se basa en la versión vigente que el usuario acaba de revisar.
        this.loaded.set({ value: latest.value, etag: latest.etag });
      },
      error: (error: unknown) => this.generalError.set(asProblem(error).detail)
    });
  }

  discard(): void {
    const id = this.id();
    if (id) this.load(id);
  }

  private load(id: string): void {
    this.loadError.set(null);
    this.api.get(id).subscribe({
      next: latest => {
        this.loaded.set(latest);
        this.clearMessages();
        this.current.set(null);
        this.form.reset({
          code: latest.value.code,
          aisle: latest.value.aisle,
          rack: latest.value.rack,
          level: latest.value.level,
          kind: latest.value.kind
        });
        for (const name of IDENTITY_FIELDS) {
          const control = this.form.controls[name];
          if (latest.value.hasMovements) control.disable({ emitEvent: false });
          else control.enable({ emitEvent: false });
        }
        this.revision.update(value => value + 1);
      },
      error: (error: unknown) => {
        const problem = asProblem(error);
        this.loadError.set(problem.status === 404 ? 'La ubicación no existe.' : problem.detail);
      }
    });
  }

  private body(): LocationFields {
    const raw = this.form.getRawValue();
    return {
      code: raw.code.trim(),
      aisle: raw.aisle.trim(),
      rack: raw.rack.trim(),
      level: raw.level.trim(),
      kind: raw.kind
    };
  }

  private intent(): string {
    return this.isEdit() ? `location:update:${this.id()}` : 'location:create';
  }

  private send(body: LocationFields): void {
    const intent = this.intent();
    const key = intentKey(intent, body);
    const loaded = this.loaded();
    const id = this.id();
    const request = id && loaded
      ? this.api.update(id, { ...body, isActive: loaded.value.isActive }, loaded.etag, key)
      : this.api.create(body, key);

    this.pendingBody = body;
    this.status.set('confirming');
    request.pipe(timeout(MUTATION_TIMEOUT_MS)).subscribe({
      next: saved => {
        completeIntent(intent);
        this.pendingBody = null;
        this.status.set('idle');
        // El aviso se muestra en la pantalla de destino (sin avisos globales).
        void this.router.navigate(['/ubicaciones', saved.value.id],
          { state: { notice: `Ubicación ${saved.value.code} guardada.` } });
      },
      error: (error: unknown) => {
        if (isUncertain(error)) {
          this.status.set('pending-check');
          return;
        }
        completeIntent(intent);
        this.pendingBody = null;
        this.status.set('idle');
        this.handle(asProblem(error));
      }
    });
  }

  private handle(problem: ApiProblem): void {
    const fields = fieldMessages(problem) as Partial<Record<Field, string>>;
    const body = this.body();
    switch (problem.code) {
      case 'DuplicateCode':
        this.serverErrors.set({ code: fields.code ?? problem.detail });
        this.duplicateSearch.set(body.code);
        break;
      case 'DuplicateLocationPlace':
        // El detail nombra la ubicación que ya ocupa ese lugar (US1.3).
        this.serverErrors.set({
          aisle: fields.aisle ?? problem.detail,
          rack: fields.rack ?? problem.detail,
          level: fields.level ?? problem.detail
        });
        this.generalError.set(problem.detail);
        this.duplicateSearch.set(body.aisle);
        break;
      case 'LocationNotEmpty':
      case 'CountScopeFrozen':
        // US4.3: se explica la circunstancia y se enlaza al contenido de la ubicación.
        this.serverErrors.set({ kind: problem.detail });
        this.contentLink.set(true);
        break;
      case 'VersionConflict':
        this.conflict.set(true);
        break;
      default:
        if (problem.status === 403) {
          this.generalError.set(FORBIDDEN_MESSAGE);
        } else {
          this.serverErrors.set(fields);
          this.generalError.set(problem.detail);
        }
    }
    this.revision.update(value => value + 1);
    this.focusFirstError();
  }

  private clearMessages(): void {
    this.serverErrors.set({});
    this.generalError.set(null);
    this.duplicateSearch.set(null);
    this.contentLink.set(false);
    this.conflict.set(false);
  }

  private focusFirstError(): void {
    afterNextRender(() => {
      // Primero el campo con error; si el error no es de un campo, el mensaje general.
      const root = this.host.nativeElement;
      const field = root.querySelector<HTMLElement>('[aria-invalid="true"]');
      const alert = root.querySelector<HTMLElement>('[role="alert"]');
      if (!field && alert && !alert.hasAttribute('tabindex')) alert.setAttribute('tabindex', '-1');
      (field ?? alert)?.focus();
    }, { injector: this.injector });
  }
}
