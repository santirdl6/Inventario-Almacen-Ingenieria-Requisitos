import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ArticleListComponent } from './article-list.component';

describe('Consulta del catálogo de artículos', () => {
  let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ imports: [ArticleListComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] }); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('no convierte un error de consulta en una tabla vacía ni pierde el resultado anterior', async () => {
    const fixture = TestBed.createComponent(ArticleListComponent); fixture.detectChanges();
    http.expectOne(request => request.url === '/api/v1/families').flush({ items: [] });
    await vi.waitFor(() => http.expectOne(request => request.url === '/api/v1/articles').flush({ items: [{ id: 'article', sku: 'ART-01', name: 'Tuerca', family: { code: 'F', name: 'General' }, baseUnit: 'UD', isActive: true }], totalCount: 1 }));
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const loading = fixture.componentInstance.load();
    http.expectOne(request => request.url === '/api/v1/articles').flush({ detail: 'Servicio temporalmente no disponible.' }, { status: 503, statusText: 'Unavailable' });
    await loading; fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Servicio temporalmente no disponible.');
    expect(fixture.nativeElement.textContent).toContain('Se conserva el último resultado disponible.');
    expect(fixture.nativeElement.textContent).toContain('ART-01');
    expect(fixture.nativeElement.textContent).not.toContain('No hay coincidencias');
  });
});
