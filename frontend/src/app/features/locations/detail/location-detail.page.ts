import { Component, ElementRef, Injector, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { SessionService } from '../../../core/session/session.service';
import { LocationDetail, LocationStock, Versioned, isZero, lotLabel } from '../data/location.models';
import { LocationsApi } from '../data/locations-api.service';
import { LOCATIONS_MANAGE, asProblem, navigationNotice } from '../shared/operation';
import { LocationStateDialog } from './location-state-dialog';

/**
 * HU-02 US3 · Datos físicos, tipo, estado y contenido de una ubicación (FR-008), con acceso a
 * corregirla y a desactivarla o reactivarla tras revisión (US4). Cada fila del contenido enlaza a todas las ubicaciones de su artículo.
 * El identificador se lee de la ruta y los avisos se muestran en la propia página.
 */
@Component({
  selector: 'app-location-detail-page',
  imports: [RouterLink, ButtonModule, TableModule, LocationStateDialog],
  styleUrl: '../locations.css',
  template: `
    @if (notice()) {
      <p class="notice" role="status" data-testid="notice">{{ notice() }}</p>
    }
    @if (loadError()) {
      <p class="error-text" role="alert">{{ loadError() }}</p>
    }

    @if (location(); as current) {
      <div class="page-header">
        <h1>Ubicación {{ current.value.code }}</h1>
        @if (session.has(manage)) {
          <div class="actions">
            <a class="p-button p-button-outlined" [routerLink]="['/ubicaciones', current.value.id, 'editar']">Editar</a>
            <span data-testid="state-button">
              <p-button [label]="current.value.isActive ? 'Desactivar' : 'Reactivar'" [outlined]="true" (onClick)="dialogOpen.set(true)" />
            </span>
          </div>
        }
      </div>

      <dl class="facts">
        <dt>Ubicación</dt><dd>{{ current.value.description }}</dd>
        <dt>Tipo</dt>
        <dd><span class="badge"><span aria-hidden="true">{{ current.value.kind === 'Receiving' ? '⇩' : '▦' }}</span> {{ current.value.kindLabel }}</span></dd>
        <dt>Estado</dt>
        <dd>
          <span class="badge" [class.badge-inactive]="!current.value.isActive">
            <span aria-hidden="true">{{ current.value.isActive ? '●' : '○' }}</span> {{ current.value.statusLabel }}
          </span>
        </dd>
      </dl>
      @if (current.value.hasReservations) {
        <p class="notice" data-testid="reservations">Hay existencias reservadas en esta ubicación.</p>
      }
      @if (current.value.inOpenCount) {
        <p class="notice" data-testid="open-count">En conteo: la ubicación está incluida en un inventario abierto.</p>
      }

      <h2 id="contenido-ubicacion" tabindex="-1">Contenido</h2>
      @if (stock(); as content) {
        @if (content.totalCount === 0) {
          <p class="empty" data-testid="empty">Esta ubicación está vacía.</p>
        } @else {
          <p-table [value]="mutable(content.items)" ariaLabel="Contenido de la ubicación">
            <ng-template #header>
              <tr>
                <th scope="col">Artículo</th>
                <th scope="col">Lote</th>
                <th scope="col">Cantidad</th>
                <th scope="col">Reservado</th>
                <th scope="col"><span class="visually-hidden">Acciones</span></th>
              </tr>
            </ng-template>
            <ng-template #body let-line>
              <tr>
                <td>
                  {{ line.article.sku }} · {{ line.article.name }}
                  @if (!line.article.isActive) {
                    <span class="badge badge-inactive"><span aria-hidden="true">○</span> Inactivo</span>
                  }
                </td>
                <td>{{ lotLabel(line.lotCode) }}</td>
                <td>{{ line.physical }} {{ line.unit }}</td>
                <td>{{ isZero(line.reserved) ? 'Sin reservas' : line.reserved + ' ' + line.unit + ' reservado' }}</td>
                <td>
                  <a [routerLink]="['/ubicaciones/articulo', line.article.id]"
                    [attr.aria-label]="'Ver todas las ubicaciones de ' + line.article.sku">Ver todas sus ubicaciones</a>
                </td>
              </tr>
            </ng-template>
          </p-table>
          @if (stockPages() > 1) {
            <nav class="pager" aria-label="Páginas del contenido de la ubicación" data-testid="stock-pager">
              <p-button label="Anterior" [text]="true" [disabled]="content.page <= 1" (onClick)="loadStock(content.page - 1)" />
              <span>Página {{ content.page }} de {{ stockPages() }} · {{ content.totalCount }} existencias</span>
              <p-button label="Siguiente" [text]="true" [disabled]="content.page >= stockPages()" (onClick)="loadStock(content.page + 1)" />
            </nav>
          }
        }
      } @else if (stockError()) {
        <p class="error-text" role="alert">{{ stockError() }}</p>
      }

      @if (session.has(manage)) {
        <app-location-state-dialog [location]="current.value" [etag]="current.etag" [visible]="dialogOpen()"
          (changed)="onChanged($event)" (closed)="onClosed()" />
      }
    }
  `
})
export class LocationDetailPage implements OnInit {
  /** Parámetro :id de la ruta (A no activa withComponentInputBinding). */
  readonly id = signal(inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '');

  private readonly api = inject(LocationsApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly session = inject(SessionService);
  protected readonly manage = LOCATIONS_MANAGE;
  protected readonly lotLabel = lotLabel;
  protected readonly isZero = isZero;

  readonly location = signal<Versioned<LocationDetail> | null>(null);
  readonly stock = signal<LocationStock | null>(null);
  readonly stockPages = computed(() => {
    const content = this.stock();
    return content ? Math.max(1, Math.ceil(content.totalCount / content.pageSize)) : 1;
  });
  readonly loadError = signal<string | null>(null);
  readonly stockError = signal<string | null>(null);
  /** Aviso de éxito de la pantalla anterior o de un cambio de estado. */
  readonly notice = signal<string | null>(navigationNotice());
  readonly dialogOpen = signal(false);

  ngOnInit(): void {
    this.load();
  }

  onChanged(saved: Versioned<LocationDetail>): void {
    this.dialogOpen.set(false);
    this.notice.set(`Ubicación ${saved.value.code} ${saved.value.isActive ? 'reactivada' : 'desactivada'}.`);
    this.load();
    this.returnFocus();
  }

  onClosed(): void {
    this.dialogOpen.set(false);
    this.returnFocus();
  }

  private returnFocus(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[data-testid="state-button"] button')?.focus(),
      { injector: this.injector });
  }

  protected mutable<T>(items: readonly T[]): T[] {
    return items as T[];
  }

  protected load(): void {
    this.loadError.set(null);
    this.api.get(this.id()).subscribe({
      next: result => this.location.set(result),
      error: (error: unknown) => {
        const problem = asProblem(error);
        this.loadError.set(problem.status === 404 ? 'La ubicación no existe.' : problem.detail);
      }
    });
    this.loadStock(this.stock()?.page ?? 1);
  }

  /** FR-008: todo el contenido es consultable, también más allá de la primera página. */
  protected loadStock(page: number): void {
    this.stockError.set(null);
    this.api.stock(this.id(), page).subscribe({
      next: result => {
        if (result.totalCount > 0 && result.items.length === 0 && result.page > 1) {
          this.loadStock(Math.max(1, Math.ceil(result.totalCount / result.pageSize)));
          return;
        }
        this.stock.set(result);
      },
      error: (error: unknown) => this.stockError.set(asProblem(error).detail)
    });
  }
}
