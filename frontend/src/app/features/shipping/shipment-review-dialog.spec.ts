import { TestBed } from '@angular/core/testing';
import { locationsTestProviders, text } from '../locations/testing/locations-testing';
import { ReviewLine, ShipmentReviewDialog } from './shipment-review-dialog';

// HU-03 T016 (FR-006, US1.2, US3.2; contracts/shipping-ui.md § Revisar y confirmar; analyze-1 H5).
function line(overrides: Partial<ReviewLine> = {}): ReviewLine {
  return {
    id: 'l-1', sku: 'ART-01', name: 'Tornillo', isActive: true, unit: 'UD', origin: 'N01 · sin lote', quantity: '6.000',
    review: { lineId: 'l-1', pending: '6.000', physical: '10.000', reserved: '7.000', ownReserved: '4.000', otherReserved: '3.000', possible: '6.000' },
    ...overrides
  };
}

describe('ShipmentReviewDialog', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [ShipmentReviewDialog], providers: locationsTestProviders() }));

  function render(lines: ReviewLine[]) {
    const fixture = TestBed.createComponent(ShipmentReviewDialog);
    fixture.componentRef.setInput('orderReference', 'PED-0042');
    fixture.componentRef.setInput('customer', 'Cliente Norte');
    fixture.componentRef.setInput('lines', lines);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('muestra pedido, cliente y por línea pendiente, físico, propio, ajeno y posible, con la nota de revalidación', () => {
    const { root } = render([line()]);
    const dialog = root.querySelector('[role="dialog"]')!;
    expect(text(dialog)).toContain('PED-0042');
    expect(text(dialog)).toContain('Cliente Norte');
    const row = text(root.querySelector('[data-testid="review-line"]'));
    for (const value of ['ART-01 · Tornillo', 'N01 · sin lote', 'Cantidad 6.000 UD', 'Pendiente 6.000 UD', 'Físico 10.000 UD',
      'Reservado propio 4.000 UD', 'Reservado ajeno 3.000 UD', 'Posible 6.000 UD']) expect(row).toContain(value);
    expect(text(dialog)).toContain('El sistema vuelve a comprobar las cantidades al confirmar');
    expect(root.querySelector('[data-testid="over-possible"]')).toBeNull();
  });

  it('un artículo inactivo se indica con texto sin bloquear la confirmación (US3.2, H5)', () => {
    const { root } = render([line({ isActive: false })]);
    expect(text(root.querySelector('[data-testid="review-line"]'))).toContain('Inactivo');
    expect(root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.disabled).toBe(false);
  });

  it('avisa en texto si la cantidad supera la posible, sin deshabilitar «Confirmar salida»', () => {
    const { root } = render([line({ quantity: '8.000' })]);
    expect(text(root.querySelector('[data-testid="over-possible"]'))).toBe('La cantidad supera la posible ahora (6.000 UD).');
    expect(root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.disabled).toBe(false);
  });

  it('«Confirmar salida» emite la confirmación y Escape o «Cancelar» cierran', () => {
    const { fixture, root } = render([line()]);
    const events: string[] = [];
    fixture.componentInstance.confirmed.subscribe(() => events.push('confirm'));
    fixture.componentInstance.cancelled.subscribe(() => events.push('cancel'));
    root.querySelector<HTMLButtonElement>('[data-testid="confirm"]')!.click();
    root.querySelector('[role="dialog"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    root.querySelector<HTMLButtonElement>('[data-testid="review-cancel"]')!.click();
    expect(events).toEqual(['confirm', 'cancel', 'cancel']);
  });
});
