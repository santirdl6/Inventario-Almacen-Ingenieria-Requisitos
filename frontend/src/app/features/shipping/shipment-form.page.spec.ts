import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { ShipmentFormPage } from './shipment-form.page';
import {
  LINE, LOT, N01, N02, ORDER, RECORD, RESERVED, SHIPMENT, click, flushExisting, order, orderContext, settle, shipment, stockPage, type
} from './testing/shipping-testing';

// HU-03 T015 (US1.1, US1.8–US1.10; FR-004–FR-006a, FR-024; contracts/shipping-ui.md § Preparar y editar).
describe('ShipmentFormPage · preparar y editar el borrador', () => {
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function renderNew(reservations: unknown[] = RESERVED) {
    TestBed.configureTestingModule({ imports: [ShipmentFormPage], providers: locationsTestProviders({ orderId: ORDER }) });
    signIn(RECORD);
    const fixture = TestBed.createComponent(ShipmentFormPage);
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}`).flush(order(reservations), { headers: { ETag: '"o1"' } });
    await settle(fixture);
    httpMock().expectOne(`/api/v1/customer-orders/${ORDER}/context`).flush(orderContext());
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  async function renderExisting() {
    TestBed.configureTestingModule({ imports: [ShipmentFormPage], providers: locationsTestProviders({ id: SHIPMENT }) });
    signIn(RECORD);
    const fixture = TestBed.createComponent(ShipmentFormPage);
    await settle(fixture);
    await flushExisting(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('propone los orígenes de la reserva propia por Id, acotados al pendiente, y los marca', async () => {
    const { fixture, root } = await renderNew();
    expect(text(root.querySelector('h1'))).toContain('PED-0042');
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    const rows = Array.from(root.querySelectorAll('[data-testid="shipment-line"]'));
    expect(rows.map(row => text(row.querySelector('[data-testid="origin"]')))).toEqual(['N02 · sin lote', 'N01 · sin lote']);
    expect(rows.map(row => row.querySelector<HTMLInputElement>('input')!.value)).toEqual(['3.000', '3.000']);
    expect(rows.every(row => text(row).includes('Propuesto desde la reserva'))).toBe(true);
    expect(text(rows[0])).toContain('Pendiente 6.000 UD');
  });

  it('cada fila del borrador identifica el artículo con SKU y nombre (US4.1, FR-018; Convergence C3)', async () => {
    const { fixture, root } = await renderNew();
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    const rows = Array.from(root.querySelectorAll('[data-testid="shipment-line"]'));
    expect(rows.map(row => text(row.querySelector('[data-testid="line-article"]')))).toEqual(['ART-01 · Tornillo', 'ART-01 · Tornillo']);
  });

  it('un borrador guardado también muestra el artículo de cada fila (C3)', async () => {
    const { root } = await renderExisting();
    expect(text(root.querySelector('[data-testid="shipment-line"] [data-testid="line-article"]'))).toBe('ART-01 · Tornillo');
  });

  it('sin reserva propia no propone nada; el selector ofrece todas las combinaciones, también la 30', async () => {
    const { fixture, root } = await renderNew([]);
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    expect(root.querySelectorAll('[data-testid="shipment-line"]')).toHaveLength(0);
    click(root, `[data-testid="choose-origin-${LINE}"]`);
    await settle(fixture);
    const request = httpMock().expectOne(r => r.url === '/api/v1/stock' && r.params.get('articleId') === 'art-1');
    expect(request.request.params.get('pageSize')).toBe('100');
    expect(request.request.params.get('includeEmpty')).toBe('false');
    request.flush(stockPage(30));
    await settle(fixture);
    const options = Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="bucket-option"]'));
    expect(options).toHaveLength(30);
    options[29].click();
    await settle(fixture);
    const row = root.querySelector('[data-testid="shipment-line"]')!;
    expect(text(row.querySelector('[data-testid="origin"]'))).toBe('B30 · sin lote');
    expect(row.querySelector<HTMLInputElement>('input')!.value).toBe('');
    expect(text(row)).not.toContain('Propuesto desde la reserva');
  });

  it('guardar un borrador nuevo envía clave, cuerpo y appRoute y abre la salida', async () => {
    const { fixture, root } = await renderNew();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    type(root, '[data-testid="shipment-line"] input', '2');
    await settle(fixture);
    click(root, '[data-testid="save"]');
    await settle(fixture);
    const request = httpMock().expectOne('/api/v1/shipments');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.headers.has('If-Match')).toBe(false);
    expect(request.request.body.customerOrderId).toBe(ORDER);
    expect(request.request.body.lines).toEqual([
      { customerOrderLineId: LINE, locationId: N02, lotId: LOT, quantity: '2' },
      { customerOrderLineId: LINE, locationId: N01, lotId: LOT, quantity: '3.000' }]);
    expect(Date.parse(request.request.body.occurredAt)).toBeLessThanOrEqual(Date.now());
    const pending = JSON.parse(sessionStorage.getItem('inventario.pending-operations')!);
    expect(pending[0].appRoute).toBe(`/salidas/pedidos/${ORDER}`);
    request.flush({ operationId: 'op-1', shipment: { ...shipment(), lines: [] } },
      { status: 201, statusText: 'Created', headers: { ETag: '"s1"', Location: `/api/v1/shipments/${SHIPMENT}` } });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(`/salidas/${SHIPMENT}`);
  });

  it('un 422 por línea marca su cantidad, conserva los datos y lleva el foco al primer error', async () => {
    const { fixture, root } = await renderNew();
    click(root, `[data-testid="add-${LINE}"]`);
    await settle(fixture);
    type(root, '[data-testid="shipment-line"] input', '9');
    click(root, '[data-testid="save"]');
    await settle(fixture);
    flushProblem(httpMock().expectOne('/api/v1/shipments'), 422, 'ValidationFailed', 'Revisa las cantidades marcadas de la salida.',
      { errors: { 'lines[0].quantity': ['La suma de esta línea de pedido supera su pendiente (6.000).'] } });
    await settle(fixture);
    const input = root.querySelector<HTMLInputElement>('[data-testid="shipment-line"] input')!;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.value).toBe('9');
    expect(text(root.querySelector('[data-testid="shipment-line"]'))).toContain('supera su pendiente');
    expect(document.activeElement).toBe(input);
  });

  it('editar un borrador envía PUT con If-Match y un 412 recarga sin perder lo escrito', async () => {
    const { fixture, root } = await renderExisting();
    expect(root.querySelector<HTMLInputElement>('[data-testid="shipment-line"] input')!.value).toBe('2.000');
    type(root, '[data-testid="shipment-line"] input', '5');
    click(root, '[data-testid="save"]');
    await settle(fixture);
    const request = httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"s1"');
    expect(request.request.body.customerOrderId).toBeUndefined();
    flushProblem(request, 412, 'VersionConflict', 'Otra persona cambió el registro.');
    await settle(fixture);
    await flushExisting(fixture, shipment('Draft', '1.000'), '"s2"');
    expect(text(root.querySelector('[role="alert"]'))).toContain('Otra persona cambió la salida');
    expect(root.querySelector<HTMLInputElement>('[data-testid="shipment-line"] input')!.value).toBe('5');

    click(root, '[data-testid="save"]');
    await settle(fixture);
    expect(httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`).request.headers.get('If-Match')).toBe('"s2"');
  });

  it('eliminar pide confirmación, envía DELETE con If-Match y vuelve al pedido con el aviso', async () => {
    const { fixture, root } = await renderExisting();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    click(root, '[data-testid="delete"]');
    await settle(fixture);
    httpMock().expectNone(`/api/v1/shipments/${SHIPMENT}`);
    expect(text(root.querySelector('[data-testid="delete-confirmation"]'))).toContain('¿Eliminar el borrador?');
    click(root, '[data-testid="delete-confirm"]');
    await settle(fixture);
    const request = httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.get('If-Match')).toBe('"s1"');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(`/salidas/pedidos/${ORDER}`,
      { state: { notice: 'Borrador eliminado. No se ha modificado ninguna existencia.' } });
  });
});
