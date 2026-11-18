import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ArticleHistoryComponent } from './article-history.component';

describe('Historial de artículos', () => {
  let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ imports: [ArticleHistoryComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'article' }) } } }] }); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  function setup() {
    const fixture = TestBed.createComponent(ArticleHistoryComponent); fixture.detectChanges();
    http.expectOne('/api/v1/articles/article').flush({ sku: 'ART-01', name: 'Artículo inactivo', isActive: false }); return fixture;
  }
  it('muestra integración pendiente y nunca la confunde con un historial vacío', async () => {
    const fixture = setup();
    await vi.waitFor(() => http.expectOne(request => request.url.endsWith('/movements')).flush({ code: 'DependencyUnavailable', detail: 'Falta el diario compartido.' }, { status: 503, statusText: 'Unavailable' }));
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Inactivo'); expect(fixture.nativeElement.textContent).toContain('Historial pendiente de integración'); expect(fixture.nativeElement.textContent).not.toContain('No hay movimientos');
  });
  it('solo presenta vacío ante una página válida y conserva cantidades históricas como texto exacto', async () => {
    const fixture = setup();
    await vi.waitFor(() => http.expectOne(request => request.url.endsWith('/movements')).flush({ items: [], totalCount: 0 }));
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay movimientos');
    const loaded = fixture.componentInstance.load(); http.expectOne('/api/v1/articles/article').flush({ sku: 'ART-01', isActive: false });
    await vi.waitFor(() => http.expectOne(request => request.url.endsWith('/movements')).flush({ items: [{ id: 'entry', kind: 'Receipt', quantity: '999999999999999.999', baseUnit: 'KG', actor: { name: 'Responsable' }, document: { type: 'Receipt', id: 'document' } }], totalCount: 1 }));
    await loaded; fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('999999999999999.999 KG');
  });
});
