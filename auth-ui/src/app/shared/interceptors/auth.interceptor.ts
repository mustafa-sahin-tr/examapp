import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { defer, from, switchMap } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { isCrossOriginUrl, pathnameOf } from '../utils/request-url.util';

/**
 * Token kontrolü (süresi dolmak üzereyse refresh) ve Bearer ekleme YAPILMAYACAK HTTP uçları —
 * yol (pathname) tam eşleşmesiyle (issue #255). Eskiden `req.url.includes(...)` ile taranıyordu;
 * alt dizeyi içeren başka bir API yolu da yanlışlıkla muaf tutulurdu.
 */
const AUTH_EXCLUDED_PATHS: ReadonlySet<string> = new Set([
  // `refreshToken()` bu ucu çağırır; burada yeniden refresh tetiklenirse sonsuz özyineleme olur.
  // Kimlik, refresh cookie'sidir.
  '/api/auth/refresh-token',
  // Anonim kimlik uçları: localStorage'da kalmış süresi dolmuş bir token burada refresh denemesine,
  // başarısızlıkta da giriş/kayıt/callback akışının ortasında `/login`'e yönlendirmeye yol açardı.
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/exchange',
  // `logout()` token'ı önce temizler ve Authorization başlığını kendisi taşır.
  '/api/exam/auth/logout',
]);

function isAuthExcluded(req: HttpRequest<unknown>): boolean {
  return AUTH_EXCLUDED_PATHS.has(pathnameOf(req.url));
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Dış origin: Bearer token ve çerez gönderilmez, refresh denenmez — istek olduğu gibi geçer.
  if (isCrossOriginUrl(req.url)) {
    return next(req);
  }

  if (isAuthExcluded(req)) {
    // Refresh cookie'si için çerezler gönderilir; token eklenmez.
    return next(req.clone({ withCredentials: true }));
  }

  const authService = inject(AuthService);

  return defer(() => {
    const token = localStorage.getItem('auth_token');

    if (token && authService.isExpiringSoon(token)) {
      return from(authService.refreshToken()).pipe(
        switchMap((newToken) => {
          const effectiveToken = newToken || token;
          if (newToken) {
            localStorage.setItem('auth_token', newToken);
          }
          return next(
            req.clone({
              withCredentials: true,
              setHeaders: { Authorization: `Bearer ${effectiveToken}` },
            })
          );
        })
      );
    }

    return next(
      req.clone({
        withCredentials: true,
        setHeaders: token ? { Authorization: `Bearer ${token}` } : {},
      })
    );
  });
};
