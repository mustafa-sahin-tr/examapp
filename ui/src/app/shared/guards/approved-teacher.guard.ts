import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { TEACHER_APPROVAL_PENDING_URL, teacherAccountApprovalOf } from '../../models/teacher-approval.model';

/**
 * Issue #287: öğretmen hesabı admin tarafından onaylanmamış Teacher'ı öğretmen özelliklerinden uzak tutar ve
 * başvuru durumu sayfasına yönlendirir. Teacher rolü olmayanlar (öğrenci, veli) ve Admin/SuperAdmin (backend'de muaf)
 * hiç etkilenmez. authGuard (+ gerekiyorsa roleGuard) ile birlikte, onlardan SONRA kullanılır; resolver'lar guard'dan
 * sonra çalıştığı için yönlendirilen öğretmen için öğretmen uçları hiç çağrılmaz.
 *
 * Yalnız onaylı olduğu BİLİNEN öğretmen doğrudan geçer. Onaysız ya da bilinmeyen durumda profil (tek uçuş) bir kez
 * yenilenir — arada onaylanan öğretmen durum sayfasına sekmez. Yenileme başarısızsa önbellekteki karar geçerlidir:
 * onaysız → durum sayfası, bilinmiyor → geçer (asıl kapı backend; 403 `TeacherNotApproved` interceptor'da yakalanır).
 */
export const approvedTeacherGuard: CanActivateFn = (): boolean | UrlTree | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.hasRealmRole('Teacher') || auth.isTeacherApprovalExempt()) {
    return true;
  }

  const decide = (): boolean | UrlTree =>
    auth.isUnapprovedTeacher() ? router.parseUrl(TEACHER_APPROVAL_PENDING_URL) : true;

  if (!auth.isUnapprovedTeacher() && teacherAccountApprovalOf(auth.user()) === true) {
    return true;
  }

  return auth.refreshProfile().pipe(
    map(() => decide()),
    catchError(() => of(decide()))
  );
};
