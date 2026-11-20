import { Routes } from '@angular/router';

/**
 * HU-05 · Persona B. Pantallas de specs/004-consultar-existencias/contracts/stock-ui.md. El área
 * ya exige Inventory.Read (operation-areas.ts); el servidor sigue decidiendo cada permiso.
 */
export const STOCK_ROUTES: Routes = [
  {
    path: '',
    title: 'Existencias',
    loadComponent: () => import('./query/stock-query.page').then(module => module.StockQueryPage)
  }
];
