import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { apiProblem } from '../../../core/http/api-problem';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { SessionService } from '../../../core/session/session.service';
import { CustomerOrdersApiService, OrderChangedError } from '../data/customer-orders-api.service';
import { OrderDetail, OrderLine, OrderReservation, RESERVATIONS, STATE_LABELS, lotText, releaseResource } from '../data/stock.models';
import { ReleaseDialog } from './release-dialog';
import { ReserveDialog } from './reserve-dialog';

/**
 * HU-05 T016 · /existencias/pedidos/:id (US2.4, US3, US4; contracts/stock-ui.md § Detalle del pedido).
 * Combina dos lecturas (research D-11): el detalle versionado (datos propios y ETag para los
 * If-Match) y el contexto actual sin ETag (SKU, nombre, unidad, estados y códigos). Si no son de la
 * misma revisión, el servicio vuelve a leer ambas antes de mostrar o permitir reservar o liberar.
 * Las líneas se ordenan por el SKU del contexto. Tras cada confirmación se repiten las dos lecturas.
 */
@Component({
  selector: 'app-order-detail-page',
  imports: [DatePipe, OperationStatusComponent, ReserveDialog, ReleaseDialog],
  styleUrl: '../stock.css',
  template: `
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    <app-operation-status [resource]="reservations" />
    @if (reservePending()) {
      <p data-testid="reserve-blocked">Comprueba la reserva pendiente antes de reservar de nuevo.</p>
    }

    @if (detail(); as current) {
      <h1>Pedido {{ current.order.externalReference }}</h1>
      <p>Cliente: {{ current.order.customerReference }} · Estado: {{ label(current.order.state) }}</p>
      @if (current.context.creationOperationId; as creation) {
        <p data-testid="creation-operation">Pedido creado con la operación {{ creation }}</p>
      }
      <p class="meta">
        Datos a las {{ current.context.asOf | date: 'dd/MM/yyyy HH:mm:ss' }}
        <button type="button" data-testid="refresh-order" (click)="load()">Actualizar</button>
      </p>

      @for (line of lines(); track line.id) {
        <section class="order-line" data-testid="line" [attr.aria-label]="'Línea ' + article(line.articleId).sku">
          <h2>
            {{ article(line.articleId).sku }} · {{ article(line.articleId).name }}
            @if (!article(line.articleId).isActive) { <span class="badge badge-inactive"> Inactivo</span> }
          </h2>
          <p>
            <span>Pedido {{ line.ordered }} {{ unit(line) }}</span> ·
            <span>Servido {{ line.shipped }} {{ unit(line) }}</span> ·
            <span>Pendiente {{ line.pending }} {{ unit(line) }}</span> ·
            <span>Reservado {{ line.reservedRemaining }} {{ unit(line) }}</span>
          </p>
          @if (canManage() && current.order.state === 'Open' && reservable(line)) {
            <button type="button" [attr.data-testid]="'reserve-' + line.id" [disabled]="reservePending()" (click)="reserveLine.set(line.id)">Reservar</button>
          }
          @if (reserveLine() === line.id) {
            <app-reserve-dialog [orderId]="current.order.id" [etag]="current.etag" [line]="line" [context]="current.context"
              [blocked]="reservePending()" (done)="confirmed('Reserva confirmada', $event)" (stale)="load()" (cancelled)="reserveLine.set(null)" />
          }

          @for (reservation of line.reservations; track reservation.id) {
            <div class="reservation">
              <p>Reserva {{ label(reservation.state) }}</p>
              @for (allocation of reservation.allocations; track allocation.id) {
                <p data-testid="reservation">
                  {{ locationCode(allocation.locationId) }} · {{ lot(allocation.lotId) }} ·
                  asignado {{ allocation.allocated }} · consumido {{ allocation.consumed }} · liberado {{ allocation.released }} ·
                  remanente {{ allocation.remaining }} {{ unit(line) }}
                </p>
              }
              <app-operation-status [resource]="release(reservation)" />
              @if (canManage() && reservation.state === 'Active') {
                <button type="button" [attr.data-testid]="'release-' + reservation.id" (click)="releaseReservation.set(reservation.id)">Liberar</button>
              }
              @if (releaseReservation() === reservation.id) {
                <app-release-dialog [orderId]="current.order.id" [reservation]="reservation" [context]="current.context" [unit]="unit(line)"
                  (done)="confirmed('Liberación confirmada', $event)" (stale)="load()" (cancelled)="releaseReservation.set(null)" />
              }
            </div>
          }
        </section>
      }
    }
  `
})
export class OrderDetailPage implements OnInit {
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  private readonly api = inject(CustomerOrdersApiService);
  private readonly session = inject(SessionService);
  private readonly mutations = inject(MasterDataMutationsService);
  protected readonly reservations = RESERVATIONS;

  readonly detail = signal<OrderDetail | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly reserveLine = signal<string | null>(null);
  readonly releaseReservation = signal<string | null>(null);
  readonly canManage = computed(() => this.session.has('Reservations.Manage'));
  readonly reservePending = computed(() => !!this.mutations.pending.forResource(RESERVATIONS));
  readonly lines = computed(() => {
    const current = this.detail();
    if (!current) return [];
    return [...current.order.lines].sort((a, b) => this.article(a.articleId).sku.localeCompare(this.article(b.articleId).sku));
  });

  constructor() {
    // Operación recuperada («Comprobar resultado» o «Repetir»): se vuelven a leer pedido y contexto.
    this.mutations.recovered.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => void this.load());
  }

  ngOnInit(): void { void this.load(); }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.detail.set(await this.api.detail(this.id));
    } catch (error) {
      this.detail.set(null);
      this.error.set(error instanceof OrderChangedError ? error.message
        : apiProblem(error).status === 404 ? 'El pedido no existe.' : apiProblem(error).detail);
    }
  }

  async confirmed(what: string, operationId: string): Promise<void> {
    this.reserveLine.set(null);
    this.releaseReservation.set(null);
    this.message.set(`${what}. Operación ${operationId}.`);
    await this.load();
  }

  article(id: string) {
    return this.detail()?.context.articles.find(item => item.id === id)
      ?? { id, sku: '', name: 'Artículo no disponible', baseUnit: '', isActive: true, tracksLot: false, allowsFractional: false };
  }

  unit(line: OrderLine): string { return this.article(line.articleId).baseUnit; }

  reservable(line: OrderLine): boolean { return Number(line.pending) - Number(line.reservedRemaining) > 0; }

  locationCode(id: string): string { return this.detail()?.context.locations.find(location => location.id === id)?.code ?? ''; }

  lot(id: string): string {
    const lot = this.detail()?.context.lots.find(item => item.id === id);
    return lot ? lotText(lot) : '';
  }

  label(state: string): string { return STATE_LABELS[state] ?? state; }

  release(reservation: OrderReservation): string { return releaseResource(reservation.id); }
}
