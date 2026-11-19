import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { timeout } from 'rxjs';
import { ApiProblem } from '../../../core/http/api-problem';
import { completeIntent, intentKey } from '../data/idempotent-intent';
import { LocationDetail, UpdateLocationRequest, Versioned } from '../data/location.models';
import { LocationsApi } from '../data/locations-api.service';
import {
  FORBIDDEN_MESSAGE,
  MUTATION_TIMEOUT_MS,
  OperationStatus,
  PENDING_CHECK_MESSAGE,
  asProblem,
  isUncertain
} from '../shared/operation';

/**
 * HU-02 US4 · Revisión y confirmación de la desactivación o reactivación de una ubicación
 * (FR-012, FR-013, FR-020). La última zona de recepción activa no puede retirarse (FR-021,
 * acuerdo con A del 2026-11-19): si se sabe de antemano no se ofrece confirmar, y si el
 * servidor responde 409 NoActiveReceivingLeft se muestra su explicación sin reintentar.
 * No existe acción de borrado (FR-014).
 */
@Component({
  selector: 'app-location-state-dialog',
  imports: [DialogModule, ButtonModule],
  styleUrl: '../locations.css',
  template: `
    <p-dialog [visible]="visible()" (visibleChange)="$event || close()" [modal]="true" [draggable]="false"
      [resizable]="false" [closable]="status() !== 'confirming'" [header]="title()" [style]="{ width: '32rem' }">
      <dl class="facts">
        <dt>Código</dt><dd>{{ location().code }}</dd>
        <dt>Ubicación</dt><dd>{{ location().description }}</dd>
        <dt>Tipo</dt><dd>{{ location().kindLabel }}</dd>
        <dt>Contenido</dt><dd data-testid="content">{{ contentText() }}</dd>
      </dl>
      <p data-testid="consequence">{{ consequence() }}</p>

      @if (lastReceiving()) {
        <p class="warning" role="alert" data-testid="no-receiving">
          No se puede retirar la única zona de recepción activa: el almacén se quedaría sin ninguna.
          Crea o activa otra zona de recepción antes de retirar esta.
        </p>
      }
      @if (problem(); as message) {
        <p class="error-text" role="alert" data-testid="dialog-error">
          {{ message }}
          @if (notEmpty()) {
            <a href="#contenido-ubicacion" (click)="close()">Ver el contenido</a>
          }
        </p>
      }
      @if (status() === 'pending-check') {
        <div class="pending" role="status" data-testid="pending">
          <p>{{ pendingMessage }}</p>
          <p-button label="Comprobar" (onClick)="check()" />
        </div>
      }

      <ng-template #footer>
        @if (!lastReceiving()) {
          <p-button [label]="deactivating() ? 'Desactivar' : 'Reactivar'" (onClick)="confirm()"
            [disabled]="status() !== 'idle' || blocked()" [loading]="status() === 'confirming'"
            [severity]="deactivating() ? 'danger' : undefined" />
        }
        <p-button [label]="lastReceiving() ? 'Cerrar' : 'Cancelar'" [text]="true" (onClick)="close()"
          [disabled]="status() === 'confirming'" />
      </ng-template>
    </p-dialog>
  `
})
export class LocationStateDialog {
  readonly location = input.required<LocationDetail>();
  readonly etag = input.required<string>();
  readonly visible = input(false);
  readonly changed = output<Versioned<LocationDetail>>();
  readonly closed = output<void>();

  private readonly api = inject(LocationsApi);
  protected readonly pendingMessage = PENDING_CHECK_MESSAGE;

  readonly status = signal<OperationStatus>('idle');
  readonly problem = signal<string | null>(null);
  readonly notEmpty = signal(false);
  /** El servidor confirmó que es la última zona de recepción activa. */
  private readonly serverLast = signal(false);
  /** Tras un rechazo que exige reabrir (versión obsoleta, permiso) no se ofrece reintentar. */
  readonly blocked = signal(false);
  private pendingBody: UpdateLocationRequest | null = null;

  readonly deactivating = computed(() => this.location().isActive);
  readonly title = computed(() => (this.deactivating() ? 'Desactivar ubicación' : 'Reactivar ubicación'));
  readonly lastReceiving = computed(() =>
    this.deactivating() && this.location().kind === 'Receiving' && (this.location().isLastActiveReceiving || this.serverLast()));
  readonly contentText = computed(() => {
    const location = this.location();
    if (location.isEmpty) return 'Vacía';
    return location.hasReservations ? 'Con existencias y reservas' : 'Con existencias';
  });
  readonly consequence = computed(() =>
    this.deactivating()
      ? 'Dejará de ofrecerse para nuevas recepciones, salidas y reservas. Seguirá pudiendo consultarse y su historial se conserva.'
      : 'Volverá a ofrecerse para nuevas operaciones. Su historial no cambia.'
  );

  constructor() {
    // Cada apertura empieza sin mensajes de la anterior.
    effect(() => {
      if (this.visible()) this.reset();
    });
  }

  confirm(): void {
    if (this.status() !== 'idle' || this.lastReceiving()) return;
    const location = this.location();
    this.problem.set(null);
    this.notEmpty.set(false);
    this.send({
      code: location.code,
      aisle: location.aisle,
      rack: location.rack,
      level: location.level,
      kind: location.kind,
      isActive: !location.isActive
    });
  }

  check(): void {
    if (this.pendingBody && this.status() === 'pending-check') {
      this.status.set('idle');
      this.send(this.pendingBody);
    }
  }

  close(): void {
    if (this.status() === 'confirming') return;
    this.closed.emit();
  }

  private send(body: UpdateLocationRequest): void {
    const location = this.location();
    const intent = `location:state:${location.id}`;
    const key = intentKey(intent, body);
    this.pendingBody = body;
    this.status.set('confirming');
    this.api.update(location.id, body, this.etag(), key).pipe(timeout(MUTATION_TIMEOUT_MS)).subscribe({
      next: saved => {
        completeIntent(intent);
        this.pendingBody = null;
        this.status.set('idle');
        this.changed.emit(saved);
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
    switch (problem.code) {
      case 'NoActiveReceivingLeft':
        // Se impide: no hay reenvío posible desde este diálogo.
        this.serverLast.set(true);
        this.problem.set(problem.detail);
        return;
      case 'LocationNotEmpty':
        this.notEmpty.set(true);
        this.problem.set(problem.detail);
        return;
      case 'VersionConflict':
        this.blocked.set(true);
        this.problem.set('Otra persona ha cambiado esta ubicación. Cierra este diálogo y vuelve a abrirla para ver los datos vigentes.');
        return;
      default:
        if (problem.status === 403) {
          this.blocked.set(true);
          this.problem.set(FORBIDDEN_MESSAGE);
        } else {
          this.problem.set(problem.detail);
        }
    }
  }

  private reset(): void {
    this.status.set('idle');
    this.problem.set(null);
    this.notEmpty.set(false);
    this.serverLast.set(false);
    this.blocked.set(false);
    this.pendingBody = null;
  }
}
