import { permissionGuard } from '../../core/session/permission.guard';
import { CYCLE_COUNTS_ROUTES } from './cycle-counts.routes';

// HU-04 T014 (D-15; contracts/cycle-counts-ui.md § Rutas): sustituye el placeholder de H0.
describe('Rutas de /inventarios', () => {
  it('declara lista y nuevo conteo; «nuevo» exige Counts.Record', async () => {
    expect(CYCLE_COUNTS_ROUTES.map(route => route.path)).toEqual(expect.arrayContaining(['', 'nuevo']));
    const list = CYCLE_COUNTS_ROUTES.find(route => route.path === '')!;
    expect(((await list.loadComponent!()) as { name: string }).name).toContain('CountListPage');
    const create = CYCLE_COUNTS_ROUTES.find(route => route.path === 'nuevo')!;
    expect(create.canActivate).toEqual([permissionGuard]);
    expect(create.data).toEqual({ permission: 'Counts.Record' });
  });
});
