import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminTeacherSchoolDialogComponent,
  AdminTeacherSchoolDialogData,
  AdminTeacherSchoolDialogResult,
} from './admin-teacher-school-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { SchoolService } from '../../../services/school.service';
import { School } from '../../../models/taxonomy';
import { AdminTeacherSchoolResponse } from '../../../models/admin-teacher-school.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';
import adminEn from '../../../../../public/i18n/admin/en.json';

const school = (id: number, name: string): School => ({
  id,
  name,
  provinceId: null,
  provinceName: null,
  districtId: null,
  districtName: null,
  addressLine: null,
});

describe('AdminTeacherSchoolDialogComponent (issue #313)', () => {
  const texts = adminTr.teacherSchool;
  let fixture: ComponentFixture<AdminTeacherSchoolDialogComponent>;
  let component: AdminTeacherSchoolDialogComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AdminTeacherSchoolDialogComponent, AdminTeacherSchoolDialogResult | undefined>>;

  function configure(data: Partial<AdminTeacherSchoolDialogData> = {}): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['changeTeacherSchool']);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [AdminTeacherSchoolDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: AdminService, useValue: adminService },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: SchoolService, useValue: { getSchools: () => of([school(5, 'Ankara Lisesi'), school(8, 'İzmir Lisesi')]) } },
        { provide: MatDialogRef, useValue: dialogRef },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            teacherId: 12,
            displayName: 'Ayşe Yılmaz',
            currentSchoolId: 5,
            currentSchoolName: 'Ankara Lisesi',
            independent: false,
            accountApproved: true,
            ...data,
          },
        },
      ],
    });
    fixture = TestBed.createComponent(AdminTeacherSchoolDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const textOf = (testId: string): string => el().querySelector(`[data-testid="${testId}"]`)?.textContent ?? '';
  const button = (id: string): HTMLButtonElement | null => el().querySelector(`button[data-testid="${id}"]`);

  function pick(id = 8): void {
    component.selectedSchoolId.set(id);
    component.onSchoolSelected(school(id, id === 8 ? 'İzmir Lisesi' : 'Başka Okul'));
    fixture.detectChanges();
  }

  function confirmWithError(status: number, error: unknown = null, headers?: HttpHeaders): void {
    adminService.changeTeacherSchool.and.returnValue(throwError(() => new HttpErrorResponse({ status, error, headers })));
    pick();
    button('confirm')!.click();
    fixture.detectChanges();
  }

  // ── Render ───────────────────────────────────────────────────────────────

  it('render_SchooledTeacher_ChangeModeWithNameCurrentSchoolAndOldSchoolEffect', () => {
    configure();

    expect(el().querySelector('h2')?.textContent).toContain(texts.changeTitle);
    expect(textOf('teacher-name')).toContain('Ayşe Yılmaz');
    expect(textOf('current-school')).toContain('Ankara Lisesi');
    expect(textOf('transition-from')).toContain('Ankara Lisesi');
    expect(textOf('transition-to')).toContain(texts.notSelected);
    expect(el().querySelector('[data-testid="effect-old-school"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="independent-note"]')).toBeNull();
    expect(button('confirm')!.textContent).toContain(texts.confirmChange);
    expect(button('confirm')!.disabled).toBeTrue();
  });

  it('render_TeacherWithoutSchool_AssignModeWithoutOldSchoolEffect', () => {
    configure({ currentSchoolId: null, currentSchoolName: null });

    expect(el().querySelector('h2')?.textContent).toContain(texts.assignTitle);
    expect(textOf('current-school')).toContain(texts.noSchool);
    expect(textOf('transition-from')).toContain(texts.noSchool);
    expect(el().querySelector('[data-testid="effect-old-school"]')).toBeNull();
    expect(button('confirm')!.textContent).toContain(texts.confirmAssign);
  });

  it('render_IndependentTeacher_ShowsIndependentProfileNote', () => {
    configure({ currentSchoolId: null, currentSchoolName: null, independent: true });

    expect(textOf('independent-note')).toContain(texts.independentNote);
  });

  it('render_ApprovedSchoolTeacher_PendingRequestEffectSaysApplicationBecomesApproved', () => {
    configure();
    expect(textOf('effect-pending-request')).toContain(texts.effectPendingRequestApproved);
  });

  it('render_IndependentOrUnapprovedAccount_GenericPendingRequestEffect', () => {
    configure({ independent: true, currentSchoolId: null, currentSchoolName: null });
    expect(textOf('effect-pending-request')).toContain(texts.effectPendingRequest);
    expect(textOf('effect-pending-request')).not.toContain(texts.effectPendingRequestApproved);
  });

  it('render_NotIndependentAccountNotApproved_GenericPendingRequestEffect', () => {
    configure({ accountApproved: false });
    expect(textOf('effect-pending-request')).not.toContain(texts.effectPendingRequestApproved);
  });

  it('pick_ShowsOldToNewTransitionAndEnablesConfirm', () => {
    configure();
    pick();

    expect(textOf('transition-from')).toContain('Ankara Lisesi');
    expect(textOf('transition-to')).toContain('İzmir Lisesi');
    expect(el().querySelector('[data-testid="school-transition"]')?.getAttribute('aria-label')).toBe(
      'Ankara Lisesi → İzmir Lisesi',
    );
    expect(button('confirm')!.disabled).toBeFalse();
  });

  // ── İstek ────────────────────────────────────────────────────────────────

  it('confirm_WhileSubmitting_SendsSingleRequestWithTeacherAndSchoolId', () => {
    configure();
    adminService.changeTeacherSchool.and.returnValue(new Subject<AdminTeacherSchoolResponse>());
    pick();

    component.confirm();
    component.confirm();
    fixture.detectChanges();

    expect(adminService.changeTeacherSchool).toHaveBeenCalledOnceWith(12, 8);
    expect(component.submitting()).toBeTrue();
    expect(button('cancel')!.disabled).toBeTrue();
  });

  it('confirm_Success_ClosesWithServerResponseAndSelectedName', () => {
    configure();
    const response: AdminTeacherSchoolResponse = { teacherId: 12, schoolId: 8, previousSchoolId: 5, changed: true };
    adminService.changeTeacherSchool.and.returnValue(of(response));
    pick();

    button('confirm')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ response, schoolName: 'İzmir Lisesi' });
    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('confirm_ChangedFalse_ClosesWithUnchangedResponse', () => {
    configure();
    const response: AdminTeacherSchoolResponse = { teacherId: 12, schoolId: 8, previousSchoolId: 8, changed: false };
    adminService.changeTeacherSchool.and.returnValue(of(response));
    pick();

    button('confirm')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ response, schoolName: 'İzmir Lisesi' });
  });

  it('confirm_ServerReturnsDifferentSchool_ClosesWithNullName', () => {
    configure();
    const response: AdminTeacherSchoolResponse = { teacherId: 12, schoolId: 9, previousSchoolId: 5, changed: true };
    adminService.changeTeacherSchool.and.returnValue(of(response));
    pick();

    button('confirm')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ response, schoolName: null });
  });

  // ── Hata eşleme ──────────────────────────────────────────────────────────

  const errorCases: { status: number; expected: string }[] = [
    { status: 400, expected: texts.errors.invalidSchool },
    { status: 403, expected: texts.errors.forbidden },
    { status: 404, expected: texts.errors.notFound },
    { status: 409, expected: texts.errors.conflict },
    { status: 429, expected: texts.errors.rateLimited },
    { status: 502, expected: texts.errors.upstream },
    { status: 500, expected: texts.errors.generic },
    { status: 0, expected: texts.errors.generic },
  ];

  for (const { status, expected } of errorCases) {
    it(`error${status}WithoutBody_ShowsInlineAlertAndSnackbar`, () => {
      configure();
      confirmWithError(status);

      expect(textOf('school-error')).toContain(expected);
      expect(snackBar.open).toHaveBeenCalledOnceWith(expected, texts.close, jasmine.objectContaining({ politeness: 'off' }));
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  }

  it('error403WithBody_ShowsServerMessage', () => {
    configure();
    confirmWithError(403, { message: 'Hedef hesap yönetici.' });

    expect(textOf('school-error')).toContain('Hedef hesap yönetici.');
    expect(snackBar.open).toHaveBeenCalledWith('Hedef hesap yönetici.', texts.close, jasmine.any(Object));
  });

  it('error429WithRetryAfter_ShowsSeconds', () => {
    configure();
    confirmWithError(429, 'Too many requests', new HttpHeaders({ 'Retry-After': '30' }));

    expect(textOf('school-error')).toContain(texts.errors.rateLimitedSeconds.replace('{{seconds}}', '30'));
  });

  it('error502_RetryButtonThenCancel_ClosesWithRefresh', () => {
    configure();
    confirmWithError(502);
    expect(button('confirm')!.textContent).toContain(texts.retry);
    expect(button('confirm')!.disabled).toBeFalse();

    button('cancel')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ refresh: true });
  });

  it('error409_HidesConfirmAndReloadClosesWithRefresh', () => {
    configure();
    confirmWithError(409);
    expect(button('confirm')).toBeNull();

    button('reload')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ refresh: true });
  });

  it('cancelWithoutError_ClosesWithUndefined', () => {
    configure();
    button('cancel')!.click();
    expect(dialogRef.close).toHaveBeenCalledOnceWith(undefined);
  });

  it('i18n_TrAndEnHaveSameKeys', () => {
    const keys = (o: object): string[] =>
      Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' ? keys(v).map((c) => `${k}.${c}`) : [k]));
    expect(keys(adminEn.teacherSchool).sort()).toEqual(keys(adminTr.teacherSchool).sort());
  });
});
