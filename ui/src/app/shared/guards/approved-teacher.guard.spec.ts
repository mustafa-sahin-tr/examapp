import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Route, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';

import { approvedTeacherGuard } from './approved-teacher.guard';
import { AuthService, UserProfile } from '../../services/auth.service';
import { Teacher } from '../../models/teacher';
import { routes } from '../../app.routes';
import { TestService } from '../../services/test.service';
import { TEACHER_APPROVAL_PENDING_PATH } from '../../models/teacher-approval.model';

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
      isTeacherApprovalExempt: () => roles.includes('Admin') || roles.includes('SuperAdmin'),
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

  it('TeacherAndAdmin_ExemptEvenIfProfileSaysUnapproved', () => {
    setup(['Teacher', 'Admin'], profileWith(false));
    expect(run()).toBeTrue();
    expect(refreshProfile).not.toHaveBeenCalled();
  });

  it('CachedUnapprovedTeacher_RefreshesOnceThenRedirectsWhenStillUnapproved', () => {
    setup(['Teacher'], profileWith(false), true);
    refreshProfile.and.returnValue(of(profileWith(false)));

    let decision: unknown;
    (run() as Observable<unknown>).subscribe((d) => (decision = d));

    expect(refreshProfile).toHaveBeenCalledTimes(1);
    expect(serialize(decision)).toBe('/teacher-approval-pending');
  });

  it('CachedUnapprovedTeacher_ApprovedMeanwhile_AllowedWithoutBounce', () => {
    setup(['Teacher'], profileWith(false), true);
    refreshProfile.and.callFake(() => {
      unapproved.set(false);
      user.set(profileWith(true));
      return of(profileWith(true));
    });

    let decision: unknown;
    (run() as Observable<unknown>).subscribe((d) => (decision = d));

    expect(decision).toBeTrue();
  });

  it('CachedUnapprovedTeacher_RefreshFails_RedirectsOnCachedDecision', () => {
    setup(['Teacher'], profileWith(false), true);
    refreshProfile.and.returnValue(throwError(() => new Error('network')));

    let decision: unknown;
    (run() as Observable<unknown>).subscribe((d) => (decision = d));

    expect(serialize(decision)).toBe('/teacher-approval-pending');
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
      'tests',
      'tests-enhanced',
      'test/:testId',
      'testsolve/:testInstanceId',
      'testsolve/v2/:testInstanceId',
      'exam',
      'exam/:id',
      'study-pages',
      'study-pages/new',
      'study-pages/:id',
      'study-links',
      'question-transfer',
      'assignment-permission-requests',
      'my-calendar',
      'availability',
      'booking-requests',
      'lessons/:bookingId/video',
    ]) {
      expect(guarded(path)).withContext(path).toBeTrue();
    }
    // tutor-profile: bağımsız öğretmen başvurusunun formu — onay bekleyen öğretmene bilerek açık (backend de izin verir).
    for (const path of ['teacher-approval-pending', 'tutor-profile', 'programs', 'practice', 'tutors', 'student-profile', 'admin']) {
      expect(guarded(path)).withContext(path).toBeFalse();
    }
  });

  // ── Review: öğretmen girişi /tests'e düşer — onaysız öğretmen exam/list çağrılmadan durum sayfasına ──
  describe('login landing on /tests', () => {
    @Component({ standalone: true, template: 'page' })
    class StubPageComponent {}

    function realRoute(path: string): Route {
      const route = routes.find((r) => Array.isArray(r.children))!.children!.find((r) => r.path === path)!;
      return { ...route, component: StubPageComponent, loadComponent: undefined };
    }

    async function navigateToTests(unapprovedTeacher: boolean, userRoles: string[]): Promise<{
      url: string;
      listWorksheets: jasmine.Spy;
    }> {
      const listWorksheets = jasmine.createSpy('listWorksheets').and.returnValue(of({ items: [], totalCount: 0 }));
      const profile = profileWith(!unapprovedTeacher);
      const authStub: Partial<AuthService> = {
        hasRealmRole: (role: string) => userRoles.includes(role),
        isTeacherApprovalExempt: () => false,
        isAuthenticated: () => of(true),
        user: signal(profile),
        isUnapprovedTeacher: signal(unapprovedTeacher && userRoles.includes('Teacher')),
        refreshProfile: () => of(profile),
      };
      TestBed.configureTestingModule({
        providers: [
          provideRouter([
            realRoute('tests'),
            { ...realRoute(TEACHER_APPROVAL_PENDING_PATH), canActivate: [] },
          ]),
          { provide: AuthService, useValue: authStub },
          { provide: TestService, useValue: { listWorksheets } },
        ],
      });
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/tests');
      return { url: TestBed.inject(Router).url, listWorksheets };
    }

    it('UnapprovedTeacher_GoesToPendingPage_WithoutCallingWorksheetList', async () => {
      const { url, listWorksheets } = await navigateToTests(true, ['Teacher']);
      expect(url).toBe('/teacher-approval-pending');
      expect(listWorksheets).not.toHaveBeenCalled();
    });

    it('ApprovedTeacher_LandsOnTests_AndResolverRuns', async () => {
      const { url, listWorksheets } = await navigateToTests(false, ['Teacher']);
      expect(url).toBe('/tests');
      expect(listWorksheets).toHaveBeenCalledTimes(1);
    });

    it('Student_LandsOnTests_GuardIsNoOp', async () => {
      const { url, listWorksheets } = await navigateToTests(false, ['Student']);
      expect(url).toBe('/tests');
      expect(listWorksheets).toHaveBeenCalledTimes(1);
    });
  });
});
