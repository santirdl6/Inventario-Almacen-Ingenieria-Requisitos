import { Routes } from '@angular/router';

/** HU-02 · Persona B. Sustituir el placeholder por las pantallas reales en su spec. */
export const LOCATIONS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('../../shared/placeholder-page.component').then(module => module.PlaceholderPageComponent),
    data: {
      title: 'Ubicaciones',
      story: 'HU-02 · Gestionar las ubicaciones de los artículos',
      description: 'Código único con pasillo, estantería y nivel; cantidades por artículo y ubicación.'
    }
  }
];
