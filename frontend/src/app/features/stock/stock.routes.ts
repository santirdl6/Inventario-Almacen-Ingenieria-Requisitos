import { Routes } from '@angular/router';

/** HU-05 · Persona B. Sustituir el placeholder por las pantallas reales en su spec. */
export const STOCK_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('../../shared/placeholder-page.component').then(module => module.PlaceholderPageComponent),
    data: {
      title: 'Existencias',
      story: 'HU-05 · Consultar existencias y reservas',
      description: 'Físico, reservado y disponible por artículo, con filtros por ubicación o lote.'
    }
  }
];
