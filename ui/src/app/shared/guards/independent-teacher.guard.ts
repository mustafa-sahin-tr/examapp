import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';

/**
 * Issue #384: yalnız bağımsız öğretmenlere ait sayfaları (ör. özel ders profili) bağımsız OLMADIĞI bilinen öğretmene
 * kapatır ve panoya yönlendirir. Kural backend'le aynıdır: profildeki `teacher.isIndependentTutor`. Bayrak bilinmiyorsa
 * (login yanıtında yok) profil bir kez yenilenir; yine bilinmiyorsa ya da yenileme başarısızsa geçer — asıl kapı
 * backend'dir. Teacher rolü olmayanları etkilemez; authGuard + roleGuard'dan SONRA kullanılır.
 */
export const independentTeacherGuard: CanActivateFn = (): boolean | UrlTree | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.hasRealmRole('Teacher')) {
    return true;
  }

  const decide = (): boolean | UrlTree =>
    AuthService.isIndependentTutorOf(auth.user()) === false ? router.createUrlTree(['/dashboard']) : true;

  if (AuthService.isIndependentTutorOf(auth.user()) !== null) {
    return decide();
  }
  return auth.refreshProfile().pipe(
    map(() => decide()),
    catchError(() => of(true))
  );
};
