import { Routes } from '@angular/router';
import { permissionGuard } from './core/session/permission.guard';

/**
 * Áreas de Persona B (contracts/ui.md). Una sola fuente para la ruta diferida, el permiso
 * que la protege y la entrada de navegación del layout común. La pantalla de entrada de
 * cada área exige el permiso de api.md que necesita su tarea principal; las subrutas de
 * mutación añadirán el suyo en cada spec. El backend sigue aplicando los permisos reales.
 */
export interface OperationArea {
  readonly path: string;
  readonly label: string;
  readonly story: string;
  readonly permission: string;
  readonly loadChildren: () => Promise<Routes>;
}

export const OPERATION_AREAS: readonly OperationArea[] = [
  { path: 'ubicaciones', label: 'Ubicaciones', story: 'HU-02', permission: 'Inventory.Read',
    loadChildren: () => import('./features/locations/locations.routes').then(module => module.LOCATIONS_ROUTES) },
  { path: 'existencias', label: 'Existencias', story: 'HU-05', permission: 'Inventory.Read',
    loadChildren: () => import('./features/stock/stock.routes').then(module => module.STOCK_ROUTES) },
  // HU-03 (D-14): consultar salidas solo exige Inventory.Read (US4.3); las acciones piden Shipping.Record.
  { path: 'salidas', label: 'Salidas', story: 'HU-03', permission: 'Inventory.Read',
    loadChildren: () => import('./features/shipping/shipping.routes').then(module => module.SHIPPING_ROUTES) },
  { path: 'inventarios', label: 'Inventarios', story: 'HU-04', permission: 'Counts.Record',
    loadChildren: () => import('./features/cycle-counts/cycle-counts.routes').then(module => module.CYCLE_COUNTS_ROUTES) }
];

export const operationAreaRoutes: Routes = OPERATION_AREAS.map(area => ({
  path: area.path,
  canActivate: [permissionGuard],
  data: { permission: area.permission },
  loadChildren: area.loadChildren
}));
