import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

/** #874: yalnız admin olan hesabın ana sayfası. */
export const ADMIN_HOME_URL = '/admin/dashboard';

/**
 * #874: /dashboard öğrenci panelini çizer; yalnız admin rolü olan hesap orada öğrenci uçlarından 403 alıyordu.
 * Öğrenci ya da öğretmen rolü de taşıyan hesap kendi panelinde kalır. Koşul adminGuard ile aynı rol ('Admin'),
 * böylece yönlendirme döngüye girmez.
 */
export const adminHomeRedirectGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const isAdminOnly = auth.hasRealmRole('Admin') && !auth.hasRealmRole('Student') && !auth.hasRealmRole('Teacher');
  return isAdminOnly ? inject(Router).createUrlTree([ADMIN_HOME_URL]) : true;
};
