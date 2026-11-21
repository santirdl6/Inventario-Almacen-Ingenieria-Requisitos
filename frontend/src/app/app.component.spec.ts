import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AppComponent } from './app.component';

describe('Estado de la base H0', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('espera a la respuesta antes de comunicar disponibilidad', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('Comprobando');

    http.expectOne('/api/v1/health').flush({ status: 'healthy', database: 'ready' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('SQL Server disponibles');
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(false);
  });

  it('explica un fallo y permite volver a comprobar la conexión', () => {
    const fixture = TestBed.createComponent(AppComponent);
    http.expectOne('/api/v1/health').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('No se puede conectar');

    fixture.nativeElement.querySelector('button').click();
    http.expectOne('/api/v1/health').flush({ status: 'healthy', database: 'ready' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('SQL Server disponibles');
  });
});
