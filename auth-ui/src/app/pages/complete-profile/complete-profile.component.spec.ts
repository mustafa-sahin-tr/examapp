import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
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
});
