import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { SessionService } from '../../../core/session/session.service';
import { LOCATIONS_MANAGE, asProblem } from '../shared/operation';
import { KIND_OPTIONS, LocationKind, LocationStatusFilter, LocationSummary, Page } from '../data/location.models';
import { LocationsApi } from '../data/locations-api.service';

/** HU-02 US1.5 · Lista de ubicaciones con búsqueda por código o pasillo y filtros de tipo y estado. */
@Component({
  selector: 'app-location-list-page',
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, TableModule],
  styleUrl: '../locations.css',
  template: `
    <div class="page-header">
      <h1>Ubicaciones</h1>
      <div class="actions">
        <a routerLink="/ubicaciones/articulo">Localizar un artículo</a>
        @if (session.has(manage)) {
          <a class="p-button" routerLink="/ubicaciones/nueva">Nueva ubicación</a>
        }
      </div>
    </div>

    <form class="filters" [formGroup]="filters" (ngSubmit)="search()" role="search" aria-label="Buscar ubicaciones">
      <div class="field">
        <label for="search">Código o pasillo</label>
        <input pInputText id="search" formControlName="search" autocomplete="off" />
      </div>
      <div class="field">
        <label for="kind">Tipo</label>
        <select id="kind" formControlName="kind">
          <option value="">Todos</option>
          @for (option of kindOptions; track option.value) {
            <option [value]="option.value">{{ option.label }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label for="status">Estado</label>
        <select id="status" formControlName="status">
          <option value="active">Activas</option>
          <option value="inactive">Inactivas</option>
          <option value="all">Todas (incluir inactivas)</option>
        </select>
      </div>
      <p-button type="submit" label="Buscar" />
    </form>

    @if (error()) {
      <p class="error-text" role="alert">{{ error() }}</p>
    }

    <p class="visually-hidden" role="status" aria-live="polite">{{ statusText() }}</p>

    @if (result(); as page) {
      @if (page.items.length === 0) {
        <div class="empty" data-testid="empty">
          <p>No hay ubicaciones que coincidan con la búsqueda.</p>
          <p-button label="Limpiar filtros" [outlined]="true" (onClick)="clear()" />
        </div>
      } @else {
        <p-table [value]="mutable(page.items)" dataKey="id" ariaLabel="Ubicaciones encontradas">
          <ng-template #header>
            <tr>
              <th scope="col">Código</th>
              <th scope="col">Descripción</th>
              <th scope="col">Tipo</th>
              <th scope="col">Estado</th>
            </tr>
          </ng-template>
          <ng-template #body let-location>
            <tr>
              <td><a [routerLink]="['/ubicaciones', location.id]">{{ location.code }}</a></td>
              <td>{{ location.description }}</td>
              <td><span class="badge"><span aria-hidden="true">{{ location.kind === 'Receiving' ? '⇩' : '▦' }}</span> {{ location.kindLabel }}</span></td>
              <td>
                <span class="badge" [class.badge-inactive]="!location.isActive">
                  <span aria-hidden="true">{{ location.isActive ? '●' : '○' }}</span> {{ location.statusLabel }}
                </span>
              </td>
            </tr>
          </ng-template>
        </p-table>
        <nav class="pager" aria-label="Páginas de ubicaciones">
          <p-button label="Anterior" [text]="true" [disabled]="page.page <= 1" (onClick)="go(page.page - 1)" />
          <span>Página {{ page.page }} de {{ pages() }} · {{ page.totalCount }} ubicaciones</span>
          <p-button label="Siguiente" [text]="true" [disabled]="page.page >= pages()" (onClick)="go(page.page + 1)" />
        </nav>
      }
    }
  `
})
export class LocationListPage implements OnInit {
  private readonly api = inject(LocationsApi);
  protected readonly session = inject(SessionService);
  protected readonly manage = LOCATIONS_MANAGE;
  private readonly route = inject(ActivatedRoute);
  protected readonly kindOptions = KIND_OPTIONS;

  readonly filters = new FormGroup({
    search: new FormControl(this.route.snapshot.queryParamMap.get('search') ?? '', { nonNullable: true }),
    kind: new FormControl<LocationKind | ''>('', { nonNullable: true }),
    status: new FormControl<LocationStatusFilter>('active', { nonNullable: true })
  });

  readonly result = signal<Page<LocationSummary> | null>(null);
  readonly error = signal<string | null>(null);
  readonly loading = signal(false);
  readonly pages = computed(() => {
    const page = this.result();
    return page ? Math.max(1, Math.ceil(page.totalCount / page.pageSize)) : 1;
  });
  readonly statusText = computed(() => {
    if (this.loading()) return 'Buscando ubicaciones…';
    const page = this.result();
    return page ? `${page.totalCount} ubicaciones encontradas.` : '';
  });

  ngOnInit(): void {
    this.load(1);
  }

  search(): void {
    this.load(1);
  }

  clear(): void {
    this.filters.reset();
    this.load(1);
  }

  go(page: number): void {
    this.load(page);
  }

  protected mutable<T>(items: readonly T[]): T[] {
    return items as T[];
  }

  private load(page: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list({ ...this.filters.getRawValue(), page }).subscribe({
      next: result => {
        this.result.set(result);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(asProblem(error).detail);
      }
    });
  }
}
