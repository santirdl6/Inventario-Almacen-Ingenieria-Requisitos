import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SessionService } from '../../core/session/session.service';
import { apiProblem } from '../../core/http/api-problem';

interface AlertRow { id: string; articleId: string; articleSku: string; articleName: string; baseUnit: string; state: string;
  physicalSnapshot: string; reservedSnapshot: string; availableSnapshot: string; minimumSnapshot: string; suggestedQuantity: string;
  openedAt: string; resolvedAt: string | null }
interface AlertPage { items: AlertRow[]; totalCount: number }

@Component({ standalone: true, imports: [ReactiveFormsModule, RouterLink], template: `
  <h1>Alertas de stock mínimo</h1>
  <label for="alert-state">Estado</label><select id="alert-state" [formControl]="state"><option value="Active">Activas</option><option value="Resolved">Resueltas</option><option value="All">Todas</option></select>
  <label for="alert-query">Código o nombre</label><input id="alert-query" [formControl]="query"><button type="button" (click)="load(1)">Buscar</button>
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (loading()) { <p role="status">Cargando alertas…</p> }
  @if (!loading() && !error() && !items().length) { <p>No hay alertas para los filtros seleccionados.</p> }
  @if (items().length) {
    <table><caption>Alertas de reposición</caption><thead><tr><th>Artículo</th><th>Estado</th><th>Físico</th><th>Reservado</th><th>Disponible</th><th>Mínimo</th><th>Sugerido</th><th>Fecha</th><th>Acción</th></tr></thead><tbody>
    @for (item of items(); track item.id) { <tr><td><a [routerLink]="['/maestros/articulos', item.articleId]">{{ item.articleSku }} — {{ item.articleName }}</a></td>
      <td>{{ item.state === 'Active' ? 'Activa' : 'Resuelta' }}</td><td>{{ item.physicalSnapshot }} {{ item.baseUnit }}</td><td>{{ item.reservedSnapshot }} {{ item.baseUnit }}</td>
      <td>{{ item.availableSnapshot }} {{ item.baseUnit }}</td><td>{{ item.minimumSnapshot }} {{ item.baseUnit }}</td><td>{{ item.suggestedQuantity }} {{ item.baseUnit }}</td>
      <td>{{ item.openedAt }} @if (item.resolvedAt) { / {{ item.resolvedAt }} }</td><td>
      @if (canPurchase() && item.state === 'Active') { <a routerLink="/compras/nuevo" [queryParams]="{ articleId: item.articleId, suggestedQuantity: item.suggestedQuantity }">Crear pedido</a> }
      </td></tr> }
    </tbody></table>
  }
  <p>Las sugerencias no crean ni envían pedidos automáticamente.</p>
  <button type="button" [disabled]="page() <= 1 || loading()" (click)="load(page()-1)">Anterior</button>
  <span>Página {{ page() }} · {{ total() }} alertas</span>
  <button type="button" [disabled]="page()*25 >= total() || loading()" (click)="load(page()+1)">Siguiente</button>` })
export class AlertsPage implements OnInit {
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly state = new FormControl('Active', { nonNullable: true }); readonly query = new FormControl('', { nonNullable: true });
  readonly items = signal<AlertRow[]>([]); readonly error = signal(''); readonly loading = signal(false); readonly page = signal(1); readonly total = signal(0);
  canPurchase(): boolean { return this.session.has('Purchasing.Manage'); }
  ngOnInit(): void { void this.load(1); }
  async load(page: number): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const params = new HttpParams().set('state', this.state.value).set('q', this.query.value).set('page', page).set('pageSize', 25);
      const result = await firstValueFrom(this.http.get<AlertPage>('/api/v1/stock-alerts', { params }));
      this.items.set(result.items); this.total.set(result.totalCount); this.page.set(page);
    } catch (error) { this.error.set(apiProblem(error).detail); }
    finally { this.loading.set(false); }
  }
}
