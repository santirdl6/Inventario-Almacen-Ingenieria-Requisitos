import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { ArticleLocations, isZero, lotLabel } from '../data/location.models';
import { LocationsApi } from '../data/locations-api.service';
import { asProblem } from '../shared/operation';

/**
 * HU-02 US2 · Todas las ubicaciones de un artículo, con cantidad por ubicación y lote.
 * El total procede de `totals` (todo el artículo), nunca de sumar la página visible.
 *
 * Se llega desde la búsqueda de artículos (article-search.ts, US2.1) o desde el contenido
 * de una ubicación.
 */
@Component({
  selector: 'app-article-locations-page',
  imports: [DatePipe, RouterLink, ButtonModule, TableModule],
  styleUrl: '../locations.css',
  template: `
    <h1>Ubicaciones del artículo</h1>

    <p><a routerLink="/ubicaciones/articulo">Buscar otro artículo</a></p>

    @if (error()) {
      <p class="error-text" role="alert">{{ error() }}</p>
    }

    @if (data(); as result) {
      <h2>
        {{ result.article.sku }} · {{ result.article.name }}
        @if (!result.article.isActive) {
          <span class="badge badge-inactive"><span aria-hidden="true">○</span> Inactivo</span>
        }
      </h2>
      <p class="totals" data-testid="totals">
        Total del artículo: {{ result.totals.physical }} {{ result.article.unit }}
        @if (!isZero(result.totals.reserved)) {
          <span> · reservado {{ result.totals.reserved }} {{ result.article.unit }}</span>
        }
      </p>
      <p class="meta">
        Datos a {{ result.asOf | date: 'dd/MM/yyyy HH:mm:ss' }}
        <p-button label="Actualizar" [text]="true" (onClick)="load(result.page)" />
      </p>

      @if (result.totalCount === 0) {
        <p class="empty" data-testid="empty">Este artículo no está almacenado en ninguna ubicación.</p>
      } @else {
        <p-table [value]="mutable(result.items)" ariaLabel="Ubicaciones del artículo">
          <ng-template #header>
            <tr>
              <th scope="col">Ubicación</th>
              <th scope="col">Descripción</th>
              <th scope="col">Lote</th>
              <th scope="col">Cantidad</th>
              <th scope="col">Reservado</th>
            </tr>
          </ng-template>
          <ng-template #body let-bucket>
            <tr>
              <td>
                <a [routerLink]="['/ubicaciones', bucket.location.id]">{{ bucket.location.code }}</a>
                @if (!bucket.location.isActive) {
                  <span class="badge badge-inactive"><span aria-hidden="true">○</span> Inactiva</span>
                }
              </td>
              <td>{{ bucket.location.description }}</td>
              <td>{{ lotLabel(bucket.lotCode) }}</td>
              <td>{{ bucket.physical }} {{ result.article.unit }}</td>
              <td>{{ isZero(bucket.reserved) ? 'Sin reservas' : bucket.reserved + ' ' + result.article.unit }}</td>
            </tr>
          </ng-template>
        </p-table>
        <nav class="pager" aria-label="Páginas de ubicaciones del artículo">
          <p-button label="Anterior" [text]="true" [disabled]="result.page <= 1" (onClick)="load(result.page - 1)" />
          <span>Página {{ result.page }} de {{ pages() }}</span>
          <p-button label="Siguiente" [text]="true" [disabled]="result.page >= pages()" (onClick)="load(result.page + 1)" />
        </nav>
      }
    }
  `
})
export class ArticleLocationsPage implements OnInit {
  /** Parámetro :articleId de la ruta (A no activa withComponentInputBinding). */
  readonly articleId = signal(inject(ActivatedRoute).snapshot.paramMap.get('articleId') ?? '');

  private readonly api = inject(LocationsApi);
  protected readonly lotLabel = lotLabel;
  protected readonly isZero = isZero;

  readonly data = signal<ArticleLocations | null>(null);
  readonly error = signal<string | null>(null);
  readonly pages = computed(() => {
    const result = this.data();
    return result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;
  });

  ngOnInit(): void {
    this.load(1);
  }

  load(page: number): void {
    this.error.set(null);
    this.api.articleLocations(this.articleId(), page).subscribe({
      next: result => {
        if (result.totalCount > 0 && result.items.length === 0 && result.page > 1) {
          this.load(Math.max(1, Math.ceil(result.totalCount / result.pageSize)));
          return;
        }
        this.data.set(result);
      },
      error: (error: unknown) => {
        const problem = asProblem(error);
        this.error.set(problem.status === 404 ? 'El artículo no existe.' : problem.detail);
      }
    });
  }

  protected mutable<T>(items: readonly T[]): T[] {
    return items as T[];
  }
}
