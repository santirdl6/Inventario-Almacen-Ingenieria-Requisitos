import { ComponentFixture, TestBed } from '@angular/core/testing';
import { detail, flushProblem, httpMock, locationsTestProviders, settle, signIn, text } from '../testing/locations-testing';
import { LocationDetailPage } from './location-detail.page';

const article = { id: 'art-1', sku: 'ART-01', name: 'Tornillo', unit: 'UD', isActive: true };

describe('LocationDetailPage', () => {
  let current: ComponentFixture<LocationDetailPage>;

  beforeEach(() => {
    history.replaceState(null, '');
    TestBed.configureTestingModule({ imports: [LocationDetailPage], providers: locationsTestProviders({ id: detail().id }) });
  });

  async function render(location = detail(), items: unknown[] = [], permissions = ['Inventory.Read']) {
    signIn(permissions);
    const fixture = TestBed.createComponent(LocationDetailPage);
    current = fixture;
    await settle(fixture);
    httpMock().expectOne(`/api/v1/locations/${location.id}`).flush(location, { headers: { ETag: '"V1"' } });
    httpMock().expectOne(r => r.url === `/api/v1/locations/${location.id}/stock`).flush({
      asOf: '2026-11-18T10:00:00Z', location: { id: location.id, code: location.code, description: location.description, kind: location.kind, isActive: location.isActive },
      items, hasReservations: location.hasReservations, inOpenCount: false, page: 1, pageSize: 25, totalCount: items.length
    });
    await settle(fixture);
    return fixture.nativeElement as HTMLElement;
  }

  it('una ubicación vacía lo indica y muestra sus datos, tipo y estado; sin gestión no ofrece editar', async () => {
    const root = await render();
    expect(text(root.querySelector('[data-testid="empty"]'))).toBe('Esta ubicación está vacía.');
    expect(text(root.querySelector('dl'))).toContain('Pasillo P01 · Estantería E01 · Nivel N01');
    expect(text(root.querySelector('dl'))).toContain('Almacenamiento');
    expect(text(root.querySelector('dl'))).toContain('Activa');
    expect(root.querySelector(`a[href="/ubicaciones/${detail().id}/editar"]`)).toBeNull();
  });

  it('lista artículo, lote, cantidad y reservas con texto, y enlaza a sus ubicaciones', async () => {
    const root = await render(detail({ isEmpty: false, hasReservations: true, hasMovements: true }), [
      { article, lotCode: 'L-001', physical: '4.000', reserved: '1.000', unit: 'UD' },
      { article: { ...article, id: 'art-2', sku: 'ART-02', isActive: false }, lotCode: 'SIN-LOTE', physical: '2.000', reserved: '0.000', unit: 'UD' }
    ]);
    const rows = Array.from(root.querySelectorAll('tbody tr'));
    expect(text(rows[0])).toContain('ART-01');
    expect(text(rows[0])).toContain('L-001');
    expect(text(rows[0])).toContain('4.000 UD');
    expect(text(rows[0])).toContain('1.000 UD reservado');
    expect(text(rows[1])).toContain('Inactivo');
    expect(text(rows[1])).toContain('Sin lote');
    expect(rows[0].querySelector('a')!.getAttribute('href')).toBe('/ubicaciones/articulo/art-1');
    expect(text(root.querySelector('[data-testid="reservations"]'))).toContain('reservadas');
  });

  it('con permiso de gestión ofrece editar, nunca eliminar, y muestra el aviso de la pantalla anterior', async () => {
    history.replaceState({ notice: 'Ubicación P01-E01-N01 guardada.' }, '');
    const root = await render(detail(), [], ['Inventory.Read', 'Locations.Manage']);
    expect(root.querySelector(`a[href="/ubicaciones/${detail().id}/editar"]`)).not.toBeNull();
    expect(Array.from(root.querySelectorAll('button, a')).some(element => /eliminar|borrar/i.test(text(element)))).toBe(false);
    expect(text(root.querySelector('[data-testid="notice"]'))).toBe('Ubicación P01-E01-N01 guardada.');
  });

  it('abre la revisión desde el detalle, confirma, avisa en la página y recarga datos y contenido', async () => {
    const root = await render(detail(), [], ['Inventory.Read', 'Locations.Manage']);
    expect(text(root.querySelector('[data-testid="state-button"]'))).toBe('Desactivar');
    (root.querySelector('[data-testid="state-button"] button') as HTMLButtonElement).click();
    await settle(current);
    const confirm = Array.from(document.querySelectorAll('.p-dialog button')).find(b => text(b) === 'Desactivar') as HTMLButtonElement;
    confirm.click();
    httpMock().expectOne(`/api/v1/locations/${detail().id}`).flush(
      { operationId: 'op', resource: detail({ isActive: false, statusLabel: 'Inactiva' }) }, { headers: { ETag: '"V2"' } });
    await settle(current);
    const reload = httpMock().match(r => r.url.startsWith(`/api/v1/locations/${detail().id}`));
    expect(reload.map(r => r.request.url).sort()).toEqual([`/api/v1/locations/${detail().id}`, `/api/v1/locations/${detail().id}/stock`]);
    expect(text(root.querySelector('[data-testid="notice"]'))).toBe('Ubicación P01-E01-N01 desactivada.');
  });

  it('sin permiso de gestión no ofrece desactivar ni reactivar', async () => {
    const root = await render();
    expect(root.querySelector('[data-testid="state-button"]')).toBeNull();
    expect(document.querySelector('app-location-state-dialog')).toBeNull();
  });

  it('una ubicación inexistente se explica en la página', async () => {
    signIn();
    const fixture = TestBed.createComponent(LocationDetailPage);
    current = fixture;
    await settle(fixture);
    flushProblem(httpMock().expectOne(`/api/v1/locations/${detail().id}`), 404, 'NotFound', 'El recurso solicitado no existe.');
    flushProblem(httpMock().expectOne(r => r.url === `/api/v1/locations/${detail().id}/stock`), 404, 'NotFound', 'El recurso solicitado no existe.');
    await settle(current);
    expect(text((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]'))).toBe('La ubicación no existe.');
  });
});
