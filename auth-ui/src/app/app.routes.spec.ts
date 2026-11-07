import { ActivatedRouteSnapshot, Route } from '@angular/router';

import { routes } from './app.routes';
import { profileStepUrl } from './services/oidc-flow.service';

/** #1009: tek kayıt yolu — eski `/app/register` bağlantıları Keycloak kayıt akışına (login + intent), eski rol adımı ana uygulamaya düşer. */
describe('app routes', () => {
  const registerRoute = routes[0].children!.find((r: Route) => r.path === 'register')!;

  function redirectFor(queryParams: Record<string, string>): unknown {
    const redirect = registerRoute.redirectTo as (snapshot: Partial<ActivatedRouteSnapshot>) => string;
    return redirect({ queryParams });
  }

  it('register_HasNoOwnFormAnymore', () => {
    expect(registerRoute.component).toBeUndefined();
    expect(typeof registerRoute.redirectTo).toBe('function');
  });

  it('registerWithRole_GoesToSignUpWithThatIntent', () => {
    expect(redirectFor({ role: 'student' })).toBe('/login?intent=student');
    expect(redirectFor({ role: 'Teacher' })).toBe('/login?intent=teacher');
  });

  it('registerWithoutOrWithUnknownRole_GoesToSignIn', () => {
    expect(redirectFor({})).toBe('/login');
    expect(redirectFor({ role: 'admin' })).toBe('/login');
  });

  it('completeProfile_MovedToTheMainAppRegisterWizard', () => {
    const route = routes[0].children!.find((r: Route) => r.path === 'complete-profile')!;
    expect(route.component).toBeUndefined();
    expect(profileStepUrl('parent')).toBe('/register?role=parent');
    expect(profileStepUrl('Teacher')).toBe('/register?role=teacher');
    expect(profileStepUrl('admin')).toBe('/register');
    expect(profileStepUrl(undefined)).toBe('/register');
  });
});
