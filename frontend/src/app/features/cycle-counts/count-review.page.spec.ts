import { TestBed } from '@angular/core/testing';
import { flushNetworkError, flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { CountDetailPage } from './count-detail.page';
import {
  ADJUST, ART_A, COUNT, L1, L2, ORDER, RECORD, SHIP, U1, U2, USER, context, count, flushDetail, line, settle
} from './testing/cycle-counts-testing';

// HU-04 T016 (US2, US4; FR-019–FR-022; contracts/cycle-counts-ui.md § Pendiente de aprobación, § Cerrado,
// § Aviso de corrección de salida P-07).
const RESOURCE = `/api/v1/cycle-counts/${COUNT}`;
const NOTICE = 'El ajuste corrige solo las existencias: no cambia lo servido del pedido, su estado ni las reservas consumidas (P-07).';

function awaiting(reason: string | null = null, correctsShipmentId: string | null = null) {
  return count('AwaitingApproval', [
    line({ id: L1, articleId: ART_A, locationId: U1, theoreticalPhysical: '10.000', countedPhysical: '8.000', difference: '-2.000',
      countedBy: USER, countedAt: '2026-11-21T09:00:00Z', reason, correctsShipmentId,
      shipmentCorrectionNotice: correctsShipmentId ? NOTICE : null }),
    line({ id: L2, articleId: ART_A, locationId: U2, theoreticalPhysical: '5.000', countedPhysical: '5.000', difference: '0.000',
      countedBy: USER, countedAt: '2026-11-21T09:01:00Z' })
  ], { submittedBy: USER, submittedAt: '2026-11-21T09:05:00Z' });
}

/** Conteo cerrado de US4: salida de 6 cuando salieron 4 → contado 6 frente a teórico 4, diferencia +2 vinculada. */
function confirmedWithCorrection() {
  return count('Confirmed', [
    line({ id: L1, articleId: ART_A, locationId: U1, theoreticalPhysical: '4.000', countedPhysical: '6.000', difference: '2.000',
      countedBy: USER, countedAt: '2026-11-21T09:00:00Z', reason: 'Salida registrada de más', correctsShipmentId: SHIP,
      shipmentCorrectionNotice: NOTICE })
  ], { approvedBy: USER, approvedAt: '2026-11-21T10:00:00Z', operationId: 'op-confirm', adjustmentMovementId: 'mov-1' });
}

const SHIPMENTS_CONTEXT = { shipments: [{ shipmentId: SHIP, customerOrderId: ORDER, externalReference: 'PED-0042' }] };

describe('CountDetailPage: revisión, confirmación, cancelación y cierre', () => {
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function render(body: object, permissions = ADJUST, ctx: object = context()) {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [CountDetailPage], providers: locationsTestProviders({ id: COUNT }) });
    signIn(permissions);
    const fixture = TestBed.createComponent(CountDetailPage);
    await flushDetail(fixture, body, ctx);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function button(root: HTMLElement, label: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('button')).find(item => text(item) === label);
  }

  function type(input: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('revisa con teórico, contado, diferencia, reservado y quién contó; motivo solo en las diferencias', async () => {
    const { root } = await render(awaiting(), ADJUST, context('"c1"', {
      reservedByBucket: [{ articleId: ART_A, locationId: U1, lotCode: 'SIN-LOTE', reserved: '3.000' }] }));
    const row = root.querySelector(`tr[data-review="${L1}"]`)!;
    expect(text(row)).toContain('10 UD');
    expect(text(row)).toContain('8 UD');
    expect(text(row)).toContain('Faltan 2 UD');
    expect(text(row)).toContain('Reservado 3 UD');
    expect(text(row)).toContain('operario@h0.test');
    expect(root.querySelector(`textarea[data-reason="${L1}"]`)).not.toBeNull();
    expect(root.querySelector(`textarea[data-reason="${L2}"]`)).toBeNull();
    expect(text(root)).toContain('1 línea con diferencia');
    expect(button(root, 'Confirmar ajuste')).toBeDefined();
  });

  it('confirmar sin motivo marca la línea y no envía', async () => {
    const { fixture, root } = await render(awaiting());
    button(root, 'Confirmar ajuste')!.click();
    await settle(fixture);
    expect(root.querySelector(`textarea[data-reason="${L1}"]`)!.getAttribute('aria-invalid')).toBe('true');
    expect(text(root)).toContain('Indica el motivo de esta diferencia');
  });

  it('guarda los motivos, revisa en el diálogo final y confirma con If-Match mostrando la operación', async () => {
    const { fixture, root } = await render(awaiting());
    type(root.querySelector<HTMLTextAreaElement>(`textarea[data-reason="${L1}"]`)!, ' Rotura ');
    await settle(fixture);
    button(root, 'Confirmar ajuste')!.click();
    await settle(fixture);
    const review = httpMock().expectOne(r => r.method === 'PUT' && r.url === `${RESOURCE}/review`);
    expect(review.request.body).toEqual({ lines: [{ lineId: L1, reason: 'Rotura' }] });
    expect(review.request.headers.get('If-Match')).toBe('"c1"');
    review.flush({ operationId: 'op-r', count: awaiting('Rotura') }, { headers: { ETag: '"c2"' } });
    await flushDetail(fixture, awaiting('Rotura'), context('"c2"'), '"c2"');
    const dialog = root.querySelector('[data-testid="count-review-dialog"]')!;
    expect(text(dialog)).toContain('Faltan 2 UD');
    expect(text(dialog)).toContain('Rotura');
    button(dialog as HTMLElement, 'Confirmar ajuste')!.click();
    await settle(fixture);
    const confirm = httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/confirm`);
    expect(confirm.request.headers.get('If-Match')).toBe('"c2"');
    confirm.flush({ operationId: 'op-confirm', countId: COUNT, movementId: 'mov-1', adjustedLines: [{ lineId: L1, difference: '-2.000' }],
      shipmentCorrections: [] });
    await flushDetail(fixture, count('Confirmed', awaiting('Rotura').lines, { operationId: 'op-confirm', approvedBy: USER,
      approvedAt: '2026-11-21T10:00:00Z', adjustmentMovementId: 'mov-1' }), context('"c3"'), '"c3"');
    expect(text(root.querySelector('[role="status"]'))).toContain('Ajuste registrado. Operación op-confirm');
    expect(text(root)).toContain('Confirmado');
    expect(button(root, 'Confirmar ajuste')).toBeUndefined();
  });

  it('un 409 ReservedStockConflict al confirmar avisa, ofrece liberar la reserva y relee', async () => {
    const { fixture, root } = await render(awaiting('Rotura'));
    button(root, 'Confirmar ajuste')!.click();
    await settle(fixture);
    button(root.querySelector('[data-testid="count-review-dialog"]') as HTMLElement, 'Confirmar ajuste')!.click();
    await settle(fixture);
    flushProblem(httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/confirm`), 409, 'ReservedStockConflict',
      'En U1 el contado (8) queda por debajo del reservado (9).');
    await flushDetail(fixture, awaiting('Rotura'));
    expect(text(root.querySelector('[role="alert"]'))).toContain('reservado (9)');
    expect(root.querySelector('a[data-testid="release-link"]')?.getAttribute('href')).toBe('/existencias');
  });

  it('un fallo de red al confirmar deja la operación pendiente con «Abrir conteo»', async () => {
    const { fixture, root } = await render(awaiting('Rotura'));
    button(root, 'Confirmar ajuste')!.click();
    await settle(fixture);
    button(root.querySelector('[data-testid="count-review-dialog"]') as HTMLElement, 'Confirmar ajuste')!.click();
    await settle(fixture);
    flushNetworkError(httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/confirm`));
    await settle(fixture);
    expect(text(root)).toContain('Resultado pendiente de comprobar');
    expect(text(root)).toContain('Comprobar resultado');
  });

  it('vincula una diferencia a una salida con el aviso P-07 y la envía en la revisión', async () => {
    const { fixture, root } = await render(awaiting('Salida de más'));
    button(root, 'Vincular a salida')!.click();
    await settle(fixture);
    httpMock().expectOne(r => r.url === `${RESOURCE}/lines/${L1}/shipment-candidates` && r.params.get('page') === '1').flush({
      items: [{ shipmentId: SHIP, customerOrderId: ORDER, externalReference: 'PED-0042', confirmedAt: '2026-11-20T10:00:00Z',
        quantity: '6.000', linkedCountLineIds: [] }], page: 1, pageSize: 100, totalCount: 1 });
    await settle(fixture);
    const dialog = root.querySelector('[data-testid="shipment-link-dialog"]') as HTMLElement;
    expect(text(dialog)).toContain('No cambia lo servido del pedido ni las reservas consumidas');
    dialog.querySelector<HTMLInputElement>(`input[value="${SHIP}"]`)!.click();
    await settle(fixture);
    button(dialog, 'Vincular')!.click();
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="p07-notice"]'))).toContain('el pedido no se corrige');
    button(root, 'Guardar revisión')!.click();
    await settle(fixture);
    const review = httpMock().expectOne(r => r.method === 'PUT' && r.url === `${RESOURCE}/review`);
    expect(review.request.body).toEqual({ lines: [{ lineId: L1, reason: 'Salida de más', correctsShipmentId: SHIP }] });
    review.flush({ operationId: 'op-r', count: awaiting('Salida de más', SHIP) }, { headers: { ETag: '"c2"' } });
    await flushDetail(fixture, awaiting('Salida de más', SHIP), context('"c2"', SHIPMENTS_CONTEXT), '"c2"');
  });

  it('el conteo cerrado con una línea vinculada muestra la salida corregida y el aviso P-07 visible', async () => {
    const { root } = await render(confirmedWithCorrection(), ADJUST, context('"c1"', SHIPMENTS_CONTEXT));
    expect(text(root)).toContain('Confirmado');
    expect(text(root)).toContain('Operación op-confirm');
    expect(text(root.querySelector(`[data-correction="${L1}"]`))).toContain('PED-0042');
    expect(text(root.querySelector(`[data-correction="${L1}"]`))).toContain(NOTICE);
    const notice = root.querySelector('[data-testid="p07-notice"]')!;
    expect(text(notice)).toContain('salida de 6 cuando salieron 4');
    expect(text(notice)).toContain('el pedido sigue mostrando 6 servidas');
    expect(text(notice)).toContain('el pedido no se corrige');
    expect(root.querySelector('textarea')).toBeNull();
    expect(button(root, 'Cancelar conteo')).toBeUndefined();
  });

  it('cancela con motivo obligatorio y muestra el conteo cancelado', async () => {
    const { fixture, root } = await render(awaiting());
    button(root, 'Cancelar conteo')!.click();
    await settle(fixture);
    button(root, 'Confirmar cancelación')!.click();
    await settle(fixture);
    expect(root.querySelector('#cancel-reason')!.getAttribute('aria-invalid')).toBe('true');
    type(root.querySelector<HTMLTextAreaElement>('#cancel-reason')!, 'Conteo duplicado');
    await settle(fixture);
    button(root, 'Confirmar cancelación')!.click();
    await settle(fixture);
    const request = httpMock().expectOne(r => r.method === 'POST' && r.url === `${RESOURCE}/cancel`);
    expect(request.request.body).toEqual({ reason: 'Conteo duplicado' });
    request.flush({ operationId: 'op-c', countId: COUNT });
    await flushDetail(fixture, count('Cancelled', [], { cancelledBy: USER, cancelledAt: '2026-11-21T10:00:00Z',
      cancelReason: 'Conteo duplicado', operationId: 'op-c' }), context('"c2"'), '"c2"');
    expect(text(root)).toContain('Cancelado');
    expect(text(root)).toContain('Conteo duplicado');
  });

  it('sin Inventory.Adjust no ofrece revisar, confirmar ni cancelar', async () => {
    const { root } = await render(awaiting(), RECORD);
    expect(button(root, 'Confirmar ajuste')).toBeUndefined();
    expect(button(root, 'Cancelar conteo')).toBeUndefined();
    expect(root.querySelector('textarea')).toBeNull();
  });
});
