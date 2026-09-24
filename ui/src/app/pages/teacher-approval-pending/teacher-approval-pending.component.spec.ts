import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { TeacherApprovalPendingComponent } from './teacher-approval-pending.component';
import { AuthService, UserProfile } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';
import { Teacher } from '../../models/teacher';
import { TeacherApplicationDecidedPayload } from '../../models/teacher-application.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import teacherApprovalTr from '../../../../public/i18n/teacher-approval/tr.json';
import teacherApprovalEn from '../../../../public/i18n/teacher-approval/en.json';

/** Issue #287: öğretmen hesabı başvuru durumu sayfası. */
describe('TeacherApprovalPendingComponent (issue #287)', () => {
  let fixture: ComponentFixture<TeacherApprovalPendingComponent>;
  let user: WritableSignal<UserProfile | null>;
  let unapproved: WritableSignal<boolean>;
  let refreshProfile: jasmine.Spy;
  let decided$: Subject<TeacherApplicationDecidedPayload>;

  function profile(teacher: Partial<Teacher> | null): UserProfile {
    return {
      email: '',
      avatar: '',
      fullName: '',
      id: 1,
      keycloakId: 'k',
      profileId: 1,
      role: 'Teacher',
      teacher: teacher ? ({ id: 3, userId: 1, schoolName: '', ...teacher } as Teacher) : undefined,
    };
  }

  /** `refreshProfile` gerçek servisteki gibi dönen profili `user` signal'ına yazar. */
  function respondWith(next: UserProfile | null): void {
    refreshProfile.and.callFake(() => {
      if (next) user.set(next);
      unapproved.set(next?.teacher?.teacherAccountApproved === false);
      return of(next);
    });
  }

  function create(initial: UserProfile | null, response: UserProfile | null): void {
    user = signal(initial);
    unapproved = signal(initial?.teacher?.teacherAccountApproved === false);
    refreshProfile = jasmine.createSpy('refreshProfile');
    respondWith(response);
    decided$ = new Subject();
    const authStub: Partial<AuthService> = { user, isUnapprovedTeacher: unapproved, refreshProfile };

    TestBed.configureTestingModule({
      imports: [
        TeacherApprovalPendingComponent,
        translocoTestingModule({
          langs: { 'teacher-approval/tr': teacherApprovalTr, 'teacher-approval/en': teacherApprovalEn },
        }),
      ],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
        { provide: SignalRService, useValue: { teacherApplicationDecided$: decided$.asObservable() } },
      ],
    });
    fixture = TestBed.createComponent(TeacherApprovalPendingComponent);
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => el().textContent ?? '';

  it('init_Pending_ShowsUnderReviewStateAndRefreshButton', () => {
    const pending = profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Pending' });
    create(pending, pending);

    expect(refreshProfile).toHaveBeenCalledTimes(1);
    expect(el().querySelector('[data-view="pending"]')).not.toBeNull();
    expect(text()).toContain('Başvurunuz inceleniyor');
    expect(el().querySelector('[data-testid="refresh"]')).not.toBeNull();
  });

  it('init_Rejected_ShowsReasonAndNextSteps', () => {
    const rejected = profile({
      teacherAccountApproved: false,
      teacherApplicationStatus: 'Rejected',
      rejectionReason: 'Belge eksik',
    });
    create(rejected, rejected);

    expect(el().querySelector('[data-view="rejected"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="rejection-reason"]')?.textContent).toContain('Belge eksik');
    expect(text()).toContain('Ne yapabilirsiniz?');
  });

  it('init_RejectedWithoutReason_ShowsNoReasonText', () => {
    const rejected = profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Rejected', rejectionReason: null });
    create(rejected, rejected);

    expect(el().querySelector('[data-testid="rejection-reason"]')).toBeNull();
    expect(text()).toContain('Gerekçe belirtilmedi.');
  });

  it('refreshButton_ApprovedMeanwhile_ShowsApprovedStateAndDashboardLink', () => {
    const pending = profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Pending' });
    create(pending, pending);
    respondWith(profile({ teacherAccountApproved: true, teacherApplicationStatus: 'Approved' }));

    (el().querySelector('[data-testid="refresh"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(refreshProfile).toHaveBeenCalledTimes(2);
    expect(el().querySelector('[data-view="approved"]')).not.toBeNull();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    (el().querySelector('[data-testid="go-dashboard"]') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/dashboard']);
  });

  it('approvedAccountWithPendingLaterApplication_ShowsApproved (school → independent switch)', () => {
    const switched = profile({ teacherAccountApproved: true, teacherApplicationStatus: 'Pending' });
    create(switched, switched);

    expect(el().querySelector('[data-view="approved"]')).not.toBeNull();
  });

  it('refreshReturnsNull_ShowsNoRecordWithRegisterLink', () => {
    create(profile({ teacherAccountApproved: false }), null);

    expect(el().querySelector('[data-view="noRecord"]')).not.toBeNull();
    const link = el().querySelector('a[href^="/register"]') as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('/register?role=teacher');
  });

  it('initialLoadFails_ShowsErrorWithRetry', () => {
    user = signal(null);
    unapproved = signal(false);
    refreshProfile = jasmine.createSpy('refreshProfile').and.returnValue(throwError(() => new Error('x')));
    TestBed.configureTestingModule({
      imports: [
        TeacherApprovalPendingComponent,
        translocoTestingModule({ langs: { 'teacher-approval/tr': teacherApprovalTr } }),
      ],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { user, isUnapprovedTeacher: unapproved, refreshProfile } },
        { provide: SignalRService, useValue: { teacherApplicationDecided$: new Subject() } },
      ],
    });
    fixture = TestBed.createComponent(TeacherApprovalPendingComponent);
    fixture.detectChanges();

    expect(el().querySelector('[data-testid="error"]')).not.toBeNull();
    expect(text()).toContain('Başvuru durumunuz alınamadı.');

    refreshProfile.and.returnValue(of(profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Pending' })));
    user.set(profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Pending' }));
    (el().querySelector('[data-testid="error"] button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(el().querySelector('[data-testid="error"]')).toBeNull();
    expect(el().querySelector('[data-view="pending"]')).not.toBeNull();
  });

  it('decisionPush_RefreshesStatus', () => {
    const pending = profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Pending' });
    create(pending, pending);
    respondWith(profile({ teacherAccountApproved: true, teacherApplicationStatus: 'Approved' }));

    decided$.next({ notificationId: 1, teacherId: 3, approved: true, isIndependentTutor: false, title: '', body: '' });
    fixture.detectChanges();

    expect(refreshProfile).toHaveBeenCalledTimes(2);
    expect(el().querySelector('[data-view="approved"]')).not.toBeNull();
  });

  it('i18n_TrAndEnHaveSameKeys', () => {
    const keys = (o: object, prefix = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) =>
        v && typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]
      );
    expect(keys(teacherApprovalEn).sort()).toEqual(keys(teacherApprovalTr).sort());
  });
});
