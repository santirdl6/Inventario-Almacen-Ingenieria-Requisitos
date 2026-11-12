import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';

/** Pantalla técnica de H0: comprueba que API y SQL Server responden. */
@Component({
  selector: 'app-status-page',
  standalone: true,
  imports: [CardModule, ButtonModule],
  template: `
    <h1>Estado de la aplicación</h1>
    <p-card header="Conexión con API y base de datos">
      <p role="status" aria-live="polite">{{ message() }}</p>
      <p-button label="Comprobar conexión" (onClick)="check()" [disabled]="checking()" />
    </p-card>
  `
})
export class StatusPage {
  private readonly http = inject(HttpClient);
  readonly message = signal('Comprobando la conexión con la base de datos…');
  readonly checking = signal(false);

  constructor() {
    this.check();
  }

  check(): void {
    this.checking.set(true);
    this.http.get<{ status: string }>('/api/v1/health').subscribe({
      next: () => {
        this.message.set('Aplicación y SQL Server disponibles.');
        this.checking.set(false);
      },
      error: () => {
        this.message.set('No se puede conectar con la base de datos. Espera unos segundos y vuelve a comprobar.');
        this.checking.set(false);
      }
    });
  }
}
