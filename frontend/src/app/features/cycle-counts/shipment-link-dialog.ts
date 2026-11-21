import { A11yModule } from '@angular/cdk/a11y';
import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { apiProblem } from '../../core/http/api-problem';
import { CycleCountsApiService } from './data/cycle-counts-api.service';
import { ShipmentCandidate, trimQuantity } from './data/cycle-counts.models';

/** Salida elegida para una línea, con la referencia de su pedido. */
export interface LinkedShipment { shipmentId: string; externalReference: string }

/**
 * HU-04 T016 · Vincular una diferencia a una salida confirmada (US4; D-09; contracts/cycle-counts-ui.md
 * § Pendiente de aprobación). Lee las candidatas de todas las páginas y avisa de la limitación P-07.
 */
@Component({
  selector: 'app-shipment-link-dialog',
  imports: [A11yModule, DatePipe],
  styleUrl: '../stock/stock.css',
  template: `
    <section class="dialog" role="dialog" aria-modal="true" aria-labelledby="link-title" data-testid="shipment-link-dialog"
      cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)="cancelled.emit()">
      <h2 id="link-title">Vincular a salida · {{ label() }}</h2>
      <p class="meta"><strong>El ajuste corrige solo las existencias. No cambia lo servido del pedido ni las reservas consumidas.</strong></p>
      @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
      @if (loading()) { <p>Cargando salidas…</p> }
      @else if (!candidates().length) { <p>No hay salidas confirmadas que sacaran este artículo de esta ubicación y lote.</p> }
      @else {
        <fieldset>
          <legend>Salidas confirmadas de este artículo, ubicación y lote</legend>
          @for (candidate of candidates(); track candidate.shipmentId) {
            <div>
              <input type="radio" name="shipment" [id]="'ship-' + candidate.shipmentId" [value]="candidate.shipmentId"
                [checked]="chosen() === candidate.shipmentId" (click)="chosen.set(candidate.shipmentId)" />
              <label [for]="'ship-' + candidate.shipmentId">Pedido {{ candidate.externalReference }} ·
                {{ candidate.confirmedAt | date: 'dd/MM/yyyy HH:mm' }} · {{ quantity(candidate.quantity) }} {{ unit() }}
                @if (candidate.linkedCountLineIds.length) { <span class="meta">(ya vinculada a otra línea)</span> }</label>
            </div>
          }
        </fieldset>
      }
      <button type="button" [disabled]="!chosen()" (click)="link()">Vincular</button>
      @if (current()) { <button type="button" (click)="linked.emit(null)">Quitar vínculo</button> }
      <button type="button" (click)="cancelled.emit()">Cancelar</button>
    </section>
  `
})
export class ShipmentLinkDialog implements OnInit {
  private readonly api = inject(CycleCountsApiService);
  readonly countId = input.required<string>();
  readonly lineId = input.required<string>();
  readonly label = input('');
  readonly unit = input('');
  readonly current = input<string | null>(null);
  readonly linked = output<LinkedShipment | null>();
  readonly cancelled = output<void>();
  readonly candidates = signal<ShipmentCandidate[]>([]);
  readonly chosen = signal<string | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.chosen.set(this.current());
    this.api.candidates(this.countId(), this.lineId()).subscribe({
      next: items => { this.candidates.set(items); this.loading.set(false); },
      error: error => { this.error.set(apiProblem(error).detail); this.loading.set(false); }
    });
  }

  quantity(value: string): string { return trimQuantity(value); }

  link(): void {
    const candidate = this.candidates().find(item => item.shipmentId === this.chosen());
    if (candidate) this.linked.emit({ shipmentId: candidate.shipmentId, externalReference: candidate.externalReference });
  }
}
