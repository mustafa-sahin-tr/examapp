import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { defer, from, of, switchMap } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { isCrossOriginUrl, pathnameOf } from '../utils/request-url.util';

/** Kimlik gerektirmeyen, statik i18n sözlükleri (issue #180). */
const I18N_PATH_PREFIX = '/i18n/';

/**
 * Token kontrolü (token yoksa logout, süresi dolmak üzereyse refresh) YAPILMAYACAK HTTP uçları —
 * yol (pathname) tam eşleşmesiyle (issue #255).
 *
 * Eskiden liste `req.url.includes(...)` ile taranıyordu ve router yolları (`/about`, `/faq`, `/terms` …)
 * ile auth-ui sayfa yolları (`/app/logout`, `/app/exchange` …) içeriyordu. Bunlar HTTP isteği değil
 * tarayıcı navigasyonudur, interceptor'dan hiç geçmez; tek etkileri alt dizeyi içeren API isteklerinin
 * Bearer'sız gitmesiydi. Kaldırıldılar; yalnızca döngü/yanlış logout üreten kimlik uçları kalır.
 */
const AUTH_EXCLUDED_PATHS: ReadonlySet<string> = new Set([
  // `refreshToken()` bu ucu çağırır; burada yeniden refresh tetiklenirse tek-uçuş observable'ı
  // kendini bekler (kilitlenme), token yoksa da gereksiz logout olur. Kimlik refresh cookie'sidir.
  '/api/auth/refresh-token',
  // Giriş/kod takası oturum AÇMADAN önce çağrılır: token olmaması normaldir, logout tetiklenmemeli.
  '/api/auth/login',
  '/api/auth/exchange',
  // `logout()` yerel oturumu önce temizler ve Authorization başlığını kendisi ekler; interceptor
  // token bulamayıp yeniden `logout()` çağırırsa sonsuz döngü olur.
  '/api/exam/auth/logout',
]);

function isAuthExcluded(req: HttpRequest<unknown>): boolean {
  return AUTH_EXCLUDED_PATHS.has(pathnameOf(req.url));
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Dış origin (Jitsi, MinIO presigned URL, üçüncü taraf): Bearer token ve çerez gönderilmez,
  // oturum kontrolü de yapılmaz — istek olduğu gibi geçer.
  if (isCrossOriginUrl(req.url)) {
    return next(req);
  }

  // Sözlük dosyaları anonim ve statiktir: oturumsuz ziyaretçide `logout()` tetiklememeli,
  // Bearer token da eklenmemelidir. `withCredentials` de açılmaz — statik dosyaya çerez gereksiz.
  if (pathnameOf(req.url).startsWith(I18N_PATH_PREFIX)) {
    return next(req);
  }

  if (isAuthExcluded(req)) {
    // Token kontrolü/refresh yapılmaz; refresh cookie'si için çerezler gönderilir.
    return next(req.clone({ withCredentials: true }));
  }

  const authService = inject(AuthService);

  return defer(() => {
    const token = localStorage.getItem('auth_token');

    if (!token) {
      authService.logout();
      return next(req.clone({ withCredentials: true }));
    }

    if (authService.isExpiringSoon(token)) {
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

    return of(
      req.clone({
        withCredentials: true,
        setHeaders: { Authorization: `Bearer ${token}` },
      })
    ).pipe(switchMap(next));
  });
};
