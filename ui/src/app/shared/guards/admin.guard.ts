import { CanActivateFn } from '@angular/router';
import { roleGuard } from './role.guard';

/**
 * Allows the route only for holders of the Keycloak realm role "Admin".
 * Pair with authGuard, which handles the unauthenticated case.
 */
export const adminGuard: CanActivateFn = roleGuard('Admin');
