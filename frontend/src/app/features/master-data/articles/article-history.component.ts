import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { apiProblem } from '../../../core/http/api-problem';
import { MasterDataStatusComponent } from '../../../shared/master-data-status.component';
import { ArticleHistoryApiService } from './article-history-api.service';
import { ArticleMovement, MovementFilterField, MovementFilterOption } from './article-history.models';
import { Article } from './article.models';

interface ReferenceFilter { field: MovementFilterField; control: 'actorId' | 'locationId' | 'lotId'; label: string; plural: string }
interface OptionState { items: MovementFilterOption[]; page: number; total: number; busy: boolean; error: string }
const emptyOptions = (): OptionState => ({ items: [], page: 1, total: 0, busy: false, error: '' });

@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink, TableModule, ButtonModule, MasterDataStatusComponent], template: `
  <h1>Historial de movimientos</h1><a [routerLink]="['/maestros/articulos', id]">Volver al artículo</a>
  @if (article(); as value) { <h2>{{ value.sku }} — {{ value.name }}</h2><app-master-data-status [active]="value.isActive" /> }
  <form [formGroup]="filters" (ngSubmit)="search()"><fieldset disabled [disabled]="loading()"><legend>Filtros del historial</legend>
    <label for="movement-from">Desde (hora local)</label><input id="movement-from" type="datetime-local" formControlName="from">
    <label for="movement-to">Hasta, sin incluir (hora local)</label><input id="movement-to" type="datetime-local" formControlName="to">
    @for (filter of referenceFilters; track filter.field) {
      <fieldset [disabled]="options()[filter.field].busy"><legend>{{ filter.label }}</legend>
        <label [for]="'search-' + filter.field">Buscar {{ filter.label.toLowerCase() }} por nombre o código</label>
        <input [id]="'search-' + filter.field" [formControl]="referenceQueries.controls[filter.field]" maxlength="200">
        <button type="button" (click)="loadOptions(filter)">Buscar {{ filter.plural }}</button>
        <label [for]="'movement-' + filter.field">{{ filter.label }}</label>
        <select [id]="'movement-' + filter.field" [formControlName]="filter.control">
          <option value="">Cualquier {{ filter.label.toLowerCase() }}</option>
          @for (option of options()[filter.field].items; track option.id) { <option [value]="option.id">{{ option.label }}</option> }
        </select>
        <p role="status">Página {{ options()[filter.field].page }}. {{ options()[filter.field].total }} opciones del historial.</p>
        <button type="button" [disabled]="options()[filter.field].page === 1" (click)="loadOptions(filter, -1)">Anteriores {{ filter.plural }}</button>
        <button type="button" [disabled]="options()[filter.field].page * 25 >= options()[filter.field].total" (click)="loadOptions(filter, 1)">Siguientes {{ filter.plural }}</button>
      </fieldset>
      @if (options()[filter.field].error) { <p role="alert">{{ options()[filter.field].error }} Vuelve a buscar {{ filter.plural }} para reintentar.</p> }
    }
    <p-button type="submit" label="Consultar movimientos" [disabled]="loading()" />
  </fieldset></form>
  @if (loading()) { <p role="status">Cargando historial…</p> }
  @else if (error()) { <p role="alert">{{ unavailable() ? 'Historial pendiente de integración. ' : '' }}{{ error() }}</p><button type="button" (click)="load()">Volver a consultar historial</button> }
  @else if (!items().length) { <p>No hay movimientos que coincidan con los filtros.</p> }
  @if (items().length) {
    @if (error()) { <p>Se conserva el último resultado disponible.</p> }
    <p-table [value]="items()" [pt]="{ table: { 'aria-label': 'Movimientos del artículo' } }"><ng-template #header><tr><th>Tipo</th><th>Cantidad y unidad histórica</th><th>Fecha del hecho</th><th>Registro</th><th>Responsable</th><th>Motivo</th><th>Documento</th><th>Soporte / contraparte</th><th>Origen</th><th>Destino</th><th>Lote</th></tr></ng-template>
      <ng-template #body let-movement><tr><td>{{ kindLabel(movement.kind) }}</td><td>{{ movement.quantity }} {{ movement.baseUnit }}</td><td>{{ movement.occurredAt }}</td><td>{{ movement.recordedAt }}</td><td>{{ movement.actor.name }}</td><td>{{ movement.reason }}</td><td>{{ documentLabel(movement.document.type) }}<details><summary>Identificador del documento</summary>{{ movement.document.id }}</details></td><td>{{ movement.supportReference || movement.supportNotApplicableReason || 'Soporte no registrado' }} / {{ movement.counterpartyReference || movement.counterpartyNotApplicableReason || 'Contraparte no registrada' }}</td><td>{{ movement.origin?.code || 'No aplica' }}</td><td>{{ movement.destination?.code || 'No aplica' }}</td><td>{{ movement.lot?.code || 'Sin lote' }}</td></tr></ng-template>
    </p-table>
  }
  @if (!error() && !loading()) { <p role="status">Página {{ page() }}. Total: {{ total() }} movimientos.</p> }
  <p-button label="Anterior" [disabled]="page() === 1 || loading() || !!error()" (onClick)="changePage(-1)" /><p-button label="Siguiente" [disabled]="page() * 25 >= total() || loading() || !!error()" (onClick)="changePage(1)" />
` })
export class ArticleHistoryComponent implements OnInit {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!; private readonly api = inject(ArticleHistoryApiService);
  readonly article = signal<Article | null>(null); readonly items = signal<ArticleMovement[]>([]); readonly loading = signal(true); readonly error = signal(''); readonly unavailable = signal(false);
  readonly page = signal(1); readonly total = signal(0);
  readonly filters = new FormGroup({ from: new FormControl('', { nonNullable: true }), to: new FormControl('', { nonNullable: true }), actorId: new FormControl('', { nonNullable: true }), locationId: new FormControl('', { nonNullable: true }), lotId: new FormControl('', { nonNullable: true }) });
  readonly referenceFilters: ReferenceFilter[] = [
    { field: 'actor', control: 'actorId', label: 'Responsable', plural: 'responsables' },
    { field: 'location', control: 'locationId', label: 'Ubicación', plural: 'ubicaciones' },
    { field: 'lot', control: 'lotId', label: 'Lote', plural: 'lotes' }
  ];
  readonly referenceQueries = new FormGroup({ actor: new FormControl('', { nonNullable: true }), location: new FormControl('', { nonNullable: true }), lot: new FormControl('', { nonNullable: true }) });
  readonly options = signal<Record<MovementFilterField, OptionState>>({ actor: emptyOptions(), location: emptyOptions(), lot: emptyOptions() });
  private readonly generations = { actor: 0, location: 0, lot: 0 };
  ngOnInit(): void { void this.load(); for (const filter of this.referenceFilters) void this.loadOptions(filter); }
  kindLabel(kind: string): string { return ({ Receipt: 'Recepción', Shipment: 'Salida', Adjustment: 'Ajuste', Reservation: 'Reserva', Release: 'Liberación de reserva', Correction: 'Corrección' } as Record<string, string>)[kind] ?? 'Movimiento'; }
  documentLabel(type: string): string { return ({ receipt: 'Recepción', shipment: 'Salida', reservation: 'Reserva', release: 'Liberación de reserva', 'purchase-order': 'Pedido de compra', 'customer-order': 'Pedido de cliente', 'cycle-count': 'Conteo', correction: 'Corrección' } as Record<string, string>)[type.toLowerCase()] ?? 'Documento relacionado'; }
  async loadOptions(filter: ReferenceFilter, delta = 0): Promise<void> {
    const field = filter.field; const generation = ++this.generations[field];
    const previous = this.options()[field]; const page = delta ? previous.page + delta : 1;
    this.options.update(value => ({ ...value, [field]: { ...previous, busy: true, error: '' } }));
    try {
      const result = await this.api.options(this.id, field, this.referenceQueries.controls[field].value, page);
      if (generation !== this.generations[field]) return;
      const selected = previous.items.find(option => option.id === this.filters.controls[filter.control].value);
      const items = selected && !result.items.some(option => option.id === selected.id) ? [selected, ...result.items] : result.items;
      this.options.update(value => ({ ...value, [field]: { items, page: result.page, total: result.totalCount, busy: false, error: '' } }));
    } catch (error) {
      if (generation === this.generations[field]) this.options.update(value => ({ ...value, [field]: { ...previous, busy: false, error: apiProblem(error).detail } }));
    }
  }
  search(): void { this.page.set(1); void this.load(); }
  changePage(delta: number): void { this.page.update(value => value + delta); void this.load(); }
  async load(): Promise<void> {
    this.loading.set(true); this.error.set(''); this.unavailable.set(false);
    try { this.article.set(await this.api.article(this.id)); const result = await this.api.list(this.id, this.filters.getRawValue(), this.page()); this.items.set(result.items); this.total.set(result.totalCount); }
    catch (error) { const issue = apiProblem(error); this.error.set(issue.detail); this.unavailable.set(issue.code === 'DependencyUnavailable'); }
    finally { this.loading.set(false); }
  }
}
