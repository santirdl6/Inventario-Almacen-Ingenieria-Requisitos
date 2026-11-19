import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { detail, flushNetworkError, flushProblem, httpMock, locationsTestProviders, settle, signIn, text } from '../testing/locations-testing';
import { LocationFormPage } from './location-form.page';

const ID = detail().id;

function type(root: HTMLElement, id: string, value: string): void {
  const input = root.querySelector(`#${id}`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function button(root: HTMLElement, label: string): HTMLButtonElement {
  return Array.from(root.querySelectorAll('button')).find(b => text(b) === label) as HTMLButtonElement;
}

describe('LocationFormPage (conflictos y resultado incierto, US5)', () => {
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

  it('un 412 conserva lo escrito y permite ver los datos vigentes y guardar sobre ellos', async () => {
    const { fixture, root } = await render();
    type(root, 'level', 'N09');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 412, 'VersionConflict',
      'Otra persona cambió el registro. Consulta la versión actual y revisa tus cambios.');
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="conflict"]'))).toContain('Otra persona ha cambiado esta ubicación');
    expect((root.querySelector('#level') as HTMLInputElement).value).toBe('N09');

    button(root, 'Ver datos vigentes').click();
    httpMock().expectOne(`/api/v1/locations/${ID}`).flush(detail({ level: 'N05', description: 'Pasillo P01 · Estantería E01 · Nivel N05' }),
      { headers: { ETag: '"V7"' } });
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="current"]'))).toContain('Nivel N05');
    expect((root.querySelector('#level') as HTMLInputElement).value).toBe('N09');

    // La nueva intención se basa en la versión vigente revisada.
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    expect(httpMock().expectOne(`/api/v1/locations/${ID}`).request.headers.get('If-Match')).toBe('"V7"');
  });

  it('descartar recarga los datos vigentes', async () => {
    const { fixture, root } = await render();
    type(root, 'level', 'N09');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 412, 'VersionConflict', 'Versión obsoleta.');
    await settle(fixture);
    button(root, 'Descartar mis cambios').click();
    httpMock().expectOne(`/api/v1/locations/${ID}`).flush(detail({ level: 'N05' }), { headers: { ETag: '"V8"' } });
    await settle(fixture);
    expect((root.querySelector('#level') as HTMLInputElement).value).toBe('N05');
    expect(root.querySelector('[data-testid="conflict"]')).toBeNull();
  });

  it('tras un error de red queda pendiente de comprobar y «Comprobar» reutiliza la clave', async () => {
    const { fixture, root } = await render();
    type(root, 'level', 'N02');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    const first = httpMock().expectOne(`/api/v1/locations/${ID}`);
    flushNetworkError(first);
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="pending"]'))).toContain('Resultado pendiente de comprobar');
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
    fixture.componentInstance.form.controls.level.setValue('N99');
    fixture.componentInstance.loaded.set({ value: detail(), etag: '"V9"' });
    button(root, 'Comprobar').click();
    const check = httpMock().expectOne(request => request.url.startsWith('/api/v1/operations/'));
    flushProblem(check, 404, 'NotFound', 'Sin resultado observado'); await settle(fixture);
    button(root, 'Repetir petición original').click();
    const retry = httpMock().expectOne(`/api/v1/locations/${ID}`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(first.request.headers.get('Idempotency-Key'));
    expect(retry.request.headers.get('If-Match')).toBe('"V1"');
    expect(retry.request.body.level).toBe('N02');
    retry.flush({ operationId: first.request.headers.get('Idempotency-Key'), resource: detail() }, { headers: { ETag: '"V2"' } }); await settle(fixture);
  });

  it('un 5xx también deja el resultado pendiente, nunca un éxito', async () => {
    const { fixture, root } = await render();
    type(root, 'level', 'N02');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 503, 'ServiceUnavailable', 'Comprueba el resultado.');
    await settle(fixture);
    expect(root.querySelector('[data-testid="pending"]')).not.toBeNull();
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });

  it('el doble clic envía una sola petición', async () => {
    const { root } = await render();
    type(root, 'level', 'N02');
    const form = root.querySelector('form')!;
    form.dispatchEvent(new Event('submit'));
    form.dispatchEvent(new Event('submit'));
    expect(httpMock().match(`/api/v1/locations/${ID}`).length).toBe(1);
  });

  it('un 403 lo explica y conserva lo escrito', async () => {
    const { fixture, root } = await render();
    type(root, 'level', 'N04');
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    flushProblem(httpMock().expectOne(`/api/v1/locations/${ID}`), 403, 'Forbidden', 'Tu cuenta no tiene permiso para esta operación.');
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="general-error"]'))).toContain('no tiene permiso para modificar ubicaciones');
    expect((root.querySelector('#level') as HTMLInputElement).value).toBe('N04');
  });
});
