import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError, switchMap } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { isCrossOriginUrl, pathnameOf } from '../utils/request-url.util';

/**
 * 401'de token yenilemesi DENENMEYECEK uçlar — yol (pathname) tam eşleşmesiyle (issue #241).
 *
 * Eskiden liste `req.url.includes(...)` ile taranıyordu ve landing sayfaları eklenirken (4241f6c)
 * router yolları (`/`, `/about`, `/pricing` …) HTTP uçlarıymış gibi listeye kopyalanmıştı; `'/'`
 * her URL'de geçtiği için 401 akışı hiç çalışmıyordu. Public sayfalar korumalı API çağırmaz,
 * dolayısıyla o girdiler kaldırıldı; burada yalnızca kimlik uçları kalır.
 */
const REFRESH_EXCLUDED_PATHS: ReadonlySet<string> = new Set([
  // Yenileme ucunun kendisi: 401'i yeniden refresh tetiklerse sonsuz döngü olur.
  // Oturum temizliği/yönlendirme `AuthService.refreshToken()` içinde yapılır.
  '/api/auth/refresh-token',
  // Kimlik bilgisi/kod uçları: 401 = hatalı giriş, refresh anlamsız.
  '/api/auth/login',
  '/api/auth/exchange',
  // Logout yerel oturumu zaten temizleyip login'e yönlendiriyor.
  '/api/exam/auth/logout',
]);

export function isRefreshExcluded(req: HttpRequest<unknown>): boolean {
  return REFRESH_EXCLUDED_PATHS.has(pathnameOf(req.url));
}

/**
 * İstek uygulamanın origin'i dışına mı gidiyor? Üçüncü taraf 401'i bizim oturumumuzla ilgili değildir;
 * refresh denemek ve isteği Bearer token ile tekrarlamak token'ı dış origin'e sızdırır.
 */
export function isCrossOrigin(req: HttpRequest<unknown>): boolean {
  return isCrossOriginUrl(req.url);
}

export const authErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  return next(req).pipe(
    catchError((error) => {
      if (error?.status !== 401 || isCrossOrigin(req) || isRefreshExcluded(req)) {
        return throwError(() => error);
      }

      // Normal bir uçta 401: token'ı yenile, isteği yeni token ile bir kez tekrarla.
      // Tekrar `next` ile yapıldığı için bu interceptor'dan yeniden geçmez → döngü yok.
      // Eşzamanlı 401'ler `refreshToken()` içindeki tek-uçuş (single-flight) paylaşımını kullanır.
      return authService.refreshToken().pipe(
        catchError(() => {
          // Oturum temizliği + login yönlendirmesi `refreshToken()` içinde (boş token dahil) yapıldı.
          return throwError(() => error);
        }),
        switchMap((newToken) => {
          // Boş token `refreshToken()` içinde hataya çevrilir; buraya yalnız geçerli token gelir.
          localStorage.setItem('auth_token', newToken);
          return next(
            req.clone({
              withCredentials: true,
              setHeaders: { Authorization: `Bearer ${newToken}` },
            })
          ).pipe(
            catchError((retryError) => {
              // Taze token ile yine 401: oturum sunucuda geçersiz → kapat ve login'e yönlendir.
              if (retryError?.status === 401) {
                authService.clearLocalStorage();
              }
              return throwError(() => retryError);
            })
          );
        })
      );
    })
  );
};
