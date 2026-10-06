import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable, isObservable, of, throwError } from 'rxjs';

import { independentTeacherGuard } from './independent-teacher.guard';
import { AuthService, UserProfile } from '../../services/auth.service';

/** Issue #384: bağımsız olmayan öğretmeni (backend kuralı `isIndependentTutor`) panoya yönlendiren guard. */
describe('independentTeacherGuard (issue #384)', () => {
  let user: ReturnType<typeof signal<UserProfile | null>>;
  let refreshProfile: jasmine.Spy;

  function profile(isIndependentTutor: boolean | undefined, schoolId: number | null = null): UserProfile {
    return {
      email: '',
      avatar: '',
      fullName: '',
      id: 1,
      keycloakId: 'k',
      profileId: 1,
      role: 'Teacher',
      teacher: { id: 1, userId: 1, schoolName: '', schoolId, isIndependentTutor },
    } as UserProfile;
  }

  function setup(roles: string[], initial: UserProfile | null, refreshed?: Observable<UserProfile | null>): void {
    user = signal(initial);
    refreshProfile = jasmine.createSpy('refreshProfile').and.returnValue(refreshed ?? of(initial));
    const authStub: Partial<AuthService> = {
      hasRealmRole: (role: string) => roles.includes(role),
      user,
      refreshProfile,
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: authStub }],
    });
  }

  function serialize(result: unknown): string | boolean {
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : (result as boolean);
  }

  function run(): string | boolean {
    const result = TestBed.runInInjectionContext(() =>
      independentTeacherGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    );
    if (isObservable(result)) {
      let value: unknown;
      result.subscribe((v) => (value = v));
      return serialize(value);
    }
    return serialize(result);
  }

  it('NotIndependentTeacher_RedirectsToDashboard_WithoutRefresh', () => {
    setup(['Teacher'], profile(false, 7));
    expect(run()).toBe('/dashboard');
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('NotIndependentTeacherWithoutSchool_StillRedirects', () => {
    setup(['Teacher'], profile(false, null));
    expect(run()).toBe('/dashboard');
  });

  it('IndependentTeacher_Allowed_EvenWithSchoolId', () => {
    setup(['Teacher'], profile(true, 7));
    expect(run()).toBeTrue();
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('FlagUnknown_RefreshesProfileOnce_ThenDecides', () => {
    setup(['Teacher'], profile(undefined));
    refreshProfile.and.callFake(() => {
      user.set(profile(false));
      return of(user());
    });
    expect(run()).toBe('/dashboard');
    expect(refreshProfile).toHaveBeenCalledTimes(1);
  });

  it('FlagStillUnknownAfterRefresh_Allowed', () => {
    setup(['Teacher'], null);
    expect(run()).toBeTrue();
    expect(refreshProfile).toHaveBeenCalledTimes(1);
  });

  it('RefreshFails_Allowed', () => {
    setup(['Teacher'], null, throwError(() => new Error('boom')));
    expect(run()).toBeTrue();
  });

  it('NonTeacher_Allowed_WithoutRefresh', () => {
    setup(['Admin'], profile(false));
    expect(run()).toBeTrue();
    expect(refreshProfile).not.toHaveBeenCalled();
  });
});
