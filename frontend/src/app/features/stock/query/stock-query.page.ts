import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { apiProblem } from '../../../core/http/api-problem';
import { StockApiService } from '../data/stock-api.service';
import { StockArticleOption, StockLocation, StockLot, StockQueryResult, StockRow, isZeroQuantity, lotText } from '../data/stock.models';

/**
 * HU-05 T015 · /existencias (US1; FR-001–FR-008; contracts/stock-ui.md § Consulta de existencias).
 * Físico, reservado y disponible de un artículo por ubicación y lote. Solo lee: no hay mutaciones
 * ni intención idempotente. Los totales vienen de `totals` (todo el filtro), nunca de sumar la
 * página visible. Las filas a cero se ocultan salvo que se marque la casilla (clarificación 2).
 */
@Component({
  selector: 'app-stock-query-page',
  imports: [DatePipe],
  styleUrl: '../stock.css',
  template: `
    <h1>Existencias</h1>

    <form aria-label="Buscar artículo" (submit)="search($event)">
      <label for="stock-article">Artículo (SKU o nombre)</label>
      <input id="stock-article" type="search" autocomplete="off" [value]="query()" (input)="query.set(value($event))" />
      <button type="submit" [disabled]="!query().trim()">Buscar</button>
    </form>
    @if (options().length) {
      <ul class="options" aria-label="Artículos encontrados">
        @for (option of options(); track option.id) {
          <li>
            <button type="button" data-testid="article-option" (click)="choose(option)">
              {{ option.sku }} · {{ option.name }}@if (!option.isActive) { <span class="badge"> (Inactivo)</span> }
            </button>
          </li>
        }
      </ul>
    } @else if (searched()) {
      <p>No hay artículos que coincidan.</p>
    }

    @if (!article()) {
      <p class="hint">Elige un artículo para ver sus existencias.</p>
    } @else {
      <fieldset class="filters">
        <legend>Filtros</legend>
        <label for="stock-location">Ubicación</label>
        <select id="stock-location" [value]="locationId()" (change)="filter('location', value($event))">
          <option value="">Todas</option>
          @for (place of locations(); track place.id) { <option [value]="place.id">{{ place.code }}</option> }
        </select>
        @if (article()!.tracksLot) {
          <label for="stock-lot">Lote</label>
          <select id="stock-lot" [value]="lotId()" (change)="filter('lot', value($event))">
            <option value="">Todos</option>
            @for (lot of lots(); track lot.id) { <option [value]="lot.id">{{ lot.code }}</option> }
          </select>
        }
        <label>
          <input id="stock-include-empty" type="checkbox" [checked]="includeEmpty()" (change)="toggleEmpty(checked($event))" />Mostrar ubicaciones sin existencias</label>
      </fieldset>
    }

    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }

    @if (data(); as result) {
      <h2>
        {{ result.article.sku }} · {{ result.article.name }}
        @if (!result.article.isActive) { <span class="badge badge-inactive"><span aria-hidden="true">○</span> Inactivo</span> }
      </h2>
      <section class="totals" data-testid="totals" aria-label="Totales de todo el filtro">
        <h3>Totales de todo el filtro</h3>
        <p>
          <span>Físico {{ result.totals.physical }} {{ result.article.baseUnit }}</span> ·
          <span>Reservado {{ result.totals.reserved }} {{ result.article.baseUnit }}</span> ·
          <span>Disponible {{ result.totals.available }} {{ result.article.baseUnit }}</span>
        </p>
      </section>
      <p class="meta">
        <span data-testid="as-of">Datos a las {{ result.asOf | date: 'dd/MM/yyyy HH:mm:ss' }}</span>
        <button type="button" data-testid="refresh" (click)="load(result.page)">Actualizar</button>
      </p>

      @if (empty()) { <p class="empty" data-testid="empty">No hay existencias para esta selección</p> }
      @if (result.items.length) {
        <table aria-label="Existencias por ubicación y lote">
          <thead>
            <tr>
              <th scope="col">Ubicación</th><th scope="col">Lote</th><th scope="col">Físico</th>
              <th scope="col">Reservado</th><th scope="col">Disponible</th>
            </tr>
          </thead>
          <tbody>
            @for (row of result.items; track row.location.id + row.lot.id) {
              <tr>
                <td>
                  {{ row.location.code }}
                  <span class="meta">Pasillo {{ row.location.aisle }} · Estantería {{ row.location.rack }} · Nivel {{ row.location.level }}</span>
                  @if (!row.location.isActive) { <span class="badge badge-inactive"> Inactiva</span> }
                </td>
                <td>{{ lotText(row.lot) }}</td>
                <td>{{ row.physical }} {{ result.article.baseUnit }}</td>
                <td>{{ row.reserved }} {{ result.article.baseUnit }}</td>
                <td>{{ row.available }} {{ result.article.baseUnit }}</td>
              </tr>
            }
          </tbody>
        </table>
        @if (pages() > 1) {
          <nav class="pager" aria-label="Páginas de existencias">
            <button type="button" [disabled]="result.page <= 1" (click)="load(result.page - 1)">Anterior</button>
            <span>Página {{ result.page }} de {{ pages() }}</span>
            <button type="button" [disabled]="result.page >= pages()" (click)="load(result.page + 1)">Siguiente</button>
          </nav>
        }
      }
    }
  `
})
export class StockQueryPage {
  private readonly api = inject(StockApiService);
  protected readonly lotText = lotText;

  readonly query = signal('');
  readonly searched = signal(false);
  readonly options = signal<StockArticleOption[]>([]);
  readonly article = signal<StockArticleOption | null>(null);
  readonly locationId = signal('');
  readonly lotId = signal('');
  readonly includeEmpty = signal(false);
  /** Opciones de filtro del artículo, tomadas de su consulta sin filtros y conservadas al filtrar. */
  readonly locations = signal<StockLocation[]>([]);
  readonly lots = signal<StockLot[]>([]);
  /** Artículo y casilla cuyas opciones ya se leyeron de todas las páginas. */
  private completeOptions: string | null = null;
  readonly data = signal<StockQueryResult | null>(null);
  readonly error = signal<string | null>(null);
  readonly empty = computed(() => {
    const totals = this.data()?.totals;
    return !!totals && isZeroQuantity(totals.physical) && isZeroQuantity(totals.reserved);
  });
  readonly pages = computed(() => {
    const result = this.data();
    return result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;
  });

  search(event: Event): void {
    event.preventDefault();
    const text = this.query().trim();
    if (!text) return;
    this.api.searchArticles(text).subscribe({
      next: items => { this.options.set(items); this.searched.set(true); },
      error: (error: unknown) => this.error.set(apiProblem(error).detail)
    });
  }

  choose(option: StockArticleOption): void {
    this.article.set(option);
    this.options.set([]);
    this.searched.set(false);
    this.locationId.set('');
    this.lotId.set('');
    this.locations.set([]);
    this.lots.set([]);
    this.completeOptions = null;
    this.data.set(null);
    this.load(1);
  }

  filter(kind: 'location' | 'lot', id: string): void {
    (kind === 'location' ? this.locationId : this.lotId).set(id);
    this.load(1);
  }

  toggleEmpty(value: boolean): void {
    this.includeEmpty.set(value);
    this.load(1);
  }

  load(page: number): void {
    const article = this.article();
    if (!article) return;
    this.error.set(null);
    const unfiltered = !this.locationId() && !this.lotId();
    this.api.stock({ articleId: article.id, locationId: this.locationId() || undefined, lotId: this.lotId() || undefined,
      includeEmpty: this.includeEmpty(), page }).subscribe({
      next: result => {
        this.data.set(result);
        if (!unfiltered) return;
        this.collectOptions(result.items);
        // Con más de una página, las opciones salen de todas las combinaciones (C4, FR-004), una
        // vez por artículo y casilla.
        const key = `${article.id}|${this.includeEmpty()}`;
        if (result.totalCount > result.pageSize && this.completeOptions !== key) {
          this.completeOptions = key;
          this.api.allRows(article.id, this.includeEmpty()).subscribe({
            next: rows => { if (this.article()?.id === article.id) this.collectOptions(rows); },
            error: () => { this.completeOptions = null; }
          });
        }
      },
      error: (error: unknown) => {
        const problem = apiProblem(error);
        this.data.set(null);
        this.error.set(problem.status === 404 ? 'El artículo ya no existe.' : problem.detail);
      }
    });
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  private collectOptions(rows: StockRow[]): void {
    const locations = new Map(this.locations().map(place => [place.id, place]));
    const lots = new Map(this.lots().map(lot => [lot.id, lot]));
    for (const row of rows) {
      locations.set(row.location.id, row.location);
      if (!row.lot.isUntracked) lots.set(row.lot.id, row.lot);
    }
    this.locations.set([...locations.values()].sort((a, b) => a.code.localeCompare(b.code)));
    this.lots.set([...lots.values()].sort((a, b) => a.code.localeCompare(b.code)));
  }
}
