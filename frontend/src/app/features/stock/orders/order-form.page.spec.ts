import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { flushNetworkError, flushProblem, httpMock, locationsTestProviders, signIn, text } from '../../locations/testing/locations-testing';
import { OrderFormPage } from './order-form.page';

/** Como settle(), pero deja terminar las cadenas de promesas (await tras cada respuesta HTTP). */
async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 4; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

// HU-05 T016 · Alta de pedido (US2.1–US2.3; contracts/stock-ui.md § Alta de pedido).
const ORDER_ID = '30000000-0000-4000-8000-000000000003';
const article = (id: string, sku: string, baseUnit = 'UD') =>
  ({ id, sku, name: `Artículo ${sku}`, baseUnit, isActive: true, tracksLot: false, allowsFractional: false, family: { id: 'f', code: 'F', name: 'F' } });

describe('OrderFormPage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OrderFormPage], providers: locationsTestProviders() });
    signIn(['Inventory.Read', 'Shipping.Record']);
  });
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function render() {
    const fixture = TestBed.createComponent(OrderFormPage);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function type(root: HTMLElement, selector: string, value: string) {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function addLine(fixture: Awaited<ReturnType<typeof render>>['fixture'], root: HTMLElement,
    found: ReturnType<typeof article>[], pick: string) {
    type(root, '#order-article-search', 'art');
    root.querySelector<HTMLFormElement>('form[aria-label="Buscar artículo para la línea"]')!.dispatchEvent(new Event('submit'));
    httpMock().expectOne(r => r.url === '/api/v1/articles' && r.params.get('state') === 'all')
      .flush({ items: found, page: 1, pageSize: 25, totalCount: found.length });
    await settle(fixture);
    const option = Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="article-option"]'))
      .find(button => text(button).startsWith(pick))!;
    option.click();
    await settle(fixture);
  }

  async function fill(fixture: Awaited<ReturnType<typeof render>>['fixture'], root: HTMLElement) {
    type(root, '#order-reference', ' PED-1001 ');
    type(root, '#order-customer', 'Cliente Norte');
    await addLine(fixture, root, [article('art-1', 'ART-01'), article('art-2', 'ART-02', 'KG')], 'ART-01');
    await addLine(fixture, root, [article('art-1', 'ART-01'), article('art-2', 'ART-02', 'KG')], 'ART-02');
    type(root, '#line-quantity-0', '7');
    type(root, '#line-quantity-1', '3');
    await settle(fixture);
  }

  it('un artículo ya usado en otra línea no se puede elegir otra vez', async () => {
    const { fixture, root } = await render();
    await addLine(fixture, root, [article('art-1', 'ART-01')], 'ART-01');
    type(root, '#order-article-search', 'art');
    root.querySelector<HTMLFormElement>('form[aria-label="Buscar artículo para la línea"]')!.dispatchEvent(new Event('submit'));
    httpMock().expectOne(r => r.url === '/api/v1/articles').flush({ items: [article('art-1', 'ART-01')], page: 1, pageSize: 25, totalCount: 1 });
    await settle(fixture);
    const option = root.querySelector<HTMLButtonElement>('[data-testid="article-option"]')!;
    expect(option.disabled).toBe(true);
    expect(text(option)).toContain('ya está en el pedido');
    expect(root.querySelectorAll('[data-testid="order-line"]')).toHaveLength(1);
  });

  it('revisa, confirma con clave idempotente y navega al detalle del pedido creado', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const { fixture, root } = await render();
    await fill(fixture, root);
    expect(text(root.querySelector('label[for="line-quantity-1"]'))).toBe('Cantidad (KG)');

    root.querySelector<HTMLButtonElement>('[data-testid="review"]')!.click();
    await settle(fixture);
    const review = text(root.querySelector('[data-testid="order-review"]'));
    expect(review).toContain('PED-1001');
    expect(review).toContain('ART-01');
    expect(review).toContain('7 UD');
    httpMock().expectNone('/api/v1/customer-orders');

    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    await settle(fixture);
    const request = httpMock().expectOne('/api/v1/customer-orders');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.headers.has('If-Match')).toBe(false);
    expect(request.request.body).toEqual({ externalReference: 'PED-1001', customerReference: 'Cliente Norte',
      lines: [{ articleId: 'art-1', quantity: '7' }, { articleId: 'art-2', quantity: '3' }] });
    request.flush({ operationId: 'op-1', resource: { id: ORDER_ID } },
      { status: 201, statusText: 'Created', headers: { Location: `/api/v1/customer-orders/${ORDER_ID}`, ETag: '"AQ=="' } });
    await settle(fixture);
    expect(text(root.querySelector('[role="status"]'))).toContain('op-1');
    expect(navigate).toHaveBeenCalledWith(`/existencias/pedidos/${ORDER_ID}`);
  });

  it('los errores por línea se asocian a su campo y se conservan los datos', async () => {
    const { fixture, root } = await render();
    await fill(fixture, root);
    root.querySelector<HTMLButtonElement>('[data-testid="review"]')!.click();
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/customer-orders'), 422, 'ValidationFailed', 'Revisa las cantidades marcadas.',
      { errors: { 'lines[1].quantity': ['Este artículo solo admite cantidades enteras.'] } });
    await settle(fixture);

    expect(root.querySelector('[data-testid="order-review"]')).toBeNull();
    expect(root.querySelector<HTMLInputElement>('#line-quantity-0')!.value).toBe('7');
    expect(root.querySelector<HTMLInputElement>('#line-quantity-1')!.value).toBe('3');
    const error = root.querySelector('#line-quantity-1-error');
    expect(text(error)).toBe('Este artículo solo admite cantidades enteras.');
    expect(root.querySelector('#line-quantity-1')!.getAttribute('aria-describedby')).toBe('line-quantity-1-error');
    expect(text(root.querySelector('[role="alert"]'))).toContain('Revisa las cantidades marcadas.');
  });

  it('una referencia ya usada se marca en su campo', async () => {
    const { fixture, root } = await render();
    await fill(fixture, root);
    root.querySelector<HTMLButtonElement>('[data-testid="review"]')!.click();
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/customer-orders'), 409, 'DuplicateCode', 'La referencia PED-1001 ya pertenece a otro pedido.',
      { errors: { externalReference: ['Introduce una referencia que no esté usada.'] } });
    await settle(fixture);
    expect(text(root.querySelector('#order-reference-error'))).toBe('Introduce una referencia que no esté usada.');
  });

  // Convergence C1 (G1, FR-027/FR-028, stock-ui § Accesibilidad): el foco va al primer error.
  it('tras un 422 el foco va al primer campo con error y los campos se marcan aria-invalid', async () => {
    const { fixture, root } = await render();
    await fill(fixture, root);
    root.querySelector<HTMLButtonElement>('[data-testid="review"]')!.click();
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/customer-orders'), 422, 'ValidationFailed', 'Revisa los datos marcados del pedido.',
      { errors: { customerReference: ['Indica el cliente.'], 'lines[1].quantity': ['La cantidad debe ser mayor que cero.'] } });
    await settle(fixture);
    expect(document.activeElement?.id).toBe('order-customer');
    expect(root.querySelector('#order-customer')!.getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector('#line-quantity-1')!.getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector('#line-quantity-0')!.getAttribute('aria-invalid')).toBeNull();
  });

  it('una referencia duplicada lleva el foco a su campo', async () => {
    const { fixture, root } = await render();
    await fill(fixture, root);
    root.querySelector<HTMLButtonElement>('[data-testid="review"]')!.click();
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/customer-orders'), 409, 'DuplicateCode', 'La referencia PED-1001 ya pertenece a otro pedido.',
      { errors: { externalReference: ['Introduce una referencia que no esté usada.'] } });
    await settle(fixture);
    expect(document.activeElement?.id).toBe('order-reference');
  });

  it('con la respuesta perdida queda «pendiente de comprobar» con la intención conservada', async () => {
    const { fixture, root } = await render();
    await fill(fixture, root);
    root.querySelector<HTMLButtonElement>('[data-testid="review"]')!.click();
    await settle(fixture);
    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    await settle(fixture);
    flushNetworkError(httpMock().expectOne('/api/v1/customer-orders'));
    await settle(fixture);
    expect(text(root)).toContain('Resultado pendiente de comprobar');
    expect(root.querySelector('button[data-testid="confirm"]')?.hasAttribute('disabled') ?? true).toBe(true);
    expect(sessionStorage.getItem('inventario.pending-operations')).toContain('/existencias/pedidos/nuevo');
  });
});
