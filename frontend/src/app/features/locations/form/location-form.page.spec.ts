import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { detail, flushProblem, httpMock, locationsTestProviders, mutation, settle, signIn, text } from '../testing/locations-testing';
import { LocationFormPage } from './location-form.page';

function type(root: HTMLElement, id: string, value: string): void {
  const input = root.querySelector(`#${id}`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('blur'));
}

function submit(root: HTMLElement): void {
  root.querySelector('form')!.dispatchEvent(new Event('submit'));
}

function fill(root: HTMLElement, code = 'P01-E01-N01'): void {
  for (const [field, value] of [['code', code], ['aisle', 'P01'], ['rack', 'E01'], ['level', 'N01']]) type(root, field, value);
}

describe('LocationFormPage (alta)', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [LocationFormPage], providers: locationsTestProviders() });
    signIn(['Inventory.Read', 'Locations.Manage']);
  });

  async function render() {
    const fixture = TestBed.createComponent(LocationFormPage);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('campos vacíos o solo con espacios bloquean el envío y se señalan', async () => {
    const { fixture, root } = await render();
    type(root, 'aisle', '   ');
    submit(root);
    await settle(fixture);
    httpMock().expectNone('/api/v1/locations');
    for (const field of ['code', 'aisle', 'rack', 'level']) {
      expect(root.querySelector(`#${field}`)!.getAttribute('aria-invalid')).toBe('true');
      expect(text(root.querySelector(`#${field}-error`))).toBe('Este campo es obligatorio.');
      // Contrato UI: el mensaje de error queda asociado al campo.
      expect(root.querySelector(`#${field}`)!.getAttribute('aria-describedby')!.split(' ')).toContain(`${field}-error`);
    }
    expect(document.activeElement?.id).toBe('code');
  });

  it('muestra las longitudes máximas (32 y 16) y la obligatoriedad con texto', async () => {
    const { root } = await render();
    expect(text(root.querySelector('#code-help'))).toBe('Máximo 32 caracteres: letras A–Z, dígitos y guion.');
    for (const field of ['aisle', 'rack', 'level']) expect(text(root.querySelector(`#${field}-help`))).toBe('Máximo 16 caracteres: letras A–Z, dígitos y guion.');
    expect(root.querySelector('#code')!.getAttribute('maxlength')).toBe('32');
    expect(root.querySelector('#aisle')!.getAttribute('maxlength')).toBe('16');
    expect(root.querySelector('#code')!.getAttribute('aria-describedby')).toBe('code-help');
    expect(text(root.querySelector('label[for="code"]'))).toContain('(obligatorio)');
  });

  it('caracteres no admitidos se señalan en su campo, no se envía y se conserva lo escrito (US1.6)', async () => {
    const { fixture, root } = await render();
    // Prueba manual 2026-11-19, caso 2.
    type(root, 'code', '  nay - a2  ');
    type(root, 'aisle', 'ÁREA');
    type(root, 'rack', 'A_B');
    type(root, 'level', 'Ñ1');
    submit(root);
    await settle(fixture);
    httpMock().expectNone('/api/v1/locations');
    for (const field of ['code', 'aisle', 'rack', 'level']) {
      expect(root.querySelector(`#${field}`)!.getAttribute('aria-invalid')).toBe('true');
      expect(text(root.querySelector(`#${field}-error`))).toBe('Solo letras A–Z sin acentos ni Ñ, dígitos y guion, sin espacios interiores.');
    }
    expect((root.querySelector('#code') as HTMLInputElement).value).toBe('  nay - a2  ');
    expect(document.activeElement?.id).toBe('code');
  });

  it('minúsculas, dígitos y guion se admiten y se envían recortados', async () => {
    const { root } = await render();
    for (const [field, value] of [['code', ' p01-e01-n01 '], ['aisle', 'p01'], ['rack', 'e01'], ['level', 'n-1']]) type(root, field, value);
    submit(root);
    expect(httpMock().expectOne('/api/v1/locations').request.body).toEqual({ code: 'p01-e01-n01', aisle: 'p01', rack: 'e01', level: 'n-1', kind: 'Storage' });
  });

  it('un 422 del servidor por caracteres se muestra en su campo', async () => {
    const { fixture, root } = await render();
    fill(root, 'X');
    submit(root);
    flushProblem(httpMock().expectOne('/api/v1/locations'), 422, 'ValidationFailed', 'Revisa los datos marcados.',
      { errors: { code: ['Solo se admiten letras A-Z sin acentos ni Ñ, dígitos y guion, sin espacios interiores.'] } });
    await settle(fixture);
    expect(text(root.querySelector('#code-error'))).toContain('Solo se admiten letras A-Z');
  });

  it('propone el código a partir de pasillo, estantería y nivel', async () => {
    const { fixture, root } = await render();
    type(root, 'aisle', ' p01 ');
    type(root, 'rack', 'e01');
    type(root, 'level', 'n02');
    await settle(fixture);
    const propose = Array.from(root.querySelectorAll('button')).find(b => text(b) === 'Proponer código')!;
    propose.click();
    await settle(fixture);
    expect((root.querySelector('#code') as HTMLInputElement).value).toBe('P01-E01-N02');
  });

  it('crea con clave de idempotencia y navega al detalle con el aviso de guardado', async () => {
    const { fixture, root } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    type(root, 'code', 'REC-01');
    type(root, 'aisle', 'R');
    type(root, 'rack', 'E01');
    type(root, 'level', 'N01');
    const kind = root.querySelector('#kind') as HTMLSelectElement;
    kind.value = 'Receiving';
    kind.dispatchEvent(new Event('change'));
    submit(root);
    const request = httpMock().expectOne('/api/v1/locations');
    expect(request.request.body).toEqual({ code: 'REC-01', aisle: 'R', rack: 'E01', level: 'N01', kind: 'Receiving' });
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    request.flush(mutation(detail({ id: 'new-id', code: 'REC-01' })), { status: 201, statusText: 'Created', headers: { ETag: '"V1"' } });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(['/ubicaciones', 'new-id'], { state: { notice: 'Ubicación REC-01 guardada.' } });
  });

  it('un código repetido marca el campo, ofrece buscar la existente y conserva lo escrito', async () => {
    const { fixture, root } = await render();
    fill(root);
    submit(root);
    flushProblem(httpMock().expectOne('/api/v1/locations'), 409, 'DuplicateCode',
      'Ese código ya está utilizado, también puede pertenecer a un registro inactivo.', { errors: { code: ['Introduce un código libre.'] } });
    await settle(fixture);
    expect(text(root.querySelector('#code-error'))).toContain('Introduce un código libre.');
    expect(root.querySelector('[data-testid="duplicate-link"]')!.getAttribute('href')).toBe('/ubicaciones?search=P01-E01-N01&status=all');
    expect((root.querySelector('#aisle') as HTMLInputElement).value).toBe('P01');
    expect((root.querySelector('#code') as HTMLInputElement).value).toBe('P01-E01-N01');
    expect(document.activeElement?.id).toBe('code');
  });

  it('un lugar físico repetido marca pasillo, estantería y nivel y nombra la ubicación existente', async () => {
    const { fixture, root } = await render();
    fill(root, 'X');
    submit(root);
    const message = 'Ese pasillo, estantería y nivel ya corresponden a la ubicación P01-E01-N01. Usa esa ubicación o elige otro lugar.';
    flushProblem(httpMock().expectOne('/api/v1/locations'), 409, 'DuplicateLocationPlace', message,
      { errors: { aisle: ['Ya registrado.'], rack: ['Ya registrado.'], level: ['Ya registrado.'] } });
    await settle(fixture);
    for (const field of ['aisle', 'rack', 'level']) expect(root.querySelector(`#${field}-error`)).not.toBeNull();
    expect(root.querySelector('#code-error')).toBeNull();
    expect(text(root.querySelector('[data-testid="general-error"]'))).toContain('P01-E01-N01');
    expect(root.querySelector('[data-testid="duplicate-link"]')!.getAttribute('href')).toBe('/ubicaciones?search=P01&status=all');
  });

  it('un 422 del servidor se muestra por campo', async () => {
    const { fixture, root } = await render();
    fill(root, 'X');
    submit(root);
    flushProblem(httpMock().expectOne('/api/v1/locations'), 422, 'ValidationFailed', 'Revisa los datos.',
      { errors: { Level: ['El nivel no es válido.'] } });
    await settle(fixture);
    expect(text(root.querySelector('#level-error'))).toContain('El nivel no es válido.');
    expect(document.activeElement?.id).toBe('level');
  });

  it('un 403 se explica en la página sin perder lo escrito', async () => {
    const { fixture, root } = await render();
    fill(root);
    submit(root);
    flushProblem(httpMock().expectOne('/api/v1/locations'), 403, 'Forbidden', 'Tu cuenta no tiene permiso para esta operación.');
    await settle(fixture);
    expect(text(root.querySelector('[data-testid="general-error"]'))).toContain('no tiene permiso para modificar ubicaciones');
    expect((root.querySelector('#code') as HTMLInputElement).value).toBe('P01-E01-N01');
  });
});
