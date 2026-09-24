import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable, Subject, isObservable, throwError } from 'rxjs';

import { approvedTeacherGuard } from './approved-teacher.guard';
import { AuthService, UserProfile } from '../../services/auth.service';
import { Teacher } from '../../models/teacher';
import { routes } from '../../app.routes';

/** Issue #287: onaysız öğretmeni öğretmen rotalarından başvuru durumu sayfasına yönlendiren guard. */
describe('approvedTeacherGuard (issue #287)', () => {
  let roles: string[];
  let user: ReturnType<typeof signal<UserProfile | null>>;
  let unapproved: ReturnType<typeof signal<boolean>>;
  let refreshProfile: jasmine.Spy;

  function profileWith(teacherAccountApproved?: boolean): UserProfile {
    const teacher = { id: 1, userId: 1, schoolName: '' } as Teacher;
    if (teacherAccountApproved !== undefined) teacher.teacherAccountApproved = teacherAccountApproved;
    return { email: '', avatar: '', fullName: '', id: 1, keycloakId: 'k', profileId: 1, role: 'Teacher', teacher };
  }

  function setup(userRoles: string[], profile: UserProfile | null, isUnapproved = false): void {
    roles = userRoles;
    user = signal(profile);
    unapproved = signal(isUnapproved);
    refreshProfile = jasmine.createSpy('refreshProfile');
    const authStub: Partial<AuthService> = {
      hasRealmRole: (role: string) => roles.includes(role),
      user,
      isUnapprovedTeacher: unapproved,
      refreshProfile,
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: authStub }],
    });
  }

  function run(): ReturnType<typeof approvedTeacherGuard> {
    return TestBed.runInInjectionContext(() =>
      approvedTeacherGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    );
  }

  function serialize(result: unknown): string | boolean {
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : (result as boolean);
  }

  it('Student_Allowed_WithoutRefresh', () => {
    setup(['Student'], null);
    expect(run()).toBeTrue();
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('Admin_Allowed_WithoutRefresh', () => {
    setup(['Admin'], null);
    expect(run()).toBeTrue();
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('ApprovedTeacher_Allowed', () => {
    setup(['Teacher'], profileWith(true));
    expect(run()).toBeTrue();
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('UnapprovedTeacher_RedirectsToPendingPage', () => {
    setup(['Teacher'], profileWith(false), true);
    expect(serialize(run())).toBe('/teacher-approval-pending');
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('TeacherWithUnknownApproval_RefreshesProfileThenRedirectsWhenUnapproved', () => {
    setup(['Teacher'], profileWith(undefined));
    const response = new Subject<UserProfile | null>();
    refreshProfile.and.returnValue(response.asObservable());

    const result = run();
    expect(isObservable(result)).toBeTrue();
    let decision: unknown;
    (result as Observable<unknown>).subscribe((d) => (decision = d));

    unapproved.set(true); // refreshProfile profili yazdı → computed onaysız
    response.next(profileWith(false));
    response.complete();

    expect(refreshProfile).toHaveBeenCalledTimes(1);
    expect(serialize(decision)).toBe('/teacher-approval-pending');
  });

  it('TeacherWithUnknownApproval_RefreshSaysApproved_Allowed', () => {
    setup(['Teacher'], profileWith(undefined));
    const response = new Subject<UserProfile | null>();
    refreshProfile.and.returnValue(response.asObservable());

    let decision: unknown;
    (run() as Observable<unknown>).subscribe((d) => (decision = d));
    response.next(profileWith(true));
    response.complete();

    expect(decision).toBeTrue();
  });

  it('TeacherWithUnknownApproval_RefreshFails_AllowsAndLeavesGateToBackend', () => {
    setup(['Teacher'], profileWith(undefined));
    refreshProfile.and.returnValue(throwError(() => new Error('network')));

    let decision: unknown;
    (run() as Observable<unknown>).subscribe((d) => (decision = d));

    expect(decision).toBeTrue();
  });

  it('routes_TeacherOnlyRoutesAndDashboard_UseGuard_StudentOnlyRoutesDoNot', () => {
    const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
    const guarded = (path: string) =>
      !!children.find((r) => r.path === path)?.canActivate?.includes(approvedTeacherGuard);

    for (const path of [
      'dashboard',
      'exam',
      'exam/:id',
      'study-pages',
      'study-pages/new',
      'study-pages/:id',
      'study-links',
      'question-transfer',
      'assignment-permission-requests',
      'tutor-profile',
      'my-calendar',
      'availability',
      'booking-requests',
      'lessons/:bookingId/video',
    ]) {
      expect(guarded(path)).withContext(path).toBeTrue();
    }
    for (const path of ['teacher-approval-pending', 'programs', 'practice', 'tutors', 'student-profile', 'admin']) {
      expect(guarded(path)).withContext(path).toBeFalse();
    }
  });
});
