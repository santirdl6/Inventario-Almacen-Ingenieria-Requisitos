import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

export const PURCHASING_ROUTES: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/purchase-order-list.page').then(module => module.PurchaseOrderListPage) },
  { path: 'nuevo', canActivate: [permissionGuard], data: { permission: 'Purchasing.Manage' },
    loadComponent: () => import('./pages/purchase-order-form.page').then(module => module.PurchaseOrderFormPage) },
  { path: ':id/editar', canActivate: [permissionGuard], data: { permission: 'Purchasing.Manage' },
    loadComponent: () => import('./pages/purchase-order-form.page').then(module => module.PurchaseOrderFormPage) },
  { path: ':id/enviar', canActivate: [permissionGuard], data: { permission: 'Purchasing.Send' },
    loadComponent: () => import('./pages/purchase-order-send.page').then(module => module.PurchaseOrderSendPage) },
  { path: ':id', canActivate: [permissionGuard], data: { permission: 'Inventory.Read' },
    loadComponent: () => import('./pages/purchase-order-detail.page').then(module => module.PurchaseOrderDetailPage) }
];
