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
import { SuppliersApiService } from './suppliers-api.service';
import { Supplier } from './supplier.models';

@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink, TableModule, ButtonModule, InputTextModule, QueryStateComponent, MasterDataStatusComponent], template: `
  <h1>Proveedores</h1>@if (session.has('MasterData.Manage')) { <a routerLink="/maestros/proveedores/nuevo">Nuevo proveedor</a> }
  <form [formGroup]="filters" (ngSubmit)="search()"><fieldset disabled [disabled]="loading()"><legend>Filtros de proveedores</legend>
    <label for="supplier-q">Código o nombre del proveedor</label><input pInputText id="supplier-q" formControlName="q" maxlength="200">
    <label for="supplier-state">Estado del proveedor</label><select id="supplier-state" formControlName="state"><option value="active">Activos</option><option value="inactive">Inactivos</option><option value="all">Todos</option></select>
    <p-button type="submit" label="Buscar proveedores" [disabled]="loading()" />
  </fieldset></form><app-query-state [loading]="loading()" [error]="error()" [empty]="!items().length" (retry)="load()" />
  @if (error() && items().length) { <p>Se conserva el último resultado disponible.</p> }
  <p-table [value]="items()" ariaLabel="Catálogo de proveedores"><ng-template #header><tr><th>Código</th><th>Nombre</th><th>Correo</th><th>Identificador fiscal</th><th>Estado</th><th>Acción</th></tr></ng-template>
    <ng-template #body let-supplier><tr><td>{{ supplier.code }}</td><td>{{ supplier.name }}</td><td>{{ supplier.email || 'Sin correo' }}</td><td>{{ supplier.taxIdentifier || 'No informado' }}</td><td><app-master-data-status [active]="supplier.isActive" /></td><td><a [routerLink]="['/maestros/proveedores', supplier.id]">Consultar proveedor</a></td></tr></ng-template>
  </p-table><p role="status">Página {{ page() }}. Total: {{ total() }} proveedores.</p>
  <p-button label="Anterior" [disabled]="page() === 1 || loading()" (onClick)="changePage(-1)" /><p-button label="Siguiente" [disabled]="page() * 25 >= total() || loading()" (onClick)="changePage(1)" />
` })
export class SupplierListComponent implements OnInit {
  readonly session = inject(SessionService); private readonly api = inject(SuppliersApiService);
  readonly filters = new FormGroup({ q: new FormControl('', { nonNullable: true }), state: new FormControl('active', { nonNullable: true }) });
  readonly items = signal<Supplier[]>([]); readonly page = signal(1); readonly total = signal(0); readonly loading = signal(true); readonly error = signal(''); private generation = 0;
  ngOnInit(): void { void this.load(); }
  search(): void { this.page.set(1); void this.load(); }
  changePage(delta: number): void { this.page.update(value => value + delta); void this.load(); }
  async load(): Promise<void> {
    const generation = ++this.generation; this.loading.set(true); this.error.set('');
    try { const filter = this.filters.getRawValue(); const result = await this.api.list(filter.q, filter.state, this.page()); if (generation === this.generation) { this.items.set(result.items); this.total.set(result.totalCount); } }
    catch (error) { if (generation === this.generation) this.error.set(apiProblem(error).detail); }
    finally { if (generation === this.generation) this.loading.set(false); }
  }
}
