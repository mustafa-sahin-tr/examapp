import { CanActivateFn } from '@angular/router';
import { roleGuard } from './role.guard';

/**
 * Allows the route only for holders of the Keycloak realm role "Student".
 * Pair with authGuard, which handles the unauthenticated case.
 */
export const studentGuard: CanActivateFn = roleGuard('Student');
