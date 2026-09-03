import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { QuestionCanvasComponent } from './question-canvas.component';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { SubjectService } from '../../services/subject.service';
import { BookService } from '../../services/book.service';

/**
 * These tests exercise the redesigned shell logic of QuestionCanvasComponent:
 * pure getters, inspector proxies and the quick-create flow.
 * The heavy child (`ImageSelectorComponent`) is replaced by a lightweight fake
 * so no canvas / HTTP work happens — fixture.detectChanges() is intentionally
 * NOT called.
 */
describe('QuestionCanvasComponent', () => {
  let fixture: ComponentFixture<QuestionCanvasComponent>;
  let component: QuestionCanvasComponent;
  let testService: jasmine.SpyObj<TestService>;

  function makeImageSelectorFake(overrides: Partial<any> = {}) {
    return {
      previewMode: jasmine.createSpy('previewMode').and.returnValue(false),
      togglePreviewMode: jasmine.createSpy('togglePreviewMode'),
      answerCount: Object.assign(jasmine.createSpy('answerCount').and.returnValue(4), {
        set: jasmine.createSpy('answerCount.set'),
      }),
      recomputeWarnings: jasmine.createSpy('recomputeWarnings'),
      ...overrides,
    };
  }

  beforeEach(async () => {
    window.history.replaceState({}, '');

    testService = jasmine.createSpyObj<TestService>('TestService', ['search', 'get']);
    testService.search.and.returnValue(of({ items: [] }) as any);
    testService.get.and.returnValue(of({ subtitle: 'T-1', id: 99 }) as any);

    const questionService = jasmine.createSpyObj<QuestionService>('QuestionService', ['getAll', 'saveBulk']);
    questionService.getAll.and.returnValue(of([]) as any);
    questionService.saveBulk.and.returnValue(of({}) as any);

    const bookService = jasmine.createSpyObj<BookService>('BookService', ['getAll', 'getTestsByBook']);
    bookService.getAll.and.returnValue(of([]));
    bookService.getTestsByBook.and.returnValue(of([]));

    const subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);

    const router = jasmine.createSpyObj<Router>('Router', ['navigate', 'getCurrentNavigation']);
    router.getCurrentNavigation.and.returnValue(null as any);

    await TestBed.configureTestingModule({
      imports: [QuestionCanvasComponent],
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TestService, useValue: testService },
        { provide: QuestionService, useValue: questionService },
        { provide: BookService, useValue: bookService },
        { provide: SubjectService, useValue: subjectService },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuestionCanvasComponent);
    component = fixture.componentInstance;
  });

  it('builds', () => {
    expect(component).toBeTruthy();
  });

  describe('canPreview', () => {
    it('canPreview_NoIdAndNoTestValue_ReturnsFalse', () => {
      component.id = null;
      component.questionForm.patchValue({ testValue: 0 });
      expect(component.canPreview).toBeFalse();
    });

    it('canPreview_HasRouteId_ReturnsTrue', () => {
      component.id = 5;
      expect(component.canPreview).toBeTrue();
    });

    it('canPreview_HasPositiveTestValue_ReturnsTrue', () => {
      component.id = null;
      component.questionForm.patchValue({ testValue: 12 });
      expect(component.canPreview).toBeTrue();
    });
  });

  describe('previewOn', () => {
    it('previewOn_NoImageSelector_ReturnsFalse', () => {
      expect(component.previewOn).toBeFalse();
    });

    it('previewOn_ReflectsImageSelectorPreviewMode', () => {
      component.imageSelector = makeImageSelectorFake({
        previewMode: jasmine.createSpy().and.returnValue(true),
      }) as any;
      expect(component.previewOn).toBeTrue();
    });
  });

  describe('togglePreviewMode', () => {
    it('togglePreviewMode_PreviewOff_UsesRouteId_WhenIdPresent', () => {
      const sel = makeImageSelectorFake();
      component.imageSelector = sel as any;
      component.id = 5;
      component.questionForm.patchValue({ testValue: 12 });

      component.togglePreviewMode();

      expect(sel.togglePreviewMode).toHaveBeenCalledOnceWith(5);
    });

    it('togglePreviewMode_PreviewOff_FallsBackToTestValue_WhenNoId', () => {
      const sel = makeImageSelectorFake();
      component.imageSelector = sel as any;
      component.id = null;
      component.questionForm.patchValue({ testValue: 12 });

      component.togglePreviewMode();

      expect(sel.togglePreviewMode).toHaveBeenCalledOnceWith(12);
    });

    it('togglePreviewMode_CannotPreview_DoesNothing', () => {
      const sel = makeImageSelectorFake();
      component.imageSelector = sel as any;
      component.id = null;
      component.questionForm.patchValue({ testValue: 0 });

      component.togglePreviewMode();

      expect(sel.togglePreviewMode).not.toHaveBeenCalled();
    });

    it('togglePreviewMode_PreviewOn_ExitsWithZero', () => {
      const sel = makeImageSelectorFake({ previewMode: jasmine.createSpy().and.returnValue(true) });
      component.imageSelector = sel as any;
      component.id = 5;

      component.togglePreviewMode();

      expect(sel.togglePreviewMode).toHaveBeenCalledOnceWith(0);
    });
  });

  describe('answerCount / setAnswerCount', () => {
    it('answerCount_NoImageSelector_DefaultsToFour', () => {
      expect(component.answerCount).toBe(4);
    });

    it('answerCount_DelegatesToImageSelectorSignal', () => {
      component.imageSelector = makeImageSelectorFake({
        answerCount: jasmine.createSpy().and.returnValue(5),
      }) as any;
      expect(component.answerCount).toBe(5);
    });

    it('setAnswerCount_SetsSignalAndRecomputesWarnings', () => {
      const sel = makeImageSelectorFake();
      component.imageSelector = sel as any;

      component.setAnswerCount(5);

      expect(sel.answerCount.set).toHaveBeenCalledWith(5);
      expect(sel.recomputeWarnings).toHaveBeenCalledTimes(1);
    });
  });

  describe('breadcrumb labels', () => {
    it('selectedBookLabel_NoSelection_ShowsPlaceholder', () => {
      expect(component.selectedBookLabel()).toBe('Kitap seç');
    });

    it('selectedBookTestLabel_NoSelection_ShowsPlaceholder', () => {
      expect(component.selectedBookTestLabel()).toBe('Kitap testi');
    });

    it('selectedTestLabel_NoSelection_ShowsPlaceholder', () => {
      expect(component.selectedTestLabel).toBe('Test seç');
    });

    it('selectedTestLabel_ReflectsFormTestIdText', () => {
      component.questionForm.get('testId')?.setValue('2024 TYT Deneme 1');
      expect(component.selectedTestLabel).toBe('2024 TYT Deneme 1');
    });
  });

  describe('inspector classification proxies', () => {
    function tceFake() {
      return {
        testForm: {
          value: { subjectId: 3, topicId: 0, subtopicId: 0 },
          patchValue: jasmine.createSpy('patchValue'),
        },
        onSubjectChange: jasmine.createSpy('onSubjectChange'),
        onTopicChange: jasmine.createSpy('onTopicChange'),
        subjects: [],
        topics: [],
        subtopics: [],
      };
    }

    it('insSubjectId_Get_ReadsTceForm', () => {
      component.testCreateEnhancedComponent = tceFake() as any;
      expect(component.insSubjectId).toBe(3);
    });

    it('insSubjectId_Set_PatchesTceAndTriggersSubjectChange', () => {
      const tce = tceFake();
      component.testCreateEnhancedComponent = tce as any;

      component.insSubjectId = 5;

      expect(tce.testForm.patchValue).toHaveBeenCalledWith({ subjectId: 5 }, { emitEvent: false });
      expect(tce.onSubjectChange).toHaveBeenCalledWith(5);
    });

    it('insTopicId_Set_EmptyValue_DoesNotCallOnTopicChange', () => {
      const tce = tceFake();
      component.testCreateEnhancedComponent = tce as any;

      component.insTopicId = null;

      expect(tce.onTopicChange).not.toHaveBeenCalled();
    });

    it('insSubtopicId_Set_PushesClassificationIntoQuestionForm', () => {
      const tce = tceFake();
      tce.testForm.value = { subjectId: 3, topicId: 7, subtopicId: 9 };
      component.testCreateEnhancedComponent = tce as any;

      component.insSubtopicId = 9;

      expect(component.questionForm.value.subjectId).toBe(3);
      expect(component.questionForm.value.topicId).toBe(7);
      expect(component.questionForm.value.subtopicId).toBe(9);
    });
  });

  describe('quick create flow', () => {
    it('openQuickCreate_OpensPanelAndSidePanel', () => {
      component.showQuickCreate.set(false);
      component.showSidePanel.set(false);

      component.openQuickCreate();

      expect(component.showQuickCreate()).toBeTrue();
      expect(component.showSidePanel()).toBeTrue();
    });

    it('onQuickCreated_ClosesPanel_SetsId_AndSyncsFormFromTest', () => {
      component.showQuickCreate.set(true);

      component.onQuickCreated(42);

      expect(component.showQuickCreate()).toBeFalse();
      expect(component.id).toBe(42);
      expect(testService.get).toHaveBeenCalledWith(42);
      expect(component.questionForm.get('testId')?.value).toBe('T-1');
      expect(component.questionForm.get('testValue')?.value).toBe(99);
    });
  });
});
