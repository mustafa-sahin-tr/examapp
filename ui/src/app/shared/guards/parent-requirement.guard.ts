import { inject } from '@angular/core';
import { CanActivateChildFn, Router, UrlTree } from '@angular/router';
import { Observable, map } from 'rxjs';

import { PARENT_REQUIRED_URL, isSubjectToParentRule } from '../../models/parent-status.model';
import { AuthService } from '../../services/auth.service';
import { ParentStatusStore } from '../../services/parent-status.store';
import { StudentParentService } from '../../services/student-parent.service';

/**
 * Issue #441: hoşgörü süresi dolmuş (`Expired`) öğrenci ana uygulamada yalnız "veli bilgisi ekle" ekranını görür. Layout
 * route'unun `canActivateChild`'ı olarak tüm korumalı sayfalara uygulanır. Veli kuralına tabi olmayanlar (öğretmen, veli, admin,
 * oturumsuz) hiç beklemez. Durum bu oturumda henüz yüklenmediyse bir kez çekilir (ilk çağrı hoşgörü sayacını da başlatır);
 * çekilemezse geçer — asıl kapı sunucudaki 403 `ParentRequired` (interceptor ekrana yönlendirir).
 */
export const parentRequirementGuard: CanActivateChildFn = (_route, state): boolean | UrlTree | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  if (!isSubjectToParentRule(auth.getRealmRoles())) {
    return true;
  }
  const path = state.url.split(/[?#]/)[0];
  if (path === PARENT_REQUIRED_URL) {
    return true;
  }
  const router = inject(Router);
  const store = inject(ParentStatusStore);
  return inject(StudentParentService)
    .ensureLoaded()
    .pipe(map(() => (store.expired() ? router.parseUrl(PARENT_REQUIRED_URL) : true)));
};
