import { inject } from '@angular/core';
import { CanActivateFn, Router, } from '@angular/router';
import { map } from 'rxjs/operators';
import { AuthService } from '../../services/auth.service';


export const authGuard: CanActivateFn = () => {
    const router = inject(Router);
    const authService = inject(AuthService);

    return authService.isAuthenticated().pipe(
        map((isAuth) => isAuth || router.createUrlTree(['/login'])),
    );
}