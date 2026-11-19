import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { detail, flushProblem, httpMock, locationsTestProviders, mutation, settle, signIn, text } from '../testing/locations-testing';
import { LocationFormPage } from './location-form.page';

const ID = detail().id;

function type(root: HTMLElement, id: string, value: string): void {
  const input = root.querySelector(`#${id}`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('LocationFormPage (corrección, US4)', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [LocationFormPage], providers: locationsTestProviders({ id: ID }) });
    signIn(['Inventory.Read', 'Locations.Manage']);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  async function render(location = detail()) {
    const fixture = TestBed.createComponent(LocationFormPage);
    await settle(fixture);
    httpMock().expectOne(`/api/v1/locations/${ID}`).flush(location, { headers: { ETag: '"V1"' } });
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('sin movimientos permite corregir el nivel, envía If-Match y conserva el estado', async () => {
    const { fixture, root } = await render();
    expect(text(root.querySelector('h1'))).toBe('Corregir ubicación');
    type(root, 'level', 'N02');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = httpMock().expectOne(`/api/v1/locations/${ID}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"V1"');
    expect(request.request.body).toEqual({ code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N02', kind: 'Storage', isActive: true });
    request.flush(mutation(detail({ level: 'N02' })), { headers: { ETag: '"V2"' } });
    await settle(fixture);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/ubicaciones', ID], { state: { notice: 'Ubicación P01-E01-N01 guardada.' } });
  });

  it('con movimientos bloquea código, pasillo, estantería y nivel, y deja cambiar el tipo', async () => {
    const { root } = await render(detail({ hasMovements: true }));
    for (const field of ['code', 'aisle', 'rack', 'level']) {
      expect((root.querySelector(`#${field}`) as HTMLInputElement).disabled).toBe(true);
    }
    expect((root.querySelector('#kind') as HTMLSelectElement).disabled).toBe(false);
    expect(text(root.querySelector('[data-testid="identity-locked"]'))).toContain('forman parte del historial');
    expect(root.querySelector('[data-testid="identity-locked"] a')!.getAttribute('href')).toBe('/ubicaciones/nueva');
    expect(Array.from(root.querySelectorAll('button')).some(b => text(b) === 'Proponer código')).toBe(false);
  });

  it('cambiar el tipo con existencias muestra el motivo junto al campo', async () => {
    const { fixture, root } = await render(detail({ hasMovements: true }));
    const kind = root.querySelector('#kind') as HTMLSelectElement;
    kind.value = 'Receiving';
    kind.dispatchEvent(new Event('change'));
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 409, 'LocationNotEmpty',
      'La ubicación no está vacía: tiene 4.000 unidades físicas y 0.000 reservadas. Revisa su contenido; no se ha movido nada.');
    await settle(fixture);
    expect(text(root.querySelector('#kind-error'))).toContain('4.000 unidades físicas');
    // US4.3: además de explicar el motivo, enlaza al contenido de la ubicación.
    expect(root.querySelector('[data-testid="content-link"]')!.getAttribute('href')).toBe(`/ubicaciones/${ID}#contenido-ubicacion`);
  });

  it('un conteo abierto también enlaza al contenido', async () => {
    const { fixture, root } = await render(detail({ hasMovements: true }));
    const kind = root.querySelector('#kind') as HTMLSelectElement;
    kind.value = 'Receiving';
    kind.dispatchEvent(new Event('change'));
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 409, 'CountScopeFrozen',
      'La ubicación está incluida en un conteo abierto. Espera a que termine el conteo.');
    await settle(fixture);
    expect(text(root.querySelector('#kind-error'))).toContain('conteo abierto');
    expect(root.querySelector('[data-testid="content-link"]')).not.toBeNull();
  });

  it('una identificación bloqueada por el servidor se muestra por campo', async () => {
    const { fixture, root } = await render();
    type(root, 'level', 'N03');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 409, 'LocationIdentityLocked',
      'La ubicación tiene movimientos.', { errors: { level: ['Forma parte del historial de movimientos y no puede cambiarse.'] } });
    await settle(fixture);
    expect(text(root.querySelector('#level-error'))).toContain('Forma parte del historial');
    expect((root.querySelector('#level') as HTMLInputElement).value).toBe('N03');
  });
});
