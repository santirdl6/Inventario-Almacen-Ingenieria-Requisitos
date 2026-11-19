import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/session/permission.guard';

/**
 * HU-02 · Persona B. Pantallas de specs/003-gestionar-ubicaciones/contracts/locations-ui.md.
 * El área ya exige Inventory.Read (operation-areas.ts); alta y edición exigen además
 * Locations.Manage con el permissionGuard de A. El servidor sigue decidiendo (FR-010).
 */
export const LOCATIONS_ROUTES: Routes = [
  {
    path: '',
    title: 'Ubicaciones',
    loadComponent: () => import('./list/location-list.page').then(m => m.LocationListPage)
  },
  {
    path: 'nueva',
    title: 'Nueva ubicación',
    canActivate: [permissionGuard],
    data: { permission: 'Locations.Manage' },
    loadComponent: () => import('./form/location-form.page').then(m => m.LocationFormPage)
  },
  {
    path: 'articulo',
    title: 'Localizar un artículo',
    loadComponent: () => import('./article/article-search').then(m => m.ArticleSearch)
  },
  {
    path: 'articulo/:articleId',
    title: 'Ubicaciones del artículo',
    loadComponent: () => import('./article/article-locations.page').then(m => m.ArticleLocationsPage)
  }
];
