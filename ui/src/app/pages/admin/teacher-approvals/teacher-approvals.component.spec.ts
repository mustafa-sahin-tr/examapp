import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { EMPTY, Observable, Subject, of, throwError } from 'rxjs';

import { PUSH_RELOAD_AUDIT_MS, TeacherApprovalsComponent } from './teacher-approvals.component';
import { AdminService } from '../../../services/admin.service';
import { SignalRService } from '../../../services/signalr.service';
import {
  TeacherApplicationDetail,
  TeacherApplicationListItem,
} from '../../../models/teacher-application.model';
import { Paged } from '../../../models/test-instance';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

function paged(items: TeacherApplicationListItem[], totalCount = items.length, pageNumber = 1): Paged<TeacherApplicationListItem> {
  return { pageNumber, pageSize: 20, totalCount, items };
}

describe('TeacherApprovalsComponent — Type Label (Issue #234)', () => {
  let component: TeacherApprovalsComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let signalR: jasmine.SpyObj<SignalRService>;

  function createComponent(): void {
    adminService = jasmine.createSpyObj('AdminService', [
      'getTeacherApplications',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    signalR = jasmine.createSpyObj('SignalRService', [], { teacherApplicationSubmitted$: of(undefined) });

    adminService.getTeacherApplications.and.returnValue(of(paged([])));

    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: signalR },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
        provideNoopAnimations(),
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

    const app: TeacherApplicationListItem = {
      teacherId: 1,
      fullName: 'Ali Öğretmen',
      email: 'ali@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Bağımsız').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithSchoolName_ReturnsOkulWithSchoolName', () => {
    createComponent();

    const app: TeacherApplicationListItem = {
      teacherId: 2,
      fullName: 'Ayşe Öğretmen',
      email: 'ayse@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 5,
      requestedSchoolName: 'Atatürk Lisesi',
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul: Atatürk Lisesi').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithoutSchoolName_UsesSchoolIdFallback', () => {
    createComponent();

    const app: TeacherApplicationListItem = {
      teacherId: 3,
      fullName: 'Mehmet Öğretmen',
      email: 'mehmet@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 42,
      requestedSchoolName: null,
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul #42').toBeTrue();
  });

  it('typeLabel_SchoolConnectionWithBlankSchoolName_UsesFallback', () => {
    createComponent();

    const app: TeacherApplicationListItem = {
      teacherId: 4,
      fullName: 'Fatma Öğretmen',
      email: 'fatma@test.com',
      appliedAt: new Date().toISOString(),
      isIndependentTutor: false,
      requestedSchoolId: 99,
      requestedSchoolName: '   ',
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
    };

    const label = component.typeLabel(app);
    expect(label).toBeTruthy();
    expect(label === 'Okul #99').toBeTrue();
  });

  it('typeLabel_TwoApplicationsDifferentTypes_ReturnCorrectLabelsForEach', () => {
    createComponent();

    const independent: TeacherApplicationListItem = {
      teacherId: 1,
      fullName: 'Ali Bağımsız',
      email: 'ali@test.com',
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
    };

    const schoolBased: TeacherApplicationListItem = {
      teacherId: 2,
      fullName: 'Ayşe Okul',
      email: 'ayse@test.com',
      appliedAt: '2026-09-21T14:30:00Z',
      isIndependentTutor: false,
      requestedSchoolId: 7,
      requestedSchoolName: 'Cumhuriyet Ortaokulu',
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
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

  function application(overrides: Partial<TeacherApplicationListItem> = {}): TeacherApplicationListItem {
    return {
      teacherId: 7,
      fullName: 'Ali Öğretmen',
      email: 'a***@okul.k12.tr', // listede maskeli
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
      ...overrides,
    };
  }

  function detail(overrides: Partial<TeacherApplicationDetail> = {}): TeacherApplicationDetail {
    return { ...application(), email: 'ali@okul.k12.tr', ...overrides };
  }

  function httpError(status: number, headers?: Record<string, string>): HttpErrorResponse {
    return new HttpErrorResponse({ status, headers: new HttpHeaders(headers ?? {}) });
  }

  function createComponent(
    list: TeacherApplicationListItem[] | Observable<Paged<TeacherApplicationListItem>> = [application()],
    push$: Observable<unknown> = EMPTY,
  ): ComponentFixture<TeacherApprovalsComponent> {
    adminService = jasmine.createSpyObj('AdminService', [
      'getTeacherApplications',
      'getTeacherApplication',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    snackBar = jasmine.createSpyObj('MatSnackBar', ['open']);
    adminService.getTeacherApplications.and.returnValue(Array.isArray(list) ? of(paged(list)) : list);

    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: jasmine.createSpyObj('SignalRService', [], { teacherApplicationSubmitted$: push$ }) },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
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

  it('load_429OnInitialLoad_ShowsInlineRateLimitedMessage', () => {
    createComponent(throwError(() => httpError(429, { 'Retry-After': '30' })));

    expect(component.error()).toBe(approvals.rateLimited);
    expect(component.applications()).toEqual([]);
    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('load_429AfterSuccessfulLoad_KeepsListAndRevealedEmails_ShowsSnackbar', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(of(detail()));
    component.revealEmail(application());
    adminService.getTeacherApplications.and.returnValue(throwError(() => httpError(429)));

    component.load();

    expect(component.error()).toBeNull();
    expect(component.loading()).toBeFalse();
    expect(component.applications().map((a) => a.teacherId)).toEqual([7]);
    expect(component.revealedEmails().get(7)).toBe('ali@okul.k12.tr');
    expect(snackBar.open).toHaveBeenCalledWith(approvals.rateLimited, approvals.close, jasmine.any(Object));
  });

  it('load_500AfterSuccessfulLoad_ClearsListAndShowsInlineError', () => {
    createComponent();
    adminService.getTeacherApplications.and.returnValue(throwError(() => httpError(500)));

    component.load();

    expect(component.error()).toBe(approvals.loadFailed);
    expect(component.applications()).toEqual([]);
  });

  it('signalRPushBurst_IsCoalescedIntoSingleReload', fakeAsync(() => {
    const push$ = new Subject<void>();
    createComponent([application()], push$);
    adminService.getTeacherApplications.calls.reset();

    push$.next();
    push$.next();
    tick(1000);
    push$.next();
    expect(adminService.getTeacherApplications).not.toHaveBeenCalled();

    tick(PUSH_RELOAD_AUDIT_MS);
    expect(adminService.getTeacherApplications).toHaveBeenCalledTimes(1);

    discardPeriodicTasks();
  }));

  it('displayName_BlankFullName_FallsBackToTeacherId', () => {
    createComponent();

    expect(component.displayName(application({ fullName: '  ' }))).toBe(
      approvals.unnamed.replace('{{teacherId}}', '7'),
    );
  });

  it('load_403_ShowsForbiddenMessage', () => {
    createComponent();
    adminService.getTeacherApplications.and.returnValue(throwError(() => httpError(403)));

    component.load();

    expect(component.error()).toBe(approvals.forbidden);
  });

  it('load_500_ShowsGenericLoadFailed', () => {
    createComponent();
    adminService.getTeacherApplications.and.returnValue(throwError(() => httpError(500)));

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
    adminService.getTeacherApplications.calls.reset();
    adminService.getTeacherApplications.and.returnValue(of(paged([])));

    component.revealEmail(application());

    expect(snackBar.open).toHaveBeenCalledWith(approvals.emailErrors.notFound, approvals.close, jasmine.any(Object));
    expect(adminService.getTeacherApplications).toHaveBeenCalledTimes(1);
    expect(component.applications()).toEqual([]);
    expect(component.isRevealing(7)).toBeFalse();
  });

  it('revealEmail_429WithRetryAfter_ShowsSecondsMessage_NoReload', () => {
    createComponent();
    adminService.getTeacherApplication.and.returnValue(throwError(() => httpError(429, { 'Retry-After': '42' })));
    adminService.getTeacherApplications.calls.reset();

    component.revealEmail(application());

    const expected = approvals.emailErrors.rateLimitedSeconds.replace('{{seconds}}', '42');
    expect(snackBar.open).toHaveBeenCalledWith(expected, approvals.close, jasmine.any(Object));
    expect(adminService.getTeacherApplications).not.toHaveBeenCalled();
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
    adminService.getTeacherApplications.and.returnValue(of(paged([])));
    component.approve(application());

    expect(component.revealedEmails().has(7)).toBeFalse();
    expect(component.applications()).toEqual([]);
  });

  it('revealedEmail_PrunedOnReloadForRowsNoLongerListed', () => {
    createComponent([application(), application({ teacherId: 8, email: 'b***@x.com' })]);
    adminService.getTeacherApplication.and.callFake((id: number) =>
      of(detail({ teacherId: id, email: id === 7 ? 'ali@okul.k12.tr' : 'bora@x.com' })),
    );
    component.revealEmail(application());
    component.revealEmail(application({ teacherId: 8 }));

    adminService.getTeacherApplications.and.returnValue(of(paged([application({ teacherId: 8 })])));
    component.load();

    expect([...component.revealedEmails().keys()]).toEqual([8]);
  });
});

describe('TeacherApprovalsComponent — status filter & paging (issue #187)', () => {
  let fixture: ComponentFixture<TeacherApprovalsComponent>;
  let component: TeacherApprovalsComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let push$: Subject<void>;

  const approvals = adminTr.approvals;

  function app(overrides: Partial<TeacherApplicationListItem> = {}): TeacherApplicationListItem {
    return {
      teacherId: 7,
      fullName: 'Ali Öğretmen',
      email: 'a***@okul.k12.tr',
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: true,
      requestedSchoolId: null,
      requestedSchoolName: null,
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: false,
      ...overrides,
    };
  }

  const approved = app({ teacherId: 8, status: 'Approved', decidedAt: '2026-09-22T09:30:00Z' });
  const rejected = app({
    teacherId: 9,
    status: 'Rejected',
    rejectionReason: 'Belge eksik',
    decidedAt: '2026-09-21T08:00:00Z',
  });

  function create(first: Paged<TeacherApplicationListItem> = paged([app()])): void {
    adminService = jasmine.createSpyObj('AdminService', [
      'getTeacherApplications',
      'getTeacherApplication',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    snackBar = jasmine.createSpyObj('MatSnackBar', ['open']);
    dialog = jasmine.createSpyObj('MatDialog', ['open']);
    push$ = new Subject<void>();
    adminService.getTeacherApplications.and.returnValue(of(first));

    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: jasmine.createSpyObj('SignalRService', [], { teacherApplicationSubmitted$: push$ }) },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
      ],
    });
    // MatDialog komponent import'u (MatDialogModule) üzerinden sağlandığı için komponent seviyesinde override edilir.
    TestBed.overrideProvider(MatDialog, { useValue: dialog });

    fixture = TestBed.createComponent(TeacherApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function lastQuery(): { status: string; page: number; pageSize: number } {
    return adminService.getTeacherApplications.calls.mostRecent().args[0];
  }

  function toggleButtons(): HTMLButtonElement[] {
    return Array.from(el().querySelectorAll<HTMLButtonElement>('mat-button-toggle button'));
  }

  it('init_DefaultsToPendingFilterFirstPage', () => {
    create();

    expect(component.filter()).toBe('pending');
    expect(lastQuery()).toEqual({ status: 'pending', page: 1, pageSize: 20 });
    const group = el().querySelector('mat-button-toggle-group')!;
    expect(group.getAttribute('aria-label')).toBe(approvals.filter.label);
    expect(toggleButtons().map((b) => b.textContent?.trim())).toEqual([approvals.filter.pending, approvals.filter.all]);
    const toggles = el().querySelectorAll('mat-button-toggle');
    expect(toggles[0].classList).toContain('mat-button-toggle-checked');
    expect(toggles[1].classList).not.toContain('mat-button-toggle-checked');
  });

  it('filterToggle_ClickAll_ResetsToFirstPageAndReloadsWithStatusAll', () => {
    create(paged([app()], 45));
    component.onPage({ pageIndex: 2, pageSize: 20, length: 45, previousPageIndex: 0 });
    expect(lastQuery().page).toBe(3);
    adminService.getTeacherApplications.and.returnValue(of(paged([app(), approved, rejected], 3)));

    toggleButtons()[1].click();
    fixture.detectChanges();

    expect(component.filter()).toBe('all');
    expect(component.pageIndex()).toBe(0);
    expect(lastQuery()).toEqual({ status: 'all', page: 1, pageSize: 20 });
    expect(component.applications().length).toBe(3);
  });

  it('setFilter_SameValue_DoesNotReload', () => {
    create();
    adminService.getTeacherApplications.calls.reset();

    component.setFilter('pending');

    expect(adminService.getTeacherApplications).not.toHaveBeenCalled();
  });

  it('paginator_RenderedWithTotalCount_AndPageEventLoadsRequestedPage', () => {
    create(paged([app()], 45));

    const paginator = el().querySelector('mat-paginator');
    expect(paginator).not.toBeNull();

    component.onPage({ pageIndex: 1, pageSize: 50, length: 45, previousPageIndex: 0 });

    expect(lastQuery()).toEqual({ status: 'pending', page: 2, pageSize: 50 });
    expect(component.totalCount()).toBe(45);
  });

  it('emptyList_HidesPaginatorAndShowsEmptyTextPerFilter', () => {
    create(paged([]));
    expect(el().querySelector('mat-paginator')).toBeNull();
    expect(el().textContent).toContain(approvals.empty);

    adminService.getTeacherApplications.and.returnValue(of(paged([])));
    component.setFilter('all');
    fixture.detectChanges();

    expect(el().textContent).toContain(approvals.emptyAll);
  });

  it('statusChips_RenderPerRow_WithRejectionReasonAndDecidedAt', () => {
    create();
    adminService.getTeacherApplications.and.returnValue(of(paged([app(), approved, rejected])));
    component.setFilter('all');
    fixture.detectChanges();

    const chips = Array.from(el().querySelectorAll('[data-testid="status-chip"]'));
    expect(chips.map((c) => c.textContent?.trim())).toEqual([
      approvals.status.pending,
      approvals.status.approved,
      approvals.status.rejected,
    ]);
    expect(chips[0].classList).toContain('ta__status--pending');
    expect(chips[1].classList).toContain('ta__status--approved');
    expect(chips[2].classList).toContain('ta__status--rejected');

    const reasons = el().querySelectorAll('[data-testid="rejection-reason"]');
    expect(reasons.length).toBe(1);
    expect(reasons[0].textContent?.trim()).toBe(approvals.rejectionReason.replace('{{reason}}', 'Belge eksik'));

    expect(el().querySelectorAll('th.mat-column-decidedAt').length).toBe(1);
    expect(el().querySelectorAll('td.mat-column-decidedAt')[0].textContent?.trim()).toBe('—');
    expect(el().querySelectorAll('td.mat-column-decidedAt')[1].textContent?.trim()).not.toBe('—');
  });

  it('pendingFilter_HidesDecidedAtColumn', () => {
    create();
    expect(el().querySelector('th.mat-column-decidedAt')).toBeNull();
    expect(el().querySelector('th.mat-column-status')).not.toBeNull();
  });

  it('actions_OnlyForPendingRows', () => {
    create();
    adminService.getTeacherApplications.and.returnValue(of(paged([app(), approved, rejected])));
    component.setFilter('all');
    fixture.detectChanges();

    const rows = Array.from(el().querySelectorAll('tr.mat-mdc-row'));
    expect(rows.length).toBe(3);
    expect(rows[0].querySelector('[data-testid="approve"]')).not.toBeNull();
    expect(rows[0].querySelector('[data-testid="reject"]')).not.toBeNull();
    for (const row of rows.slice(1)) {
      expect(row.querySelector('[data-testid="approve"]')).toBeNull();
      expect(row.querySelector('[data-testid="reject"]')).toBeNull();
    }
  });

  it('approve_DecidedRow_IsIgnored', () => {
    create();

    component.approve(approved);
    component.reject(rejected);

    expect(adminService.approveTeacherApplication).not.toHaveBeenCalled();
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('approve_409_ReloadsList', () => {
    create();
    adminService.approveTeacherApplication.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { success: false, message: 'Zaten karar verildi' } })),
    );
    adminService.getTeacherApplications.calls.reset();
    adminService.getTeacherApplications.and.returnValue(of(paged([])));

    component.approve(app());

    expect(snackBar.open).toHaveBeenCalledWith('Zaten karar verildi', approvals.close, jasmine.any(Object));
    expect(adminService.getTeacherApplications).toHaveBeenCalledTimes(1);
  });

  it('signalRPush_ReloadsCurrentFilterAndPage', fakeAsync(() => {
    create(paged([app()], 45));
    adminService.getTeacherApplications.and.returnValue(of(paged([app(), approved], 45, 2)));
    component.setFilter('all');
    component.onPage({ pageIndex: 1, pageSize: 20, length: 45, previousPageIndex: 0 });
    adminService.getTeacherApplications.calls.reset();

    push$.next();
    tick(PUSH_RELOAD_AUDIT_MS);

    expect(adminService.getTeacherApplications).toHaveBeenCalledOnceWith({ status: 'all', page: 2, pageSize: 20 });
    discardPeriodicTasks();
  }));

  it('filterChange_429AfterFirstLoad_KeepsListAndRestoresPreviousFilter', () => {
    create();
    adminService.getTeacherApplications.and.returnValue(throwError(() => new HttpErrorResponse({ status: 429 })));

    component.setFilter('all');

    expect(component.filter()).toBe('pending');
    expect(component.applications().map((a) => a.teacherId)).toEqual([7]);
    expect(component.error()).toBeNull();
    expect(snackBar.open).toHaveBeenCalledWith(approvals.rateLimited, approvals.close, jasmine.any(Object));
  });

  it('revealButton_OnlyOnPendingRows_DecidedRowsKeepMaskedEmail', () => {
    create();
    adminService.getTeacherApplications.and.returnValue(of(paged([app(), approved, rejected])));
    component.setFilter('all');
    fixture.detectChanges();

    const rows = Array.from(el().querySelectorAll('tr.mat-mdc-row'));
    expect(rows[0].querySelector('button.ta__reveal')).not.toBeNull();
    expect(rows[1].querySelector('button.ta__reveal')).toBeNull();
    expect(rows[2].querySelector('button.ta__reveal')).toBeNull();
    expect(rows[1].textContent).toContain('a***@okul.k12.tr');

    component.revealEmail(approved);
    expect(adminService.getTeacherApplication).not.toHaveBeenCalled();
  });

  it('approve_PendingView_RemovesRowLocallyAndDecrementsTotal_NoReload', () => {
    create(paged([app(), app({ teacherId: 10 })], 25));
    adminService.approveTeacherApplication.and.returnValue(of({ success: true, message: '' }));
    adminService.getTeacherApplications.calls.reset();

    component.approve(app());

    expect(adminService.getTeacherApplications).not.toHaveBeenCalled();
    expect(component.applications().map((a) => a.teacherId)).toEqual([10]);
    expect(component.totalCount()).toBe(24);
    expect(snackBar.open).toHaveBeenCalledWith(approvals.approved, approvals.close, jasmine.any(Object));
  });

  it('approve_LastRowOnLastPage_ReloadsPreviousPageOnce', () => {
    create(paged([app()], 21));
    adminService.getTeacherApplications.and.returnValue(of(paged([app()], 21, 2)));
    component.onPage({ pageIndex: 1, pageSize: 20, length: 21, previousPageIndex: 0 });
    adminService.approveTeacherApplication.and.returnValue(of({ success: true, message: '' }));
    adminService.getTeacherApplications.calls.reset();
    const prevPage = Array.from({ length: 20 }, (_, i) => app({ teacherId: 100 + i }));
    adminService.getTeacherApplications.and.returnValue(of(paged(prevPage, 20, 1)));

    component.approve(app());

    expect(adminService.getTeacherApplications).toHaveBeenCalledOnceWith({ status: 'pending', page: 1, pageSize: 20 });
    expect(component.pageIndex()).toBe(0);
    expect(component.applications().length).toBe(20);
  });

  it('approve_OnlyRowOfOnlyPage_ShowsEmptyState_NoReload', () => {
    create(paged([app()], 1));
    adminService.approveTeacherApplication.and.returnValue(of({ success: true, message: '' }));
    adminService.getTeacherApplications.calls.reset();

    component.approve(app());
    fixture.detectChanges();

    expect(adminService.getTeacherApplications).not.toHaveBeenCalled();
    expect(component.totalCount()).toBe(0);
    expect(el().textContent).toContain(approvals.empty);
  });

  it('reject_AllView_PatchesRowLocally_NoReload', () => {
    create();
    adminService.getTeacherApplications.and.returnValue(of(paged([app(), approved])));
    component.setFilter('all');
    fixture.detectChanges();
    dialog.open.and.returnValue({ afterClosed: () => of('Belge eksik') } as ReturnType<MatDialog['open']>);
    adminService.rejectTeacherApplication.and.returnValue(of({ success: true, message: '' }));
    adminService.getTeacherApplications.calls.reset();

    component.reject(app());
    fixture.detectChanges();

    expect(adminService.rejectTeacherApplication).toHaveBeenCalledOnceWith(7, 'Belge eksik');
    expect(adminService.getTeacherApplications).not.toHaveBeenCalled();
    const row = component.applications().find((a) => a.teacherId === 7)!;
    expect(row.status).toBe('Rejected');
    expect(row.rejectionReason).toBe('Belge eksik');
    expect(row.decidedAt).not.toBeNull();
    expect(component.applications().length).toBe(2);
    const firstRow = el().querySelector('tr.mat-mdc-row')!;
    expect(firstRow.querySelector('[data-testid="approve"]')).toBeNull();
    expect(firstRow.querySelector('[data-testid="status-chip"]')!.textContent?.trim()).toBe(approvals.status.rejected);
    expect(firstRow.querySelector('button.ta__reveal')).toBeNull();
  });

  it('approve_Succeeds_ReloadWould429_RowNoLongerActionable', () => {
    // Pending görünümü: sayfada başka satır var → yeniden yükleme yapılmaz, satır yerelde düşer.
    create(paged([app(), app({ teacherId: 10 })], 2));
    adminService.approveTeacherApplication.and.returnValue(of({ success: true, message: '' }));
    adminService.getTeacherApplications.and.returnValue(throwError(() => new HttpErrorResponse({ status: 429 })));

    component.approve(app());
    fixture.detectChanges();

    expect(component.applications().some((a) => a.teacherId === 7)).toBeFalse();
    expect(snackBar.open).not.toHaveBeenCalledWith(approvals.rateLimited, jasmine.anything(), jasmine.anything());

    // Tümü görünümü: satır kalır ama Onaylandı olur, aksiyonları kaybolur.
    adminService.getTeacherApplications.and.returnValue(of(paged([app({ teacherId: 11 })])));
    component.setFilter('all');
    fixture.detectChanges();
    adminService.getTeacherApplications.and.returnValue(throwError(() => new HttpErrorResponse({ status: 429 })));

    component.approve(app({ teacherId: 11 }));
    fixture.detectChanges();

    expect(component.applications()[0].status).toBe('Approved');
    expect(component.isPending(component.applications()[0])).toBeFalse();
    expect(el().querySelector('[data-testid="approve"]')).toBeNull();
    expect(el().querySelector('[data-testid="reject"]')).toBeNull();
    component.approve(component.applications()[0]);
    expect(adminService.approveTeacherApplication).toHaveBeenCalledTimes(2);
  });
});

/** Issue #287: hesap onayı gerektiren başvurular — tür metni ve "Hesap onayı" etiketi. */
describe('TeacherApprovalsComponent — account approval label (Issue #287)', () => {
  let fixture: ComponentFixture<TeacherApprovalsComponent>;
  let component: TeacherApprovalsComponent;
  let adminService: jasmine.SpyObj<AdminService>;

  function row(overrides: Partial<TeacherApplicationListItem> = {}): TeacherApplicationListItem {
    return {
      teacherId: 1,
      fullName: 'Ali Öğretmen',
      email: 'a***@okul.k12.tr',
      appliedAt: '2026-09-20T10:00:00Z',
      isIndependentTutor: false,
      requestedSchoolId: null,
      requestedSchoolName: null,
      status: 'Pending',
      rejectionReason: null,
      decidedAt: null,
      requiresAccountApproval: true,
      ...overrides,
    };
  }

  function create(items: TeacherApplicationListItem[]): void {
    adminService = jasmine.createSpyObj('AdminService', [
      'getTeacherApplications',
      'approveTeacherApplication',
      'rejectTeacherApplication',
    ]);
    adminService.getTeacherApplications.and.returnValue(of(paged(items)));
    TestBed.configureTestingModule({
      imports: [TeacherApprovalsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: SignalRService, useValue: { teacherApplicationSubmitted$: EMPTY } },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
        provideNoopAnimations(),
      ],
    });
    fixture = TestBed.createComponent(TeacherApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('typeLabel_NoSchoolNotIndependentAccountApproval_ReturnsTeacherAccount', () => {
    create([]);
    expect(component.typeLabel(row())).toBe('Öğretmen hesabı');
    expect(component.typeIcon(row())).toBe('badge');
  });

  it('typeLabel_IndependentWithAccountApproval_AppendsAccountApproval', () => {
    create([]);
    expect(component.typeLabel(row({ isIndependentTutor: true }))).toBe('Bağımsız + hesap onayı');
  });

  it('typeLabel_SchoolRequestWithAccountApproval_AppendsAccountApproval', () => {
    create([]);
    expect(
      component.typeLabel(row({ requestedSchoolId: 5, requestedSchoolName: 'Atatürk Lisesi' })),
    ).toBe('Okul: Atatürk Lisesi + hesap onayı');
  });

  it('typeLabel_ApprovedAccountLaterIndependentApplication_NoAccountSuffix', () => {
    create([]);
    expect(component.typeLabel(row({ isIndependentTutor: true, requiresAccountApproval: false }))).toBe('Bağımsız');
  });

  it('accountApprovalChip_RenderedOnlyForRowsRequiringAccountApproval', () => {
    create([
      row({ teacherId: 1 }),
      row({ teacherId: 2, isIndependentTutor: true, requiresAccountApproval: false }),
    ]);
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row'));

    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('[data-testid="account-approval-chip"]')?.textContent?.trim()).toBe('Hesap onayı');
    expect(rows[0].querySelector('td.mat-column-type')?.textContent).toContain('Öğretmen hesabı');
    expect(rows[1].querySelector('[data-testid="account-approval-chip"]')).toBeNull();
  });
});
