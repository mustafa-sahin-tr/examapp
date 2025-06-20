// import { HttpInterceptorFn } from '@angular/common/http';
// import { inject } from '@angular/core';
// import { from, firstValueFrom } from 'rxjs';
// import { AuthService } from '../../services/auth.service';

// export const authInterceptor: HttpInterceptorFn = (req, next) => {
//   const authService = inject(AuthService);

//   /**from(...asyncFn()) ile sardı ve içeride firstValueFrom() ile Observable'ı Promise haline getirdi, sonra from(...) ile tekrar Observable */

//   return from(
//     (async () => {
//       let token = localStorage.getItem('auth_token');
//       console.log('Token:', token);
//       if (token && authService.isExpiringSoon(token)) {
//         try {
//           token = await authService.refreshToken();
//           if (token) localStorage.setItem('auth_token', token);
//         } catch {
//           token = null;
//         }
//       }

//       const cloned = req.clone({
//         withCredentials: true,
//         setHeaders: token
//           ? {
//               Authorization: `Bearer ${token}`,
//             }
//           : {},
//       });

//       return firstValueFrom(next(cloned));
//     })()
//   );
// };
// ----------
// import { HttpInterceptorFn } from '@angular/common/http';

// export const authInterceptor: HttpInterceptorFn = (req, next) => {
//   const token = localStorage.getItem('auth_token');

//   if (token) {
//     const clonedReq = req.clone({
//       setHeaders: {
//         Authorization: `Bearer ${token}`,
//       },
//     });
//     return next(clonedReq);
//   }

//   return next(req);
// };
// --------------

import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { defer, from, of, switchMap } from 'rxjs';
import { AuthService } from '../../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const excludedUrls = ['/api/auth/logout', '/api/auth/exchange', '/api/auth/refresh-token'];

  if (excludedUrls.some((url) => req.url.includes(url))) {
    // Bu URL’lerde hiçbir token kontrolü, refresh işlemi yapma
    return next(req.clone({ withCredentials: true }));
  }

  const authService = inject(AuthService);

  return defer(() => {
    let token = localStorage.getItem('auth_token');

    if (token && authService.isExpiringSoon(token)) {
      return from(authService.refreshToken()).pipe(
        switchMap((newToken) => {
          if (newToken) {
            localStorage.setItem('auth_token', newToken);
            token = newToken;
          }

          const cloned = req.clone({
            withCredentials: true,
            setHeaders: {
              Authorization: `Bearer ${token}`,
            },
          });

          return next(cloned);
        })
      );
    }

    const cloned = req.clone({
      withCredentials: true,
      setHeaders: token ? { Authorization: `Bearer ${token}` } : {},
    });

    return of(cloned).pipe(switchMap(next));
  });
};

/*
Hiç Promise yok → Observable zinciri bozulmaz.

Angular interceptor pipeline’ı tam uyumlu çalışır.

Navigasyon/sayfa geçişlerinde istekler iptal edilmez.

Daha az yan etki ve daha temiz debugging imkanı sağlar.*/
