import { provideZonelessChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeuix/themes/aura';
import { provideRouter } from '@angular/router';
import { routes } from './app/app.routes';
import { AppLayoutComponent } from './app/core/layout/app-layout.component';

bootstrapApplication(AppLayoutComponent, {
  providers: [
    provideZonelessChangeDetection(),
    provideHttpClient(),
    provideRouter(routes),
    providePrimeNG({ theme: { preset: Aura } })
  ]
}).catch(console.error);
