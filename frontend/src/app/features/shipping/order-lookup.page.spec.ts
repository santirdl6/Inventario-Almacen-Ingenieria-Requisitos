import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { OrderLookupPage } from './order-lookup.page';

async function settle(fixture: { detectChanges(): void }): Promise<void> {
  for (let round = 0; round < 4; round++) {
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

// HU-03 T014 (US1.11, FR-001a; contracts/shipping-ui.md § Buscar pedido).
const ORDER = '10000000-0000-4000-8000-000000000001';

describe('OrderLookupPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [OrderLookupPage], providers: locationsTestProviders() });
    signIn(['Inventory.Read']);
  });
  afterEach(() => httpMock().verify());

  async function render() {
    const fixture = TestBed.createComponent(OrderLookupPage);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function search(root: HTMLElement, value: string) {
    const input = root.querySelector<HTMLInputElement>('#order-reference')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    root.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
  }

  it('con una referencia encontrada abre el pedido en /salidas', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const { fixture, root } = await render();
    expect(text(root.querySelector('h1'))).toBe('Salidas');
    expect(text(root.querySelector('label[for="order-reference"]'))).toBe('Referencia del pedido');
    search(root, ' ped-0042 ');
    await settle(fixture);
    const request = httpMock().expectOne(r => r.url === '/api/v1/customer-orders/lookup');
    expect(request.request.params.get('externalReference')).toBe('ped-0042');
    request.flush({ id: ORDER, externalReference: 'PED-0042', state: 'Open' });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(`/salidas/pedidos/${ORDER}`);
  });

  it('si no existe lo dice sin error técnico, conserva el texto y devuelve el foco al campo', async () => {
    const { fixture, root } = await render();
    search(root, 'PED-NO');
    await settle(fixture);
    flushProblem(httpMock().expectOne(r => r.url === '/api/v1/customer-orders/lookup'), 404, 'NotFound',
      'No hay ningún pedido con esa referencia.');
    await settle(fixture);
    expect(text(root.querySelector('[role="alert"]'))).toBe('No hay ningún pedido con la referencia «PED-NO».');
    const input = root.querySelector<HTMLInputElement>('#order-reference')!;
    expect(input.value).toBe('PED-NO');
    expect(document.activeElement).toBe(input);
  });

  it('no busca con el campo vacío', async () => {
    const { fixture, root } = await render();
    search(root, '   ');
    await settle(fixture);
    httpMock().expectNone(r => r.url === '/api/v1/customer-orders/lookup');
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
  });
});
