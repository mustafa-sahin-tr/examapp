import { ComponentFixture, TestBed, fakeAsync, flush } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NEVER, of, throwError } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { RegisterWizardComponent, retryAfterSecondsOf } from './register-wizard.component';
import { AuthService } from '../../services/auth.service';
import { StudentService } from '../../services/student.service';
import { TeacherService } from '../../services/teacher.service';
import { ParentService } from '../../services/parent.service';
import { GradesService } from '../../services/grades.service';
import { SchoolService } from '../../services/school.service';
import { School } from '../../models/taxonomy';
import { Grade } from '../../models/student';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import registerTr from '../../../../public/i18n/register/tr.json';
import registerEn from '../../../../public/i18n/register/en.json';

const GRADES: Grade[] = [{ id: 10, name: '10. Sınıf' }];
const SCHOOLS: School[] = [
  { id: 7, name: 'Ankara Fen Lisesi', provinceId: 6, provinceName: 'Ankara', districtId: 1, districtName: 'Çankaya', addressLine: null },
];

describe('RegisterWizardComponent (Issue #234)', () => {
  let fixture: ComponentFixture<RegisterWizardComponent>;
  let component: RegisterWizardComponent;
  let authService: jasmine.SpyObj<AuthService>;
  let teacherService: jasmine.SpyObj<TeacherService>;
  let studentService: jasmine.SpyObj<StudentService>;
  let router: jasmine.SpyObj<Router>;

  function createComponent(role?: string): void {
    authService = jasmine.createSpyObj('AuthService', [
      'getRealmRoles',
      'isCachedUserCurrent',
      'clearCachedUser',
      'setUser',
    ]);
    studentService = jasmine.createSpyObj('StudentService', ['register']);
    teacherService = jasmine.createSpyObj('TeacherService', ['register']);
    jasmine.createSpyObj('ParentService', ['register']);
    jasmine.createSpyObj('GradesService', ['getGrades']);
    router = jasmine.createSpyObj('Router', ['navigate']);
    jasmine.createSpyObj('MatSnackBar', ['open']);

    authService.getRealmRoles.and.returnValue([]);
    authService.isCachedUserCurrent.and.returnValue(true);

    TestBed.configureTestingModule({
      imports: [RegisterWizardComponent, translocoTestingModule({ langs: { 'register/tr': registerTr } })],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: { get: (key: string) => (key === 'role' ? role ?? null : null) },
            },
          },
        },
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: authService },
        { provide: StudentService, useValue: studentService },
        { provide: TeacherService, useValue: teacherService },
        { provide: ParentService, useValue: jasmine.createSpyObj('ParentService', ['register']) },
        { provide: GradesService, useValue: jasmine.createSpyObj('GradesService', ['getGrades'], { getGrades: () => of(GRADES) }) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
        { provide: SchoolService, useValue: { getSchools: () => of(SCHOOLS) } },
        provideNoopAnimations(),
      ],
    });

    fixture = TestBed.createComponent(RegisterWizardComponent);
    component = fixture.componentInstance;
  }

  it('should create', () => {
    createComponent('teacher');
    expect(component).toBeTruthy();
  });

  // ── Issue #234: öğretmen okul onayı beklemesi ──────────────────────────────

  it('submit_TeacherRegistrationSuccessWithSchoolApprovalPending_SetsPendingSignalTrue', fakeAsync(() => {
    createComponent('teacher');
    fixture.detectChanges(); // ngOnInit
    expect(component.role()).toBe('teacher');
    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
    teacherService.register.and.returnValue(
      of({
        accessToken: 'token',
        expiresIn: 3600,
        profileId: 42,
        approvalStatus: 0,
        requestedSchoolId: 1,
        schoolApprovalPending: true,
        teacherAccountApproved: true,
      }),
    );

    component.submit();
    flush();

    expect(component.schoolApprovalPending()).toBeTrue();
  }));

  it('continueAfterPending_NavigatesToTests', () => {
    createComponent('teacher');
    component.schoolApprovalPending.set(true);

    component.continueAfterPending();

    expect(router.navigate).toHaveBeenCalledWith(['/tests']);
  });

  // ── Issue #234: 409 hata mesajı (okul değişikliğine izin yok) ──────────────

  it('submit_TeacherRegistrationConflict409_SetSubmitErrorSignal', fakeAsync(() => {
    createComponent('teacher');
    fixture.detectChanges(); // ngOnInit
    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
    const errorMsg = 'Okul bilgisi bu adımla değiştirilemez.';
    teacherService.register.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: errorMsg },
            url: 'http://test',
          }),
      ),
    );

    component.submit();
    flush();

    expect(teacherService.register).toHaveBeenCalled();
    expect(component.isSubmitting()).toBeFalse();
  }));

  it('submit_StudentRegistrationConflict409_SetSubmitErrorSignal', fakeAsync(() => {
    createComponent('student');
    fixture.detectChanges(); // ngOnInit
    component.studentForm.setValue({ studentNumber: '123', schoolId: 7, gradeId: 10 });
    const errorMsg = 'Profil zaten tamamlanmış.';
    studentService.register.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: errorMsg },
            url: 'http://test',
          }),
      ),
    );

    component.submit();
    flush();

    expect(studentService.register).toHaveBeenCalled();
    expect(component.isSubmitting()).toBeFalse();
  }));

  it('back_ClearsErrorAndResetRole', () => {
    createComponent('teacher');
    component.submitError.set('Error message');
    component.role.set('teacher');

    component.back();

    expect(component.submitError()).toBeNull();
    expect(component.role()).toBeNull();
  });

  // ── Issue #287: yeni öğretmen hesabı yönetici onayı bekler ─────────────────

  describe('teacher account approval pending (issue #287)', () => {
    afterEach(() => {
      localStorage.removeItem('auth_token');
      localStorage.removeItem('user_role');
      localStorage.removeItem('user');
    });

    function registerTeacher(schoolApprovalPending: boolean, message?: string): void {
      teacherService.register.and.returnValue(
        of({
          accessToken: 'token',
          expiresIn: 3600,
          profileId: 42,
          approvalStatus: 0,
          requestedSchoolId: schoolApprovalPending ? 1 : null,
          schoolApprovalPending,
          teacherAccountApproved: false,
          message,
        }),
      );
    }

    it('submit_TeacherAccountNotApprovedWithServerMessage_ShowsServerMessage', fakeAsync(() => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
      registerTeacher(false, 'Sunucu: hesabınız onay bekliyor.');
      const internals = component as unknown as { notify: (k: string) => void; notifyText: (m: string) => void };
      const notify = spyOn(internals, 'notify');
      const notifyText = spyOn(internals, 'notifyText');

      component.submit();
      flush();

      expect(notifyText).toHaveBeenCalledOnceWith('Sunucu: hesabınız onay bekliyor.');
      expect(notify).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledOnceWith(['/teacher-approval-pending']);
    }));

    for (const schoolApprovalPending of [false, true]) {
      it(`submit_TeacherAccountNotApproved_schoolPending=${schoolApprovalPending}_NavigatesToPendingPageWithMessage`, fakeAsync(() => {
        createComponent('teacher');
        fixture.detectChanges();
        component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
        registerTeacher(schoolApprovalPending);
        // MatSnackBarModule komponentin kendi injector'ına gerçek MatSnackBar verir; bildirim anahtarı doğrulanır.
        const notify = spyOn(component as unknown as { notify: (key: string) => void }, 'notify');

        component.submit();
        flush();

        expect(router.navigate).toHaveBeenCalledOnceWith(['/teacher-approval-pending']);
        expect(router.navigate).not.toHaveBeenCalledWith(['/tests']);
        expect(component.schoolApprovalPending()).toBeFalse();
        expect(notify).toHaveBeenCalledOnceWith('wizard.teacherAccountPending');
      }));
    }

    it('i18n_TeacherAccountPendingMessage_TrAndEn', () => {
      expect(registerTr.wizard.teacherAccountPending).toBe('Öğretmen kaydınız alındı; hesabınız yönetici onayı bekliyor.');
      expect(registerEn.wizard.teacherAccountPending).toContain('awaiting administrator approval');
    });

    it('submit_TeacherAccountNotApproved_WritesApprovalStateToUserSignal', fakeAsync(() => {
      localStorage.setItem('user', JSON.stringify({ id: 1, keycloakId: 'k', role: '' }));
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
      registerTeacher(false);

      component.submit();
      flush();

      expect(authService.setUser).toHaveBeenCalledTimes(1);
      const written = authService.setUser.calls.mostRecent().args[0];
      expect(written?.role).toBe('Teacher');
      expect(written?.teacher?.id).toBe(42);
      expect(written?.teacher?.teacherAccountApproved).toBeFalse();
      expect(written?.teacher?.teacherApplicationStatus).toBe('Pending');
    }));

    it('submit_TeacherAccountApproved_KeepsExistingTestsRedirect', fakeAsync(() => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
      teacherService.register.and.returnValue(
        of({
          accessToken: 'token',
          expiresIn: 3600,
          profileId: 42,
          approvalStatus: 1,
          requestedSchoolId: null,
          schoolApprovalPending: false,
          teacherAccountApproved: true,
        }),
      );

      component.submit();
      flush();

      expect(router.navigate).toHaveBeenCalledOnceWith(['/tests']);
    }));
  });
  // ── Issue #277 (madde 6): okul serbest metin değil, listeden seçilir; backend `schoolId` okur ─────────────

  describe('school selection payload (issue #277)', () => {
    it('submit_Teacher_SendsSchoolIdAndIsIndependentTutor_WithoutSchoolName', () => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
      teacherService.register.and.returnValue(NEVER);

      component.submit();

      expect(teacherService.register).toHaveBeenCalledOnceWith({ schoolId: 7, isIndependentTutor: false });
    });

    it('submit_IndependentTeacher_DropsPreviouslySelectedSchool', () => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: true, schoolId: 7 });
      teacherService.register.and.returnValue(NEVER);

      component.submit();

      expect(teacherService.register).toHaveBeenCalledOnceWith({ schoolId: null, isIndependentTutor: true });
    });

    it('submit_Student_SendsSchoolIdGradeIdAndNumber_WithoutSchoolName', () => {
      createComponent('student');
      fixture.detectChanges();
      component.studentForm.setValue({ studentNumber: ' 123 ', schoolId: 7, gradeId: 10 });
      studentService.register.and.returnValue(NEVER);

      component.submit();

      expect(studentService.register).toHaveBeenCalledOnceWith({ studentNumber: '123', schoolId: 7, gradeId: 10 });
    });

    it('submit_StudentWithoutGrade_DoesNotSend', () => {
      createComponent('student');
      fixture.detectChanges();
      component.studentForm.setValue({ studentNumber: '123', schoolId: null, gradeId: null });

      component.submit();

      expect(studentService.register).not.toHaveBeenCalled();
    });

    it('template_StudentAndTeacherForms_RenderSchoolSelectInsteadOfFreeText', () => {
      createComponent('student');
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('app-school-select')).not.toBeNull();
      expect(el.querySelector('input[formcontrolname="schoolName"]')).toBeNull();

      component.pickRole('teacher');
      fixture.detectChanges();
      expect(el.querySelector('app-school-select')).not.toBeNull();
    });

    it('template_IndependentTeacher_HidesSchoolSelect', () => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.controls.isIndependentTutor.setValue(true);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).querySelector('app-school-select')).toBeNull();
    });
  });

  // ── Issue #277 (madde 2): reddedilen okul talebinden sonra 24 saat bekleme → 429 ──────────────────────

  describe('school request cooldown 429 (issue #277)', () => {
    function cooldown429(body: object | null, headers?: Record<string, string>): HttpErrorResponse {
      return new HttpErrorResponse({
        status: 429,
        error: body,
        headers: new HttpHeaders(headers ?? {}),
        url: 'http://test',
      });
    }

    it('submit_Teacher429_ShowsServerMessageAndRemainingTime', fakeAsync(() => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
      const message = 'Okul talebiniz reddedildi; yeni talep için beklemeniz gerekiyor.';
      teacherService.register.and.returnValue(
        throwError(() =>
          cooldown429(
            { message, retryAfterSeconds: 5 * 3600 + 30 * 60, retryAfterUtc: '2030-01-01T00:00:00Z' },
            { 'Retry-After': '19800' },
          ),
        ),
      );

      component.submit();
      flush();
      fixture.detectChanges();

      expect(component.isSubmitting()).toBeFalse();
      expect(component.submitError()).toBe(message);
      expect(component.retryAfter()).toEqual({ hours: 5, minutes: 30 });
      expect(router.navigate).not.toHaveBeenCalled();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('[data-testid="submit-error"]')?.textContent).toContain(message);
      expect(el.querySelector('[data-testid="retry-after"]')?.textContent?.trim()).toBe(
        'Yeni bir okul talebini yaklaşık 5 saat 30 dakika sonra gönderebilirsiniz.',
      );
    }));

    it('submit_Teacher429WithoutBody_UsesRetryAfterHeaderAndFallbackText', fakeAsync(() => {
      createComponent('teacher');
      fixture.detectChanges();
      component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 7 });
      teacherService.register.and.returnValue(throwError(() => cooldown429(null, { 'Retry-After': '90' })));

      component.submit();
      flush();
      fixture.detectChanges();

      expect(component.submitError()).toBeNull();
      expect(component.retryAfter()).toEqual({ hours: 0, minutes: 2 });
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('[data-testid="submit-error"]')?.textContent).toContain(registerTr.wizard.schoolRequestCooldown);
      expect(el.querySelector('[data-testid="retry-after"]')?.textContent?.trim()).toBe(
        'Yeni bir okul talebini yaklaşık 2 dakika sonra gönderebilirsiniz.',
      );
    }));

    it('back_After429_ClearsCooldown', () => {
      createComponent('teacher');
      component.submitError.set('x');
      component.retryAfterSeconds.set(60);

      component.back();

      expect(component.retryAfter()).toBeNull();
      expect(component.submitError()).toBeNull();
    });

    it('retryAfterSecondsOf_FallsBackToRetryAfterUtc', () => {
      const now = Date.parse('2030-01-01T00:00:00Z');
      const err = cooldown429(null);
      expect(retryAfterSecondsOf(err, { retryAfterUtc: '2030-01-01T01:00:00Z' }, now)).toBe(3600);
      expect(retryAfterSecondsOf(err, { retryAfterUtc: '2029-12-31T23:00:00Z' }, now)).toBeNull();
      expect(retryAfterSecondsOf(err, null, now)).toBeNull();
    });

    it('i18n_CooldownTexts_TrAndEn', () => {
      expect(registerEn.wizard.retryAfterHours).toContain('{{hours}}');
      expect(registerEn.wizard.retryAfterMinutes).toContain('{{minutes}}');
      expect(registerEn.wizard.schoolRequestCooldown).toBeTruthy();
      expect(registerTr.fields.studentSchoolHint).toContain('yönetici');
    });
  });
});
