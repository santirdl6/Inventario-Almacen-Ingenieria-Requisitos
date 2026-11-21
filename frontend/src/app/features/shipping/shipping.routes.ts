import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

/**
 * HU-03 · Persona B. Pantallas de specs/005-registrar-salida-mercancia/contracts/shipping-ui.md. El área
 * exige Inventory.Read (operation-areas.ts, D-14); preparar una salida nueva exige además
 * Shipping.Record. El servidor sigue decidiendo cada permiso.
 */
export const SHIPPING_ROUTES: Routes = [
  {
    path: '',
    title: 'Salidas',
    loadComponent: () => import('./order-lookup.page').then(module => module.OrderLookupPage)
  },
  {
    path: 'pedidos/:orderId',
    title: 'Salidas del pedido',
    loadComponent: () => import('./order-shipments.page').then(module => module.OrderShipmentsPage)
  },
  {
    path: 'pedidos/:orderId/nueva',
    title: 'Nueva salida',
    canActivate: [permissionGuard],
    data: { permission: 'Shipping.Record' },
    loadComponent: () => import('./shipment-form.page').then(module => module.ShipmentFormPage)
  },
  {
    path: ':id',
    title: 'Salida',
    loadComponent: () => import('./shipment-form.page').then(module => module.ShipmentFormPage)
  }
];
