import { Component, ElementRef, Injector, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { apiProblem } from '../../core/http/api-problem';
import { focusFirstError } from '../stock/shared/focus-first-error';
import { ShipmentsApiService } from './data/shipments-api.service';
import { orderShipmentsRoute } from './data/shipping.models';

/**
 * HU-03 T014 · /salidas (US1.11, FR-001a; contracts/shipping-ui.md § Buscar pedido). Búsqueda exacta
 * por referencia externa, sin lista ni coincidencias parciales. Si no existe, se avisa con texto, se
 * conserva lo escrito y el foco vuelve al campo.
 */
@Component({
  selector: 'app-order-lookup-page',
  styleUrl: '../stock/stock.css',
  template: `
    <h1>Salidas</h1>
    <p>Escribe la referencia del pedido de cliente que vas a servir.</p>
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    <form aria-label="Buscar pedido" (submit)="search($event)">
      <label for="order-reference">Referencia del pedido</label>
      <input id="order-reference" autocomplete="off" [value]="reference()" (input)="change($event)"
        [attr.aria-invalid]="notFound() ? 'true' : null" />
      <button type="submit" [disabled]="!reference().trim() || busy()">Buscar</button>
    </form>
  `
})
export class OrderLookupPage {
  private readonly api = inject(ShipmentsApiService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);

  readonly reference = signal('');
  readonly error = signal<string | null>(null);
  readonly notFound = signal(false);
  readonly busy = signal(false);

  change(event: Event): void {
    this.reference.set((event.target as HTMLInputElement).value);
    this.notFound.set(false);
  }

  async search(event: Event): Promise<void> {
    event.preventDefault();
    const text = this.reference().trim();
    if (!text || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const order = await this.api.lookup(text);
      void this.router.navigateByUrl(orderShipmentsRoute(order.id));
    } catch (error) {
      const problem = apiProblem(error);
      this.notFound.set(problem.status === 404);
      this.error.set(problem.status === 404 ? `No hay ningún pedido con la referencia «${text}».` : problem.detail);
      focusFirstError(this.host, this.injector);
    } finally {
      this.busy.set(false);
    }
  }
}
