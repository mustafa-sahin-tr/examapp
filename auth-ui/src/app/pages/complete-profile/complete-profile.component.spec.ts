import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NEVER, of, throwError } from 'rxjs';

import { CompleteProfileComponent, cooldownMessage } from './complete-profile.component';
import { AuthService } from '../../services/auth.service';
import { Grade, School } from '../../models/registration.model';

const SCHOOLS: School[] = [
  { id: 1, name: 'Atatürk Lisesi' },
  { id: 2, name: 'Cumhuriyet Ortaokulu' },
];

const GRADES: Grade[] = [{ id: 10, name: '10. Sınıf' }];

/** Issue #440: veli kuralına tabi öğrencinin zorunlu veli bilgisi. */
const PARENT_INPUT = { parentFullName: 'Ayşe Yılmaz', parentEmail: 'ayse@example.com', parentPhone: '0532 123 45 67' };

describe('CompleteProfileComponent', () => {
  let authServiceSpy: MockedObject<AuthService>;
  let routerSpy: MockedObject<Router>;
  let snackBarSpy: MockedObject<MatSnackBar>;

  function createComponent(options?: {
    role?: string;
    schools$?: ReturnType<typeof of<School[]>> | ReturnType<typeof throwError>;
  }): ComponentFixture<CompleteProfileComponent> {
    authServiceSpy = {
      getRealmRoles: vi.fn().mockName('AuthService.getRealmRoles'),
      getGrades: vi.fn().mockName('AuthService.getGrades'),
      getSchools: vi.fn().mockName('AuthService.getSchools'),
      registerStudentProfile: vi.fn().mockName('AuthService.registerStudentProfile'),
      registerTeacherProfile: vi.fn().mockName('AuthService.registerTeacherProfile'),
      registerParentProfile: vi.fn().mockName('AuthService.registerParentProfile'),
      isCachedUserCurrent: vi.fn().mockName('AuthService.isCachedUserCurrent'),
      clearCachedUser: vi.fn().mockName('AuthService.clearCachedUser'),
    } as unknown as MockedObject<AuthService>;
    routerSpy = {
      navigate: vi.fn().mockName('Router.navigate'),
    } as unknown as MockedObject<Router>;
    snackBarSpy = {
      open: vi.fn().mockName('MatSnackBar.open'),
    } as unknown as MockedObject<MatSnackBar>;

    authServiceSpy.getRealmRoles.mockReturnValue([]);
    authServiceSpy.getGrades.mockReturnValue(of(GRADES));
    authServiceSpy.getSchools.mockReturnValue((options?.schools$ as any) ?? of(SCHOOLS));
    // Deliberately never emit on register calls, so the success handler's
    // `window.location.href = ...` write (a real navigation) never runs
    // during these tests — see callback.component.spec.ts for precedent.
    authServiceSpy.registerStudentProfile.mockReturnValue(NEVER);
    authServiceSpy.registerTeacherProfile.mockReturnValue(NEVER);
    authServiceSpy.registerParentProfile.mockReturnValue(NEVER);

    TestBed.configureTestingModule({
      imports: [CompleteProfileComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: (key: string) => (key === 'role' ? (options?.role ?? null) : null) } },
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

    component.studentForm.setValue({ studentNumber: '123', schoolId: 2, gradeId: 10, ...PARENT_INPUT });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: 2,
      gradeId: 10,
      ...PARENT_INPUT,
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

    component.studentForm.setValue({ studentNumber: '123', schoolId: null, gradeId: 10, ...PARENT_INPUT });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: null,
      gradeId: 10,
      ...PARENT_INPUT,
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

    expect(component.isIndependentTutor()).toBe(true);
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

    expect(component.isIndependentTutor()).toBe(false);

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

    component.studentForm.setValue({ studentNumber: '123', schoolId: null, gradeId: 10, ...PARENT_INPUT });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: null,
      gradeId: 10,
      ...PARENT_INPUT,
    });
  });

  // ── Issue #234: öğretmen okul onayı beklemesi ──────────────────────────────

  it('onSubmit_TeacherRegistrationSuccessWithSchoolApprovalPending_SetsApprovalPendingSignal', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerTeacherProfile.mockReturnValue(
      of({
        accessToken: 'token',
        expiresIn: 3600,
        profileId: 99,
        approvalStatus: 0,
        requestedSchoolId: 1,
        schoolApprovalPending: true,
        teacherAccountApproved: true,
      })
    );

    authServiceSpy.isCachedUserCurrent.mockReturnValue(false);

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 1 });
    component.onSubmit();
    localStorage.removeItem('auth_token');
    localStorage.removeItem('user_role');

    expect(component.schoolApprovalPending()).toBe(true);
    expect(component.accountApprovalPending()).toBe(false);
    expect(component.isLoading()).toBe(false);
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
      const redirectSpy = vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);
      authServiceSpy.isCachedUserCurrent.mockReturnValue(false);
      authServiceSpy.registerTeacherProfile.mockReturnValue(
        of({
          accessToken: 'token',
          expiresIn: 3600,
          profileId: 99,
          approvalStatus: 0,
          requestedSchoolId: scenario.schoolPending ? 1 : null,
          schoolApprovalPending: scenario.schoolPending,
          teacherAccountApproved: false,
        })
      );

      component.teacherForm.setValue(scenario.form);
      component.onSubmit();
      localStorage.removeItem('auth_token');
      localStorage.removeItem('user_role');
      fixture.detectChanges();

      expect(component.accountApprovalPending()).toBe(true);
      expect(component.schoolApprovalPending()).toBe(scenario.schoolPending);
      expect(redirectSpy).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('[data-testid="account-approval-pending"]')).not.toBeNull();

      component.continueAfterPending();
      expect(redirectSpy).toHaveBeenCalledTimes(1);
      expect(redirectSpy).toHaveBeenCalledWith('/teacher-approval-pending');
    });
  }

  it('onSubmit_TeacherAccountNotApprovedWithServerMessage_ShowsServerMessageInCard', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);
    authServiceSpy.isCachedUserCurrent.mockReturnValue(false);
    authServiceSpy.registerTeacherProfile.mockReturnValue(
      of({
        accessToken: 'token',
        expiresIn: 3600,
        profileId: 99,
        approvalStatus: 0,
        requestedSchoolId: null,
        schoolApprovalPending: false,
        teacherAccountApproved: false,
        message: 'Öğretmen kaydınız alındı; hesabınız yönetici onayı bekliyor.',
      })
    );

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: null });
    component.onSubmit();
    localStorage.removeItem('auth_token');
    localStorage.removeItem('user_role');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="account-approval-message"]')?.textContent?.trim()).toBe(
      'Öğretmen kaydınız alındı; hesabınız yönetici onayı bekliyor.'
    );
  });

  it('continueAfterPending_OnlySchoolApprovalPending_GoesToTests', () => {
    const fixture = createComponent({ role: 'teacher' });
    const component = fixture.componentInstance;
    const redirectSpy = vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);
    component.schoolApprovalPending.set(true);

    component.continueAfterPending();

    expect(redirectSpy).toHaveBeenCalledTimes(1);

    expect(redirectSpy).toHaveBeenCalledWith('/tests');
  });

  it('onSubmit_TeacherRegistrationConflict409_SetsSubmitErrorKeepsFormOpen', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const errorMsg = 'Mevcut öğretmen kaydınızın okul bilgisi değiştirilemez.';
    authServiceSpy.registerTeacherProfile.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { message: errorMsg } }))
    );

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 1 });
    component.onSubmit();

    expect(component.submitError()).toBe(errorMsg);
    expect(component.isLoading()).toBe(false);
    expect(component.schoolApprovalPending()).toBe(false);
    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });

  // ── Issue #277: reddedilen okul talebinden sonra 24 saat bekleme (429) ────

  it('onSubmit_TeacherSchoolRequestCooldown429_ShowsServerMessageWithRemainingTimeAndStaysOnForm', () => {
    const fixture = createComponent({ role: 'teacher' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const redirectSpy = vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);
    const openSpy = vi
      .spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open')
      .mockReturnValue(undefined as never);
    authServiceSpy.registerTeacherProfile.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 429,
            error: {
              message: 'Okul talebiniz reddedildi.',
              retryAfterSeconds: 23 * 3600 + 59 * 60 + 1,
              retryAfterUtc: null,
            },
            headers: new HttpHeaders({ 'Retry-After': '86341' }),
          })
      )
    );

    component.teacherForm.setValue({ isIndependentTutor: false, schoolId: 1 });
    component.onSubmit();
    fixture.detectChanges();

    expect(component.submitError()).toBe(
      'Okul talebiniz reddedildi. Yeni bir okul talebini yaklaşık 24 saat 0 dakika sonra gönderebilirsin.'
    );
    expect(component.isLoading()).toBe(false);
    expect(openSpy).not.toHaveBeenCalled();
    expect(redirectSpy).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent).toContain(
      'Okul talebiniz reddedildi.'
    );
  });

  it('cooldownMessage_NoBody_UsesRetryAfterHeaderAndDefaultText', () => {
    const err = new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '90' }) });
    expect(cooldownMessage(err)).toBe(
      'Reddedilen okul talebinizden sonra bekleme süresi henüz dolmadı. Yeni bir okul talebini yaklaşık 2 dakika sonra gönderebilirsin.'
    );
  });

  // ── Issue #259: öğrenci kaydında okul kilidi / eşzamanlı kayıt 409'ları ────

  function submitStudentWith409(error: unknown) {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerStudentProfile.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error }))
    );
    const openSpy = vi
      .spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open')
      .mockReturnValue(undefined as never);
    const redirectSpy = vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);

    component.studentForm.setValue({ studentNumber: '123', schoolId: 2, gradeId: 10, ...PARENT_INPUT });
    component.onSubmit();
    fixture.detectChanges();
    return { fixture, component, openSpy, redirectSpy };
  }

  it('onSubmit_StudentSchoolLocked409WithMessage_ShowsServerMessageAndStaysOnForm', () => {
    const errorMsg = 'Okul bilgisi ilk kayıttan sonra değiştirilemez.';
    const { fixture, component, openSpy, redirectSpy } = submitStudentWith409({ message: errorMsg });

    expect(component.submitError()).toBe(errorMsg);
    expect(component.isLoading()).toBe(false);
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

    expect(openSpy).toHaveBeenCalledWith('Profiliniz zaten tamamlanmış.', 'Tamam', expect.any(Object));
    expect(redirectSpy).toHaveBeenCalledWith('/tests');
    expect(component.submitError()).toBeNull();
  });

  // Issue #420: profili zaten tamamlanmış veli (409) veli paneline gider.
  it('onSubmit_ParentAlreadyRegistered409_RedirectsToParentDashboard', () => {
    const fixture = createComponent({ role: 'parent' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const redirectSpy = vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);
    authServiceSpy.registerParentProfile.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    // Bilgi snackbar'ı bileşenin kendi ortam enjektöründen gelir; animasyonsuz test ortamında açılmasın.
    vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);

    component.onSubmit();

    expect(redirectSpy).toHaveBeenCalledTimes(1);

    expect(redirectSpy).toHaveBeenCalledWith('/parent');
  });

  it('onSubmit_ParentRegistrationProfileNotResolved404_SetsSubmitErrorWithoutRedirect', () => {
    const fixture = createComponent({ role: 'parent' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerParentProfile.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Kullanıcı çözümlenemedi.' } }))
    );
    // MatSnackBarModule bileşenin kendi ortam enjektöründe MatSnackBar sağlar; kök sahte örnek yerine o izlenir.
    const openSpy = vi
      .spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open')
      .mockReturnValue(undefined as never);

    component.onSubmit();

    expect(authServiceSpy.registerParentProfile).toHaveBeenCalled();
    expect(component.submitError()).toContain('doğrulanamadı');
    expect(component.isLoading()).toBe(false);
    expect(routerSpy.navigate).not.toHaveBeenCalled();
    expect(openSpy).not.toHaveBeenCalled();
  });

  it('onSubmit_ParentRegistrationSessionExpired401_ShowsSessionMessageAndNavigatesToLogin', () => {
    const fixture = createComponent({ role: 'parent' });
    fixture.detectChanges();
    const component = fixture.componentInstance;
    authServiceSpy.registerParentProfile.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const openSpy = vi
      .spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open')
      .mockReturnValue(undefined as never);

    component.onSubmit();

    expect(openSpy).toHaveBeenCalledWith(
      'Oturumunuz sona ermiş, lütfen tekrar giriş yapın.',
      'Kapat',
      expect.any(Object)
    );
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    expect(component.submitError()).toBeNull();
  });
  // ── Issue #440: öğrenci adımında veli bilgisi ──────────────────────────────

  it('studentStep_RequiresParent_RendersParentFieldsWithExplanation', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();

    const section = fixture.nativeElement.querySelector('[data-testid="parent-section"]') as HTMLElement | null;
    expect(section).not.toBeNull();
    expect(section?.textContent).toContain('velinize bir aktivasyon bağlantısı göndereceğiz');
    expect(fixture.nativeElement.querySelector('#parentFullName')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#parentEmail')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#parentPhone')).not.toBeNull();
    const link = section?.querySelector('a') as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('/privacy-policy');
  });

  it('onSubmit_StudentMissingParentInfo_DoesNotCallRegisterAndShowsFieldErrors', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.studentForm.patchValue({ studentNumber: '123', gradeId: 10 });
    component.onSubmit();
    fixture.detectChanges();

    expect(authServiceSpy.registerStudentProfile).not.toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Veli adı gerekli.');
    expect(text).toContain('Veli e-postası gerekli.');
    expect(text).toContain('Veli telefonu gerekli.');
  });

  it('onSubmit_StudentInvalidParentEmailAndPhone_DoesNotCallRegister', () => {
    const fixture = createComponent({ role: 'student' });
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.studentForm.setValue({
      studentNumber: '123',
      schoolId: null,
      gradeId: 10,
      parentFullName: 'Ayşe Yılmaz',
      parentEmail: 'ayse@',
      parentPhone: '0212 123 45 67',
    });
    component.onSubmit();
    fixture.detectChanges();

    expect(authServiceSpy.registerStudentProfile).not.toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Geçerli bir e-posta adresi gir.');
    expect(text).toContain('Geçerli bir cep telefonu gir');
  });

  for (const phone of ['05321234567', '5321234567', '905321234567', '+90 (532) 123-45-67']) {
    it(`onSubmit_StudentAcceptedPhoneFormat_${phone}_CallsRegisterWithTrimmedParentInfo`, () => {
      const fixture = createComponent({ role: 'student' });
      fixture.detectChanges();
      const component = fixture.componentInstance;

      component.studentForm.setValue({
        studentNumber: '123',
        schoolId: null,
        gradeId: 10,
        parentFullName: '  Ayşe Yılmaz ',
        parentEmail: ' ayse@example.com ',
        parentPhone: phone,
      });
      component.onSubmit();

      expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
        studentNumber: '123',
        schoolId: null,
        gradeId: 10,
        parentFullName: 'Ayşe Yılmaz',
        parentEmail: 'ayse@example.com',
        parentPhone: phone,
      });
    });
  }

  it('studentStep_AccountWithAdultRole_HidesParentFieldsAndOmitsThemFromPayload', () => {
    const fixture = createComponent({ role: 'student' });
    authServiceSpy.getRealmRoles.mockReturnValue(['Teacher']);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    expect(component.parentInfoRequired()).toBe(false);
    expect(fixture.nativeElement.querySelector('[data-testid="parent-section"]')).toBeNull();

    component.studentForm.patchValue({ studentNumber: '123', gradeId: 10 });
    component.onSubmit();

    expect(authServiceSpy.registerStudentProfile).toHaveBeenCalledWith({
      studentNumber: '123',
      schoolId: null,
      gradeId: 10,
    });
  });

  const parentErrorCases: {
    name: string;
    status: number;
    body: Record<string, unknown>;
    expected: string;
  }[] = [
    {
      name: 'EmailNotUsable409',
      status: 409,
      body: { errorCode: 'ParentEmailNotUsable', message: 'x' },
      expected: 'Bu e-posta veli hesabı olarak kullanılamaz.',
    },
    {
      name: 'InvalidPhone400',
      status: 400,
      body: { errorCode: 'InvalidParentPhone' },
      expected: 'Geçerli bir cep telefonu gir',
    },
    {
      name: 'InfoRequired400',
      status: 400,
      body: { errorCode: 'ParentInfoRequired' },
      expected: 'Veli adı, e-postası ve telefonu zorunludur.',
    },
    {
      name: 'EmailChangeLocked429',
      status: 429,
      body: { errorCode: 'ParentEmailChangeLocked', retryAt: '2026-10-15T12:00:00Z' },
      expected: 'Çok sayıda farklı e-posta denendi.',
    },
    {
      name: 'Unavailable502',
      status: 502,
      body: { errorCode: 'ParentInvitationUnavailable' },
      expected: 'Veli daveti şu anda gönderilemedi.',
    },
    {
      name: 'InvalidName400',
      status: 400,
      body: { errorCode: 'InvalidParentName' },
      expected: 'Veli adı yalnız harf içerebilir.',
    },
    {
      name: 'Busy409',
      status: 409,
      body: { errorCode: 'Busy', message: 'x' },
      expected: 'Veliniz şu anda hesabını etkinleştiriyor',
    },
  ];

  for (const scenario of parentErrorCases) {
    it(`onSubmit_StudentParent${scenario.name}_ShowsMappedMessageAndStaysOnForm`, () => {
      const fixture = createComponent({ role: 'student' });
      fixture.detectChanges();
      const component = fixture.componentInstance;
      authServiceSpy.registerStudentProfile.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: scenario.status, error: scenario.body }))
      );
      const redirectSpy = vi.spyOn(component, 'redirectTo').mockReturnValue(undefined);

      component.studentForm.setValue({ studentNumber: '123', schoolId: null, gradeId: 10, ...PARENT_INPUT });
      component.onSubmit();
      fixture.detectChanges();

      expect(component.submitError()).toContain(scenario.expected);
      expect(component.isLoading()).toBe(false);
      expect(redirectSpy).not.toHaveBeenCalled();
      expect(snackBarSpy.open).not.toHaveBeenCalled();
    });
  }
});
