import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SessionService } from '../../../core/session/session.service';
import { apiProblem } from '../../../core/http/api-problem';
import { MasterDataStatusComponent } from '../../../shared/master-data-status.component';
import { QueryStateComponent } from '../../../shared/query-state.component';
import { FamiliesApiService } from './families-api.service';
import { Family } from './family.models';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, TableModule, ButtonModule, InputTextModule, MasterDataStatusComponent, QueryStateComponent],
  template: `
    <h1>Familias</h1>
    @if (session.has('MasterData.Manage')) { <a routerLink="/maestros/familias/nuevo">Nueva familia</a> }
    <form [formGroup]="filters" (ngSubmit)="search()">
      <fieldset disabled [disabled]="loading()"><legend>Filtros del catálogo</legend>
      <label for="family-search">Código o nombre</label>
      <input pInputText id="family-search" [formControl]="filters.controls.q" maxlength="200">
      <label for="family-state">Estado</label>
      <select id="family-state" [formControl]="filters.controls.state"><option value="active">Activos</option><option value="inactive">Inactivos</option><option value="all">Todos</option></select>
      <p-button type="submit" label="Buscar" [disabled]="loading()" />
      </fieldset>
    </form>
    <app-query-state [loading]="loading()" [error]="error()" [empty]="!items().length" (retry)="load()" />
    @if (error() && items().length) { <p>Se conserva el último resultado disponible.</p> }
    <p-table [value]="items()" ariaLabel="Catálogo de familias">
      <ng-template #header><tr><th>Código</th><th>Nombre</th><th>Estado</th><th>Acción</th></tr></ng-template>
      <ng-template #body let-family><tr><td>{{ family.code }}</td><td>{{ family.name }}</td>
        <td><app-master-data-status [active]="family.isActive" /></td>
        <td><a [routerLink]="['/maestros/familias', family.id]">Consultar</a></td></tr></ng-template>
    </p-table>
    <p role="status">Página {{ page() }}. Total: {{ total() }} familias.</p>
    <p-button label="Anterior" [disabled]="page() === 1 || loading()" (onClick)="changePage(-1)" />
    <p-button label="Siguiente" [disabled]="page() * 25 >= total() || loading()" (onClick)="changePage(1)" />
  `
})
export class FamilyListComponent implements OnInit {
  readonly session = inject(SessionService);
  private readonly api = inject(FamiliesApiService);
  readonly filters = new FormGroup({ q: new FormControl('', { nonNullable: true }), state: new FormControl('active', { nonNullable: true }) });
  readonly items = signal<Family[]>([]); readonly page = signal(1); readonly total = signal(0);
  readonly loading = signal(true); readonly error = signal('');
  private generation = 0;
  ngOnInit(): void { void this.load(); }
  search(): void { this.page.set(1); void this.load(); }
  changePage(delta: number): void { this.page.update(value => value + delta); void this.load(); }
  async load(): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true); this.error.set('');
    try {
      const filter = this.filters.getRawValue();
      const result = await this.api.list(filter.q, filter.state, this.page());
      if (generation === this.generation) { this.items.set(result.items); this.total.set(result.totalCount); }
    } catch (error) { if (generation === this.generation) this.error.set(apiProblem(error).detail); }
    finally { if (generation === this.generation) this.loading.set(false); }
  }
}
