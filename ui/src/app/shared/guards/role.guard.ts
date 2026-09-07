import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

/**
 * Builds a route guard that allows access only to holders of at least one of the
 * given Keycloak realm roles. Pair with authGuard, which handles the
 * unauthenticated case. Redirects to /dashboard when the role check fails.
 */
export const roleGuard =
  (...roles: string[]): CanActivateFn =>
  () => {
    const router = inject(Router);
    const auth = inject(AuthService);

    if (roles.some((role) => auth.hasRealmRole(role))) {
      return true;
    }
    return router.createUrlTree(['/dashboard']);
  };
