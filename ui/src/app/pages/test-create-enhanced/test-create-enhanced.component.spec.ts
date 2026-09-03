import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { TestCreateEnhancedComponent } from './test-create-enhanced.component';
import { TestService } from '../../services/test.service';
import { BookService } from '../../services/book.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';

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
      imports: [TestCreateEnhancedComponent],
      providers: [
        provideNoopAnimations(),
        { provide: TestService, useValue: testService },
        { provide: BookService, useValue: bookService },
        { provide: GradesService, useValue: gradesService },
        { provide: SubjectService, useValue: subjectService },
        { provide: Router, useValue: router },
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
