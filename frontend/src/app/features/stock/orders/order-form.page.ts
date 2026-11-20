import { Component, DestroyRef, ElementRef, Injector, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { apiProblem } from '../../../core/http/api-problem';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../../core/operations/operation-status.component';
import { CustomerOrdersApiService, isUncertain } from '../data/customer-orders-api.service';
import { StockApiService } from '../data/stock-api.service';
import { ORDERS, StockArticleOption, orderRoute } from '../data/stock.models';
import { focusFirstError } from '../shared/focus-first-error';

interface DraftLine { article: StockArticleOption; quantity: string }

/**
 * HU-05 T016 · /existencias/pedidos/nuevo (US2.1–US2.3; contracts/stock-ui.md § Alta de pedido).
 * Referencia, cliente y líneas (un artículo por línea). Revisión (estado 2) antes de confirmar
 * (estado 3) con la intención idempotente de A; los errores por línea se asocian a su campo y se
 * conservan los datos (estado 5). Al confirmar se muestra la operación y se abre el pedido.
 */
@Component({
  selector: 'app-order-form-page',
  imports: [OperationStatusComponent],
  styleUrl: '../stock.css',
  template: `
    <h1>Nuevo pedido de cliente</h1>
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    <app-operation-status [resource]="resource" />

    @if (!reviewing()) {
      <div class="field">
        <label for="order-reference">Referencia del pedido</label>
        <input id="order-reference" [value]="reference()" (input)="reference.set(value($event))"
          [attr.aria-invalid]="errors()['externalReference'] ? 'true' : null"
          [attr.aria-describedby]="errors()['externalReference'] ? 'order-reference-error' : null" />
        @if (errors()['externalReference']; as messages) { <span class="field-error" id="order-reference-error">{{ messages[0] }}</span> }
      </div>
      <div class="field">
        <label for="order-customer">Cliente</label>
        <input id="order-customer" [value]="customer()" (input)="customer.set(value($event))"
          [attr.aria-invalid]="errors()['customerReference'] ? 'true' : null"
          [attr.aria-describedby]="errors()['customerReference'] ? 'order-customer-error' : null" />
        @if (errors()['customerReference']; as messages) { <span class="field-error" id="order-customer-error">{{ messages[0] }}</span> }
      </div>

      <h2>Líneas</h2>
      @if (errors()['lines']; as messages) { <p class="field-error">{{ messages[0] }}</p> }
      @for (line of lines(); track line.article.id; let index = $index) {
        <div class="field" data-testid="order-line">
          <span>{{ line.article.sku }} · {{ line.article.name }}</span>
          @if (errors()['lines[' + index + '].articleId']; as messages) { <span class="field-error">{{ messages[0] }}</span> }
          <label [for]="'line-quantity-' + index">Cantidad ({{ line.article.baseUnit }})</label>
          <input [id]="'line-quantity-' + index" inputmode="decimal" [value]="line.quantity" (input)="quantity(index, value($event))"
            [attr.aria-invalid]="errors()['lines[' + index + '].quantity'] ? 'true' : null"
            [attr.aria-describedby]="errors()['lines[' + index + '].quantity'] ? 'line-quantity-' + index + '-error' : null" />
          @if (errors()['lines[' + index + '].quantity']; as messages) {
            <span class="field-error" [id]="'line-quantity-' + index + '-error'">{{ messages[0] }}</span>
          }
          <button type="button" (click)="remove(index)">Quitar línea</button>
        </div>
      }

      <form aria-label="Buscar artículo para la línea" (submit)="search($event)">
        <label for="order-article-search">Añadir línea: artículo (SKU o nombre)</label>
        <input id="order-article-search" type="search" autocomplete="off" [value]="query()" (input)="query.set(value($event))" />
        <button type="submit" [disabled]="!query().trim()">Buscar</button>
      </form>
      @if (options().length) {
        <ul class="options" aria-label="Artículos encontrados">
          @for (option of options(); track option.id) {
            <li>
              <button type="button" data-testid="article-option" [disabled]="used(option.id)" (click)="add(option)">
                {{ option.sku }} · {{ option.name }}@if (!option.isActive) { (Inactivo) }@if (used(option.id)) { (ya está en el pedido) }
              </button>
            </li>
          }
        </ul>
      }

      <button type="button" data-testid="review" [disabled]="pending()" (click)="review()">Revisar</button>
    } @else {
      <section data-testid="order-review" aria-label="Revisión del pedido">
        <h2>Revisa el pedido antes de confirmar</h2>
        <p>Referencia: {{ reference().trim() }} · Cliente: {{ customer().trim() }}</p>
        <ul>
          @for (line of lines(); track line.article.id) {
            <li>{{ line.article.sku }} · {{ line.article.name }}: {{ line.quantity.trim() }} {{ line.article.baseUnit }}</li>
          }
        </ul>
        <button type="button" data-testid="confirm" [disabled]="confirming() || pending()" (click)="confirm()">Confirmar</button>
        <button type="button" [disabled]="confirming()" (click)="reviewing.set(false)">Volver a editar</button>
      </section>
    }
  `
})
export class OrderFormPage {
  private readonly api = inject(CustomerOrdersApiService);
  private readonly stock = inject(StockApiService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  private readonly mutations = inject(MasterDataMutationsService);
  protected readonly resource = ORDERS;

  readonly reference = signal('');
  readonly customer = signal('');
  readonly lines = signal<DraftLine[]>([]);
  readonly query = signal('');
  readonly options = signal<StockArticleOption[]>([]);
  readonly reviewing = signal(false);
  readonly confirming = signal(false);
  readonly errors = signal<Record<string, string[]>>({});
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly pending = computed(() => !!this.mutations.pending.forResource(ORDERS));

  constructor() {
    // Alta recuperada con «Comprobar resultado» o «Repetir»: se abre el pedido de su Location.
    this.mutations.recovered.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(({ operation, outcome }) => {
      const location = outcome.headers['Location'] ?? outcome.headers['location'];
      if (operation.resource === ORDERS && location) void this.router.navigateByUrl(orderRoute(location.split('/').pop()!));
    });
  }

  search(event: Event): void {
    event.preventDefault();
    const text = this.query().trim();
    if (!text) return;
    this.stock.searchArticles(text).subscribe({
      next: items => this.options.set(items),
      error: (error: unknown) => this.error.set(apiProblem(error).detail)
    });
  }

  used(id: string): boolean { return this.lines().some(line => line.article.id === id); }

  add(option: StockArticleOption): void {
    if (this.used(option.id)) return;
    this.lines.update(lines => [...lines, { article: option, quantity: '' }]);
    this.options.set([]);
    this.query.set('');
  }

  remove(index: number): void {
    this.lines.update(lines => lines.filter((_, position) => position !== index));
    this.errors.set({});
  }

  quantity(index: number, value: string): void {
    this.lines.update(lines => lines.map((line, position) => position === index ? { ...line, quantity: value } : line));
  }

  review(): void {
    this.error.set(null);
    this.errors.set({});
    this.reviewing.set(true);
  }

  async confirm(): Promise<void> {
    if (this.confirming()) return;
    this.confirming.set(true);
    this.error.set(null);
    try {
      const result = await this.api.create({
        externalReference: this.reference().trim(), customerReference: this.customer().trim(),
        lines: this.lines().map(line => ({ articleId: line.article.id, quantity: line.quantity.trim() }))
      });
      const created = result.body;
      this.message.set(`Pedido creado. Operación ${created?.operationId ?? ''}.`);
      if (created?.resource.id) void this.router.navigateByUrl(orderRoute(created.resource.id));
    } catch (error) {
      if (isUncertain(error)) {
        this.error.set('Resultado pendiente de comprobar: no sabemos si el pedido se creó. Comprueba el resultado antes de repetir.');
      } else {
        const problem = apiProblem(error);
        this.errors.set(problem.errors);
        this.error.set(problem.detail);
        this.reviewing.set(false);
        focusFirstError(this.host, this.injector);
      }
    } finally {
      this.confirming.set(false);
    }
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
