import { Routes } from '@angular/router';

/** HU-04 · Persona B. Sustituir el placeholder por las pantallas reales en su spec. */
export const CYCLE_COUNTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('../../shared/placeholder-page.component').then(module => module.PlaceholderPageComponent),
    data: {
      title: 'Inventarios cíclicos',
      story: 'HU-04 · Realizar inventarios cíclicos y ajustar diferencias',
      description: 'Conteo de una parte del almacén, diferencias y ajuste autorizado.'
    }
  }
];
