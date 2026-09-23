import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

/**
 * Kimlik bilgisi doğrulayan uçlar: 401 burada "oturum düştü" değil "bilgiler/code geçersiz"
 * anlamına gelir (issue #231). Hatayı çağıran bileşen kendi mesajıyla ele alır; interceptor
 * yönlendirme yapmaz.
 */
const CREDENTIAL_ENDPOINTS = ['/api/auth/login', '/api/auth/exchange'];

function isCredentialEndpoint(url: string): boolean {
  const path = url.split('?')[0];
  return CREDENTIAL_ENDPOINTS.some((endpoint) => path.endsWith(endpoint));
}

export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !isCredentialEndpoint(req.url)) {
        // Oturum geçersiz — kullanıcıyı login sayfasına yönlendir
        router.navigate(['/login']);
      }
      return throwError(() => error);
    })
  );
};
