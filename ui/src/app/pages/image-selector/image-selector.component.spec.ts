import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { ImageSelectorComponent } from './image-selector.component';
import { COORDINATE_JSON_MAX_BYTES } from './coordinate-json';
import { ClassificationSource } from '../../models/draws';

function answer(x: number, y: number) {
  return { label: '?', x, y, width: 40, height: 20, isCorrect: false, id: 0, imageUrl: '' };
}

function region(name: string, answers: any[] = []) {
  return {
    name,
    x: 0,
    y: 0,
    width: 300,
    height: 300,
    answers,
    passageId: '0',
    imageId: '',
    imageUrl: '',
    id: 0,
    isExample: false,
    exampleAnswer: null,
  };
}

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import imageSelectorTr from '../../../../public/i18n/image-selector/tr.json';

/** Gercek sozluk yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { 'image-selector/tr': imageSelectorTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // Uygulama config'i ile ayni: scope oneki klasor adiyla birebir ayni kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('ImageSelectorComponent', () => {
  let component: ImageSelectorComponent;
  let fixture: ComponentFixture<ImageSelectorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImageSelectorComponent, translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(ImageSelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('answerCount', () => {
    it('answerCount_NoExplicitChange_DefaultsToFour', () => {
      expect(component.answerCount()).toBe(4);
    });
  });

  describe('renameAnswers', () => {
    // Regression: "Şık 4" bug — last answer must be relabelled too.
    it('renameAnswers_ThreeVerticalAnswers_ProducesABC', () => {
      component.regions.set([region('Soru 1', [answer(10, 90), answer(10, 10), answer(10, 50)])] as any);

      component.renameAnswers(0);

      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B', 'C']);
    });

    it('renameAnswers_FourVerticalAnswers_ProducesABCD', () => {
      component.regions.set([
        region('Soru 1', [answer(10, 30), answer(10, 90), answer(10, 0), answer(10, 60)]),
      ] as any);

      component.renameAnswers(0);

      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B', 'C', 'D']);
    });

    it('renameAnswers_FiveVerticalAnswers_ProducesABCDE', () => {
      component.regions.set([
        region('Soru 1', [
          answer(10, 40),
          answer(10, 10),
          answer(10, 70),
          answer(10, 100),
          answer(10, 130),
        ]),
      ] as any);

      component.renameAnswers(0);

      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B', 'C', 'D', 'E']);
    });

    it('renameAnswers_HorizontalRow_LabelsLeftToRight', () => {
      component.regions.set([
        region('Soru 1', [answer(120, 10), answer(0, 10), answer(240, 10), answer(60, 12)]),
      ] as any);

      component.renameAnswers(0);

      const sorted = [...component.regions()[0].answers].sort((a, b) => a.x - b.x);
      expect(sorted.map((a) => a.label)).toEqual(['A', 'B', 'C', 'D']);
    });
  });

  describe('question selection', () => {
    it('activeRegionIndex_NoRegions_ReturnsNull', () => {
      component.regions.set([]);
      expect(component.activeRegionIndex).toBeNull();
    });

    it('selectQuestion_ValidIndex_EmitsSelectedQuestionChangeWithRegion', () => {
      component.regions.set([region('Soru 1'), region('Soru 2')] as any);
      const spy = jasmine.createSpy('selectedQuestionChange');
      component.selectedQuestionChange.subscribe(spy);

      component.selectQuestion(1);

      expect(spy).toHaveBeenCalledWith({ index: 1, region: component.regions()[1] });
      expect(component.activeRegionIndex).toBe(1);
    });

    it('selectNextQuestion_AtLastRegion_WrapsToFirst', () => {
      component.regions.set([region('Soru 1'), region('Soru 2')] as any);
      component.selectQuestion(1);

      component.selectNextQuestion();

      expect(component.activeRegionIndex).toBe(0);
    });

    it('selectPreviousQuestion_AtFirstRegion_WrapsToLast', () => {
      component.regions.set([region('Soru 1'), region('Soru 2'), region('Soru 3')] as any);
      component.selectQuestion(0);

      component.selectPreviousQuestion();

      expect(component.activeRegionIndex).toBe(2);
    });
  });

  describe('togglePreviewMode', () => {
    it('togglePreviewMode_Entering_KeepsImageFiles', () => {
      spyOn((component as any).questionService, 'getAll').and.returnValue(of([]));
      spyOn((component as any).testService, 'convertQuestionsToRegions').and.returnValue([]);
      const files = [new File(['x'], 'p1.png', { type: 'image/png' })];
      component.imageFiles = files;

      component.togglePreviewMode(7);

      expect(component.previewMode()).toBeTrue();
      expect(component.imageFiles).toBe(files);
    });
  });

  // Issue #454: klasörde question-coordinates.json varsa detector yerine JSON'dan bölge kurulur.
  describe('coordinate JSON mode', () => {
    /** 1x1 PNG; FileReader + Image.onload gerçekten tetiklensin diye çözülebilir bir görsel. */
    const PNG_1X1 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=';

    function png(name: string): File {
      const bytes = Uint8Array.from(atob(PNG_1X1), (c) => c.charCodeAt(0));
      return new File([bytes], name, { type: 'image/png' });
    }

    function coordinatesJson(entries: unknown[]): File {
      return new File([JSON.stringify(entries)], 'question-coordinates.json', { type: 'application/json' });
    }

    function jsonEntry(filename: string, soruNo: string) {
      return {
        filename,
        soru_no: soruNo,
        ders: { id: '144', ad: 'Matematik' },
        konu: { id: '261', ad: 'Doğal Sayılar' },
        alt_konular: [{ id: '809', ad: 'Milyonlar' }],
        x: 0,
        y: 0,
        width: 1,
        height: 1,
        siklar: {
          A: { rel_x: 0, rel_y: 0, width: 1, height: 1 },
          B: { rel_x: 0, rel_y: 0, width: 1, height: 1 },
        },
      };
    }

    function fileEvent(files: File[]): Event {
      return { target: { files } } as unknown as Event;
    }

    async function waitFor(condition: () => boolean, timeoutMs = 3000): Promise<void> {
      const start = Date.now();
      while (!condition()) {
        if (Date.now() - start > timeoutMs) {
          throw new Error('waitFor timed out');
        }
        await new Promise((resolve) => setTimeout(resolve, 10));
      }
    }

    let predictSpy: jasmine.Spy;

    beforeEach(() => {
      predictSpy = spyOn(component.questionDetectorService, 'predict').and.returnValue(
        of({ predictions: [] }) as any
      );
      spyOn(component.questionDetectorService, 'readQrData').and.returnValue(of({}) as any);
    });

    it('handleFilesInput2_FolderWithCoordinateJson_BuildsRegionsWithoutCallingDetector', async () => {
      const files = [
        png('page_10.png'),
        png('page_2.png'),
        png('page_1.png'), // JSON'da yok → atlanır
        coordinatesJson([jsonEntry('page_2.png', '2'), jsonEntry('page_2.png', '1'), jsonEntry('page_10.png', '1')]),
      ];

      component.handleFilesInput2(fileEvent(files));
      await waitFor(() => component.regions().length > 0);

      expect(predictSpy).not.toHaveBeenCalled();
      expect(component.coordinateJsonMode()).toBeTrue();
      expect(component.imageFiles.map((f) => f.name)).toEqual(['page_2.png', 'page_10.png']);
      expect(component.regions().map((r) => r.name)).toEqual(['Soru 1', 'Soru 2']);
      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B']);

      const payload = component.getRegions({ testId: 1 });
      expect(payload.questions[0].classification).toEqual({
        subjectId: 144,
        topicId: 261,
        subTopicIds: [809],
        source: 'AI',
      });
    });

    it('handleFilesInput2_FolderWithoutJson_CallsDetectorAndResetsJsonMode', async () => {
      component.handleFilesInput2(fileEvent([png('page_1.png'), coordinatesJson([jsonEntry('page_1.png', '1')])]));
      await waitFor(() => component.regions().length > 0);
      expect(component.coordinateJsonMode()).toBeTrue();

      component.handleFilesInput2(fileEvent([png('page_1.png')]));
      await waitFor(() => predictSpy.calls.count() > 0);

      expect(component.coordinateJsonMode()).toBeFalse();
      expect(predictSpy).toHaveBeenCalledTimes(1);
    });

    it('handleFilesInput2_UnusableJson_FallsBackToDetector', async () => {
      const broken = new File(['{oops'], 'question-coordinates.json', { type: 'application/json' });

      component.handleFilesInput2(fileEvent([png('page_1.png'), broken]));
      await waitFor(() => predictSpy.calls.count() > 0);

      expect(component.coordinateJsonMode()).toBeFalse();
      expect(component.imageFiles.map((f) => f.name)).toEqual(['page_1.png']);
    });

    it('updateRegionClassification_JsonMode_KeepsAiSourceSoGeminiSkipsIt', async () => {
      component.handleFilesInput2(fileEvent([png('page_1.png'), coordinatesJson([jsonEntry('page_1.png', '1')])]));
      await waitFor(() => component.regions().length > 0);

      component.updateRegionClassification(0, { subjectId: 5, topicId: 6, subtopicIds: [7] });

      expect(component.regions()[0].classification).toEqual({
        subjectId: 5,
        topicId: 6,
        subTopicIds: [7],
        source: 'AI',
      });
      expect(component.regions()[0].classificationSource).toBe(ClassificationSource.AI);
      expect(component.regions()[0].subtopicId).toBe(7);
    });

    it('handleFilesInput2_LateJsonReadAfterNewerSelection_DoesNotEnableJsonMode', async () => {
      let resolveText!: (text: string) => void;
      const slowJson = coordinatesJson([jsonEntry('page_1.png', '1')]);
      spyOn(slowJson, 'text').and.returnValue(new Promise<string>((resolve) => (resolveText = resolve)));

      component.handleFilesInput2(fileEvent([png('page_1.png'), slowJson]));
      component.handleFilesInput2(fileEvent([png('page_9.png')])); // daha yeni seçim, JSON'suz
      resolveText(JSON.stringify([jsonEntry('page_1.png', '1')]));
      await waitFor(() => predictSpy.calls.count() > 0);
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(component.coordinateJsonMode()).toBeFalse();
      expect(component.imageFiles.map((f) => f.name)).toEqual(['page_9.png']);
    });

    it('handleFilesInput2_JsonModeWithAnswerLock_KeepsWholePageRegionAndSkipsJson', async () => {
      component.selectionModeLocked.set('answer');

      component.handleFilesInput2(fileEvent([png('page_1.png'), coordinatesJson([jsonEntry('page_1.png', '1')])]));
      await waitFor(() => component.regions().length > 0);

      expect(component.coordinateJsonMode()).toBeTrue();
      expect(predictSpy).not.toHaveBeenCalled();
      expect(component.regions().length).toBe(1);
      expect(component.regions()[0].name).toBe('Soru');
      expect(component.regions()[0].classification).toBeUndefined();
    });

    it('handleFilesInput2_JsonLargerThanCap_FallsBackToNormalModeWithoutReading', async () => {
      const big = coordinatesJson([jsonEntry('page_1.png', '1')]);
      Object.defineProperty(big, 'size', { value: COORDINATE_JSON_MAX_BYTES + 1 });
      const textSpy = spyOn(big, 'text').and.callThrough();

      component.handleFilesInput2(fileEvent([png('page_1.png'), big]));
      await waitFor(() => predictSpy.calls.count() > 0);

      expect(textSpy).not.toHaveBeenCalled();
      expect(component.coordinateJsonMode()).toBeFalse();
    });

    it('predict_JsonMode_IsNoOpAndAlignAllAnswersKeepsRegions', async () => {
      component.handleFilesInput2(fileEvent([png('page_1.png'), coordinatesJson([jsonEntry('page_1.png', '1')])]));
      await waitFor(() => component.regions().length > 0);

      component.predict();
      component.alignAllAnswers();

      expect(predictSpy).not.toHaveBeenCalled();
      expect(component.regions().length).toBe(1);
      expect(component.regions()[0].classification?.source).toBe('AI');
    });

    it('updateRegionClassification_NormalMode_DoesNothing', () => {
      component.regions.set([region('Soru 1')] as any);

      component.updateRegionClassification(0, { subjectId: 5, topicId: 6, subtopicIds: [7] });

      expect(component.regions()[0].classification).toBeUndefined();
    });
  });

  describe('sendToFix pageComplete', () => {
    beforeEach(() => component.imageData.set('data:image/png;base64,AAAA'));

    function arrangeSendToFix() {
      component.answerCount.set(2);
      component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
      return spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: false, message: 'x' })
      );
    }

    it('sendToFix_DefaultArg_PostsPageCompleteTrue', () => {
      const sendtoFix = arrangeSendToFix();

      component.sendToFix();

      expect(sendtoFix).toHaveBeenCalledTimes(1);
      expect(sendtoFix.calls.mostRecent().args[0].pageComplete).toBeTrue();
    });

    it('sendToFix_PageCompleteFalse_PostsPageCompleteFalse', () => {
      const sendtoFix = arrangeSendToFix();

      component.sendToFix(false);

      expect(sendtoFix.calls.mostRecent().args[0].pageComplete).toBeFalse();
    });

    it('sendToFix_AdvanceFalse_SuccessDoesNotCallNextImage', () => {
      arrangeSendToFix().and.returnValue(of({ success: true, added: 1, imageFile: 'p.png' }));
      const nextImage = spyOn(component, 'nextImage');

      component.sendToFix(true, false);

      expect(nextImage).not.toHaveBeenCalled();
    });

    it('sendToFix_DefaultAdvance_SuccessCallsNextImage', () => {
      arrangeSendToFix().and.returnValue(of({ success: true, added: 1, imageFile: 'p.png' }));
      const nextImage = spyOn(component, 'nextImage');

      component.sendToFix();

      expect(nextImage).toHaveBeenCalledTimes(1);
    });
  });

  describe('validateQuestions', () => {
    function correct(x: number, y: number) {
      return { ...answer(x, y), isCorrect: true };
    }

    it('validateQuestions_AnswerCountMismatch_ReportsIssueWithOneBasedNo', () => {
      component.answerCount.set(4);
      component.regions.set([
        region('Soru 1', [correct(0, 0), answer(0, 30), answer(0, 60), answer(0, 90)]),
        region('Soru 2', [correct(0, 0), answer(0, 30), answer(0, 60)]),
      ] as any);

      const issues = component.validateQuestions({ requireCorrectAnswer: false });

      expect(issues.length).toBe(1);
      expect(issues[0].index).toBe(1);
      expect(issues[0].no).toBe(2);
      expect(issues[0].name).toBe('Soru 2');
      expect(issues[0].messages).toEqual(['Şık sayısı 4 olmalı']);
    });

    it('validateQuestions_NoCorrectAnswer_RequireTrue_ReportsIssue', () => {
      component.answerCount.set(2);
      component.regions.set([region('Soru 1', [answer(0, 0), answer(0, 30)])] as any);

      const issues = component.validateQuestions({ requireCorrectAnswer: true });

      expect(issues.length).toBe(1);
      expect(issues[0].messages).toEqual(['Doğru cevap yok']);
    });

    it('validateQuestions_NoCorrectAnswer_RequireFalse_NoIssue', () => {
      component.answerCount.set(2);
      component.regions.set([region('Soru 1', [answer(0, 0), answer(0, 30)])] as any);

      expect(component.validateQuestions({ requireCorrectAnswer: false })).toEqual([]);
    });

    it('validateQuestions_DragDropQuestion_IsSkipped', () => {
      component.answerCount.set(4);
      component.regions.set([region('Soru 1', [])] as any);
      component.interactionTypes.set('Soru 1', 'dragDropLabeling');

      expect(component.validateQuestions({ requireCorrectAnswer: false })).toEqual([]);
      expect(component.validateQuestions({ requireCorrectAnswer: true })).toEqual([]);
    });

    it('validateQuestions_NoRegions_NoIssue', () => {
      component.regions.set([]);

      expect(component.validateQuestions({ requireCorrectAnswer: true })).toEqual([]);
      expect(component.ensureQuestionsValid({ requireCorrectAnswer: true })).toBeTrue();
    });

    it('checkRegionWarning_StillFlagsMissingCorrectAnswer', () => {
      component.answerCount.set(2);
      const r = { ...region('Soru 1', [answer(0, 0), answer(0, 30)]), id: 11 };
      component.regions.set([r] as any);

      component.checkRegionWarning(r as any);

      expect(component.warningMarkers.find((m) => m.id === 11)?.messages).toEqual(['Doğru cevap yok']);
    });

    it('ensureQuestionsValid_ManyIssues_ShowsFirstThreeAndRemainderAndSelectsFirst', () => {
      component.answerCount.set(4);
      component.regions.set(['Soru 1', 'Soru 2', 'Soru 3', 'Soru 4', 'Soru 5'].map((n) => region(n, [answer(0, 0)])) as any);
      const open = spyOn((component as any).snackBar, 'open');
      const select = spyOn(component, 'selectQuestion');

      const ok = component.ensureQuestionsValid({ requireCorrectAnswer: false });

      expect(ok).toBeFalse();
      const msg = open.calls.mostRecent().args[0] as string;
      expect(msg).toContain('1. soru: Şık sayısı 4 olmalı');
      expect(msg).toContain('3. soru');
      expect(msg).not.toContain('4. soru');
      expect(msg).toContain('ve 2 soru daha');
      expect(select).toHaveBeenCalledOnceWith(0);
    });
  });

  describe('sendToFix validation', () => {
    beforeEach(() => component.imageData.set('data:image/png;base64,AAAA'));

    it('sendToFix_AnswerCountMismatch_DoesNotPostOrAdvance', () => {
      component.answerCount.set(4);
      component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 1, imageFile: 'p.png' })
      );
      const nextImage = spyOn(component, 'nextImage');
      const open = spyOn((component as any).snackBar, 'open');

      component.sendToFix();

      expect(sendtoFix).not.toHaveBeenCalled();
      expect(nextImage).not.toHaveBeenCalled();
      expect(open.calls.mostRecent().args[0]).toContain('1. soru: Şık sayısı 4 olmalı');
    });

    it('sendToFix_ValidWithoutCorrectAnswer_PostsAndAdvances', () => {
      component.answerCount.set(2);
      component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 1, imageFile: 'p.png' })
      );
      const nextImage = spyOn(component, 'nextImage');

      component.sendToFix();

      expect(sendtoFix).toHaveBeenCalledTimes(1);
      expect(nextImage).toHaveBeenCalledTimes(1);
    });

    it('sendToFix_DragDropQuestionWithoutZones_StillPostsMcqOnly', () => {
      component.answerCount.set(2);
      component.regions.set([
        region('Soru 1', [answer(10, 10), answer(10, 50)]),
        region('Soru 2', []),
      ] as any);
      component.interactionTypes.set('Soru 2', 'dragDropLabeling');
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: false, message: 'x' })
      );

      component.sendToFix();

      expect(sendtoFix).toHaveBeenCalledTimes(1);
      expect(sendtoFix.calls.mostRecent().args[0].questions.length).toBe(1);
    });

    it('sendToFix_NoRegions_PostsEmptyQuestionsAndAdvances', () => {
      component.answerCount.set(4);
      component.regions.set([]);
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 0, imageFile: 'p.png' })
      );
      const nextImage = spyOn(component, 'nextImage');
      const open = spyOn((component as any).snackBar, 'open');

      component.sendToFix();

      expect(sendtoFix).toHaveBeenCalledTimes(1);
      const payload = sendtoFix.calls.mostRecent().args[0];
      expect(payload.questions).toEqual([]);
      expect(payload.pageComplete).toBeTrue();
      expect(payload.answerCount).toBe(4);
      expect(nextImage).toHaveBeenCalledTimes(1);
      expect(open.calls.mostRecent().args[0]).toBe('Boş sayfa eğitime eklendi.');
    });

    it('sendToFix_NoImageNoRegions_DoesNotPostOrAdvance', () => {
      component.imageData.set(null);
      component.regions.set([]);
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 0, imageFile: 'p.png' })
      );
      const nextImage = spyOn(component, 'nextImage');
      const open = spyOn((component as any).snackBar, 'open');

      component.sendToFix();

      expect(sendtoFix).not.toHaveBeenCalled();
      expect(nextImage).not.toHaveBeenCalled();
      expect(open.calls.mostRecent().args[0]).toBe('Önce bir sayfa yükleyin.');
    });

    it('sendToFix_NoRegionsPageCompleteFalse_DoesNotPostOrAdvance', () => {
      component.regions.set([]);
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 0, imageFile: 'p.png' })
      );
      const nextImage = spyOn(component, 'nextImage');
      const open = spyOn((component as any).snackBar, 'open');

      component.sendToFix(false);

      expect(sendtoFix).not.toHaveBeenCalled();
      expect(nextImage).not.toHaveBeenCalled();
      expect(open).toHaveBeenCalledTimes(1);
    });

    it('sendToFix_OnlyDragDropQuestions_DoesNotPostAndShowsMessage', () => {
      component.regions.set([region('Soru 1', [])] as any);
      component.interactionTypes.set('Soru 1', 'dragDropLabeling');
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 0, imageFile: 'p.png' })
      );
      const nextImage = spyOn(component, 'nextImage');
      const open = spyOn((component as any).snackBar, 'open');

      component.sendToFix();

      expect(sendtoFix).not.toHaveBeenCalled();
      expect(nextImage).not.toHaveBeenCalled();
      expect(open.calls.mostRecent().args[0]).toContain('sürükle-bırak');
    });

    it('sendToFix_OnlyDragDropQuestionsAdvanceFalse_DoesNotPostOrShowMessage', () => {
      component.regions.set([region('Soru 1', [])] as any);
      component.interactionTypes.set('Soru 1', 'dragDropLabeling');
      const sendtoFix = spyOn(component.questionDetectorService, 'sendtoFix').and.returnValue(
        of({ success: true, added: 0, imageFile: 'p.png' })
      );
      const open = spyOn((component as any).snackBar, 'open');

      component.sendToFix(true, false);

      expect(sendtoFix).not.toHaveBeenCalled();
      expect(open).not.toHaveBeenCalled();
    });
  });

  describe('imageChange', () => {
    function arrangeTwoPages() {
      component.imageFiles = [
        new File(['a'], 'p1.png', { type: 'image/png' }),
        new File(['b'], 'p2.png', { type: 'image/png' }),
      ];
      component.currentImageIndex = 0;
      const emitted: number[] = [];
      component.imageChange.subscribe(() => emitted.push(component.currentImageIndex));
      return emitted;
    }

    it('nextImage_EmitsImageChangeForNewPage', () => {
      const emitted = arrangeTwoPages();

      component.nextImage();

      expect(emitted).toEqual([1]);
    });

    it('previousImageAndGoToPage_EmitImageChange', () => {
      const emitted = arrangeTwoPages();

      component.previousImage();
      component.goToPage('1');

      expect(emitted).toEqual([1, 0]);
    });

    it('nextImage_NoFiles_DoesNotEmit', () => {
      const emitted: unknown[] = [];
      component.imageChange.subscribe(() => emitted.push(1));

      component.nextImage();

      expect(emitted).toEqual([]);
    });
  });

  describe('getRegions', () => {
    // Backend BulkQuestionDto/BulkAnswerDto/BulkPassageDto Width/Height int: kesirli değer 400 döner.
    it('getRegions_FractionalCoordinates_RoundsPayloadWithoutMutatingState', () => {
      const fractionalAnswer = { ...answer(10.4, 20.6), width: 40.5, height: 0.2 };
      const fractionalRegion = {
        ...region('Soru 1', [fractionalAnswer]),
        x: 1118.13123,
        y: 55.5,
        width: 300.49,
        height: 299.51,
        passage: { id: 1, title: 'P', x: 3.7, y: 4.2, width: 100.6, height: 50.4, imageUrl: '', imageId: '' },
      };
      component.regions.set([fractionalRegion] as any);
      component.passages.set([{ id: '1', x: 3.7, y: 4.2, width: 100.6, height: 50.4 }]);

      const payload = component.getRegions(null);

      const q = payload.questions[0];
      expect([q.x, q.y, q.width, q.height]).toEqual([1118, 56, 300, 300]);
      const a = q.answers[0];
      expect([a.x, a.y, a.width, a.height]).toEqual([10, 21, 41, 1]);
      const qp = q.passage!;
      expect([qp.x, qp.y, qp.width, qp.height]).toEqual([4, 4, 101, 50]);
      const p = payload.passages[0];
      expect([p.x, p.y, p.width, p.height]).toEqual([4, 4, 101, 50]);

      const stateRegion = component.regions()[0];
      expect(stateRegion.x).toBe(1118.13123);
      expect(stateRegion.answers[0].width).toBe(40.5);
      expect(stateRegion.passage!.x).toBe(3.7);
      expect(component.passages()[0].width).toBe(100.6);
    });
  });
});
