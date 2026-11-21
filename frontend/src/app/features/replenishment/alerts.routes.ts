import { Routes } from '@angular/router';

export const ALERTS_ROUTES: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./alerts.page').then(module => module.AlertsPage) }
];
