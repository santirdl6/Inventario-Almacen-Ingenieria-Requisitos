import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

export const RECEIVING_ROUTES: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/receipt-list.page').then(m => m.ReceiptListPage) },
  { path: 'nueva', canActivate: [permissionGuard], data: { permission: 'Receiving.Record' },
    loadComponent: () => import('./pages/receipt-form.page').then(m => m.ReceiptFormPage) },
  { path: 'contingencia', canActivate: [permissionGuard], data: { permission: 'Inventory.Adjust' },
    loadComponent: () => import('./pages/receipt-contingency.page').then(m => m.ReceiptContingencyPage) },
  { path: 'contingencia/:id', canActivate: [permissionGuard], data: { permission: 'Inventory.Adjust' },
    loadComponent: () => import('./pages/receipt-contingency.page').then(m => m.ReceiptContingencyPage) },
  { path: ':id', loadComponent: () => import('./pages/receipt-detail.page').then(m => m.ReceiptDetailPage) }
];
