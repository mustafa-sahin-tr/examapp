import { inject } from '@angular/core';
import { AuthService } from '../../services/auth.service';

/**
 * #875: kök adres (`/`) oturum açmış kullanıcıyı paneline, diğerlerini tanıtım sayfasına götürür.
 * Süresi dolmuş oturumda `/dashboard`'un authGuard'ı tazeler ya da girişe yönlendirir.
 */
export function rootRedirect(): string {
  return inject(AuthService).isAuthenticatedSubject.value ? '/dashboard' : '/welcome';
}
