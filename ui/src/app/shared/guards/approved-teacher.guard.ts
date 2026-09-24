import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { TEACHER_APPROVAL_PENDING_URL, teacherAccountApprovalOf } from '../../models/teacher-approval.model';

/**
 * Issue #287: öğretmen hesabı admin tarafından onaylanmamış Teacher'ı öğretmen özelliklerinden uzak tutar ve
 * başvuru durumu sayfasına yönlendirir. Teacher rolü olmayanlar (öğrenci, admin, veli) hiç etkilenmez.
 * authGuard (+ gerekiyorsa roleGuard) ile birlikte, onlardan SONRA kullanılır.
 *
 * Önbellekteki profil onay bilgisini taşımıyorsa (login/exchange yanıtı) profil bir kez yenilenir; yenileme
 * başarısız olursa erişim verilir — asıl kapı backend'dir, 403 `TeacherNotApproved` interceptor'da yakalanır.
 */
export const approvedTeacherGuard: CanActivateFn = (): boolean | UrlTree | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.hasRealmRole('Teacher')) {
    return true;
  }

  const decide = (): boolean | UrlTree =>
    auth.isUnapprovedTeacher() ? router.parseUrl(TEACHER_APPROVAL_PENDING_URL) : true;

  if (auth.isUnapprovedTeacher() || teacherAccountApprovalOf(auth.user()) !== null) {
    return decide();
  }

  return auth.refreshProfile().pipe(
    map(() => decide()),
    catchError(() => of(true))
  );
};
