import { A11yModule } from '@angular/cdk/a11y';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { apiProblem } from '../../../core/http/api-problem';
import { CustomerOrdersApiService, isUncertain } from '../data/customer-orders-api.service';
import { CustomerOrderContext, OrderAllocation, OrderReservation, lotText } from '../data/stock.models';

/**
 * HU-05 T016 · Liberar una reserva (US4; contracts/stock-ui.md). El releaseId se genera al abrir
 * el diálogo y viaja en el cuerpo congelado de la intención, así que se conserva en recargas y
 * reintentos. El ETag de la reserva se lee con GET /reservations/{id}; los códigos salen del
 * contexto del pedido. Motivo obligatorio (texto libre).
 */
@Component({
  selector: 'app-release-dialog',
  imports: [A11yModule, RouterLink],
  styleUrl: '../stock.css',
  template: `
    <section class="dialog" role="dialog" aria-modal="true" aria-labelledby="release-title" data-testid="release-dialog"
      cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)="cancelled.emit()">
      <h2 id="release-title">Liberar reserva</h2>
      @if (error()) {
        <p class="error-text" role="alert">{{ error() }}</p>
        @if (conflict()) { <p><a routerLink="/existencias">Ver existencias actuales</a></p> }
      }
      <table aria-label="Asignaciones de la reserva">
        <thead><tr><th scope="col">Ubicación</th><th scope="col">Lote</th><th scope="col">Remanente</th><th scope="col">Cantidad a liberar</th></tr></thead>
        <tbody>
          @for (allocation of allocations(); track allocation.id) {
            <tr>
              <td>{{ locationCode(allocation) }}</td>
              <td>{{ lot(allocation) }}</td>
              <td>Remanente {{ allocation.remaining }} {{ unit() }}</td>
              <td>
                <label class="visually-hidden" [for]="'release-' + allocation.id">Cantidad a liberar ({{ unit() }})</label>
                <input [id]="'release-' + allocation.id" [attr.data-testid]="'release-qty-' + allocation.id" inputmode="decimal"
                  [value]="quantities()[allocation.id] ?? ''" [disabled]="reviewing()" (input)="set(allocation.id, value($event))" />
              </td>
            </tr>
          }
        </tbody>
      </table>
      <label for="release-reason">Motivo</label>
      <textarea id="release-reason" maxlength="500" [value]="reason()" [disabled]="reviewing()" (input)="reason.set(value($event))"></textarea>
      @if (!reviewing()) {
        <button type="button" data-testid="release-review" [disabled]="!etag()" (click)="review()">Revisar</button>
      } @else {
        <p>Vas a liberar {{ total() }} {{ unit() }}. Motivo: {{ reason().trim() }}</p>
        <button type="button" data-testid="release-confirm" [disabled]="busy()" (click)="confirm()">Confirmar liberación</button>
        <button type="button" [disabled]="busy()" (click)="reviewing.set(false)">Volver a editar</button>
      }
      <button type="button" (click)="cancelled.emit()">Cerrar</button>
    </section>
  `
})
export class ReleaseDialog implements OnInit {
  readonly orderId = input.required<string>();
  readonly reservation = input.required<OrderReservation>();
  readonly context = input.required<CustomerOrderContext>();
  readonly unit = input('');
  readonly done = output<string>();
  readonly stale = output<void>();
  readonly cancelled = output<void>();

  private readonly api = inject(CustomerOrdersApiService);
  /** Identidad de esta liberación: estable al reintentar y distinta en cada liberación legítima. */
  readonly releaseId = crypto.randomUUID();
  readonly etag = signal<string | null>(null);
  readonly quantities = signal<Record<string, string>>({});
  readonly reason = signal('');
  readonly reviewing = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly conflict = signal(false);
  readonly allocations = computed(() => this.reservation().allocations.filter(allocation => Number(allocation.remaining) > 0));
  readonly chosen = computed(() => this.allocations().filter(allocation => Number(this.quantities()[allocation.id] || 0) > 0));
  readonly total = computed(() => this.chosen().reduce((sum, allocation) => sum + Number(this.quantities()[allocation.id]), 0).toFixed(3));

  ngOnInit(): void { void this.readVersion(); }

  locationCode(allocation: OrderAllocation): string {
    return this.context().locations.find(location => location.id === allocation.locationId)?.code ?? '';
  }

  lot(allocation: OrderAllocation): string {
    const lot = this.context().lots.find(item => item.id === allocation.lotId);
    return lot ? lotText(lot) : '';
  }

  set(id: string, value: string): void { this.quantities.update(values => ({ ...values, [id]: value })); }

  review(): void {
    this.conflict.set(false);
    if (!this.reason().trim()) { this.error.set('Indica el motivo de la liberación.'); return; }
    if (!this.chosen().length) { this.error.set('Indica la cantidad que liberas.'); return; }
    this.error.set(null);
    this.reviewing.set(true);
  }

  async confirm(): Promise<void> {
    const etag = this.etag();
    if (this.busy() || !etag) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.api.release(this.orderId(), this.reservation().id, {
        releaseId: this.releaseId, reason: this.reason().trim(),
        allocations: this.chosen().map(allocation => ({ allocationId: allocation.id, quantity: this.quantities()[allocation.id].trim() }))
      }, etag);
      this.done.emit(result.body?.operationId ?? '');
    } catch (error) {
      this.reviewing.set(false);
      if (isUncertain(error)) {
        this.error.set('Resultado pendiente de comprobar: no sabemos si la liberación se guardó. Compruébalo antes de repetir.');
      } else if (!(error instanceof HttpErrorResponse)) {
        this.error.set('Comprueba la liberación pendiente de esta reserva antes de liberar de nuevo.');
      } else if (error.status === 412) {
        this.error.set('Otra persona cambió el pedido o la reserva. Se ha vuelto a leer; revisa las cantidades y confirma de nuevo.');
        await this.readVersion();
        this.stale.emit();
      } else {
        const problem = apiProblem(error);
        this.conflict.set(problem.code === 'ReservedStockConflict');
        this.error.set(problem.status === 403 ? 'Tu cuenta no tiene permiso para liberar reservas.'
          : [problem.detail, ...Object.values(problem.errors).flat()].join(' '));
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async readVersion(): Promise<void> {
    try { this.etag.set((await this.api.reservation(this.reservation().id)).etag); }
    catch (error) { this.error.set(apiProblem(error).detail); }
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }
}
