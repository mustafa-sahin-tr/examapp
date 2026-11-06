import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { AuthService } from '../../services/auth.service';
import { roleGuard } from './role.guard';

describe('roleGuard', () => {
    const snackBar = { open: vi.fn().mockName('MatSnackBar.open') };

    function run(roles: string[], ...required: string[]): unknown {
        snackBar.open.mockClear();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: { hasRealmRole: (role: string) => roles.includes(role) } },
                { provide: MatSnackBar, useValue: snackBar },
                { provide: TranslocoService, useValue: { translate: (key: string) => key } },
            ],
        });
        return TestBed.runInInjectionContext(() => roleGuard(...required)({} as never, {} as never));
    }

    it('lets a holder of the role through without a notice', () => {
        expect(run(['Teacher'], 'Teacher')).toBe(true);
        expect(snackBar.open).not.toHaveBeenCalled();
    });

    it('sends others to the dashboard and says why (#877)', () => {
        const result = run(['Student'], 'Teacher');
        expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/dashboard');
        expect(snackBar.open).toHaveBeenCalledWith('common.routeDenied.role', undefined, { duration: 5000 });
    });
});
