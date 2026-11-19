import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LocationDetail, Versioned } from '../data/location.models';
import { detail, flushNetworkError, flushProblem, httpMock, locationsTestProviders, mutation, settle, text } from '../testing/locations-testing';
import { LocationStateDialog } from './location-state-dialog';

@Component({
  imports: [LocationStateDialog],
  template: `<app-location-state-dialog [location]="location()" etag='"V1"' [visible]="true"
    (changed)="saved.set($event)" (closed)="closed.set(true)" />`
})
class Host {
  readonly location = signal<LocationDetail>(detail());
  readonly saved = signal<Versioned<LocationDetail> | null>(null);
  readonly closed = signal(false);
}

function button(label: string): HTMLButtonElement | undefined {
  return Array.from(document.querySelectorAll('button')).find(b => text(b) === label) as HTMLButtonElement | undefined;
}

describe('LocationStateDialog', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [Host], providers: locationsTestProviders() });
  });

  async function render(location: LocationDetail) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.location.set(location);
    await settle(fixture);
    return fixture;
  }

  it('muestra la revisión y la consecuencia y desactiva tras confirmar, sin acción de borrado', async () => {
    const fixture = await render(detail());
    expect(text(document.querySelector('[data-testid="content"]'))).toBe('Vacía');
    expect(text(document.querySelector('[data-testid="consequence"]'))).toContain('Dejará de ofrecerse');
    expect(Array.from(document.querySelectorAll('button')).some(b => /eliminar|borrar/i.test(text(b)))).toBe(false);
    button('Desactivar')!.click();
    const request = httpMock().expectOne(`/api/v1/locations/${detail().id}`);
    expect(request.request.body).toEqual({ code: 'P01-E01-N01', aisle: 'P01', rack: 'E01', level: 'N01', kind: 'Storage', isActive: false });
    expect(request.request.headers.get('If-Match')).toBe('"V1"');
    request.flush(mutation(detail({ isActive: false, statusLabel: 'Inactiva' })), { headers: { ETag: '"V2"' } });
    await settle(fixture);
    expect(fixture.componentInstance.saved()?.etag).toBe('"V2"');
  });

  it('la única zona de recepción activa no ofrece confirmar y explica qué hacer', async () => {
    await render(detail({ kind: 'Receiving', kindLabel: 'Zona de recepción', isLastActiveReceiving: true }));
    expect(text(document.querySelector('[data-testid="no-receiving"]'))).toContain('Crea o activa otra zona de recepción');
    expect(button('Desactivar')).toBeUndefined();
    expect(button('Cerrar')).toBeDefined();
    httpMock().expectNone(`/api/v1/locations/${detail().id}`);
  });

  it('si el servidor responde NoActiveReceivingLeft muestra su explicación y no reintenta', async () => {
    const fixture = await render(detail({ kind: 'Receiving', kindLabel: 'Zona de recepción' }));
    button('Desactivar')!.click();
    flushProblem(httpMock().expectOne(`/api/v1/locations/${detail().id}`), 409, 'NoActiveReceivingLeft',
      'No puede quedar el almacén sin zonas de recepción activas. Crea o activa otra zona de recepción antes de retirar esta.');
    await settle(fixture);
    expect(text(document.querySelector('[data-testid="dialog-error"]'))).toContain('No puede quedar el almacén sin zonas de recepción');
    expect(document.querySelector('[data-testid="no-receiving"]')).not.toBeNull();
    expect(button('Desactivar')).toBeUndefined();
    httpMock().expectNone(`/api/v1/locations/${detail().id}`);
  });

  it('con existencias muestra el motivo del servidor y enlaza al contenido', async () => {
    const fixture = await render(detail({ isEmpty: false }));
    button('Desactivar')!.click();
    flushProblem(httpMock().expectOne(`/api/v1/locations/${detail().id}`), 409, 'LocationNotEmpty',
      'La ubicación no está vacía: tiene 4.000 unidades físicas y 1.000 reservadas.');
    await settle(fixture);
    expect(text(document.querySelector('[data-testid="dialog-error"]'))).toContain('4.000 unidades físicas y 1.000 reservadas');
    expect(document.querySelector('[data-testid="dialog-error"] a')!.getAttribute('href')).toBe('#contenido-ubicacion');
  });

  it('reactiva una ubicación inactiva', async () => {
    const fixture = await render(detail({ isActive: false, statusLabel: 'Inactiva' }));
    expect(text(document.querySelector('[data-testid="consequence"]'))).toContain('Volverá a ofrecerse');
    button('Reactivar')!.click();
    const request = httpMock().expectOne(`/api/v1/locations/${detail().id}`);
    expect(request.request.body.isActive).toBe(true);
    request.flush(mutation(detail()), { headers: { ETag: '"V3"' } });
    await settle(fixture);
    expect(fixture.componentInstance.saved()?.value.isActive).toBe(true);
  });

  it('con resultado incierto permite comprobar con la misma clave', async () => {
    const fixture = await render(detail({ isActive: false, statusLabel: 'Inactiva' }));
    button('Reactivar')!.click();
    const first = httpMock().expectOne(`/api/v1/locations/${detail().id}`);
    flushNetworkError(first);
    await settle(fixture);
    expect(document.querySelector('[data-testid="pending"]')).not.toBeNull();
    expect(button('Reactivar')!.disabled).toBe(true);
    button('Comprobar')!.click();
    const retry = httpMock().expectOne(`/api/v1/locations/${detail().id}`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(first.request.headers.get('Idempotency-Key'));
    expect(retry.request.body.isActive).toBe(true);
  });

  it('sin permiso lo explica y no permite reintentar', async () => {
    const fixture = await render(detail());
    button('Desactivar')!.click();
    flushProblem(httpMock().expectOne(`/api/v1/locations/${detail().id}`), 403, 'Forbidden', 'Acceso denegado.');
    await settle(fixture);
    expect(text(document.querySelector('[data-testid="dialog-error"]'))).toContain('no tiene permiso para modificar ubicaciones');
    expect(button('Desactivar')!.disabled).toBe(true);
  });
});
