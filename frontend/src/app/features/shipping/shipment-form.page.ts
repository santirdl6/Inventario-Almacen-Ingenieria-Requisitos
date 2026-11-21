import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { apiProblem } from '../../core/http/api-problem';
import { MasterDataMutationsService } from '../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../core/operations/operation-status.component';
import { SessionService } from '../../core/session/session.service';
import { CycleCountsApiService } from '../cycle-counts/data/cycle-counts-api.service';
import { CountSummary, countRoute } from '../cycle-counts/data/cycle-counts.models';
import { CustomerOrdersApiService, isUncertain } from '../stock/data/customer-orders-api.service';
import { StockApiService } from '../stock/data/stock-api.service';
import { OrderDetail, OrderLine, StockRow, lotText } from '../stock/data/stock.models';
import { focusFirstError } from '../stock/shared/focus-first-error';
import { ShipmentsApiService } from './data/shipments-api.service';
import {
  SHIPMENTS, SHIPMENT_STATE_LABELS, ShipmentDetail, ShipmentLineView, ShipmentWrite, confirmResource, orderShipmentsRoute, shipmentRoute
} from './data/shipping.models';
import { ReviewLine, ShipmentReviewDialog } from './shipment-review-dialog';

/** Fila del borrador: un origen (bucket) de una línea de pedido con su cantidad. */
export interface DraftRow {
  orderLineId: string; locationId: string; lotId: string; origin: string; quantity: string; proposed: boolean;
}

/**
 * HU-03 T015 · /salidas/pedidos/:orderId/nueva y /salidas/:id en borrador (US1.1, US1.8–US1.10;
 * FR-004–FR-006a, FR-024; contracts/shipping-ui.md § Preparar y editar un borrador). Propone los
 * orígenes desde la reserva propia (D-13) sin elegir por el operario; el selector ofrece todas las
 * combinaciones con existencias (StockApiService.allRows, analyze-1 H3). Guardar y eliminar usan el
 * transporte común con appRoute; los errores se señalan por línea y el foco va al primero.
 */
@Component({
  selector: 'app-shipment-form-page',
  imports: [DatePipe, OperationStatusComponent, RouterLink, ShipmentReviewDialog],
  styleUrl: '../stock/stock.css',
  template: `
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (error()) {
      <p class="error-text" role="alert">{{ error() }}</p>
      @if (releaseNeeded()) {
        <p><a data-testid="release-link" [routerLink]="'/existencias/pedidos/' + orderId()">Abrir el pedido en Existencias para liberar</a></p>
      }
    }
    <app-operation-status [resource]="pendingResource()" />

    @if (order(); as current) {
      <h1>{{ shipmentId ? 'Salida' : 'Nueva salida' }} del pedido {{ current.order.externalReference }}</h1>
      <p>Cliente: {{ current.order.customerReference }} · <a [routerLink]="orderRoute()">Volver al pedido</a></p>

      @if (detail(); as saved) {
        @if (!editable()) {
          <p>Estado: {{ stateLabel(saved.shipment.state) }} · Fecha del hecho: {{ saved.shipment.occurredAt | date: 'dd/MM/yyyy HH:mm' }}
            · Responsable: {{ saved.context.actorName }}</p>
          <p data-testid="recorded-at">Fecha de registro: {{ saved.shipment.recordedAt | date: 'dd/MM/yyyy HH:mm' }}</p>
          @if (saved.shipment.operationId) {
            <p data-testid="confirmed-operation">Salida confirmada con la operación {{ saved.shipment.operationId }}</p>
          }
          @if (corrections().length) {
            <p class="error-text" data-testid="count-correction"><strong>Aviso:</strong> el conteo
              @for (count of corrections(); track count.id; let last = $last) {
                <a [routerLink]="countLink(count.id)">{{ count.countCode }}</a>@if (!last) {, }
              }
              corrigió las existencias de la ubicación de origen de esta salida, pero el pedido no se corrige: sus líneas
              siguen contando {{ shippedText(saved) }} servidas, su estado no cambia y las reservas consumidas tampoco (P-07).</p>
          }
          @for (line of sortedLines(saved); track line.id) {
            <p data-testid="readonly-line">
              {{ savedArticle(saved, line.articleId) }} · {{ origin(saved, line.locationId, line.lotId) }} · {{ line.quantity }} {{ unitOf(line.customerOrderLineId) }}
              @if (saved.shipment.state === 'Confirmed') { · Reserva propia consumida {{ line.ownReservationConsumed }} {{ unitOf(line.customerOrderLineId) }} }
            </p>
          }
        }
      }

      @if (editable()) {
        <div class="field">
          <label for="occurred-at">Fecha y hora en que sale la mercancía</label>
          <input id="occurred-at" type="datetime-local" [value]="occurredAt()" (input)="occurredAt.set(value($event)); dirty.set(true)"
            [attr.aria-invalid]="errors()['occurredAt'] ? 'true' : null" />
          @if (errors()['occurredAt']; as messages) { <span class="field-error">{{ messages[0] }}</span> }
        </div>
        @if (errors()['lines']; as messages) { <p class="field-error">{{ messages[0] }}</p> }

        @for (line of current.order.lines; track line.id) {
          <section class="order-line" [attr.aria-label]="'Línea ' + article(line).sku">
            <h2>{{ article(line).sku }} · {{ article(line).name }}: pendiente {{ line.pending }} {{ article(line).baseUnit }}</h2>
            @if (hasPending(line)) {
              <button type="button" [attr.data-testid]="'add-' + line.id" (click)="propose(line)">Añadir</button>
              <button type="button" [attr.data-testid]="'choose-origin-' + line.id" (click)="chooseOrigin(line)">Elegir otro origen</button>
            }
            @if (selecting() === line.id) {
              <ul class="options" aria-label="Orígenes con existencias">
                @for (bucket of available(line); track bucket.location.id + bucket.lot.id) {
                  <li><button type="button" data-testid="bucket-option" (click)="addBucket(line, bucket)">
                    {{ bucket.location.code }} · {{ lotText(bucket.lot) }} (disponible {{ bucket.available }} {{ article(line).baseUnit }})
                  </button></li>
                } @empty { <li>No hay otros orígenes con existencias de este artículo.</li> }
              </ul>
            }
          </section>
        }

        <h2>Líneas de la salida</h2>
        @for (row of rows(); track row.orderLineId + row.locationId + row.lotId; let index = $index) {
          <div class="field" data-testid="shipment-line">
            <span data-testid="line-article">{{ articleOf(row.orderLineId) }}</span>
            <span data-testid="origin">{{ row.origin }}</span>
            <label [for]="'quantity-' + index">Cantidad ({{ unitOf(row.orderLineId) }})</label>
            <input [id]="'quantity-' + index" inputmode="decimal" [value]="row.quantity" (input)="setQuantity(index, value($event))"
              [attr.aria-invalid]="lineError(index) ? 'true' : null" />
            <span class="meta">Pendiente {{ pendingOf(row.orderLineId) }} {{ unitOf(row.orderLineId) }}</span>
            @if (row.proposed) { <span class="meta">Propuesto desde la reserva</span> }
            @if (lineError(index); as problem) { <span class="field-error">{{ problem }}</span> }
            <button type="button" (click)="removeRow(index)">Quitar</button>
          </div>
        } @empty { <p>Añade al menos un origen para alguna línea del pedido.</p> }

        <button type="button" data-testid="save" [disabled]="busy() || rows().length === 0" (click)="save()">Guardar borrador</button>
        @if (shipmentId) {
          <button type="button" data-testid="review" [disabled]="busy() || dirty()" (click)="reviewing.set(true)">Revisar y confirmar</button>
          <button type="button" data-testid="delete" [disabled]="busy()" (click)="confirmingDelete.set(true)">Eliminar borrador</button>
          @if (dirty()) { <p class="meta">Guarda el borrador antes de revisarlo.</p> }
        }
        @if (reviewing() && detail(); as saved) {
          <app-shipment-review-dialog [orderReference]="current.order.externalReference" [customer]="current.order.customerReference"
            [lines]="reviewLines(saved)" [busy]="busy()" (confirmed)="confirm()" (cancelled)="reviewing.set(false)" />
        }
        @if (confirmingDelete()) {
          <section class="dialog" role="alertdialog" aria-labelledby="delete-title" data-testid="delete-confirmation">
            <p id="delete-title">¿Eliminar el borrador? No se modifica ninguna existencia ni el pedido.</p>
            <button type="button" data-testid="delete-confirm" [disabled]="busy()" (click)="remove()">Eliminar</button>
            <button type="button" (click)="confirmingDelete.set(false)">Cancelar</button>
          </section>
        }
      }
    }
  `
})
export class ShipmentFormPage implements OnInit {
  private readonly params = inject(ActivatedRoute).snapshot.paramMap;
  readonly shipmentId = this.params.get('id');
  readonly orderId = signal(this.params.get('orderId') ?? '');
  protected readonly api = inject(ShipmentsApiService);
  private readonly orders = inject(CustomerOrdersApiService);
  private readonly counts = inject(CycleCountsApiService);
  private readonly stock = inject(StockApiService);
  private readonly session = inject(SessionService);
  protected readonly router = inject(Router);
  protected readonly host = inject(ElementRef<HTMLElement>);
  protected readonly injector = inject(Injector);
  protected readonly lotText = lotText;

  readonly order = signal<OrderDetail | null>(null);
  readonly detail = signal<ShipmentDetail | null>(null);
  readonly rows = signal<DraftRow[]>([]);
  readonly occurredAt = signal(localNow());
  readonly selecting = signal<string | null>(null);
  readonly buckets = signal<StockRow[]>([]);
  readonly errors = signal<Record<string, string[]>>({});
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly busy = signal(false);
  readonly confirmingDelete = signal(false);
  readonly reviewing = signal(false);
  /** Hay cambios sin guardar: la revisión usa las cifras de la salida guardada. */
  readonly dirty = signal(false);
  readonly releaseNeeded = signal(false);
  /** HU-04 Convergence C3 (US4.3): conteos que corrigen esta salida confirmada (limitación P-07). */
  readonly corrections = signal<CountSummary[]>([]);
  private readonly mutations = inject(MasterDataMutationsService);
  /** Operación pendiente de esta pantalla: la confirmación de la salida o el alta del borrador. */
  readonly pendingResource = computed(() => this.shipmentId ? confirmResource(this.shipmentId) : SHIPMENTS);
  readonly canRecord = computed(() => this.session.has('Shipping.Record'));
  /** Solo un borrador y solo con Shipping.Record (FR-019, D-14). */
  readonly editable = computed(() => this.canRecord() && (this.detail()?.shipment.state ?? 'Draft') === 'Draft');
  readonly orderRoute = computed(() => orderShipmentsRoute(this.orderId()));

  constructor() {
    // Operación recuperada («Comprobar resultado» o «Repetir»): se vuelve a leer la salida.
    this.mutations.recovered.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => void this.load(true));
  }

  async ngOnInit(): Promise<void> { await this.load(true); }

  stateLabel(state: string): string { return SHIPMENT_STATE_LABELS[state] ?? state; }

  sortedLines(saved: ShipmentDetail): ShipmentLineView[] {
    return [...saved.shipment.lines].sort((a, b) => a.id.localeCompare(b.id));
  }

  /** Líneas guardadas con las cifras de `review` del mismo contexto (D-12). */
  reviewLines(saved: ShipmentDetail): ReviewLine[] {
    return this.sortedLines(saved).map(line => {
      const article = saved.context.articles.find(item => item.id === line.articleId);
      return {
        id: line.id, sku: article?.sku ?? '', name: article?.name ?? '', isActive: article?.isActive ?? true,
        unit: article?.baseUnit ?? '', origin: this.origin(saved, line.locationId, line.lotId), quantity: line.quantity,
        review: saved.context.review.find(item => item.lineId === line.id)
      };
    });
  }

  /** Confirmar con el If-Match de la salida y la versión del pedido revisada (D-05). */
  async confirm(): Promise<void> {
    const saved = this.detail();
    if (this.busy() || !saved || !this.shipmentId) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    this.releaseNeeded.set(false);
    try {
      const result = await this.api.confirm(this.shipmentId, saved.context.orderVersion, saved.etag);
      this.reviewing.set(false);
      this.message.set(`Salida registrada. Operación ${result.body?.operationId ?? ''}.`);
      await this.load(true);
    } catch (error) {
      this.reviewing.set(false);
      if (error instanceof HttpErrorResponse && error.status === 409 && apiProblem(error).code === 'ReservedStockConflict') {
        this.releaseNeeded.set(true);
        this.error.set(`${apiProblem(error).detail} Pídelo a un responsable del almacén.`);
        focusFirstError(this.host, this.injector);
      } else {
        await this.fail(error, 'la salida se confirmó',
          'Los datos cambiaron desde que los revisaste. Se ha vuelto a leer la revisión; compruébala y confirma de nuevo.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  /** Lee la salida (si existe) y el pedido; con `resetRows` sustituye las filas por las guardadas. */
  async load(resetRows: boolean): Promise<void> {
    try {
      if (this.shipmentId) {
        const detail = await this.api.detail(this.shipmentId);
        this.detail.set(detail);
        this.orderId.set(detail.shipment.customerOrderId);
        if (resetRows) {
          this.dirty.set(false);
          this.occurredAt.set(localInput(detail.shipment.occurredAt));
          this.rows.set([...detail.shipment.lines].sort((a, b) => a.id.localeCompare(b.id)).map(line => ({
            orderLineId: line.customerOrderLineId, locationId: line.locationId, lotId: line.lotId,
            origin: this.origin(detail, line.locationId, line.lotId), quantity: line.quantity, proposed: false
          })));
        }
      }
      this.order.set(await this.orders.detail(this.orderId()));
      const confirmed = this.detail()?.shipment.state === 'Confirmed' && this.session.has('Inventory.Read');
      this.corrections.set(confirmed && this.shipmentId ? (await this.counts.correcting(this.shipmentId)).items : []);
    } catch (error) {
      this.error.set(apiProblem(error).status === 404 ? 'La salida o el pedido no existen.' : apiProblem(error).detail);
    }
  }

  article(line: OrderLine) {
    return this.order()?.context.articles.find(item => item.id === line.articleId)
      ?? { id: line.articleId, sku: '', name: 'Artículo no disponible', baseUnit: '', isActive: true, tracksLot: false, allowsFractional: false };
  }

  hasPending(line: OrderLine): boolean { return Number(line.pending) > 0; }

  countLink(id: string): string { return countRoute(id); }

  /** Cantidad registrada por la salida, con su unidad: lo que el pedido sigue contando como servido. */
  shippedText(saved: ShipmentDetail): string {
    return saved.shipment.lines.map(line => `${line.quantity} ${this.unitOf(line.customerOrderLineId)}`).join(', ');
  }

  private line(orderLineId: string): OrderLine | undefined {
    return this.order()?.order.lines.find(line => line.id === orderLineId);
  }

  /** Convergence C3 (G2; US4.1, FR-018): «SKU · nombre» del artículo de la línea. */
  articleOf(orderLineId: string): string {
    const line = this.line(orderLineId);
    if (!line) return '';
    const article = this.article(line);
    return `${article.sku} · ${article.name}`;
  }

  savedArticle(saved: ShipmentDetail, articleId: string): string {
    const article = saved.context.articles.find(item => item.id === articleId);
    return article ? `${article.sku} · ${article.name}` : 'Artículo no disponible';
  }

  unitOf(orderLineId: string): string { const line = this.line(orderLineId); return line ? this.article(line).baseUnit : ''; }
  pendingOf(orderLineId: string): string { return this.line(orderLineId)?.pending ?? ''; }

  /** D-13: orígenes de la reserva propia remanente, por Id de asignación y acotados al pendiente. */
  propose(line: OrderLine): void {
    const current = this.order();
    if (!current) return;
    const allocations = line.reservations.filter(reservation => reservation.state === 'Active')
      .flatMap(reservation => reservation.allocations).filter(allocation => Number(allocation.remaining) > 0)
      .sort((a, b) => a.id.localeCompare(b.id));
    let left = Number(line.pending) - this.rows().filter(row => row.orderLineId === line.id)
      .reduce((sum, row) => sum + (Number(row.quantity) || 0), 0);
    const added: DraftRow[] = [];
    for (const allocation of allocations) {
      if (left <= 0) break;
      if (this.rows().some(row => row.orderLineId === line.id && row.locationId === allocation.locationId && row.lotId === allocation.lotId)) continue;
      const quantity = Math.min(left, Number(allocation.remaining));
      left -= quantity;
      added.push({ orderLineId: line.id, locationId: allocation.locationId, lotId: allocation.lotId,
        origin: this.originFromOrder(current, allocation.locationId, allocation.lotId), quantity: quantity.toFixed(3), proposed: true });
    }
    this.rows.update(rows => [...rows, ...added]);
    if (added.length) this.dirty.set(true);
  }

  async chooseOrigin(line: OrderLine): Promise<void> {
    this.selecting.set(line.id);
    try {
      this.buckets.set(await firstValueFrom(this.stock.allRows(line.articleId, false)));
    } catch (error) {
      this.error.set(apiProblem(error).detail);
    }
  }

  available(line: OrderLine): StockRow[] {
    return this.buckets().filter(bucket => !this.rows().some(row => row.orderLineId === line.id
      && row.locationId === bucket.location.id && row.lotId === bucket.lot.id));
  }

  addBucket(line: OrderLine, bucket: StockRow): void {
    this.rows.update(rows => [...rows, { orderLineId: line.id, locationId: bucket.location.id, lotId: bucket.lot.id,
      origin: `${bucket.location.code} · ${lotText(bucket.lot)}`, quantity: '', proposed: false }]);
    this.selecting.set(null);
    this.dirty.set(true);
  }

  setQuantity(index: number, quantity: string): void {
    this.rows.update(rows => rows.map((row, position) => position === index ? { ...row, quantity } : row));
    this.dirty.set(true);
  }

  removeRow(index: number): void {
    this.rows.update(rows => rows.filter((_, position) => position !== index));
    this.dirty.set(true);
    this.errors.set({});
  }

  lineError(index: number): string | null {
    const found = Object.entries(this.errors()).find(([key]) => key.startsWith(`lines[${index}].`));
    return found ? found[1][0] : null;
  }

  protected body(): ShipmentWrite {
    return {
      ...(this.shipmentId ? {} : { customerOrderId: this.orderId() }),
      occurredAt: new Date(this.occurredAt()).toISOString(),
      lines: this.rows().map(row => ({ customerOrderLineId: row.orderLineId, locationId: row.locationId, lotId: row.lotId,
        quantity: row.quantity.trim() }))
    };
  }

  async save(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    this.errors.set({});
    try {
      if (this.shipmentId) {
        await this.api.update(this.shipmentId, this.body(), this.detail()!.etag);
        this.message.set('Borrador guardado.');
        await this.load(true);
      } else {
        const result = await this.api.create(this.orderId(), this.body());
        if (result.body) void this.router.navigateByUrl(shipmentRoute(result.body.shipment.id));
      }
    } catch (error) {
      await this.fail(error, 'el borrador se guardó');
    } finally {
      this.busy.set(false);
    }
  }

  async remove(): Promise<void> {
    if (this.busy() || !this.shipmentId) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.remove(this.shipmentId, this.detail()!.etag);
      void this.router.navigateByUrl(this.orderRoute(), { state: { notice: 'Borrador eliminado. No se ha modificado ninguna existencia.' } });
    } catch (error) {
      this.confirmingDelete.set(false);
      await this.fail(error, 'el borrador se eliminó');
    } finally {
      this.busy.set(false);
    }
  }

  /** Rechazo: incierto, 412 o 409 de estado (relee sin perder lo escrito) o errores por campo con foco al primero. */
  protected async fail(error: unknown, what: string,
    stale = 'Otra persona cambió la salida o el pedido. Se ha vuelto a leer; revisa tus datos y repite.'): Promise<void> {
    if (isUncertain(error)) {
      this.error.set(`Resultado pendiente de comprobar: no sabemos si ${what}. Comprueba el resultado antes de repetir.`);
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 412) {
      this.error.set(stale);
      await this.load(false);
    } else if (!(error instanceof HttpErrorResponse)) {
      this.error.set(error instanceof Error ? error.message : 'No se pudo completar la operación.');
    } else if (error.status === 409 && STATE_CONFLICTS.has(apiProblem(error).code)) {
      // Convergence C4 (G4): el motivo y el estado actual de salida, contexto y pedido, sin perder lo escrito.
      this.error.set(apiProblem(error).detail);
      await this.load(false);
    } else {
      const problem = apiProblem(error);
      this.errors.set(problem.errors);
      this.error.set(problem.status === 403 ? 'Tu cuenta no tiene permiso para registrar salidas.' : problem.detail);
    }
    focusFirstError(this.host, this.injector);
  }

  origin(detail: ShipmentDetail, locationId: string, lotId: string): string {
    const location = detail.context.locations.find(item => item.id === locationId);
    const lot = detail.context.lots.find(item => item.id === lotId);
    return `${location?.code ?? ''} · ${lot ? lotText(lot) : ''}`;
  }

  private originFromOrder(current: OrderDetail, locationId: string, lotId: string): string {
    const location = current.context.locations.find(item => item.id === locationId);
    const lot = current.context.lots.find(item => item.id === lotId);
    return `${location?.code ?? ''} · ${lot ? lotText(lot) : ''}`;
  }

  protected value(event: Event): string { return (event.target as HTMLInputElement).value; }
}

/** 409 que dependen del estado actual del pedido, la ubicación o la salida (shipping-ui § Resultados). */
const STATE_CONFLICTS = new Set(['CustomerOrderNotOpen', 'CountScopeFrozen', 'DocumentAlreadyConfirmed']);

/** «Ahora» para un input datetime-local (hora local, sin segundos). */
function localNow(): string { return localInput(new Date().toISOString()); }

function localInput(instant: string): string {
  const date = new Date(instant);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}
