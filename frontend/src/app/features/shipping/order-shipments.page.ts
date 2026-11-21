import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { apiProblem } from '../../core/http/api-problem';
import { SessionService } from '../../core/session/session.service';
import { CustomerOrdersApiService, OrderChangedError } from '../stock/data/customer-orders-api.service';
import { OrderDetail, OrderLine, STATE_LABELS } from '../stock/data/stock.models';
import { ShipmentsApiService } from './data/shipments-api.service';
import { SHIPMENT_STATE_LABELS, ShipmentPage, shipmentRoute } from './data/shipping.models';

/**
 * HU-03 T014 · /salidas/pedidos/:orderId (US4.1, US4.3, FR-018; contracts/shipping-ui.md § Pedido y sus
 * salidas). Pendientes por línea con el detalle y el contexto de HU-05, y las salidas del pedido,
 * borradores y confirmadas. «Nueva salida» solo con Shipping.Record y el pedido abierto.
 */
@Component({
  selector: 'app-order-shipments-page',
  imports: [DatePipe, RouterLink],
  styleUrl: '../stock/stock.css',
  template: `
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    @if (detail(); as current) {
      <h1>Salidas del pedido {{ current.order.externalReference }}</h1>
      <p>Cliente: {{ current.order.customerReference }} · Estado: {{ orderState(current.order.state) }}</p>
      @if (current.order.state === 'Fulfilled') {
        <p data-testid="fulfilled">El pedido está servido: no admite nuevas salidas.</p>
      } @else if (current.order.state === 'Open' && canRecord()) {
        <p><a data-testid="new-shipment" [routerLink]="'/salidas/pedidos/' + current.order.id + '/nueva'">Nueva salida</a></p>
      }

      <h2>Líneas del pedido</h2>
      @for (line of current.order.lines; track line.id) {
        <p data-testid="order-line">
          {{ article(line).sku }} · {{ article(line).name }}@if (!article(line).isActive) { <span class="badge-inactive"> (Inactivo)</span> }:
          Pedido {{ line.ordered }} {{ article(line).baseUnit }} · Servido {{ line.shipped }} {{ article(line).baseUnit }} ·
          Pendiente {{ line.pending }} {{ article(line).baseUnit }} · Reservado {{ line.reservedRemaining }} {{ article(line).baseUnit }}
        </p>
      }

      <h2>Salidas</h2>
      @if (shipments(); as page) {
        @if (page.items.length === 0) { <p>Este pedido aún no tiene salidas.</p> }
        @else {
          <table aria-label="Salidas del pedido">
            <thead><tr><th scope="col">Estado</th><th scope="col">Fecha del hecho</th><th scope="col">Registro</th>
              <th scope="col">Responsable</th><th scope="col">Líneas</th><th scope="col">Cantidad</th><th scope="col">Salida</th></tr></thead>
            <tbody>
              @for (item of page.items; track item.id) {
                <tr data-testid="shipment-row">
                  <td>{{ shipmentState(item.state) }}</td>
                  <td>{{ item.occurredAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                  <td>{{ item.recordedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                  <td>{{ item.actorName }}</td>
                  <td>{{ item.lineCount }}</td>
                  <td>@if (unit()) { {{ item.totalQuantity }} {{ unit() }} } @else { — }</td>
                  <td><a [routerLink]="route(item.id)">Abrir</a></td>
                </tr>
              }
            </tbody>
          </table>
        }
      }
    }
  `
})
export class OrderShipmentsPage implements OnInit {
  readonly orderId = inject(ActivatedRoute).snapshot.paramMap.get('orderId') ?? '';
  private readonly orders = inject(CustomerOrdersApiService);
  private readonly api = inject(ShipmentsApiService);
  private readonly session = inject(SessionService);

  readonly detail = signal<OrderDetail | null>(null);
  readonly shipments = signal<ShipmentPage | null>(null);
  readonly error = signal<string | null>(null);
  readonly canRecord = computed(() => this.session.has('Shipping.Record'));
  /** FR-025: el total solo tiene unidad si todas las líneas del pedido comparten la misma. */
  readonly unit = computed(() => {
    const units = new Set(this.detail()?.context.articles.map(article => article.baseUnit) ?? []);
    return units.size === 1 ? [...units][0] : null;
  });

  async ngOnInit(): Promise<void> {
    try {
      this.detail.set(await this.orders.detail(this.orderId));
      this.shipments.set(await this.api.listByOrder(this.orderId));
    } catch (error) {
      this.error.set(error instanceof OrderChangedError ? error.message
        : apiProblem(error).status === 404 ? 'El pedido no existe.' : apiProblem(error).detail);
    }
  }

  article(line: OrderLine) {
    return this.detail()?.context.articles.find(item => item.id === line.articleId)
      ?? { id: line.articleId, sku: '', name: 'Artículo no disponible', baseUnit: '', isActive: true, tracksLot: false, allowsFractional: false };
  }

  orderState(state: string): string { return STATE_LABELS[state] ?? state; }
  shipmentState(state: string): string { return SHIPMENT_STATE_LABELS[state] ?? state; }
  route(id: string): string { return shipmentRoute(id); }
}
