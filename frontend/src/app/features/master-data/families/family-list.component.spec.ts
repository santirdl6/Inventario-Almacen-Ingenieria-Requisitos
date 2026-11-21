import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FamilyListComponent } from './family-list.component';

describe('Filtros de familias', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [FamilyListComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('envía los valores escritos y seleccionados sin restablecer el formulario al buscar', async () => {
    const fixture = TestBed.createComponent(FamilyListComponent); fixture.detectChanges();
    http.expectOne(request => request.url === '/api/v1/families').flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    await fixture.whenStable();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = 'UI-CODE'; input.dispatchEvent(new Event('input', { bubbles: true }));
    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = 'inactive'; select.dispatchEvent(new Event('change', { bubbles: true }));
    expect(fixture.componentInstance.filters.getRawValue()).toEqual({ q: 'UI-CODE', state: 'inactive' });
    fixture.detectChanges();
    fixture.nativeElement.querySelector('p-button button').click();
    const request = http.expectOne(request => request.url === '/api/v1/families');
    expect(request.request.params.get('q')).toBe('UI-CODE'); expect(request.request.params.get('state')).toBe('inactive');
    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    await fixture.whenStable();
    expect(fixture.componentInstance.filters.getRawValue()).toEqual({ q: 'UI-CODE', state: 'inactive' });
  });
});
