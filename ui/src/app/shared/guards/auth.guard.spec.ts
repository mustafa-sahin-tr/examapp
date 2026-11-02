import { DOCUMENT, EnvironmentInjector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { authGuard } from './auth.guard';

/** Issue #710: korumalı adrese oturumsuz gelen kullanıcı girişe gider ve geri döner. */
describe('authGuard (issue #710)', () => {
    let assign: ReturnType<typeof vi.fn>;

    async function run(isAuth: boolean, url: string): Promise<unknown> {
        assign = vi.fn();
        TestBed.configureTestingModule({
            providers: [
                { provide: AuthService, useValue: { isAuthenticated: () => of(isAuth) } },
                { provide: DOCUMENT, useValue: { location: { assign }, querySelectorAll: () => [] } },
            ],
        });
        const result = runInInjectionContext(TestBed.inject(EnvironmentInjector), () =>
            authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
        );
        return isObservable(result) ? firstValueFrom(result) : result;
    }

    it('signedIn_AllowsWithoutRedirect', async () => {
        expect(await run(true, '/test/4')).toBe(true);
        expect(assign).not.toHaveBeenCalled();
    });

    it('anonymous_GoesToLoginWithEncodedReturnUrl', async () => {
        expect(await run(false, '/test/4?tab=a&b=1')).toBe(false);
        expect(assign).toHaveBeenCalledExactlyOnceWith('/app/login?returnUrl=%2Ftest%2F4%3Ftab%3Da%26b%3D1');
    });
});
