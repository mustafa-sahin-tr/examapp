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
    function arrangeSendToFix() {
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
});
