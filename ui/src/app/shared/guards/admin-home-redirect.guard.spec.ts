import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';

import { AuthService } from '../../services/auth.service';
import { ADMIN_HOME_URL, adminHomeRedirectGuard } from './admin-home-redirect.guard';

describe('adminHomeRedirectGuard (#874)', () => {
  function run(roles: string[]): boolean | UrlTree {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { hasRealmRole: (role: string) => roles.includes(role) } },
      ],
    });
    return TestBed.runInInjectionContext(() => adminHomeRedirectGuard({} as never, {} as never)) as boolean | UrlTree;
  }

  it('sends an admin-only account to the admin dashboard', () => {
    const result = run(['Admin']);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(ADMIN_HOME_URL);
  });

  it('keeps an admin who is also a teacher on their dashboard', () => {
    expect(run(['Admin', 'Teacher'])).toBe(true);
  });

  it('lets students through', () => {
    expect(run(['Student'])).toBe(true);
  });
});
