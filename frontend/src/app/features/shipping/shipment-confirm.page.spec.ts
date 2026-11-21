import { TestBed } from '@angular/core/testing';
import { flushProblem, httpMock, locationsTestProviders, signIn, text } from '../locations/testing/locations-testing';
import { ShipmentFormPage } from './shipment-form.page';
import {
  ORDER, RECORD, SHIPMENT, click, flushExisting, settle, shipment, shipmentContext
} from './testing/shipping-testing';

// HU-03 T016 (US1.2–US1.3, US2.2, US2.4, US4.2, US5.3; FR-006, FR-010–FR-012, FR-019, FR-023–FR-025;
// contracts/shipping-ui.md § Revisar y confirmar, Resultados y errores, Navegación; analyze-1 H2).
// Ampliación de shipment-form.page.spec.ts en otro archivo para no alargar aquel.
describe('ShipmentFormPage · revisar, confirmar y recuperar', () => {
  afterEach(() => { httpMock().verify(); sessionStorage.clear(); });

  async function render(permissions = RECORD, body = shipment(), context = shipmentContext()) {
    TestBed.configureTestingModule({ imports: [ShipmentFormPage], providers: locationsTestProviders({ id: SHIPMENT }) });
    signIn(permissions);
    const fixture = TestBed.createComponent(ShipmentFormPage);
    await settle(fixture);
    await flushExisting(fixture, body, '"s1"', context);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  async function openReviewAndConfirm(fixture: { detectChanges(): void }, root: HTMLElement) {
    click(root, '[data-testid="review"]');
    await settle(fixture);
    click(root, '[data-testid="confirm"]');
    await settle(fixture);
    return httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}/confirm`);
  }

  it('confirma con el If-Match de la salida y la orderVersion revisada; después queda en solo lectura con su operación', async () => {
    const { fixture, root } = await render();
    const request = await openReviewAndConfirm(fixture, root);
    expect(request.request.headers.get('If-Match')).toBe('"s1"');
    expect(request.request.body).toEqual({ orderVersion: '"o1"' });
    expect(JSON.parse(sessionStorage.getItem('inventario.pending-operations')!)[0].appRoute).toBe(`/salidas/${SHIPMENT}`);
    request.flush({ operationId: 'op-c', shipmentId: SHIPMENT, movementId: 'm', customerOrderId: ORDER, orderState: 'Open',
      lines: [{ id: 'x', quantity: '2.000', ownReservationConsumed: '2.000' }] });
    await settle(fixture);
    await flushExisting(fixture, shipment('Confirmed', '2.000', 'op-c'), '"s2"', shipmentContext('"s2"', true, []));
    expect(text(root.querySelector('[role="status"]'))).toBe('Salida registrada. Operación op-c.');
    expect(text(root.querySelector('[data-testid="confirmed-operation"]'))).toBe('Salida confirmada con la operación op-c');
    for (const action of ['save', 'delete', 'review']) expect(root.querySelector(`[data-testid="${action}"]`)).toBeNull();
    expect(text(root.querySelector('[data-testid="readonly-line"]'))).toContain('Reserva propia consumida 2.000 UD');
  });

  it('una salida confirmada abierta después muestra su operación sin aviso previo (FR-023, H2)', async () => {
    const { root } = await render(RECORD, shipment('Confirmed', '2.000', 'op-antigua'), shipmentContext('"s1"', true, []));
    expect(root.querySelector('[role="status"]')).toBeNull();
    expect(text(root.querySelector('[data-testid="confirmed-operation"]'))).toBe('Salida confirmada con la operación op-antigua');
    for (const action of ['save', 'delete', 'review']) expect(root.querySelector(`[data-testid="${action}"]`)).toBeNull();
  });

  it('la salida confirmada identifica el artículo por línea y muestra la fecha de registro (US4.1, FR-018; C3)', async () => {
    const { root } = await render(RECORD, shipment('Confirmed', '2.000', 'op-antigua'), shipmentContext('"s1"', true, []));
    const line = text(root.querySelector('[data-testid="readonly-line"]'));
    expect(line).toContain('ART-01 · Tornillo');
    expect(line).toContain('N01 · sin lote');
    expect(text(root.querySelector('[data-testid="recorded-at"]'))).toMatch(/^Fecha de registro: 21\/11\/2026 \d{2}:05$/);
  });

  it('una salida confirmada de 6 cuando salieron 4, corregida por un conteo, avisa de que el pedido no se corrige (US4.3; HU-04 C3)', async () => {
    TestBed.configureTestingModule({ imports: [ShipmentFormPage], providers: locationsTestProviders({ id: SHIPMENT }) });
    signIn(RECORD);
    const fixture = TestBed.createComponent(ShipmentFormPage);
    await settle(fixture);
    const count = { id: 'count-1', countCode: 'CC-4A0000000000', state: 'Confirmed' };
    await flushExisting(fixture, shipment('Confirmed', '6.000', 'op-6'), '"s1"', shipmentContext('"s1"', true, []), [], [count]);
    const root = fixture.nativeElement as HTMLElement;
    const notice = text(root.querySelector('[data-testid="count-correction"]'));
    expect(notice).toContain('CC-4A0000000000');
    expect(notice).toContain('corrigió las existencias de la ubicación');
    expect(notice).toContain('el pedido no se corrige');
    expect(notice).toContain('siguen contando 6.000 UD servidas');
    expect(root.querySelector('[data-testid="count-correction"] a')?.getAttribute('href')).toBe('/inventarios/count-1');
    // La salida sigue registrando 6 servidas: el ajuste no la modifica.
    expect(text(root.querySelector('[data-testid="readonly-line"]'))).toContain('6.000 UD');
  });

  it('una salida confirmada sin conteo vinculado no muestra el aviso de corrección', async () => {
    const { root } = await render(RECORD, shipment('Confirmed', '2.000', 'op-antigua'), shipmentContext('"s1"', true, []));
    expect(root.querySelector('[data-testid="count-correction"]')).toBeNull();
  });

  it('un borrador abierto solo con Inventory.Read no ofrece «Guardar», «Eliminar» ni «Revisar» (D-14)', async () => {
    const { root } = await render(['Inventory.Read']);
    for (const action of ['save', 'delete', 'review']) expect(root.querySelector(`[data-testid="${action}"]`)).toBeNull();
    expect(text(root.querySelector('[data-testid="readonly-line"]'))).toContain('N01 · sin lote');
    expect(text(root)).toContain('Borrador');
  });

  it('un 409 InsufficientStock muestra físico, disponible y la cantidad posible, con el foco en el aviso (US2.2, C2)', async () => {
    const { fixture, root } = await render();
    flushProblem(await openReviewAndConfirm(fixture, root), 409, 'InsufficientStock',
      'No se pueden servir 2.000 de ART-01 en N01, lote SIN-LOTE (físico 1.000, disponible 1.000). '
      + 'Se pueden servir como máximo 1.000 UD de ART-01 en N01.');
    await settle(fixture);
    const alert = root.querySelector<HTMLElement>('[role="alert"]')!;
    expect(text(alert)).toContain('físico 1.000, disponible 1.000');
    expect(text(alert)).toContain('Se pueden servir como máximo 1.000 UD de ART-01 en N01.');
    expect(document.activeElement).toBe(alert);
  });

  it('un 409 ReservedStockConflict pide liberar con enlace al pedido de /existencias', async () => {
    const { fixture, root } = await render();
    flushProblem(await openReviewAndConfirm(fixture, root), 409, 'ReservedStockConflict',
      'Antes de servir desde otro origen hay que liberar 4.000 reservadas de la línea ART-01.');
    await settle(fixture);
    expect(text(root.querySelector('[role="alert"]'))).toContain('Pídelo a un responsable del almacén');
    expect(root.querySelector<HTMLAnchorElement>('[data-testid="release-link"]')!.getAttribute('href'))
      .toBe(`/existencias/pedidos/${ORDER}`);
  });

  // Convergence C4 (G4; shipping-ui § Resultados): los 409 de estado muestran el motivo y vuelven a
  // leer salida, contexto y pedido, como el 412.
  for (const [code, detail] of [
    ['CustomerOrderNotOpen', 'El pedido PED-0042 no está abierto: no admite nuevas salidas.'],
    ['CountScopeFrozen', 'Una ubicación de origen está en un conteo abierto. Espera a que termine.'],
    ['DocumentAlreadyConfirmed', 'La salida ya está confirmada.']
  ] as const) {
    it(`un 409 ${code} muestra el motivo y recarga la salida y el pedido`, async () => {
      const { fixture, root } = await render();
      flushProblem(await openReviewAndConfirm(fixture, root), 409, code, detail);
      await settle(fixture);
      const confirmed = code === 'DocumentAlreadyConfirmed';
      await flushExisting(fixture, confirmed ? shipment('Confirmed', '2.000', 'op-otra') : shipment(), '"s4"',
        shipmentContext('"s4"', true, confirmed ? [] : undefined));
      const alert = root.querySelector<HTMLElement>('[role="alert"]')!;
      expect(text(alert)).toBe(detail);
      expect(document.activeElement).toBe(alert);
      if (confirmed) {
        expect(text(root.querySelector('[data-testid="confirmed-operation"]'))).toBe('Salida confirmada con la operación op-otra');
        expect(root.querySelector('[data-testid="review"]')).toBeNull();
      }
    });
  }

  it('un 412 avisa de que los datos cambiaron y vuelve a leer la revisión', async () => {
    const { fixture, root } = await render();
    flushProblem(await openReviewAndConfirm(fixture, root), 412, 'VersionConflict', 'El pedido cambió.');
    await settle(fixture);
    await flushExisting(fixture, shipment(), '"s3"', shipmentContext('"s3"'));
    expect(text(root.querySelector('[role="alert"]'))).toContain('Los datos cambiaron desde que los revisaste');
    click(root, '[data-testid="review"]');
    await settle(fixture);
    click(root, '[data-testid="confirm"]');
    await settle(fixture);
    expect(httpMock().expectOne(`/api/v1/shipments/${SHIPMENT}/confirm`).request.headers.get('If-Match')).toBe('"s3"');
  });

  it('tras recargar con una confirmación pendiente ofrece «Abrir salida» y «Comprobar» hace un único GET de la operación', async () => {
    const operation = '70000000-0000-4000-8000-000000000007';
    sessionStorage.setItem('inventario.pending-operations', JSON.stringify([{
      operationId: operation, actorId: 'user-1', method: 'POST', resource: `/api/v1/shipments/${SHIPMENT}/confirm`,
      payload: { orderVersion: '"o1"' }, etag: '"s1"', appRoute: `/salidas/${SHIPMENT}` }]));
    const { fixture, root } = await render();
    expect(text(root)).toContain(`Resultado pendiente de comprobar: ${operation}`);
    const open = Array.from(root.querySelectorAll('a')).find(link => text(link) === 'Abrir salida');
    expect(open?.getAttribute('href')).toBe(`/salidas/${SHIPMENT}`);
    const check = Array.from(root.querySelectorAll('button')).find(button => text(button) === 'Comprobar resultado')!;
    check.click();
    await settle(fixture);
    httpMock().expectOne(`/api/v1/operations/${operation}`).flush({}, { status: 404, statusText: 'Not Found' });
    await settle(fixture);
    httpMock().expectNone(`/api/v1/shipments/${SHIPMENT}/confirm`);
  });
});
