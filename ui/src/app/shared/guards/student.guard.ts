import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

/**
 * Allows the route only for holders of the Keycloak realm role "Student".
 * Pair with authGuard, which handles the unauthenticated case.
 */
export const studentGuard: CanActivateFn = () => {
  const router = inject(Router);
  const authService = inject(AuthService);

  if (authService.hasRealmRole('Student')) {
    return true;
  }
  return router.createUrlTree(['/dashboard']);
};
