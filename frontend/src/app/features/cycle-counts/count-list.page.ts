import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { apiProblem } from '../../core/http/api-problem';
import { SessionService } from '../../core/session/session.service';
import { CycleCountsApiService } from './data/cycle-counts-api.service';
import { COUNT_STATE_LABELS, CountPage, NEW_COUNT_ROUTE, countRoute } from './data/cycle-counts.models';

/**
 * HU-04 T014 · /inventarios (contracts/cycle-counts-ui.md § Rutas). Conteos con código, estado en texto,
 * ubicaciones con sus códigos, fechas y responsables (FR-023, Convergence C2), del más reciente al más antiguo; filtro por estado. «Nuevo conteo»
 * solo con Counts.Record. El servidor sigue decidiendo cada permiso.
 */
@Component({
  selector: 'app-count-list-page',
  imports: [DatePipe, RouterLink],
  styleUrl: '../stock/stock.css',
  template: `
    <h1>Inventarios cíclicos</h1>
    @if (canRecord()) { <p><a data-testid="new-count" [routerLink]="newRoute">Nuevo conteo</a></p> }
    <div class="filters">
      <label for="count-state">Estado</label>
      <select id="count-state" [value]="state()" (change)="filter($event)">
        <option value="">Todos</option>
        @for (option of states; track option.value) { <option [value]="option.value">{{ option.label }}</option> }
      </select>
    </div>
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    @if (page(); as current) {
      @if (current.items.length === 0) { <p>No hay conteos con este filtro.</p> }
      @else {
        <table aria-label="Conteos">
          <thead><tr><th scope="col">Código</th><th scope="col">Estado</th><th scope="col">Ubicaciones</th>
            <th scope="col">Creado</th><th scope="col">Iniciado</th><th scope="col">Cerrado</th><th scope="col">Conteo</th></tr></thead>
          <tbody>
            @for (item of current.items; track item.id) {
              <tr>
                <td>{{ item.countCode }}</td>
                <td>{{ stateLabel(item.state) }}</td>
                <td>{{ item.locationIds.length }} {{ item.locationIds.length === 1 ? 'ubicación' : 'ubicaciones' }}:
                  {{ item.locationCodes.join(', ') }}</td>
                <td>{{ item.createdAt | date: 'dd/MM/yyyy HH:mm' }} · Creó {{ item.createdByName }}</td>
                <td>@if (item.startedAt) { {{ item.startedAt | date: 'dd/MM/yyyy HH:mm' }} · Inició {{ item.startedByName }} } @else { — }</td>
                <td>@if (item.closedAt) { {{ item.closedAt | date: 'dd/MM/yyyy HH:mm' }} · Cerró {{ item.closedByName }} } @else { — }</td>
                <td><a [routerLink]="route(item.id)">Abrir</a></td>
              </tr>
            }
          </tbody>
        </table>
        <div class="pager">
          <button type="button" data-testid="previous-page" [disabled]="current.page <= 1 || busy()" (click)="go(current.page - 1)">Anterior</button>
          <span>Página {{ current.page }} de {{ pages() }}</span>
          <button type="button" data-testid="next-page" [disabled]="current.page >= pages() || busy()" (click)="go(current.page + 1)">Siguiente</button>
        </div>
      }
    }
  `
})
export class CountListPage implements OnInit {
  private readonly api = inject(CycleCountsApiService);
  private readonly session = inject(SessionService);
  readonly newRoute = NEW_COUNT_ROUTE;
  readonly states = Object.entries(COUNT_STATE_LABELS).map(([value, label]) => ({ value, label }));
  readonly state = signal('');
  readonly page = signal<CountPage | null>(null);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly canRecord = computed(() => this.session.has('Counts.Record'));
  readonly pages = computed(() => {
    const current = this.page();
    return current ? Math.max(1, Math.ceil(current.totalCount / current.pageSize)) : 1;
  });

  async ngOnInit(): Promise<void> { await this.go(1); }

  async filter(event: Event): Promise<void> {
    this.state.set((event.target as HTMLSelectElement).value);
    await this.go(1);
  }

  async go(page: number): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try { this.page.set(await this.api.list(this.state(), page)); }
    catch (error) { this.error.set(apiProblem(error).detail); }
    finally { this.busy.set(false); }
  }

  stateLabel(state: string): string { return COUNT_STATE_LABELS[state] ?? state; }
  route(id: string): string { return countRoute(id); }
}
