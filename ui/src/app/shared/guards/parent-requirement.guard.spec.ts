import type { Mock } from "vitest";
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';

import { parentRequirementGuard } from './parent-requirement.guard';
import { AuthService } from '../../services/auth.service';
import { ParentStatusStore } from '../../services/parent-status.store';
import { StudentParentService } from '../../services/student-parent.service';
import { ParentStatus } from '../../models/parent-status.model';
import { parentStatusOf } from '../testing/parent-status-testing';

/** Issue #441: süresi dolmuş öğrenci yalnız "veli bilgisi ekle" ekranını görür. */
describe('parentRequirementGuard (issue #441)', () => {
    let store: ParentStatusStore;
    let ensureLoaded: Mock;

    function setup(roles: string[], serverStatus: ParentStatus | null): void {
        ensureLoaded = vi.fn().mockName('ensureLoaded').mockImplementation(() => {
            if (serverStatus)
                store.set(serverStatus);
            return of(serverStatus);
        });
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: { getRealmRoles: () => roles } },
                { provide: StudentParentService, useValue: { ensureLoaded } },
            ],
        });
        store = TestBed.inject(ParentStatusStore);
    }

    async function run(url: string): Promise<string | boolean> {
        const result = TestBed.runInInjectionContext(() => parentRequirementGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
        const value = isObservable(result)
            ? await firstValueFrom(result as Observable<boolean | UrlTree>)
            : await Promise.resolve(result as boolean | UrlTree);
        return value instanceof UrlTree ? TestBed.inject(Router).serializeUrl(value) : (value as boolean);
    }

    it('nonStudent_PassesWithoutLoadingStatus', async () => {
        setup(['Teacher'], null);
        expect(await run('/tests')).toBe(true);
        expect(ensureLoaded).not.toHaveBeenCalled();
    });

    it('studentWithAdultRole_Passes', async () => {
        setup(['Student', 'Parent'], parentStatusOf({ mode: 'Expired' }));
        expect(await run('/dashboard')).toBe(true);
        expect(ensureLoaded).not.toHaveBeenCalled();
    });

    it('expiredStudent_RedirectedToParentRequired', async () => {
        setup(['Student'], parentStatusOf({ mode: 'Expired', daysLeft: 0, isFinalWarning: true }));
        expect(await run('/dashboard')).toBe('/parent-required');
        expect(await run('/my-bookings?x=1')).toBe('/parent-required');
    });

    it('expiredStudent_ParentRequiredPageItselfAllowed', async () => {
        setup(['Student'], parentStatusOf({ mode: 'Expired' }));
        expect(await run('/parent-required')).toBe(true);
        expect(ensureLoaded).not.toHaveBeenCalled();
    });

    for (const mode of ['Ok', 'Grace', 'AwaitingActivation'] as const) {
        it(`student_${mode}_Passes`, async () => {
            setup(['Student'], parentStatusOf({ mode }));
            expect(await run('/dashboard')).toBe(true);
        });
    }

    it('statusUnavailable_PassesAndLeavesDecisionToServer', async () => {
        setup(['Student'], null);
        expect(await run('/dashboard')).toBe(true);
    });
});
