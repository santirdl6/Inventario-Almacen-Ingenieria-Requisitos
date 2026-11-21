import { A11yModule } from '@angular/cdk/a11y';
import { Component, input, output } from '@angular/core';
import { ShipmentReviewLine } from './data/shipping.models';

/** Línea de la revisión: datos de la salida guardada y cifras de `review` del contexto. */
export interface ReviewLine {
  id: string; sku: string; name: string; isActive: boolean; unit: string; origin: string; quantity: string; review?: ShipmentReviewLine;
}

/**
 * HU-03 T016 · Revisión antes de confirmar (FR-006, US1.2, US3.2; contracts/shipping-ui.md § Revisar y
 * confirmar). Cifras orientativas del contexto: el servidor las revalida. Una cantidad mayor que la
 * posible y un artículo inactivo se avisan con texto, sin deshabilitar la confirmación.
 */
@Component({
  selector: 'app-shipment-review-dialog',
  imports: [A11yModule],
  styleUrl: '../stock/stock.css',
  template: `
    <section class="dialog" role="dialog" aria-modal="true" aria-labelledby="review-title" data-testid="review-dialog"
      cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)="cancelled.emit()">
      <h2 id="review-title">Revisa la salida del pedido {{ orderReference() }}</h2>
      <p>Cliente: {{ customer() }}</p>
      @for (line of lines(); track line.id) {
        <div data-testid="review-line" class="order-line">
          <p>
            {{ line.sku }} · {{ line.name }}@if (!line.isActive) { <span class="badge-inactive"> (Inactivo)</span> } ·
            {{ line.origin }} · Cantidad {{ line.quantity }} {{ line.unit }}
          </p>
          @if (line.review; as review) {
            <p class="meta">
              Pendiente {{ review.pending }} {{ line.unit }} · Físico {{ review.physical }} {{ line.unit }} ·
              Reservado propio {{ review.ownReserved }} {{ line.unit }} · Reservado ajeno {{ review.otherReserved }} {{ line.unit }} ·
              Posible {{ review.possible }} {{ line.unit }}
            </p>
            @if (over(line)) {
              <p class="error-text" data-testid="over-possible">La cantidad supera la posible ahora ({{ review.possible }} {{ line.unit }}).</p>
            }
          }
        </div>
      }
      <p class="meta">El sistema vuelve a comprobar las cantidades al confirmar.</p>
      <button type="button" data-testid="confirm" [disabled]="busy()" (click)="confirmed.emit()">Confirmar salida</button>
      <button type="button" data-testid="review-cancel" [disabled]="busy()" (click)="cancelled.emit()">Cancelar</button>
    </section>
  `
})
export class ShipmentReviewDialog {
  readonly orderReference = input('');
  readonly customer = input('');
  readonly lines = input<ReviewLine[]>([]);
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  over(line: ReviewLine): boolean { return !!line.review && Number(line.quantity) > Number(line.review.possible); }
}
