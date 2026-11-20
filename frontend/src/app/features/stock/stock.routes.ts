import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

/**
 * HU-05 · Persona B. Pantallas de specs/004-consultar-existencias/contracts/stock-ui.md. El área
 * ya exige Inventory.Read (operation-areas.ts); el servidor sigue decidiendo cada permiso.
 */
export const STOCK_ROUTES: Routes = [
  {
    path: '',
    title: 'Existencias',
    loadComponent: () => import('./query/stock-query.page').then(module => module.StockQueryPage)
  },
  {
    path: 'pedidos/nuevo',
    title: 'Nuevo pedido de cliente',
    canActivate: [permissionGuard],
    data: { permission: 'Shipping.Record' },
    loadComponent: () => import('./orders/order-form.page').then(module => module.OrderFormPage)
  },
  {
    path: 'pedidos/:id',
    title: 'Pedido de cliente',
    loadComponent: () => import('./orders/order-detail.page').then(module => module.OrderDetailPage)
  }
];
