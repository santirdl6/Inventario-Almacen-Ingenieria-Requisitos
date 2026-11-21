import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

/**
 * HU-04 · Persona B. Pantallas de specs/006-realizar-inventarios-ciclicos/contracts/cycle-counts-ui.md
 * (D-15); sustituye el placeholder de H0. El área exige su permiso en operation-areas.ts; crear un
 * conteo exige además Counts.Record. El servidor sigue decidiendo cada permiso.
 */
export const CYCLE_COUNTS_ROUTES: Routes = [
  {
    path: '',
    title: 'Inventarios cíclicos',
    loadComponent: () => import('./count-list.page').then(module => module.CountListPage)
  },
  {
    path: 'nuevo',
    title: 'Nuevo conteo',
    canActivate: [permissionGuard],
    data: { permission: 'Counts.Record' },
    loadComponent: () => import('./count-new.page').then(module => module.CountNewPage)
  },
  {
    path: ':id',
    title: 'Conteo',
    loadComponent: () => import('./count-detail.page').then(module => module.CountDetailPage)
  }
];
