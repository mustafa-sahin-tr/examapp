import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { roleGuard } from './role.guard';

/** Veli paneli rotası (issue #420). */
export const PARENT_HOME_URL = '/parent';

/**
 * Allows the route only for holders of the Keycloak realm role "Parent" (issue #420).
 * Pair with authGuard, which handles the unauthenticated case.
 */
export const parentGuard: CanActivateFn = roleGuard('Parent');

/**
 * Issue #420: velinin login sonrası / genel `/dashboard` girişi veli paneline düşer — öğrenci dashboard'u (öğrenci API'leri)
 * veliye hiç açılmaz. Öğrenci/öğretmen rolü de taşıyan hesap (#419 rol dışlaması nedeniyle beklenmez) mevcut dashboard'da
 * kalır. `/dashboard` rotasında authGuard'dan SONRA çalışır; roleGuard'ın `/dashboard` geri dönüşüyle döngü kurmaz
 * (yalnızca Parent rolünde yönlendirir, `/parent` ise yalnızca Parent rolüne açıktır).
 */
export const parentHomeRedirectGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const isParentOnly =
    auth.hasRealmRole('Parent') && !auth.hasRealmRole('Student') && !auth.hasRealmRole('Teacher');
  return isParentOnly ? inject(Router).createUrlTree([PARENT_HOME_URL]) : true;
};
