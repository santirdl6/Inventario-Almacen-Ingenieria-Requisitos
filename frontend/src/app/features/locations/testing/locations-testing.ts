import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { SessionService } from '../../../core/session/session.service';
import { LocationDetail, LocationSummary } from '../data/location.models';

/**
 * Proveedores de prueba sobre la base de A: backend simulado con HttpTestingController, sin
 * interceptor (los componentes reciben HttpErrorResponse e interpretan con apiProblem()) y,
 * si se indican, parámetros de ruta como los que leen las páginas con ActivatedRoute.
 */
export function locationsTestProviders(params: Record<string, string> = {},
  query: Record<string, string> = {}): (Provider | EnvironmentProviders)[] {
  return [
    provideZonelessChangeDetection(),
    provideHttpClient(),
    provideHttpClientTesting(),
    provideRouter([]),
    providePrimeNG(),
    { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params), queryParamMap: convertToParamMap(query) } } }
  ];
}

/** Sesión de A con los permisos indicados (sin pasar por /auth/me). */
export function signIn(permissions: string[] = ['Inventory.Read']): void {
  const session = TestBed.inject(SessionService);
  session.user.set({ id: 'user-1', email: 'operario@example.test', roles: ['Operario'], permissions });
  session.state.set('authenticated');
}

export function httpMock(): HttpTestingController {
  return TestBed.inject(HttpTestingController);
}

export function summary(overrides: Partial<LocationSummary> = {}): LocationSummary {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    code: 'P01-E01-N01',
    aisle: 'P01',
    rack: 'E01',
    level: 'N01',
    description: 'Pasillo P01 · Estantería E01 · Nivel N01',
    kind: 'Storage',
    kindLabel: 'Almacenamiento',
    isActive: true,
    statusLabel: 'Activa',
    ...overrides
  };
}

export function detail(overrides: Partial<LocationDetail> = {}): LocationDetail {
  return {
    ...summary(),
    hasMovements: false,
    isEmpty: true,
    hasReservations: false,
    inOpenCount: false,
    isLastActiveReceiving: false,
    ...overrides
  };
}

/** Cuerpo de POST/PUT como lo devuelve la API: { operationId, resource }. */
export function mutation(resource: LocationDetail): { operationId: string; resource: LocationDetail } {
  return { operationId: '99999999-9999-9999-9999-999999999999', resource };
}

/** Responde con ProblemDetails como hace la API. */
export function flushProblem(request: TestRequest, status: number, code: string, detail: string,
  extra: Record<string, unknown> = {}): void {
  request.flush({ status, code, detail, title: 'Error', traceId: 't', ...extra },
    { status, statusText: 'Error', headers: { 'Content-Type': 'application/problem+json' } });
}

export function flushNetworkError(request: TestRequest): void {
  request.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
}

/** Espera a que se rendericen los cambios en modo zoneless. */
export async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

export function text(element: Element | null): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
