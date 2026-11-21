import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

export const masterDataRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'articulos' },
  { path: 'proveedores', loadComponent: () => import('./suppliers/supplier-list.component').then(module => module.SupplierListComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } },
  { path: 'proveedores/nuevo', loadComponent: () => import('./suppliers/supplier-form.component').then(module => module.SupplierFormComponent), canActivate: [permissionGuard], data: { permission: 'MasterData.Manage' } },
  { path: 'proveedores/:id', loadComponent: () => import('./suppliers/supplier-form.component').then(module => module.SupplierFormComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } },
  { path: 'articulos', loadComponent: () => import('./articles/article-list.component').then(module => module.ArticleListComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } },
  { path: 'articulos/nuevo', loadComponent: () => import('./articles/article-form.component').then(module => module.ArticleFormComponent), canActivate: [permissionGuard], data: { permission: 'MasterData.Manage' } },
  { path: 'articulos/:id/movimientos', loadComponent: () => import('./articles/article-history.component').then(module => module.ArticleHistoryComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } },
  { path: 'articulos/:id', loadComponent: () => import('./articles/article-form.component').then(module => module.ArticleFormComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } },
  { path: 'familias', loadComponent: () => import('./families/family-list.component').then(module => module.FamilyListComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } },
  { path: 'familias/nuevo', loadComponent: () => import('./families/family-form.component').then(module => module.FamilyFormComponent), canActivate: [permissionGuard], data: { permission: 'MasterData.Manage' } },
  { path: 'familias/:id', loadComponent: () => import('./families/family-form.component').then(module => module.FamilyFormComponent), canActivate: [permissionGuard], data: { permission: 'Inventory.Read' } }
];
