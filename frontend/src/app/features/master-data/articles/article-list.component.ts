import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SessionService } from '../../../core/session/session.service';
import { apiProblem } from '../../../core/http/api-problem';
import { QueryStateComponent } from '../../../shared/query-state.component';
import { MasterDataStatusComponent } from '../../../shared/master-data-status.component';
import { FamiliesApiService } from '../families/families-api.service';
import { Family } from '../families/family.models';
import { ArticlesApiService } from './articles-api.service';
import { ArticleSummary } from './article.models';

@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink, TableModule, ButtonModule, InputTextModule, QueryStateComponent, MasterDataStatusComponent],
  template: `
    <h1>Artículos</h1>@if (session.has('MasterData.Manage')) { <a routerLink="/maestros/articulos/nuevo">Nuevo artículo</a> }
    <form [formGroup]="filters" (ngSubmit)="search()"><fieldset disabled [disabled]="loading()"><legend>Filtros de artículos</legend>
      <label for="article-q">Código o nombre del artículo</label><input pInputText id="article-q" [formControl]="filters.controls.q" maxlength="200">
      <label for="article-state">Estado del artículo</label><select id="article-state" [formControl]="filters.controls.state"><option value="active">Activos</option><option value="inactive">Inactivos</option><option value="all">Todos</option></select>
      <label for="filter-family-q">Buscar familia del filtro</label><input pInputText id="filter-family-q" [formControl]="familyQuery" maxlength="200">
      <p-button type="button" label="Buscar familias del filtro" (onClick)="loadFamilies()" />
      <label for="article-family">Familia del artículo</label><select id="article-family" [formControl]="filters.controls.familyId"><option value="">Todas</option>@for (family of families(); track family.id) { <option [value]="family.id">{{ family.code }} — {{ family.name }}</option> }</select>
      <p-button type="submit" label="Buscar artículos" [disabled]="loading()" />
    </fieldset></form>
    @if (familyError()) { <p role="alert">{{ familyError() }}</p> }
    <app-query-state [loading]="loading()" [error]="error()" [empty]="!items().length" (retry)="load()" />
    @if (error() && items().length) { <p>Se conserva el último resultado disponible.</p> }
    <p-table [value]="items()"><ng-template #header><tr><th>Código</th><th>Nombre</th><th>Familia</th><th>Unidad</th><th>Estado</th><th>Acción</th></tr></ng-template>
    <ng-template #body let-article><tr><td>{{ article.sku }}</td><td>{{ article.name }}</td><td>{{ article.family.code }} — {{ article.family.name }}</td><td>{{ article.baseUnit }}</td><td><app-master-data-status [active]="article.isActive" /></td><td><a [routerLink]="['/maestros/articulos', article.id]">Consultar artículo</a></td></tr></ng-template></p-table>
    <p role="status">Página {{ page() }}. Total: {{ total() }} artículos.</p>
    <p-button label="Anterior" [disabled]="page() === 1 || loading()" (onClick)="changePage(-1)" />
    <p-button label="Siguiente" [disabled]="page() * 25 >= total() || loading()" (onClick)="changePage(1)" />
  ` })
export class ArticleListComponent implements OnInit {
  readonly session = inject(SessionService); private readonly api = inject(ArticlesApiService); private readonly familyApi = inject(FamiliesApiService);
  readonly filters = new FormGroup({ q: new FormControl('', { nonNullable: true }), state: new FormControl('active', { nonNullable: true }), familyId: new FormControl('', { nonNullable: true }) });
  readonly items = signal<ArticleSummary[]>([]); readonly families = signal<Family[]>([]); readonly page = signal(1); readonly total = signal(0);
  readonly loading = signal(true); readonly error = signal(''); private generation = 0;
  readonly familyQuery = new FormControl('', { nonNullable: true }); readonly familyError = signal('');
  async ngOnInit(): Promise<void> {
    await this.loadFamilies();
    await this.load();
  }
  async loadFamilies(): Promise<void> {
    this.familyError.set('');
    try {
      const result = await this.familyApi.list(this.familyQuery.value, 'all', 1);
      const selected = this.families().find(value => value.id === this.filters.controls.familyId.value);
      this.families.set(selected && !result.items.some(value => value.id === selected.id) ? [selected, ...result.items] : result.items);
    } catch (error) { this.familyError.set(apiProblem(error).detail); }
  }
  search(): void { this.page.set(1); void this.load(); }
  changePage(delta: number): void { this.page.update(value => value + delta); void this.load(); }
  async load(): Promise<void> {
    const generation = ++this.generation; this.loading.set(true); this.error.set('');
    try {
      const filters = this.filters.getRawValue(); const result = await this.api.list(filters.q, filters.state, filters.familyId, this.page());
      if (generation === this.generation) { this.items.set(result.items); this.total.set(result.totalCount); }
    } catch (error) { if (generation === this.generation) this.error.set(apiProblem(error).detail); }
    finally { if (generation === this.generation) this.loading.set(false); }
  }
}
