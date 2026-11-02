import { DOCUMENT, inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { map, take } from 'rxjs/operators';
import { AuthService } from '../../services/auth.service';

/**
 * Issue #710: oturumsuz kullanıcı girişe gider, sonra istediği adrese döner. Giriş auth-ui'da (`/app/login`);
 * `returnUrl` orada `safeRedirectTarget` ile yalnız aynı origin göreli yol olarak kabul edilir.
 */
export const authGuard: CanActivateFn = (_route, state) => {
    const authService = inject(AuthService);
    const document = inject(DOCUMENT);

    return authService.isAuthenticated().pipe(
        take(1),
        map((isAuth) => {
            if (!isAuth) {
                document.location.assign(`/app/login?returnUrl=${encodeURIComponent(state.url)}`);
            }
            return isAuth;
        }),
    );
};
