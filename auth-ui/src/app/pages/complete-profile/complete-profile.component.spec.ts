import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NEVER, of, throwError } from 'rxjs';

import { CompleteProfileComponent } from './complete-profile.component';
import { AuthService } from '../../services/auth.service';
import { Grade, School } from '../../models/registration.model';

const SCHOOLS: School[] = [
  { id: 1, name: 'Atatürk Lisesi' },
  { id: 2, name: 'Cumhuriyet Ortaokulu' },
];

const GRADES: Grade[] = [{ id: 10, name: '10. Sınıf' }];

describe('CompleteProfileComponent', () => {
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let snackBarSpy: jasmine.SpyObj<MatSnackBar>;

  function createComponent(options?: {
    role?: string;
    schools$?: ReturnType<typeof of<School[]>> | ReturnType<typeof throwError>;
  }): ComponentFixture<CompleteProfileComponent> {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'getRealmRoles',
      'getGrades',
      'getSchools',
      'registerStudentProfile',
      'registerTeacherProfile',
      'registerParentProfile',
      'isCachedUserCurrent',
      'clearCachedUser',
    ]);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    snackBarSpy = jasmine.createSpyObj('MatSnackBar', ['open']);

    authServiceSpy.getRealmRoles.and.returnValue([]);
    authServiceSpy.getGrades.and.returnValue(of(GRADES));
    authServiceSpy.getSchools.and.returnValue((options?.schools$ as any) ?? of(SCHOOLS));
    // Deliberately never emit on register calls, so the success handler's
    // `window.location.href = ...` write (a real navigation) never runs
    // during these tests — see callback.component.spec.ts for precedent.
    authServiceSpy.registerStudentProfile.and.returnValue(NEVER);
    authServiceSpy.registerTeacherProfile.and.returnValue(NEVER);
    authServiceSpy.registerParentProfile.and.returnValue(NEVER);

    TestBed.configureTestingModule({
      imports: [CompleteProfileComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: (key: string) => (key === 'role' ? options?.role ?? null : null) } },
          },
        },
        { provide: Router, useValue: routerSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: MatSnackBar, useValue: snackBarSpy },
      ],
    }).compileComponents();

    return TestBed.createComponent(CompleteProfileComponent);
  }

  it('should create', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('ngOnInit_StudentRole_CallsGetSchoolsAndRendersOptionsInStudentDropdown', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();

    expect(authServiceSpy.getSchools).toHaveBeenCalled();
    expect(fixture.componentInstance.schools()).toEqual(SCHOOLS);

    const options: NodeListOf<HTMLOptionElement> = fixture.nativeElement.querySelectorAll('#schoolId option');
    // one placeholder ("Okul seç (opsiyonel)") + one per school
    expect(options.length).toBe(SCHOOLS.length + 1);
    expect(options[1].textContent).toContain('Atatürk Lisesi');
    expect(options[2].textContent).toContain('Cumhuriyet Ortaokulu');
  });

  it('ngOnInit_TeacherRole_CallsGetSchoolsAndRendersOptionsInTeacherDropdown', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();

    expect(authServiceSpy.getSchools).toHaveBeenCalled();

    const options: NodeListOf<HTMLOptionElement> = fixture.nativeElement.querySelectorAll('#teacherSchoolId option');
    expect(options.length).toBe(SCHOOLS.length + 1);
    expect(options[1].textContent).toContain('Atatürk Lisesi');
  });

  it('selectSchool_StudentPicksSchoolFromDropdown_UpdatesFormControlValueToSchoolId', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#schoolId');
    select.value = select.options[2].value; // second real school (index 0 is placeholder)
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.studentForm.get('schoolId')?.value).toBe(2);
  });

  it('selectSchool_TeacherPicksSchoolFromDropdown_UpdatesFormControlValueToSchoolId', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#teacherSchoolId');
    select.value = select.options[1].value; // first real school
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.teacherForm.get('schoolId')?.value).toBe(1);
  });

  it('onSubmit_StudentWithSchoolSelected_SendsSelectedSchoolIdInPayload', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.studentForm.setValue({ studentNumber: '123', schoolId: 2, gradeId: 10 });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: 2,
      gradeId: 10,
    });
  });

  it('onSubmit_TeacherWithSchoolSelected_SendsSelectedSchoolIdInPayload', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 1 });
    component.onSubmit();

    expect(authServiceSpy.registerTeacherProfile).toHaveBeenCalledWith({ schoolId: 1, isIndependentTutor: false });
  });

  it('onSubmit_StudentWithoutSchoolSelected_SendsNullSchoolIdAndIsNotBlocked', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.studentForm.setValue({ studentNumber: '123', schoolId: null, gradeId: 10 });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: null,
      gradeId: 10,
    });
  });

  it('onSubmit_TeacherWithoutSchoolSelected_SendsNullSchoolIdAndIsNotBlocked', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: null });
    component.onSubmit();

    expect(authServiceSpy.registerTeacherProfile).toHaveBeenCalledWith({ schoolId: null, isIndependentTutor: false });
  });

  it('onSubmit_TeacherSelectsIndependentAfterPickingSchool_SendsNullSchoolIdAndIsIndependentTutorTrue', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    // Önce okul seçilir (ör. kullanıcı fikrini değiştirmeden önce).
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#teacherSchoolId');
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(component.teacherForm.get('schoolId')?.value).toBe(1);

    // Sonra "Bağımsız özel ders veriyorum" radio'su seçilir.
    const independentRadio: HTMLInputElement = fixture.nativeElement.querySelector('#teacherIndependentTutor');
    independentRadio.checked = true;
    independentRadio.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(component.isIndependentTutor()).toBeTrue();
    // Okul seçim alanı UI'dan kaldırılmalı.
    expect(fixture.nativeElement.querySelector('#teacherSchoolId')).toBeNull();

    component.onSubmit();

    expect(authServiceSpy.registerTeacherProfile).toHaveBeenCalledWith({
      schoolId: null,
      isIndependentTutor: true,
    });
  });

  it('onSubmit_TeacherFormValueSetProgrammaticallyToIndependentWithStaleSchoolId_StripsSchoolIdFromPayload', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    // Eski (artık sızmaması gereken) bir schoolId değeriyle birlikte bağımsız işaretlenir.
    component.teacherForm.setValue({ isIndependentTutor: true, schoolId: 2 });
    component.onSubmit();

    expect(authServiceSpy.registerTeacherProfile).toHaveBeenCalledWith({
      schoolId: null,
      isIndependentTutor: true,
    });
  });

  it('onSubmit_TeacherKeepsDefaultSchoolAffiliatedOption_SendsChosenSchoolIdAndIsIndependentTutorFalse', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    // Varsayılan seçim değiştirilmeden (okula bağlı) okul seçilip submit edilir.
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#teacherSchoolId');
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(component.isIndependentTutor()).toBeFalse();

    component.onSubmit();

    expect(authServiceSpy.registerTeacherProfile).toHaveBeenCalledWith({
      schoolId: 1,
      isIndependentTutor: false,
    });
  });

  it('ngOnInit_GetSchoolsFails_SetsSchoolsErrorAndHidesDropdown', () => {
    const fixture = createComponent({ role: 'student', schools$: throwError(() => new Error('network error')) });
    fixture.detectChanges();

    expect(fixture.componentInstance.schoolsError()).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#schoolId')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Okul listesi yüklenemedi.');
  });

  it('onSubmit_GetSchoolsFailed_StillAllowsSubmitWithNullSchoolId', () => {
    const fixture = createComponent({ role: 'student', schools$: throwError(() => new Error('network error')) });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.studentForm.setValue({ studentNumber: '123', schoolId: null, gradeId: 10 });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: null,
      gradeId: 10,
    });
  });

  // ── Issue #234: öğretmen okul onayı beklemesi ──────────────────────────────

  it('onSubmit_TeacherRegistrationSuccessWithSchoolApprovalPending_SetsApprovalPendingSignal', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerTeacherProfile.and.returnValue(
      of({
        accessToken: 'token',
        expiresIn: 3600,
        profileId: 99,
        approvalStatus: 0,
        requestedSchoolId: 1,
        schoolApprovalPending: true,
        teacherAccountApproved: true,
      }),
    );

    authServiceSpy.isCachedUserCurrent.and.returnValue(false);

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 1 });
    component.onSubmit();
    localStorage.removeItem('auth_token');
    localStorage.removeItem('user_role');

    expect(component.schoolApprovalPending()).toBeTrue();
    expect(component.accountApprovalPending()).toBeFalse();
    expect(component.isLoading()).toBeFalse();
  });

  // ── Issue #287: yeni öğretmen hesabı yönetici onayı bekler ─────────────────

  for (const scenario of [
    { name: 'NoSchoolNotIndependent', form: { isIndependentTutor: false, schoolId: null }, schoolPending: false },
    { name: 'Independent', form: { isIndependentTutor: true, schoolId: null }, schoolPending: false },
    { name: 'SchoolRequest', form: { isIndependentTutor: false, schoolId: 1 }, schoolPending: true },
  ]) {
    it(`onSubmit_Teacher${scenario.name}AccountNotApproved_ShowsAccountPendingCardAndContinuesToPendingPage`, () => {
      const fixture = createComponent({ role: 'teacher' });
      fixture.detectChanges();
      const component = fixture.componentInstance;
      const redirectSpy = spyOn(component, 'redirectTo');
      authServiceSpy.isCachedUserCurrent.and.returnValue(false);
      authServiceSpy.registerTeacherProfile.and.returnValue(
        of({
          accessToken: 'token',
          expiresIn: 3600,
          profileId: 99,
          approvalStatus: 0,
          requestedSchoolId: scenario.schoolPending ? 1 : null,
          schoolApprovalPending: scenario.schoolPending,
          teacherAccountApproved: false,
        }),
      );

      component.teacherForm.setValue(scenario.form);
      component.onSubmit();
      localStorage.removeItem('auth_token');
      localStorage.removeItem('user_role');
      fixture.detectChanges();

      expect(component.accountApprovalPending()).toBeTrue();
      expect(component.schoolApprovalPending()).toBe(scenario.schoolPending);
      expect(redirectSpy).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('[data-testid="account-approval-pending"]')).not.toBeNull();

      component.continueAfterPending();
      expect(redirectSpy).toHaveBeenCalledOnceWith('/teacher-approval-pending');
    });
  }

  it('continueAfterPending_OnlySchoolApprovalPending_GoesToTests', () => {
    const fixture = createComponent({ role: 'teacher' });
    const component = fixture.componentInstance;
    const redirectSpy = spyOn(component, 'redirectTo');
    component.schoolApprovalPending.set(true);

    component.continueAfterPending();

    expect(redirectSpy).toHaveBeenCalledOnceWith('/tests');
  });

  it('onSubmit_TeacherRegistrationConflict409_SetsSubmitErrorKeepsFormOpen', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const errorMsg = 'Mevcut öğretmen kaydınızın okul bilgisi değiştirilemez.';
    authServiceSpy.registerTeacherProfile.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { message: errorMsg } })),
    );

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 1 });
    component.onSubmit();

    expect(component.submitError()).toBe(errorMsg);
    expect(component.isLoading()).toBeFalse();
    expect(component.schoolApprovalPending()).toBeFalse();
    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });

  // ── Issue #259: öğrenci kaydında okul kilidi / eşzamanlı kayıt 409'ları ────

  function submitStudentWith409(error: unknown) {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerStudentProfile.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error })),
    );
    const openSpy = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    const redirectSpy = spyOn(component, 'redirectTo');

    component.studentForm.setValue({ studentNumber: '123', schoolId: 2, gradeId: 10 });
    component.onSubmit();
    fixture.detectChanges();
    return { fixture, component, openSpy, redirectSpy };
  }

  it('onSubmit_StudentSchoolLocked409WithMessage_ShowsServerMessageAndStaysOnForm', () => {
    const errorMsg = 'Okul bilgisi ilk kayıttan sonra değiştirilemez.';
    const { fixture, component, openSpy, redirectSpy } = submitStudentWith409({ message: errorMsg });

    expect(component.submitError()).toBe(errorMsg);
    expect(component.isLoading()).toBeFalse();
    expect(openSpy).not.toHaveBeenCalled();
    expect(redirectSpy).not.toHaveBeenCalled();
    expect(routerSpy.navigate).not.toHaveBeenCalled();
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement | null;
    expect(alert?.textContent).toContain(errorMsg);
  });

  it('onSubmit_StudentRegistrationConflict409WithMessage_ShowsServerMessageAndStaysOnForm', () => {
    const errorMsg = 'Kaydınız şu anda başka bir istekle işleniyor, lütfen tekrar deneyin.';
    const { component, openSpy, redirectSpy } = submitStudentWith409({ message: errorMsg });

    expect(component.submitError()).toBe(errorMsg);
    expect(openSpy).not.toHaveBeenCalled();
    expect(redirectSpy).not.toHaveBeenCalled();
  });

  it('onSubmit_Student409WithoutBody_KeepsAlreadyCompletedSnackbarAndRedirectsToTests', () => {
    const { component, openSpy, redirectSpy } = submitStudentWith409(null);

    expect(openSpy).toHaveBeenCalledWith('Profiliniz zaten tamamlanmış.', 'Tamam', jasmine.any(Object));
    expect(redirectSpy).toHaveBeenCalledWith('/tests');
    expect(component.submitError()).toBeNull();
  });

  it('onSubmit_ParentRegistrationProfileNotResolved404_SetsSubmitErrorWithoutRedirect', () => {
    const fixture = createComponent({ role: 'parent' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerParentProfile.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Kullanıcı çözümlenemedi.' } })),
    );
    // MatSnackBarModule bileşenin kendi ortam enjektöründe MatSnackBar sağlar; kök sahte örnek yerine o izlenir.
    const openSpy = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');

    component.onSubmit();

    expect(authServiceSpy.registerParentProfile).toHaveBeenCalled();
    expect(component.submitError()).toContain('doğrulanamadı');
    expect(component.isLoading()).toBeFalse();
    expect(routerSpy.navigate).not.toHaveBeenCalled();
    expect(openSpy).not.toHaveBeenCalled();
  });

  it('onSubmit_ParentRegistrationSessionExpired401_ShowsSessionMessageAndNavigatesToLogin', () => {
    const fixture = createComponent({ role: 'parent' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerParentProfile.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 401 })),
    );
    const openSpy = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');

    component.onSubmit();

    expect(openSpy).toHaveBeenCalledWith(
      'Oturumunuz sona ermiş, lütfen tekrar giriş yapın.',
      'Kapat',
      jasmine.any(Object),
    );
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    expect(component.submitError()).toBeNull();
  });
});
