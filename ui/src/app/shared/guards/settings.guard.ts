import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

/** Öğrencinin ayarlar sayfası (öğrenciye özel sekmeler taşır, issue #373). */
export const STUDENT_SETTINGS_URL = '/student-profile';
/** Öğretmen ve admin için rol bazlı ayarlar sayfası (issue #417). */
export const SETTINGS_URL = '/settings';

/**
 * Issue #417: kullanıcının Ayarlar sayfası — guard ve menü aynı kuralı kullanır. Teacher veya Admin rolü varsa
 * (öğrenci rolüyle birlikte olsa da) `/settings`; yalnız öğrenciyse `/student-profile`; bilinen rol yoksa null.
 */
export function settingsUrlFor(auth: Pick<AuthService, 'hasRealmRole'>): string | null {
  if (auth.hasRealmRole('Teacher') || auth.hasRealmRole('Admin')) {
    return SETTINGS_URL;
  }
  return auth.hasRealmRole('Student') ? STUDENT_SETTINGS_URL : null;
}

/**
 * Issue #417: `/settings` yalnız Teacher/Admin realm rolüne açıktır. Yalnız öğrenci kendi ayarlar sayfasına
 * (`/student-profile`), rolü olmayan kullanıcı panoya yönlendirilir. Onay bekleyen öğretmen de girebilir —
 * sayfa yalnız dil/hesap bağlantısı ve salt-okunur bilgi gösterir. authGuard ile birlikte kullanılır.
 */
export const settingsGuard: CanActivateFn = () => {
  const router = inject(Router);
  const target = settingsUrlFor(inject(AuthService));

  if (target === SETTINGS_URL) {
    return true;
  }
  return router.createUrlTree([target ?? '/dashboard']);
};
