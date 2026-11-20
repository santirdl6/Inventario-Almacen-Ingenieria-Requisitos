import { A11yModule } from '@angular/cdk/a11y';
import { Component, ElementRef, Injector, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { apiProblem } from '../../../core/http/api-problem';
import { CustomerOrdersApiService, isUncertain } from '../data/customer-orders-api.service';
import { StockApiService } from '../data/stock-api.service';
import { CustomerOrderContext, OrderLine, StockRow, lotText } from '../data/stock.models';
import { allocationErrorIndexes, focusFirstError } from '../shared/focus-first-error';

/**
 * HU-05 T016 · Reservar existencias para una línea (US3; contracts/stock-ui.md). Lista GET /stock
 * del artículo con el máximo reservable como ayuda, mín(pendiente − reservado, disponible); el
 * servidor revalida. Revisión → confirmar con el If-Match del pedido. Un 412 pide releer el pedido
 * sin perder las cantidades escritas.
 */
@Component({
  selector: 'app-reserve-dialog',
  imports: [A11yModule, RouterLink],
  styleUrl: '../stock.css',
  template: `
    <section class="dialog" role="dialog" aria-modal="true" aria-labelledby="reserve-title" data-testid="reserve-dialog"
      cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)="cancelled.emit()">
      <h2 id="reserve-title">Reservar {{ article().sku }} · {{ article().name }}</h2>
      <p>Pendiente {{ line().pending }} {{ article().baseUnit }} · Ya reservado {{ line().reservedRemaining }} {{ article().baseUnit }}</p>
      @if (error()) {
        <p class="error-text" role="alert">{{ error() }}</p>
        @if (conflict()) { <p><a routerLink="/existencias">Ver existencias actuales</a></p> }
      }
      @if (rows().length === 0 && loaded()) { <p>No hay existencias disponibles de este artículo.</p> }
      @if (rows().length) {
        <table aria-label="Existencias para reservar">
          <thead><tr><th scope="col">Ubicación</th><th scope="col">Lote</th><th scope="col">Disponible</th><th scope="col">Cantidad a reservar</th></tr></thead>
          <tbody>
            @for (row of rows(); track key(row)) {
              <tr>
                <td>{{ row.location.code }}</td>
                <td>{{ lotText(row.lot) }}</td>
                <td>{{ row.available }} {{ article().baseUnit }}</td>
                <td>
                  <label class="visually-hidden" [for]="'reserve-' + key(row)">Cantidad en {{ row.location.code }} ({{ article().baseUnit }})</label>
                  <input [id]="'reserve-' + key(row)" [attr.data-testid]="'reserve-qty-' + key(row)" inputmode="decimal"
                    [attr.aria-invalid]="invalid().has(key(row)) ? 'true' : null"
                    [value]="quantities()[key(row)] ?? ''" [disabled]="reviewing()" (input)="set(row, value($event))" />
                  <span class="meta">Máximo reservable {{ maximum(row) }} {{ article().baseUnit }}</span>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
      @if (!reviewing()) {
        <button type="button" data-testid="reserve-review" [disabled]="!chosen().length" (click)="review()">Revisar</button>
      } @else {
        <p>Vas a reservar {{ total() }} {{ article().baseUnit }} para el pedido.</p>
        <button type="button" data-testid="reserve-confirm" [disabled]="busy() || blocked()" (click)="confirm()">Confirmar reserva</button>
        <button type="button" [disabled]="busy()" (click)="reviewing.set(false)">Volver a editar</button>
      }
      <button type="button" (click)="cancelled.emit()">Cerrar</button>
    </section>
  `
})
export class ReserveDialog implements OnInit {
  readonly orderId = input.required<string>();
  readonly etag = input.required<string>();
  readonly line = input.required<OrderLine>();
  readonly context = input.required<CustomerOrderContext>();
  /** Otra reserva del usuario sigue pendiente de comprobar: no se envía otra (stock-ui.md). */
  readonly blocked = input(false);
  readonly done = output<string>();
  readonly stale = output<void>();
  readonly cancelled = output<void>();

  private readonly api = inject(CustomerOrdersApiService);
  private readonly stock = inject(StockApiService);
  protected readonly lotText = lotText;

  readonly rows = signal<StockRow[]>([]);
  readonly loaded = signal(false);
  readonly quantities = signal<Record<string, string>>({});
  readonly reviewing = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly conflict = signal(false);
  /** Filas (ubicación-lote) señaladas por el servidor: «allocations[i]» es la i-ésima enviada. */
  readonly invalid = signal<Set<string>>(new Set());
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  readonly article = computed(() => this.context().articles.find(item => item.id === this.line().articleId)
    ?? { id: this.line().articleId, sku: '', name: '', baseUnit: '', isActive: true, tracksLot: false, allowsFractional: false });
  readonly chosen = computed(() => this.rows().filter(row => Number(this.quantities()[this.key(row)] || 0) > 0));
  readonly total = computed(() => this.chosen().reduce((sum, row) => sum + Number(this.quantities()[this.key(row)]), 0).toFixed(3));

  ngOnInit(): void {
    this.stock.stock({ articleId: this.line().articleId, includeEmpty: false, page: 1 }).subscribe({
      next: result => { this.rows.set(result.items); this.loaded.set(true); },
      error: (error: unknown) => this.error.set(apiProblem(error).detail)
    });
  }

  key(row: StockRow): string { return `${row.location.id}-${row.lot.id}`; }

  maximum(row: StockRow): string {
    const open = Number(this.line().pending) - Number(this.line().reservedRemaining);
    return Math.max(0, Math.min(open, Number(row.available))).toFixed(3);
  }

  set(row: StockRow, value: string): void {
    this.quantities.update(values => ({ ...values, [this.key(row)]: value }));
  }

  review(): void {
    this.error.set(null);
    this.conflict.set(false);
    this.reviewing.set(true);
  }

  async confirm(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.conflict.set(false);
    try {
      const result = await this.api.reserve(this.orderId(), {
        customerOrderLineId: this.line().id,
        allocations: this.chosen().map(row => ({ locationId: row.location.id, lotId: row.lot.id, quantity: this.quantities()[this.key(row)].trim() }))
      }, this.etag());
      this.done.emit(result.body?.operationId ?? '');
    } catch (error) {
      const sent = this.chosen();
      this.reviewing.set(false);
      this.invalid.set(new Set());
      if (isUncertain(error)) {
        this.error.set('Resultado pendiente de comprobar: no sabemos si la reserva se guardó. Compruébalo antes de reservar de nuevo.');
      } else if (!(error instanceof HttpErrorResponse)) {
        this.error.set('Comprueba la reserva pendiente antes de reservar de nuevo.');
      } else if (error.status === 412) {
        this.error.set('Otra persona cambió el pedido o la reserva. Se ha vuelto a leer; revisa las cantidades y confirma de nuevo.');
        this.stale.emit();
      } else {
        const problem = apiProblem(error);
        this.conflict.set(problem.code === 'InsufficientStock' || problem.code === 'ReservedStockConflict');
        this.error.set(problem.status === 403 ? 'Tu cuenta no tiene permiso para reservar.'
          : [problem.detail, ...Object.values(problem.errors).flat()].join(' '));
        this.invalid.set(new Set([...allocationErrorIndexes(problem.errors)]
          .filter(index => index < sent.length).map(index => this.key(sent[index]))));
      }
      if (!isUncertain(error)) focusFirstError(this.host, this.injector);
    } finally {
      this.busy.set(false);
    }
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
