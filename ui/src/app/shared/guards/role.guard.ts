import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { deniedRedirect } from './denied-redirect';

/**
 * Builds a route guard that allows access only to holders of at least one of the
 * given Keycloak realm roles. Pair with authGuard, which handles the
 * unauthenticated case. Redirects to /dashboard when the role check fails and says why (#877).
 */
export const roleGuard =
  (...roles: string[]): CanActivateFn =>
  () => {
    const auth = inject(AuthService);

    if (roles.some((role) => auth.hasRealmRole(role))) {
      return true;
    }
    return deniedRedirect('common.routeDenied.role');
  };
