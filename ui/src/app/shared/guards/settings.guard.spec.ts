import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { settingsGuard, settingsUrlFor } from './settings.guard';
import { AuthService } from '../../services/auth.service';

/** Issue #417: /settings yalnız Teacher/Admin; öğrenci kendi Ayarlar sayfasına yönlenir. */
describe('settingsGuard (issue #417)', () => {
  function run(roles: string[]): string | boolean {
    const authStub: Partial<AuthService> = { hasRealmRole: (role: string) => roles.includes(role) };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: authStub }],
    });
    const result = TestBed.runInInjectionContext(() =>
      settingsGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    );
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : (result as boolean);
  }

  for (const roles of [['Teacher'], ['Admin'], ['Admin', 'Teacher'], ['Student', 'Teacher']]) {
    it(`${roles.join('+')}_Allowed`, () => {
      expect(run(roles)).toBeTrue();
    });
  }

  it('Student_RedirectsToStudentProfile', () => {
    expect(run(['Student'])).toBe('/student-profile');
  });

  it('NoKnownRole_RedirectsToDashboard', () => {
    expect(run([])).toBe('/dashboard');
  });

  it('settingsUrlFor_SharedRuleForGuardAndMenu', () => {
    const urlFor = (roles: string[]) => settingsUrlFor({ hasRealmRole: (r: string) => roles.includes(r) });
    expect(urlFor(['Teacher'])).toBe('/settings');
    expect(urlFor(['Admin'])).toBe('/settings');
    expect(urlFor(['Student', 'Teacher'])).toBe('/settings');
    expect(urlFor(['Student', 'Admin'])).toBe('/settings');
    expect(urlFor(['Student'])).toBe('/student-profile');
    expect(urlFor([])).toBeNull();
  });
});
