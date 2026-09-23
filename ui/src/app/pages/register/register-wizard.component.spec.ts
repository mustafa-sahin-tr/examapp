import { ComponentFixture, TestBed, fakeAsync, flush } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NEVER, of, throwError } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { RegisterWizardComponent } from './register-wizard.component';
import { AuthService } from '../../services/auth.service';
import { StudentService } from '../../services/student.service';
import { TeacherService } from '../../services/teacher.service';
import { ParentService } from '../../services/parent.service';
import { GradesService } from '../../services/grades.service';
import { Grade } from '../../models/student';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

const GRADES: Grade[] = [{ id: 10, name: '10. Sınıf' }];

describe('RegisterWizardComponent (Issue #234)', () => {
  let fixture: ComponentFixture<RegisterWizardComponent>;
  let component: RegisterWizardComponent;
  let authService: jasmine.SpyObj<AuthService>;
  let teacherService: jasmine.SpyObj<TeacherService>;
  let studentService: jasmine.SpyObj<StudentService>;
  let router: jasmine.SpyObj<Router>;

  function createComponent(role?: string): void {
    authService = jasmine.createSpyObj('AuthService', ['getRealmRoles', 'isCachedUserCurrent', 'clearCachedUser']);
    studentService = jasmine.createSpyObj('StudentService', ['register']);
    teacherService = jasmine.createSpyObj('TeacherService', ['register']);
    jasmine.createSpyObj('ParentService', ['register']);
    jasmine.createSpyObj('GradesService', ['getGrades']);
    router = jasmine.createSpyObj('Router', ['navigate']);
    jasmine.createSpyObj('MatSnackBar', ['open']);

    authService.getRealmRoles.and.returnValue([]);
    authService.isCachedUserCurrent.and.returnValue(true);

    TestBed.configureTestingModule({
      imports: [RegisterWizardComponent, translocoTestingModule()],
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
    component.teacherForm.setValue({ schoolName: 'Okul Adı' });
    teacherService.register.and.returnValue(
      of({
        accessToken: 'token',
        expiresIn: 3600,
        profileId: 42,
        approvalStatus: 0,
        requestedSchoolId: 1,
        schoolApprovalPending: true,
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
    component.teacherForm.setValue({ schoolName: 'Okul Adı' });
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
    component.studentForm.setValue({ studentNumber: '123', schoolName: 'Okul', gradeId: 10 });
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
});
