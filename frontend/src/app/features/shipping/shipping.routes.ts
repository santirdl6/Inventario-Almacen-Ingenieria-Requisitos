import { Routes } from '@angular/router';

/** HU-03 · Persona B. Sustituir el placeholder por las pantallas reales en su spec. */
export const SHIPPING_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('../../shared/placeholder-page.component').then(module => module.PlaceholderPageComponent),
    data: {
      title: 'Salidas',
      story: 'HU-03 · Registrar la salida de mercancía',
      description: 'Salida vinculada a un pedido de cliente, con bloqueo si supera lo disponible.'
    }
  }
];
