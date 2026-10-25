import { Component, inject, signal, provideZonelessChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeuix/themes/aura';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CardModule, ButtonModule],
  template: `
    <main>
      <h1>Inventario y almacén</h1>
      <p>Entorno común de desarrollo</p>
      <p-card header="Estado de la aplicación">
        <p role="status" aria-live="polite">{{ message() }}</p>
        <p-button label="Comprobar conexión" (onClick)="check()" [disabled]="checking()" />
      </p-card>
      <p>La base técnica está disponible. Las ocho funcionalidades del almacén se desarrollarán en sus specs.</p>
    </main>
  `
})
class AppComponent {
  private readonly http = inject(HttpClient);
  readonly message = signal('Comprobando la conexión con la base de datos…');
  readonly checking = signal(false);

  constructor() { this.check(); }

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

bootstrapApplication(AppComponent, {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(),
    providePrimeNG({ theme: { preset: Aura } })
  ]
}).catch(console.error);
