import { signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { By } from '@angular/platform-browser';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatRadioButton } from '@angular/material/radio';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { of, switchMap, throwError, timer } from 'rxjs';

import { TestCreateEnhancedComponent } from './test-create-enhanced.component';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { WorksheetStudentVisibility, WorksheetTeacherSharing } from '../../models/test-instance';
import { VisibilitySectionComponent } from '../../shared/components/visibility-section/visibility-section.component';
import { BookService } from '../../services/book.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import testCreateTr from '../../../../public/i18n/test-create/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
  // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
  langs: { tr: { ...rootTr, 'test-create': testCreateTr }, 'test-create/tr': testCreateTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('TestCreateEnhancedComponent', () => {
  let testService: jasmine.SpyObj<TestService>;
  let router: jasmine.SpyObj<Router>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  function configure(idParam: string | null): ComponentFixture<TestCreateEnhancedComponent> {
    // Guard: component reads history.state.testValue when there is no :id param.
    window.history.replaceState({}, '');

    testService = jasmine.createSpyObj<TestService>('TestService', [
      'create',
      'get',
      'bulkImport',
      'updateWorksheetBackgroundImage',
      'updateVisibility',
    ]);
    testService.get.and.returnValue(
      of({ id: 1, name: 'X', gradeId: 1, maxDurationSeconds: 600, isPracticeTest: false } as any)
    );
    testService.create.and.returnValue(of({ message: 'ok', examId: 42 }));
    testService.bulkImport.and.returnValue(of({}));
    testService.updateWorksheetBackgroundImage.and.returnValue(of({ imageUrl: 'http://img/x.png' } as any));

    const bookService = jasmine.createSpyObj<BookService>('BookService', ['getAll', 'getTestsByBook']);
    bookService.getAll.and.returnValue(of([]));
    bookService.getTestsByBook.and.returnValue(of([]));

    const gradesService = jasmine.createSpyObj<GradesService>('GradesService', ['getGrades']);
    gradesService.getGrades.and.returnValue(of([{ id: 1, name: '5' }]));

    const subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', [
      'loadCategories',
      'getSubjectsByGrade',
      'getTopicsBySubjectAndGrade',
      'getSubTopicsByTopic',
    ]);
    subjectService.loadCategories.and.returnValue(of([]));
    subjectService.getSubjectsByGrade.and.returnValue(of([]));
    subjectService.getTopicsBySubjectAndGrade.and.returnValue(of([]));
    subjectService.getSubTopicsByTopic.and.returnValue(of([]));

    router = jasmine.createSpyObj<Router>('Router', ['navigate', 'getCurrentNavigation']);
    router.getCurrentNavigation.and.returnValue(null as any);

    TestBed.configureTestingModule({
      imports: [TestCreateEnhancedComponent, translocoTesting],
      providers: [
        provideNoopAnimations(),
        { provide: TestService, useValue: testService },
        { provide: BookService, useValue: bookService },
        { provide: GradesService, useValue: gradesService },
        { provide: SubjectService, useValue: subjectService },
        { provide: Router, useValue: router },
        // Alt bileşen app-visibility-section okul bilgisini AuthService.user'dan okur (issue #191).
        {
          provide: AuthService,
          useValue: { user: signal({ schoolId: 1 }), hasRole: (r: string) => r === 'Teacher' },
        },
        // Headless Chrome (800x600 yatay) Handset'e girer; radio DOM'unu doğrulamak için masaüstü sabitlenir.
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: false, breakpoints: {} }) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(idParam ? { id: idParam } : {}) } },
        },
      ],
    });

    const fixture = TestBed.createComponent(TestCreateEnhancedComponent);
    // MatSnackBar is provided by MatSnackBarModule (not root); stub it on the instance.
    snackBar = fixture.componentInstance.snackBar as jasmine.SpyObj<MatSnackBar>;
    spyOn(snackBar, 'open');
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  function fileEvent(file: File): Event {
    return { target: { files: [file], value: 'C:\\fakepath\\x' } } as unknown as Event;
  }

  it('builds', () => {
    const fixture = configure(null);
    expect(fixture.componentInstance).toBeTruthy();
  });

  describe('isEditMode', () => {
    it('is true when the route carries a positive id (exam/5)', () => {
      const c = configure('5').componentInstance;
      expect(c.isEditMode).toBeTrue();
      expect(c.id).toBe(5);
    });

    it('is false when there is no id param (exam)', () => {
      expect(configure(null).componentInstance.isEditMode).toBeFalse();
    });
  });

  describe('onSubmit', () => {
    it('calls testService.create exactly once for a valid form (no double-save)', () => {
      const c = configure(null).componentInstance;
      c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1 });

      c.onSubmit();

      expect(testService.create).toHaveBeenCalledTimes(1);
    });

    it('does not call testService.create for an invalid form', () => {
      const c = configure(null).componentInstance;
      c.testForm.patchValue({ name: '', gradeId: '' });

      c.onSubmit();

      expect(testService.create).not.toHaveBeenCalled();
      expect(snackBar.open).toHaveBeenCalled();
    });

    it('reloads the component instead of navigating in edit mode', () => {
      const c = configure('5').componentInstance;
      spyOn(c, 'reloadComponent').and.callFake(() => {});
      c.testForm.patchValue({ name: 'Düzenlenen', gradeId: 1 });

      c.onSubmit();

      expect(c.reloadComponent).toHaveBeenCalledWith(42);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('navigates to /exam/:examId after creating a new test', () => {
      const c = configure(null).componentInstance;
      c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1 });

      c.onSubmit();

      expect(router.navigate).toHaveBeenCalledWith(['/exam', 42]);
    });
  });

  describe('isMiniform', () => {
    it('isMiniform_ModeIsMiniform_ReturnsTrue', () => {
      const c = configure(null).componentInstance;
      c.mode = 'miniform';
      expect(c.isMiniform).toBeTrue();
    });

    it('isMiniform_DefaultMode_ReturnsFalse', () => {
      expect(configure(null).componentInstance.isMiniform).toBeFalse();
    });
  });

  describe('createAndContinue', () => {
    it('createAndContinue_ValidForm_CallsOnCreateAsyncWithoutSubtopicRequirement', () => {
      const c = configure(null).componentInstance;
      const spy = spyOn(c, 'onCreateAsync').and.returnValue(of({ message: 'ok', examId: 42 }));
      spyOn(c, 'reloadComponent').and.stub();

      c.createAndContinue();

      expect(spy).toHaveBeenCalledOnceWith(false);
    });

    it('createAndContinue_EmitsCreatedAndContinueWithExamId', () => {
      const c = configure(null).componentInstance;
      spyOn(c, 'onCreateAsync').and.returnValue(of({ message: 'ok', examId: 42 }));
      spyOn(c, 'reloadComponent').and.stub();
      const emitted = jasmine.createSpy('createdAndContinue');
      c.createdAndContinue.subscribe(emitted);

      c.createAndContinue();

      expect(emitted).toHaveBeenCalledWith(42);
    });

    it('createAndContinue_AlreadyCreatingInline_DoesNotCallOnCreateAsyncAgain', () => {
      const c = configure(null).componentInstance;
      const spy = spyOn(c, 'onCreateAsync').and.returnValue(of({ message: 'ok', examId: 42 }));
      c.creatingInline = true;

      c.createAndContinue();

      expect(spy).not.toHaveBeenCalled();
    });
  });

  describe('onCreateAsync', () => {
    it('onCreateAsync_RequireSubtopicDefaultAndMissingSubtopic_ErrorsWithoutCallingCreate', () => {
      const c = configure(null).componentInstance;
      c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1, subtopicId: null });

      let errored = false;
      c.onCreateAsync().subscribe({ error: () => (errored = true) });

      expect(errored).toBeTrue();
      expect(testService.create).not.toHaveBeenCalled();
    });

    it('onCreateAsync_RequireSubtopicFalse_CallsCreateEvenWithoutSubtopic', () => {
      const c = configure(null).componentInstance;
      c.testForm.patchValue({ name: 'Yeni Test', gradeId: 1, subtopicId: null });

      c.onCreateAsync(false).subscribe();

      expect(testService.create).toHaveBeenCalledTimes(1);
    });
  });

  describe('onVisibilityChange', () => {
    /** Gerçek HTTP gibi asenkron hata: iyimser değer bir kez render edilir, sonra geri alınır. */
    function failAsync(status: number, error: unknown): void {
      testService.updateVisibility.and.returnValue(
        timer(0).pipe(switchMap(() => throwError(() => new HttpErrorResponse({ status, error }))))
      );
    }

    function child(fixture: ComponentFixture<TestCreateEnhancedComponent>): VisibilitySectionComponent {
      return fixture.debugElement.query(By.directive(VisibilitySectionComponent)).componentInstance;
    }

    function checkedRadioValue(fixture: ComponentFixture<TestCreateEnhancedComponent>): unknown {
      const radios = fixture.debugElement.queryAll(By.directive(MatRadioButton));
      return radios.find((r) => (r.componentInstance as MatRadioButton).checked)?.componentInstance.value;
    }

    /** Kullanıcı alt bileşende SchoolOnly'yi seçer → emit → parent PUT'u başlatır. */
    function selectSchoolOnlyViaChild(fixture: ComponentFixture<TestCreateEnhancedComponent>): void {
      child(fixture).onTeacherSharingChange(WorksheetTeacherSharing.SchoolOnly);
      fixture.detectChanges();
      tick(0);
      fixture.detectChanges();
    }

    it('shows the backend message on 400 and rolls the child selection back to Private (issue #191)', fakeAsync(() => {
      const fixture = configure('5');
      const c = fixture.componentInstance;
      snackBar.open.calls.reset();
      const message = 'Okulsuz kullanıcı "Sadece okulum" seçemez.';
      failAsync(400, { success: false, message });

      selectSchoolOnlyViaChild(fixture);

      expect(snackBar.open).toHaveBeenCalledWith(message, jasmine.any(String), jasmine.any(Object));
      expect(c.teacherSharing()).toBe(WorksheetTeacherSharing.Private);
      expect(c.isSavingVisibility()).toBeFalse();
      // Geri alma alt bileşene input üzerinden akmalı: hem input hem seçili radio Private.
      expect(child(fixture).teacherSharing).toBe(WorksheetTeacherSharing.Private);
      expect(checkedRadioValue(fixture)).toBe(WorksheetTeacherSharing.Private);
    }));

    it('falls back to the generic message when 400 carries no message', fakeAsync(() => {
      const fixture = configure('5');
      snackBar.open.calls.reset();
      failAsync(400, { success: false });

      selectSchoolOnlyViaChild(fixture);

      expect(snackBar.open).toHaveBeenCalledWith(
        testCreateTr.snackbar.visibilityFailed,
        jasmine.any(String),
        jasmine.any(Object)
      );
      expect(child(fixture).teacherSharing).toBe(WorksheetTeacherSharing.Private);
    }));

    it('shows the forbidden message on 403 and rolls back', fakeAsync(() => {
      const fixture = configure('5');
      snackBar.open.calls.reset();
      failAsync(403, null);

      selectSchoolOnlyViaChild(fixture);

      expect(snackBar.open).toHaveBeenCalledWith(
        testCreateTr.snackbar.visibilityForbidden,
        jasmine.any(String),
        jasmine.any(Object)
      );
      expect(child(fixture).teacherSharing).toBe(WorksheetTeacherSharing.Private);
      expect(checkedRadioValue(fixture)).toBe(WorksheetTeacherSharing.Private);
    }));

    it('keeps the new value when the PUT succeeds', () => {
      const fixture = configure('5');
      testService.updateVisibility.and.returnValue(
        of({ teacherSharing: WorksheetTeacherSharing.SchoolOnly, studentVisibility: WorksheetStudentVisibility.Normal } as any)
      );

      fixture.componentInstance.onVisibilityChange({
        teacherSharing: WorksheetTeacherSharing.SchoolOnly,
        studentVisibility: WorksheetStudentVisibility.Normal,
      });

      expect(testService.updateVisibility).toHaveBeenCalledWith(5, jasmine.objectContaining({ teacherSharing: 3 }));
      expect(fixture.componentInstance.teacherSharing()).toBe(WorksheetTeacherSharing.SchoolOnly);
    });
  });

  describe('onImageSelected', () => {
    it('rejects files larger than 2MB and does not upload', () => {
      const c = configure('5').componentInstance;
      snackBar.open.calls.reset();
      const big = new File([new Uint8Array(2 * 1024 * 1024 + 16)], 'big.png', { type: 'image/png' });

      c.onImageSelected(fileEvent(big));

      expect(testService.updateWorksheetBackgroundImage).not.toHaveBeenCalled();
      expect(snackBar.open).toHaveBeenCalled();
    });

    it('rejects non-image files and does not upload', () => {
      const c = configure('5').componentInstance;
      snackBar.open.calls.reset();
      const txt = new File(['hello'], 'a.txt', { type: 'text/plain' });

      c.onImageSelected(fileEvent(txt));

      expect(testService.updateWorksheetBackgroundImage).not.toHaveBeenCalled();
      expect(snackBar.open).toHaveBeenCalled();
    });

    it('uploads a valid image in edit mode', () => {
      const c = configure('5').componentInstance;
      const png = new File(['x'], 'a.png', { type: 'image/png' });

      c.onImageSelected(fileEvent(png));

      expect(testService.updateWorksheetBackgroundImage).toHaveBeenCalledTimes(1);
      expect(testService.updateWorksheetBackgroundImage).toHaveBeenCalledWith(5, jasmine.any(File));
    });

    it('does not upload in create mode (no worksheet id yet)', () => {
      const c = configure(null).componentInstance;
      const png = new File(['x'], 'a.png', { type: 'image/png' });

      c.onImageSelected(fileEvent(png));

      expect(testService.updateWorksheetBackgroundImage).not.toHaveBeenCalled();
    });
  });
});
