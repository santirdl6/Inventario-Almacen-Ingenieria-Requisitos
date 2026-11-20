import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { LOCATIONS_ROUTES } from './locations.routes';
import { signIn } from './testing/locations-testing';

// Navegación real con el router (no solo el href): desde la lista, «Localizar un artículo»
// debe abrir la búsqueda de artículos de US2.1 (T055); también por teclado.
describe('Rutas de ubicaciones', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), providePrimeNG(),
        provideRouter([{ path: 'ubicaciones', children: LOCATIONS_ROUTES }])]
    });
    signIn(['Inventory.Read', 'Locations.Manage']);
  });

  async function list(): Promise<RouterTestingHarness> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/ubicaciones');
    TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/v1/locations')
      .flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
    harness.detectChanges();
    return harness;
  }

  function link(harness: RouterTestingHarness): HTMLAnchorElement {
    const anchor = Array.from(harness.routeNativeElement!.querySelectorAll('a'))
      .find(element => element.textContent?.trim() === 'Localizar un artículo');
    expect(anchor).toBeDefined();
    return anchor as HTMLAnchorElement;
  }

  it('«Localizar un artículo» abre la búsqueda de artículos al hacer clic', async () => {
    const harness = await list();
    expect(link(harness).getAttribute('href')).toBe('/ubicaciones/articulo');
    link(harness).click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/ubicaciones/articulo');
    expect(harness.routeNativeElement!.querySelector('h1')!.textContent).toBe('Localizar un artículo');
    expect(harness.routeNativeElement!.querySelector('#article-query')).not.toBeNull();
    // Es la primera prueba del archivo que resuelve los loadComponent de la lista y de la
    // búsqueda (con PrimeNG): sola tarda unos 2 s, pero dentro de la suite completa, con los
    // workers cargados, llega a 4-6 s y superaba el límite de 5 s. La siguiente prueba ya usa
    // los chunks en caché (unos 50 ms). Mismo margen que operation-areas.spec.ts.
  }, 20_000);

  it('la ruta de búsqueda no se confunde con el detalle de una ubicación (:id)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/ubicaciones/articulo');
    expect(harness.routeNativeElement!.querySelector('h1')!.textContent).toBe('Localizar un artículo');
    TestBed.inject(HttpTestingController).expectNone(r => r.url.startsWith('/api/v1/locations/'));
  });
});
