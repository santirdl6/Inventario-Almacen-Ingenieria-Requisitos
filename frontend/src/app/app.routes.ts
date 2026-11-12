import { Routes } from '@angular/router';
import { Shell } from './core/layout/shell';

/**
 * Rutas de pantalla de contracts/ui.md. Cada área se carga de forma diferida desde su
 * carpeta en features/. Persona A añade /maestros, /compras, /recepciones y /alertas.
 */
export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'estado' },
      { path: 'estado', loadComponent: () => import('./features/status/status.page').then(m => m.StatusPage) },

      // Persona B
      { path: 'ubicaciones', loadChildren: () => import('./features/locations/locations.routes').then(m => m.LOCATIONS_ROUTES) },
      { path: 'existencias', loadChildren: () => import('./features/stock/stock.routes').then(m => m.STOCK_ROUTES) },
      { path: 'salidas', loadChildren: () => import('./features/shipping/shipping.routes').then(m => m.SHIPPING_ROUTES) },
      { path: 'inventarios', loadChildren: () => import('./features/cycle-counts/cycle-counts.routes').then(m => m.CYCLE_COUNTS_ROUTES) },

      { path: '**', redirectTo: 'estado' }
    ]
  }
];
