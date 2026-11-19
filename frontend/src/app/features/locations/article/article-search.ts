import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { Page } from '../data/location.models';
import { asProblem } from '../shared/operation';

/** Fila de GET /api/v1/articles de A (solo los campos que usa HU-02). */
interface ArticleRow {
  readonly id: string;
  readonly sku: string;
  readonly name: string;
  readonly baseUnit: string;
  readonly isActive: boolean;
}

/**
 * HU-02 US2.1 · Localizar un artículo por código o nombre con la búsqueda de artículos de A
 * (`GET /api/v1/articles?q=`) y abrir todas sus ubicaciones. HU-02 no crea endpoints de artículos.
 */
@Component({
  selector: 'app-article-search',
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, TableModule],
  styleUrl: '../locations.css',
  template: `
    <h1>Localizar un artículo</h1>
    <form class="filters" [formGroup]="form" (ngSubmit)="search()" role="search" aria-label="Buscar artículos">
      <div class="field">
        <label for="article-query">Código o nombre del artículo</label>
        <input pInputText id="article-query" formControlName="query" autocomplete="off" />
      </div>
      <p-button type="submit" label="Buscar" />
    </form>

    @if (error()) {
      <p class="error-text" role="alert">{{ error() }}</p>
    }

    @if (result(); as page) {
      @if (page.items.length === 0) {
        <p class="empty" data-testid="empty">No hay artículos que coincidan con la búsqueda.</p>
      } @else {
        <p-table [value]="mutable(page.items)" ariaLabel="Artículos encontrados">
          <ng-template #header>
            <tr><th scope="col">Código</th><th scope="col">Nombre</th><th scope="col">Estado</th></tr>
          </ng-template>
          <ng-template #body let-article>
            <tr>
              <td><a [routerLink]="['/ubicaciones/articulo', article.id]"
                [attr.aria-label]="'Ver las ubicaciones de ' + article.sku">{{ article.sku }}</a></td>
              <td>{{ article.name }}</td>
              <td>
                <span class="badge" [class.badge-inactive]="!article.isActive">
                  <span aria-hidden="true">{{ article.isActive ? '●' : '○' }}</span> {{ article.isActive ? 'Activo' : 'Inactivo' }}
                </span>
              </td>
            </tr>
          </ng-template>
        </p-table>
      }
    }
  `
})
export class ArticleSearch {
  private readonly http = inject(HttpClient);
  readonly form = new FormGroup({ query: new FormControl('', { nonNullable: true }) });
  readonly result = signal<Page<ArticleRow> | null>(null);
  readonly error = signal<string | null>(null);

  search(): void {
    this.error.set(null);
    // state=all: un artículo inactivo puede seguir teniendo existencias (US2.4).
    const params = new HttpParams().set('q', this.form.controls.query.value.trim()).set('state', 'all').set('page', 1).set('pageSize', 25);
    this.http.get<Page<ArticleRow>>('/api/v1/articles', { params }).subscribe({
      next: page => this.result.set(page),
      error: (error: unknown) => this.error.set(asProblem(error).detail)
    });
  }

  protected mutable<T>(items: readonly T[]): T[] {
    return items as T[];
  }
}
