import { A11yModule } from '@angular/cdk/a11y';
import { Component, input, output } from '@angular/core';

/** Línea con diferencia tal como se va a ajustar. */
export interface AdjustmentLine {
  id: string; article: string; location: string; lot: string; difference: string; reason: string; shipment: string | null;
}

/**
 * HU-04 T016 · Revisión final antes de confirmar el ajuste (US2; FR-009; contracts/cycle-counts-ui.md
 * § Pendiente de aprobación). Con alguna línea vinculada, repite el aviso P-07: el pedido no se corrige.
 */
@Component({
  selector: 'app-count-review-dialog',
  imports: [A11yModule],
  styleUrl: '../stock/stock.css',
  template: `
    <section class="dialog" role="dialog" aria-modal="true" aria-labelledby="count-review-title" data-testid="count-review-dialog"
      cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)="cancelled.emit()">
      <h2 id="count-review-title">Revisa el ajuste del conteo {{ countCode() }}</h2>
      @if (lines().length) {
        <p>Se ajustarán {{ lines().length }} {{ lines().length === 1 ? 'línea' : 'líneas' }} con un único movimiento de ajuste:</p>
        <ul>
          @for (line of lines(); track line.id) {
            <li>{{ line.article }} · {{ line.location }} · {{ line.lot }} · {{ line.difference }} · Motivo: {{ line.reason }}
              @if (line.shipment) { · Corrige la salida del pedido {{ line.shipment }} }</li>
          }
        </ul>
      } @else {
        <p>No hay diferencias: se cerrará el conteo sin ajustar existencias.</p>
      }
      @if (hasCorrection()) {
        <p class="error-text"><strong>El ajuste corrige solo las existencias: el pedido no se corrige.</strong> Sigue mostrando lo servido
          en la salida y su estado y las reservas consumidas no cambian.</p>
      }
      <p class="meta">El sistema vuelve a comprobar el teórico y el reservado al confirmar.</p>
      <button type="button" [disabled]="busy()" (click)="confirmed.emit()">Confirmar ajuste</button>
      <button type="button" [disabled]="busy()" (click)="cancelled.emit()">Volver</button>
    </section>
  `
})
export class CountReviewDialog {
  readonly countCode = input('');
  readonly lines = input<AdjustmentLine[]>([]);
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  hasCorrection(): boolean { return this.lines().some(line => !!line.shipment); }
}
