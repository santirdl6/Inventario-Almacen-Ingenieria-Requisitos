import { Component, ElementRef, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { apiProblem } from '../../core/http/api-problem';
import { OperationStatusComponent } from '../../core/operations/operation-status.component';
import { LocationSummary } from '../locations/data/location.models';
import { isUncertain } from '../stock/data/customer-orders-api.service';
import { focusFirstError } from '../stock/shared/focus-first-error';
import { CycleCountsApiService } from './data/cycle-counts-api.service';
import { CYCLE_COUNTS, MAX_LOCATIONS, countRoute } from './data/cycle-counts.models';

/**
 * HU-04 T014 · /inventarios/nuevo (US1; contracts/cycle-counts-ui.md § Rutas). Elegir ubicaciones activas
 * (todas las páginas) y guardar el borrador. El borrador no congela nada: se congela al iniciarlo.
 */
@Component({
  selector: 'app-count-new-page',
  imports: [OperationStatusComponent],
  styleUrl: '../stock/stock.css',
  template: `
    <h1>Nuevo conteo</h1>
    <p>Elige las ubicaciones que vas a contar (máximo {{ max }}). Guardar el borrador no las congela: se congelan al iniciar el conteo.</p>
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    <app-operation-status [resource]="resource" />
    <form (submit)="save($event)" novalidate>
      <label for="location-filter">Buscar ubicación</label>
      <input id="location-filter" type="search" [value]="query()" (input)="query.set($any($event.target).value)" />
      <fieldset [attr.aria-invalid]="fieldError() ? 'true' : null" [attr.aria-describedby]="fieldError() ? 'locations-error' : null" tabindex="-1">
        <legend>Ubicaciones activas ({{ selected().length }} elegidas)</legend>
        @if (fieldError()) { <span id="locations-error" class="field-error">{{ fieldError() }}</span> }
        @for (location of visible(); track location.id) {
          <div>
            <input type="checkbox" [id]="'loc-' + location.id" [value]="location.id" [checked]="selected().includes(location.id)"
              (change)="toggle(location.id, $any($event.target).checked)" />
            <label [for]="'loc-' + location.id">{{ location.code }}</label>
          </div>
        } @empty { <p>No hay ubicaciones activas con ese texto.</p> }
      </fieldset>
      <button type="submit" [disabled]="busy()">Guardar borrador</button>
    </form>
  `
})
export class CountNewPage implements OnInit {
  private readonly api = inject(CycleCountsApiService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  readonly max = MAX_LOCATIONS;
  readonly resource = CYCLE_COUNTS;
  readonly locations = signal<LocationSummary[]>([]);
  readonly selected = signal<string[]>([]);
  readonly query = signal('');
  readonly error = signal<string | null>(null);
  readonly fieldError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly visible = computed(() => {
    const text = this.query().trim().toLowerCase();
    return this.locations().filter(location => !text || location.code.toLowerCase().includes(text));
  });

  async ngOnInit(): Promise<void> {
    try {
      const all = await firstValueFrom(this.api.activeLocations());
      this.locations.set([...all].sort((a, b) => a.code.localeCompare(b.code)));
    } catch (error) {
      this.error.set(apiProblem(error).detail);
    }
  }

  toggle(id: string, checked: boolean): void {
    this.selected.update(ids => checked ? [...new Set([...ids, id])] : ids.filter(item => item !== id));
    this.fieldError.set(null);
  }

  async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) return;
    this.error.set(null);
    const ids = [...this.selected()].sort();
    if (!ids.length || ids.length > MAX_LOCATIONS) {
      this.fieldError.set(ids.length ? `Elige como máximo ${MAX_LOCATIONS} ubicaciones.` : 'Elige al menos una ubicación.');
      this.error.set('Revisa las ubicaciones elegidas.');
      if (!ids.length) this.error.set('Elige al menos una ubicación para el conteo.');
      focusFirstError(this.host, this.injector);
      return;
    }
    this.busy.set(true);
    try {
      const result = await this.api.create(ids);
      const id = result.body?.count.id;
      if (id) await this.router.navigateByUrl(countRoute(id));
    } catch (error) {
      if (isUncertain(error)) {
        this.error.set('Resultado pendiente de comprobar: no sabemos si el borrador se guardó. Comprueba el resultado antes de repetir.');
      } else {
        const problem = apiProblem(error);
        const fieldMessages = Object.entries(problem.errors).filter(([key]) => key.startsWith('locationIds')).flatMap(([, value]) => value);
        this.fieldError.set(fieldMessages.length ? fieldMessages.join(' ') : null);
        this.error.set(problem.status === 403 ? 'Tu cuenta no tiene permiso para registrar conteos.' : problem.detail);
      }
      focusFirstError(this.host, this.injector);
    } finally {
      this.busy.set(false);
    }
  }
}
