import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { parentGuard, parentHomeRedirectGuard } from './parent.guard';
import { AuthService } from '../../services/auth.service';

/** Issue #420: /parent yalnız Parent rolüne açık; velinin /dashboard girişi /parent'a düşer. */
describe('parent guards (issue #420)', () => {
  function run(guard: typeof parentGuard, roles: string[]): string | boolean {
    const authStub: Partial<AuthService> = { hasRealmRole: (role: string) => roles.includes(role) };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: authStub }],
    });
    const result = TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    );
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : (result as boolean);
  }

  it('parentGuard_Parent_Allowed', () => {
    expect(run(parentGuard, ['Parent'])).toBeTrue();
  });

  for (const roles of [['Student'], ['Teacher'], ['Admin'], []]) {
    it(`parentGuard_${roles.join('+') || 'NoRole'}_RedirectsToDashboard`, () => {
      expect(run(parentGuard, roles)).toBe('/dashboard');
    });
  }

  it('parentHomeRedirectGuard_ParentOnly_RedirectsToParentHome', () => {
    expect(run(parentHomeRedirectGuard, ['Parent'])).toBe('/parent');
  });

  for (const roles of [['Student'], ['Teacher'], ['Parent', 'Student'], ['Parent', 'Teacher'], []]) {
    it(`parentHomeRedirectGuard_${roles.join('+') || 'NoRole'}_StaysOnDashboard`, () => {
      expect(run(parentHomeRedirectGuard, roles)).toBeTrue();
    });
  }
});
