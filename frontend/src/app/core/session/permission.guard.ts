import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session.service';

export const permissionGuard: CanActivateFn = async (route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  if (session.state() === 'loading' || session.state() === 'unavailable') await session.refresh();
  if (!session.user()) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  return session.has(route.data['permission'] as string) || router.createUrlTree(['/sin-permiso']);
};
