import { EnvironmentInjector, inject, runInInjectionContext } from '@angular/core';
import { CanActivateFn, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { deniedRedirect } from './denied-redirect';

/**
 * Issue #1277: okul yöneticisi sayfaları. Profil okul yöneticisi demiyorsa bir kez yenilenir (yetki az önce verilmiş
 * olabilir); yine değilse panoya yönlendirir. Asıl kapı backend'dir (`api/school-admin/*` → 403).
 */
export const schoolAdminGuard: CanActivateFn = (): boolean | UrlTree | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  const injector = inject(EnvironmentInjector);
  const denied = () => runInInjectionContext(injector, () => deniedRedirect('common.routeDenied.schoolAdmin'));
  if (auth.isSchoolAdmin()) {
    return true;
  }
  return auth.refreshProfile().pipe(
    map(() => (auth.isSchoolAdmin() ? true : denied())),
    catchError(() => of(denied())),
  );
};
