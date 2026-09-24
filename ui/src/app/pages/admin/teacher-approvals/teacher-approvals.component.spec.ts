import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { EMPTY, Subject, of, throwError } from 'rxjs';

import { TeacherApprovalsComponent } from './teacher-approvals.component';
import { AdminService } from '../../../services/admin.service';
import { SignalRService } from '../../../services/signalr.service';
import {
  PendingTeacherApplication,
  TeacherApplicationDetail,
} from '../../../models/teacher-application.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('TeacherApprovalsComponent — Type Label (Issue #234)', () => {
  let component: TeacherApprovalsComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let signalR: jasmine.SpyObj<SignalRService>;

  function createComponent(): void {
    adminService = jasmine.createSpyObj('AdminService', [
      'getPendingTeacherApplications',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    signalR = jasmine.createSpyObj('SignalRService', [], { teacherApplicationSubmitted$: of(undefined) });

    adminService.getPendingTeacherApplications.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: signalR },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
      ],
    });

    component = TestBed.createComponent(TeacherApprovalsComponent).componentInstance;
  }

  it('should create', () => {
    createComponent();
    expect(component).toBeTruthy();
  });

  // ── Issue #234: Başvuru türü etiketi (Bağımsız / Okul: <ad> / Okul #id) ──────

  it('typeLabel_IndependentTutor_ReturnsIndependentLabel', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 1,
      userId: 100,
      fullName: 'Ali Öğretmen',
      email: 'ali@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Bağımsız').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithSchoolName_ReturnsOkulWithSchoolName', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 2,
      userId: 101,
      fullName: 'Ayşe Öğretmen',
      email: 'ayse@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 5,
      requestedSchoolName: 'Atatürk Lisesi',
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul: Atatürk Lisesi').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithoutSchoolName_UsesSchoolIdFallback', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 3,
      userId: 102,
      fullName: 'Mehmet Öğretmen',
      email: 'mehmet@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 42,
      requestedSchoolName: null,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul #42').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithBlankSchoolName_UsesFallback', () => {
    createComponent();

    const app: PendingTeacherApplication = {
      teacherId: 4,
      userId: 103,
      fullName: 'Fatma Öğretmen',
      email: 'fatma@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 99,
      requestedSchoolName: '   ',
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul #99').toBeTrue();
  });

  it('typeLabel_TwoApplicationsDifferentTypes_ReturnCorrectLabelsForEach', () => {
    createComponent();

    const independent: PendingTeacherApplication = {
      teacherId: 1,
      userId: 100,
      fullName: 'Ali Bağımsız',
      email: 'ali@test.com',
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
    };

    const schoolBased: PendingTeacherApplication = {
      teacherId: 2,
      userId: 101,
      fullName: 'Ayşe Okul',
      email: 'ayse@test.com',
      appliedAt: '2026-09-21T14:30:00Z',
      isIndependentTutor: false,
      requestedSchoolId: 7,
      requestedSchoolName: 'Cumhuriyet Ortaokulu',
    };

    const independentLabel = component.typeLabel(independent);
    const schoolLabel = component.typeLabel(schoolBased);

    expect(independentLabel).toBeTruthy();
    expect(independentLabel === 'Bağımsız').toBeTrue();
    expect(schoolLabel).toBeTruthy();
    expect(schoolLabel === 'Okul: Cumhuriyet Ortaokulu').toBeTrue();
  });
});

describe('TeacherApprovalsComponent — PII hardening (issue #262)', () => {
  let component: TeacherApprovalsComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const approvals = adminTr.approvals;

  function application(overrides: Partial<PendingTeacherApplication> = {}): PendingTeacherApplication {
    return {
      teacherId: 7,
      userId: 700,
      fullName: 'Ali Öğretmen',
      email: 'a***@okul.k12.tr', // listede maskeli
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
      ...overrides,
    };
  }

  function detail(overrides: Partial<TeacherApplicationDetail> = {}): TeacherApplicationDetail {
    return { ...application(), email: 'ali@okul.k12.tr', ...overrides };
  }

  function httpError(status: number, headers?: Record<string, string>): HttpErrorResponse {
    return new HttpErrorResponse({ status, headers: new HttpHeaders(headers ?? {}) });
  }

  function createComponent(list: PendingTeacherApplication[] = [application()]): ComponentFixture<TeacherApprovalsComponent> {
    adminService = jasmine.createSpyObj('AdminService', [
      'getPendingTeacherApplications',
      'getTeacherApplication',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    snackBar = jasmine.createSpyObj('MatSnackBar', ['open']);
    adminService.getPendingTeacherApplications.and.returnValue(of(list));

    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: jasmine.createSpyObj('SignalRService', [], { teacherApplicationSubmitted$: EMPTY }) },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });

    const fixture = TestBed.createComponent(TeacherApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  }

  function revealButton(fixture: ComponentFixture<TeacherApprovalsComponent>): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.ta__reveal');
  }

  // ── Liste yükleme hataları ─────────────────────────────────────────────

  it('load_429_ShowsRateLimitedMessage', () => {
    createComponent();
    adminService.getPendingTeacherApplications.and.returnValue(throwError(() => httpError(429, { 'Retry-After': '30' })));

    component.load();

    expect(component.error()).toBe(approvals.rateLimited);
    expect(component.applications()).toEqual([]);
  });

  it('load_403_ShowsForbiddenMessage', () => {
    createComponent();
    adminService.getPendingTeacherApplications.and.returnValue(throwError(() => httpError(403)));

    component.load();

    expect(component.error()).toBe(approvals.forbidden);
  });

  it('load_500_ShowsGenericLoadFailed', () => {
    createComponent();
    adminService.getPendingTeacherApplications.and.returnValue(throwError(() => httpError(500)));

    component.load();

    expect(component.error()).toBe(approvals.loadFailed);
  });

  // ── Tam e-postayı göster ─────────────────────────────────────────────

  it('list_ShowsMaskedEmailAndRevealButton_WithoutCallingDetail', () => {
    const fixture = createComponent();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('a***@okul.k12.tr');
    expect(revealButton(fixture)).not.toBeNull();
    expect(adminService.getTeacherApplication).not.toHaveBeenCalled();
  });

  it('revealEmail_Success_ShowsFullEmailInRowAndHidesButton', () => {
    const fixture = createComponent();
    adminService.getTeacherApplication.and.returnValue(of(detail()));

    revealButton(fixture)!.click();
    fixture.detectChanges();

    expect(adminService.getTeacherApplication).toHaveBeenCalledOnceWith(7);
    expect(component.revealedEmails().get(7)).toBe('ali@okul.k12.tr');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('ali@okul.k12.tr');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('a***@okul.k12.tr');
    expect(revealButton(fixture)).toBeNull();
  });

  it('revealEmail_AlreadyRevealed_DoesNotCallAgain', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(of(detail()));

    component.revealEmail(application());
    component.revealEmail(application());

    expect(adminService.getTeacherApplication).toHaveBeenCalledTimes(1);
  });

  it('revealEmail_InFlight_IgnoresDoubleClick', () => {
    createComponent();
    const pending = new Subject<TeacherApplicationDetail>();
    adminService.getTeacherApplication.and.returnValue(pending);

    component.revealEmail(application());
    expect(component.isRevealing(7)).toBeTrue();
    component.revealEmail(application());

    expect(adminService.getTeacherApplication).toHaveBeenCalledTimes(1);
    pending.next(detail());
    pending.complete();
    expect(component.isRevealing(7)).toBeFalse();
  });

  it('revealEmail_404_ShowsSnackbarAndReloadsList', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(throwError(() => httpError(404)));
    adminService.getPendingTeacherApplications.calls.reset();
    adminService.getPendingTeacherApplications.and.returnValue(of([]));

    component.revealEmail(application());

    expect(snackBar.open).toHaveBeenCalledWith(approvals.emailErrors.notFound, approvals.close, jasmine.any(Object));
    expect(adminService.getPendingTeacherApplications).toHaveBeenCalledTimes(1);
    expect(component.applications()).toEqual([]);
    expect(component.isRevealing(7)).toBeFalse();
  });

  it('revealEmail_429WithRetryAfter_ShowsSecondsMessage_NoReload', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(throwError(() => httpError(429, { 'Retry-After': '42' })));
    adminService.getPendingTeacherApplications.calls.reset();

    component.revealEmail(application());

    const expected = approvals.emailErrors.rateLimitedSeconds.replace('{{seconds}}', '42');
    expect(snackBar.open).toHaveBeenCalledWith(expected, approvals.close, jasmine.any(Object));
    expect(adminService.getPendingTeacherApplications).not.toHaveBeenCalled();
    expect(component.isEmailRevealed(7)).toBeFalse();
  });

  it('revealEmail_429WithoutRetryAfter_ShowsGenericRateLimitMessage', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(throwError(() => httpError(429)));

    component.revealEmail(application());

    expect(snackBar.open).toHaveBeenCalledWith(approvals.emailErrors.rateLimited, approvals.close, jasmine.any(Object));
  });

  it('revealEmail_500_ShowsGenericError', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(throwError(() => httpError(500)));

    component.revealEmail(application());

    expect(snackBar.open).toHaveBeenCalledWith(approvals.emailErrors.generic, approvals.close, jasmine.any(Object));
    expect(component.isEmailRevealed(7)).toBeFalse();
  });

  it('revealEmail_EmptyEmailInDetail_ShowsUnavailableAndKeepsMasked', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(of(detail({ email: '' })));

    component.revealEmail(application());

    expect(snackBar.open).toHaveBeenCalledWith(approvals.emailUnavailable, approvals.close, jasmine.any(Object));
    expect(component.emailFor(application())).toBe('a***@okul.k12.tr');
  });

  it('revealedEmail_DroppedWhenRowApproved', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(of(detail()));
    adminService.approveTeacherApplication.and.returnValue(of({ success: true, message: '' }));

    component.revealEmail(application());
    component.approve(application());

    expect(component.revealedEmails().has(7)).toBeFalse();
    expect(component.applications()).toEqual([]);
  });

  it('revealedEmail_PrunedOnReloadForRowsNoLongerListed', () => {
    createComponent([application(), application({ teacherId: 8, userId: 800, email: 'b***@x.com' })]);
    adminService.getTeacherApplication.and.callFake((id: number) =>
      of(detail({ teacherId: id, email: id === 7 ? 'ali@okul.k12.tr' : 'bora@x.com' })),
    );
    component.revealEmail(application());
    component.revealEmail(application({ teacherId: 8 }));

    adminService.getPendingTeacherApplications.and.returnValue(of([application({ teacherId: 8, userId: 800 })]));
    component.load();

    expect([...component.revealedEmails().keys()]).toEqual([8]);
  });
});
