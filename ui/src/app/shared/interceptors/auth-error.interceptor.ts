import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError, switchMap, of, from } from 'rxjs';
import { AuthService } from '../../services/auth.service';

export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error) => {
      if (error.status === 401) {
        // Refresh token URL'leri için tekrar refresh denemeyelim (sonsuz döngü)
        const excludedUrls = ['/app/logout', '/app/exchange', '/app/refresh-token', '/api/auth/refresh-token'];

        if (excludedUrls.some((url) => req.url.includes(url))) {
          // Refresh token endpoint'inde 401 aldıysak direkt login'e yönlendir
          authService.clearLocalStorage();
          return throwError(() => error);
        }

        // Normal bir endpoint'te 401 aldık, refresh token deneyelim
        return from(authService.refreshToken()).pipe(
          switchMap((newToken) => {
            if (newToken) {
              // Refresh başarılı, yeni token ile original request'i tekrarla
              localStorage.setItem('auth_token', newToken);

              const clonedReq = req.clone({
                withCredentials: true,
                setHeaders: {
                  Authorization: `Bearer ${newToken}`,
                },
              });

              return next(clonedReq);
            } else {
              // Refresh başarısız, login sayfasına yönlendir
              authService.clearLocalStorage();
              return throwError(() => error);
            }
          }),
          catchError((refreshError) => {
            // Refresh token sırasında hata, login sayfasına yönlendir
            console.error('Refresh token failed:', refreshError);
            authService.clearLocalStorage();
            return throwError(() => error);
          })
        );
      }

      return throwError(() => error);
    })
  );
};
