import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { apiProblem } from '../../../core/http/api-problem';
import { MasterDataStatusComponent } from '../../../shared/master-data-status.component';
import { ArticleHistoryApiService } from './article-history-api.service';
import { ArticleMovement } from './article-history.models';
import { Article } from './article.models';

@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink, TableModule, ButtonModule, MasterDataStatusComponent], template: `
  <h1>Historial de movimientos</h1><a [routerLink]="['/maestros/articulos', id]">Volver al artículo</a>
  @if (article(); as value) { <h2>{{ value.sku }} — {{ value.name }}</h2><app-master-data-status [active]="value.isActive" /> }
  <form [formGroup]="filters" (ngSubmit)="search()"><fieldset disabled [disabled]="loading()"><legend>Filtros del historial</legend>
    <label for="movement-from">Desde (hora local)</label><input id="movement-from" type="datetime-local" formControlName="from">
    <label for="movement-to">Hasta, sin incluir (hora local)</label><input id="movement-to" type="datetime-local" formControlName="to">
    <label for="movement-actor">Responsable (identificador)</label><input id="movement-actor" formControlName="actorId">
    <label for="movement-location">Ubicación (identificador)</label><input id="movement-location" formControlName="locationId">
    <label for="movement-lot">Lote (identificador)</label><input id="movement-lot" formControlName="lotId">
    <p-button type="submit" label="Consultar movimientos" [disabled]="loading()" />
  </fieldset></form>
  @if (loading()) { <p role="status">Cargando historial…</p> }
  @else if (error()) { <p role="alert">{{ unavailable() ? 'Historial pendiente de integración. ' : '' }}{{ error() }}</p><button type="button" (click)="load()">Volver a consultar historial</button> }
  @else if (!items().length) { <p>No hay movimientos que coincidan con los filtros.</p> }
  @if (items().length) {
    @if (error()) { <p>Se conserva el último resultado disponible.</p> }
    <p-table [value]="items()" ariaLabel="Movimientos del artículo"><ng-template #header><tr><th>Tipo</th><th>Cantidad y unidad histórica</th><th>Fecha del hecho</th><th>Registro</th><th>Responsable</th><th>Motivo</th><th>Documento</th><th>Soporte / contraparte</th><th>Origen</th><th>Destino</th><th>Lote</th></tr></ng-template>
      <ng-template #body let-movement><tr><td>{{ movement.kind }}</td><td>{{ movement.quantity }} {{ movement.baseUnit }}</td><td>{{ movement.occurredAt }}</td><td>{{ movement.recordedAt }}</td><td>{{ movement.actor.name }}</td><td>{{ movement.reason }}</td><td>{{ movement.document.type }} {{ movement.document.id }}</td><td>{{ movement.supportReference || movement.supportNotApplicableReason }} / {{ movement.counterpartyReference || movement.counterpartyNotApplicableReason }}</td><td>{{ movement.origin?.code || 'No aplica' }}</td><td>{{ movement.destination?.code || 'No aplica' }}</td><td>{{ movement.lot?.code || 'Sin lote' }}</td></tr></ng-template>
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
  ngOnInit(): void { void this.load(); }
  search(): void { this.page.set(1); void this.load(); }
  changePage(delta: number): void { this.page.update(value => value + delta); void this.load(); }
  async load(): Promise<void> {
    this.loading.set(true); this.error.set(''); this.unavailable.set(false);
    try { this.article.set(await this.api.article(this.id)); const result = await this.api.list(this.id, this.filters.getRawValue(), this.page()); this.items.set(result.items); this.total.set(result.totalCount); }
    catch (error) { const issue = apiProblem(error); this.error.set(issue.detail); this.unavailable.set(issue.code === 'DependencyUnavailable'); }
    finally { this.loading.set(false); }
  }
}
