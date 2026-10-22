import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Subject, of, throwError } from 'rxjs';

import { CanvasDetectionSource } from './canvas-detection-source';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { LabelingAiDetectResponse, LabelingAiModel, LabelingPage, LabelingResult } from '../../models/labeling';
import { PredictionList } from '../../models/prediction';
import { LabelingService } from '../../services/labeling.service';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import questionTr from '../../../../public/i18n/question/tr.json';

/** Gerçek sözlük yüklenir; çeviri anahtarı bozulursa snackbar metinleri testte fark edilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { 'question/tr': questionTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

const HAIKU: LabelingAiModel = {
  id: 'claude-haiku-5-5',
  displayName: 'Haiku 5.5',
  inputUsdPerMTok: 1,
  outputUsdPerMTok: 5,
  isDefault: true,
};
const OPUS: LabelingAiModel = {
  id: 'claude-opus-5-5',
  displayName: 'Opus 5.5',
  inputUsdPerMTok: 5,
  outputUsdPerMTok: 25,
  isDefault: false,
};

const PREDICTIONS = { success: true, predictions: [] } as PredictionList;
/** Küçük geçerli data URL'ler: sayfa hash'i (crypto.subtle) gerçek baytlardan hesaplanır. */
const PAGE_A = `data:image/png;base64,${btoa('page-a')}`;
const PAGE_B = `data:image/png;base64,${btoa('page-b')}`;
const PAGE_C = `data:image/png;base64,${btoa('page-c')}`;

const SOURCE_KEY = 'questionCanvas.source';
const MODEL_KEY = 'questionCanvas.aiModel';

function aiResult(overrides: Partial<LabelingResult> = {}): LabelingResult {
  return {
    source: 'Ai',
    modelName: HAIKU.id,
    predictions: PREDICTIONS,
    baseSource: null,
    baseModelName: null,
    inputTokens: 10,
    outputTokens: 20,
    estimatedCostUsd: 0.001,
    promptVersion: 3,
    createdByName: null,
    createdAt: '2026-10-22T00:00:00Z',
    updatedAt: '2026-10-22T00:00:00Z',
    ...overrides,
  };
}

function aiResponse(origin: 'live' | 'cached' = 'live', model = HAIKU.id): LabelingAiDetectResponse {
  return { origin, imageSha256: 'sha-a', result: aiResult({ modelName: model }) };
}

function pageWith(results: LabelingResult[]): LabelingPage {
  return { imageSha256: 'sha-a', width: 100, height: 100, results, bank: null };
}

function makeFile(text: string): File {
  return new File([text], `${text}.png`, { type: 'image/png' });
}

/** image-selector'ın tuvale dokunan yüzeyi: sinyal + spy'lar. Gerçek `applyExternalPrediction` gibi kaynağı yazar. */
function makeSelector() {
  const detectionSource = signal<{ kind: string } | null>(null);
  let seq = 0;
  return {
    detectionSource,
    /** Otomatik ilerleme açıkken (sayfalar kullanıcı bakmadan geçilir) önceden getirme yapılmaz. */
    autoMode: signal(false),
    imageFiles: [] as File[],
    currentImageIndex: 0,
    predict: jasmine.createSpy('predict').and.callFake(() => detectionSource.set({ kind: 'Model' })),
    beginExternalDetection: jasmine.createSpy('beginExternalDetection').and.callFake(() => ++seq),
    applyExternalPrediction: jasmine
      .createSpy('applyExternalPrediction')
      .and.callFake((_seq: number, _predictions: unknown, source: { kind: string }) => detectionSource.set(source)),
    failExternalDetection: jasmine.createSpy('failExternalDetection'),
    hasUnsavedEdits: jasmine.createSpy('hasUnsavedEdits').and.returnValue(false),
  };
}

type FakeSelector = ReturnType<typeof makeSelector>;

/** Sayfa değişikliği / yükleme gibi asenkron adımların (hash, FileReader, promise zinciri) bitmesini bekler. */
async function waitUntil(condition: () => boolean, label: string): Promise<void> {
  for (let i = 0; i < 500; i++) {
    if (condition()) {
      return;
    }
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  throw new Error(`waitUntil zaman aşımı: ${label}`);
}

/** Beklenen bir çağrı olmadığını göstermek için birkaç makrotask boşaltır. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
}

describe('CanvasDetectionSource', () => {
  let labelingApi: jasmine.SpyObj<LabelingService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let selector: FakeSelector;

  /** Servisi oluşturur; tercih localStorage'a `preferred` olarak yazılır (servis kurulurken okunur). */
  function create({ enabled = true, preferred }: { enabled?: boolean; preferred?: 'Model' | 'Ai' } = {}) {
    if (preferred) {
      localStorage.setItem(SOURCE_KEY, preferred);
    }
    const source = TestBed.inject(CanvasDetectionSource);
    source.attach(() => selector as unknown as ImageSelectorComponent, enabled, 'question');
    return source;
  }

  beforeEach(() => {
    localStorage.removeItem(SOURCE_KEY);
    localStorage.removeItem(MODEL_KEY);
    spyOn(console, 'error');
    spyOn(console, 'warn');

    labelingApi = jasmine.createSpyObj<LabelingService>('LabelingService', ['getAiModels', 'getPage', 'aiDetect']);
    labelingApi.getAiModels.and.returnValue(of([HAIKU, OPUS]));
    labelingApi.getPage.and.returnValue(throwError(() => ({ status: 404 })));
    labelingApi.aiDetect.and.returnValue(of(aiResponse()));

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    snackBar.open.and.returnValue({ onAction: () => of(undefined) } as any);

    selector = makeSelector();

    TestBed.configureTestingModule({
      imports: [translocoTesting],
      providers: [
        CanvasDetectionSource,
        { provide: LabelingService, useValue: labelingApi },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
  });

  afterEach(() => {
    localStorage.removeItem(SOURCE_KEY);
    localStorage.removeItem(MODEL_KEY);
  });

  describe('attach', () => {
    it('attach_EnabledFalse_DoesNotRequestAiModels', () => {
      const source = create({ enabled: false });

      expect(labelingApi.getAiModels).not.toHaveBeenCalled();
      expect(source.available()).toBeFalse();
    });
  });

  describe('onPageReady', () => {
    it('onPageReady_ModelPreference_CallsPredictWithoutAiDetect', async () => {
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);

      expect(selector.predict).toHaveBeenCalledTimes(1);
      await waitUntil(() => labelingApi.getPage.calls.count() > 0, 'sayfa kaydı sorgusu');
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
    });

    it('onPageReady_AiPreference_CallsAiDetectAndAppliesAiSource', async () => {
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 1, 'Claude sonucu uygulandı');

      expect(labelingApi.aiDetect).toHaveBeenCalledOnceWith({ imageBase64: PAGE_A, model: HAIKU.id, force: false });
      expect(selector.applyExternalPrediction).toHaveBeenCalledOnceWith(1, PREDICTIONS, {
        kind: 'Ai',
        modelName: HAIKU.id,
        promptVersion: 3,
      });
      expect(selector.predict).not.toHaveBeenCalled();
    });

    it('onPageReady_AiPreferenceWithoutModels_FallsBackToPredict', async () => {
      labelingApi.getAiModels.and.returnValue(of([]));
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => labelingApi.getAiModels.calls.count() === 1, 'model listesi istendi');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.predict.calls.count() === 1, 'YOLO yedeği');

      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
      expect(source.available()).toBeFalse();
    });
  });

  describe('Claude yanıtı (origin)', () => {
    it('runClaude_CachedOrigin_SetsCachedAndShowsNoCostSnackbar', async () => {
      labelingApi.aiDetect.and.returnValue(of(aiResponse('cached')));
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 1, 'uygulandı');

      expect(source.aiOrigin()).toBe('cached');
      expect(snackBar.open).not.toHaveBeenCalled();
    });

    it('runClaude_LiveOrigin_SetsLiveAndShowsCost', async () => {
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 1, 'uygulandı');

      expect(source.aiOrigin()).toBe('live');
      expect(snackBar.open).toHaveBeenCalledWith(
        jasmine.stringContaining('0.0010'),
        questionTr.detection.aiOk,
        jasmine.any(Object)
      );
    });
  });

  describe('hata yolu', () => {
    it('runClaude_Error429WithServerMessage_FailsDetectionFallsBackToPredictAndShowsMessage', async () => {
      labelingApi.aiDetect.and.returnValue(
        throwError(() => ({ status: 429, error: { message: 'Günlük bütçe doldu.' } }))
      );
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.predict.calls.count() === 1, 'YOLO yedeği');

      expect(selector.failExternalDetection).toHaveBeenCalledOnceWith(1);
      expect(selector.applyExternalPrediction).not.toHaveBeenCalled();
      expect(snackBar.open).toHaveBeenCalledWith(
        jasmine.stringContaining('Günlük bütçe doldu.'),
        questionTr.detection.aiOk,
        jasmine.any(Object)
      );
    });

    it('runClaude_ErrorWithoutServerMessage_ShowsGenericText', async () => {
      labelingApi.aiDetect.and.returnValue(throwError(() => ({ status: 500 })));
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.predict.calls.count() === 1, 'YOLO yedeği');

      expect(snackBar.open).toHaveBeenCalledWith(
        jasmine.stringContaining(questionTr.detection.aiFailed),
        questionTr.detection.aiOk,
        jasmine.any(Object)
      );
    });
  });

  describe('iptal ve sayfa geçişi', () => {
    it('runClaude_ForegroundRequestCancelledOnResetPage_NoApplyNoFallback', async () => {
      const pending = new Subject<LabelingAiDetectResponse>();
      labelingApi.aiDetect.and.returnValue(pending.asObservable());
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => labelingApi.aiDetect.calls.count() === 1, 'Claude isteği başladı');
      expect(pending.observed).toBeTrue();

      source.resetPage();
      expect(pending.observed).toBeFalse();
      pending.next(aiResponse());
      pending.complete();
      await settle();

      expect(selector.applyExternalPrediction).not.toHaveBeenCalled();
      expect(selector.failExternalDetection).not.toHaveBeenCalled();
      expect(selector.predict).not.toHaveBeenCalled();
      expect(snackBar.open).not.toHaveBeenCalled();
      expect(source.aiOrigin()).toBeNull();
      expect(source.aiBusy()).toBeFalse();
    });

    it('onPageReady_PrefetchedPageOpenedWhilePending_SharesOneRequestAndAppliesOnce', async () => {
      const prefetchB = new Subject<LabelingAiDetectResponse>();
      labelingApi.aiDetect.and.returnValues(of(aiResponse()), prefetchB.asObservable());
      selector.imageFiles = [makeFile('page-a'), makeFile('page-b')];
      selector.currentImageIndex = 0;
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      // A tamamlanınca B önceden getirilir; yanıt henüz yok.
      await waitUntil(() => labelingApi.aiDetect.calls.count() === 2, 'sonraki sayfa önceden getirildi');

      // Kullanıcı B'yi bu sırada açar: aynı istek beklenir, ikinci ai-detect çağrısı olmaz.
      selector.currentImageIndex = 1;
      await source.onPageReady(PAGE_B);
      expect(labelingApi.aiDetect.calls.count()).toBe(2);

      // Açılış başına tek beginExternalDetection; önceden getirme bunu çağırmaz.
      expect(selector.beginExternalDetection).toHaveBeenCalledTimes(2);
      prefetchB.next(aiResponse());
      prefetchB.complete();
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 2, 'B uygulandı');

      expect(selector.applyExternalPrediction.calls.argsFor(1)).toEqual([
        2,
        PREDICTIONS,
        { kind: 'Ai', modelName: HAIKU.id, promptVersion: 3 },
      ]);
    });

    it('onPageReady_PrefetchedRequestResolvesAfterResetPage_IgnoredByPageToken', async () => {
      const prefetchB = new Subject<LabelingAiDetectResponse>();
      labelingApi.aiDetect.and.returnValues(of(aiResponse()), prefetchB.asObservable());
      selector.imageFiles = [makeFile('page-a'), makeFile('page-b')];
      selector.currentImageIndex = 0;
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => labelingApi.aiDetect.calls.count() === 2, 'sonraki sayfa önceden getirildi');
      selector.currentImageIndex = 1;
      await source.onPageReady(PAGE_B);
      // B'nin isteği önceden getirme olarak başladı (iptal edilmez); sayfa bu arada değişirse yanıt yok sayılmalı.
      source.resetPage();
      prefetchB.next(aiResponse());
      prefetchB.complete();
      await settle();

      expect(selector.applyExternalPrediction).toHaveBeenCalledTimes(1);
      expect(source.pageRecord()).toBeNull();
    });

    /** A başarılı, B'nin önceden getirilmesi 429 ile düşer: Claude askıya alınır. */
    async function suspendByPrefetch429(): Promise<CanvasDetectionSource> {
      labelingApi.aiDetect.and.returnValues(
        of(aiResponse()),
        throwError(() => ({ status: 429, error: { message: 'Kota doldu.' } })),
        of(aiResponse()),
        of(aiResponse())
      );
      selector.imageFiles = [makeFile('page-a'), makeFile('page-b'), makeFile('page-c')];
      selector.currentImageIndex = 0;
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      await waitUntil(() => (console.warn as jasmine.Spy).calls.count() > 0, 'prefetch hatası');
      expect(labelingApi.aiDetect.calls.count()).toBe(2);
      return source;
    }

    it('prefetch_After429_SuspendsClaudeAndNextPageOpensWithYolo', async () => {
      const source = await suspendByPrefetch429();

      selector.currentImageIndex = 1;
      await source.onPageReady(PAGE_B);
      await waitUntil(() => selector.predict.calls.count() === 1, 'askıdayken YOLO ile açıldı');
      await settle();

      expect(labelingApi.aiDetect.calls.count()).toBe(2);
      expect(selector.beginExternalDetection).toHaveBeenCalledTimes(1);
    });

    it('selectSource_AiAfterSuspension_RetriesClaudeOnce', async () => {
      const source = await suspendByPrefetch429();
      selector.currentImageIndex = 1;
      await source.onPageReady(PAGE_B);
      await waitUntil(() => selector.predict.calls.count() === 1, 'askıdayken YOLO ile açıldı');

      source.selectSource('Ai');
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 2, 'Claude yeniden denendi');
      // B başarılı olunca askı kalktığı için sonraki sayfa (C) önceden getirilir: 3. çağrı B, 4. çağrı C.
      await waitUntil(() => labelingApi.aiDetect.calls.count() === 4, 'C önceden getirildi');

      expect(labelingApi.aiDetect.calls.argsFor(2)[0]).toEqual({ imageBase64: PAGE_B, model: HAIKU.id, force: false });
      expect(labelingApi.aiDetect.calls.argsFor(3)[0].imageBase64).toBe(PAGE_C);
    });

    it('prefetch_OnLastPage_WrapsAroundToFirstPage', async () => {
      selector.imageFiles = [makeFile('page-a'), makeFile('page-b'), makeFile('page-c')];
      selector.currentImageIndex = 2;
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_C);
      await waitUntil(() => labelingApi.aiDetect.calls.count() === 2, 'ilk sayfa önceden getirildi');

      expect(labelingApi.aiDetect.calls.argsFor(0)[0].imageBase64).toBe(PAGE_C);
      expect(labelingApi.aiDetect.calls.argsFor(1)[0].imageBase64).toBe(PAGE_A);
    });

    it('prefetch_AutoModeOn_DoesNotRequestNextPage', async () => {
      selector.autoMode.set(true);
      selector.imageFiles = [makeFile('page-a'), makeFile('page-b')];
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 1, 'A uygulandı');
      await settle();

      expect(labelingApi.aiDetect.calls.count()).toBe(1);
    });
  });

  describe('aiNotice', () => {
    it('runClaude_Error429_SetsAiNoticeWithServerMessage', async () => {
      labelingApi.aiDetect.and.returnValue(throwError(() => ({ status: 429, error: { message: 'Günlük bütçe doldu.' } })));
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');

      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.predict.calls.count() === 1, 'YOLO yedeği');

      expect(source.aiNotice()).toEqual(jasmine.stringContaining('Günlük bütçe doldu.'));
    });

    it('selectSource_AfterFallback_ClearsAiNotice', async () => {
      labelingApi.aiDetect.and.returnValues(throwError(() => ({ status: 429, error: { message: 'Kota.' } })), of(aiResponse()));
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      await waitUntil(() => source.aiNotice() !== null, 'uyarı kuruldu');

      source.selectSource('Ai');

      expect(source.aiNotice()).toBeNull();
    });

    it('runClaude_SuccessOnNextPage_ClearsAiNotice', async () => {
      labelingApi.aiDetect.and.returnValues(throwError(() => ({ status: 500 })), of(aiResponse()));
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      await waitUntil(() => source.aiNotice() !== null, 'uyarı kuruldu');

      await source.onPageReady(PAGE_B);
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 1, 'B Claude ile uygulandı');

      expect(source.aiNotice()).toBeNull();
    });
  });

  describe('Manuel kaynak', () => {
    it('selectSource_SameAsActiveKind_DoesNothing', async () => {
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      expect(selector.predict).toHaveBeenCalledTimes(1);

      source.selectSource('Model');

      expect(selector.predict).toHaveBeenCalledTimes(1);
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
    });

    it('selectSource_ManualWithoutRecord_DoesNothing', async () => {
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      await waitUntil(() => labelingApi.getPage.calls.count() > 0, 'kayıt sorgusu');
      await settle();

      source.selectSource('Manual');

      expect(source.manualRecord()).toBeNull();
      expect(selector.applyExternalPrediction).not.toHaveBeenCalled();
    });

    it('selectSource_ManualWithRecord_AppliesManualPredictions', async () => {
      labelingApi.getPage.and.returnValue(of(pageWith([aiResult({ source: 'Manual', modelName: 'manual' })])));
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      await waitUntil(() => source.manualRecord() !== null, 'manuel kayıt bulundu');

      source.selectSource('Manual');

      expect(selector.applyExternalPrediction).toHaveBeenCalledOnceWith(1, PREDICTIONS, { kind: 'Manual', base: null });
    });
  });

  describe('kaynak değişimi onayı ve model seçimi', () => {
    it('selectSource_UnsavedEdits_AsksBeforeSwitchingToClaude', async () => {
      selector.hasUnsavedEdits.and.returnValue(true);
      const discard = new Subject<void>();
      snackBar.open.and.returnValue({ onAction: () => discard.asObservable() } as any);
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);

      source.selectSource('Ai');

      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
      expect(snackBar.open).toHaveBeenCalledWith(
        questionTr.detection.unsavedEdits,
        questionTr.detection.discardAction,
        jasmine.any(Object)
      );

      discard.next();
      await waitUntil(() => labelingApi.aiDetect.calls.count() === 1, 'onay sonrası Claude');
      expect(localStorage.getItem(SOURCE_KEY)).toBe('Ai');
    });

    it('selectSource_NoUnsavedEdits_SwitchesImmediatelyAndPersistsPreference', async () => {
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);

      source.selectSource('Ai');

      expect(snackBar.open).not.toHaveBeenCalled();
      expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem(SOURCE_KEY)).toBe('Ai');
    });

    it('setAiModel_ClaudeActive_PersistsAndReRunsWithNewModel', async () => {
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');
      await source.onPageReady(PAGE_A);
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 1, 'ilk Claude sonucu');

      labelingApi.aiDetect.and.returnValue(of(aiResponse('live', OPUS.id)));
      source.setAiModel(OPUS.id);

      expect(source.aiModelId()).toBe(OPUS.id);
      expect(localStorage.getItem(MODEL_KEY)).toBe(OPUS.id);
      expect(labelingApi.aiDetect).toHaveBeenCalledWith({ imageBase64: PAGE_A, model: OPUS.id, force: false });
      await waitUntil(() => selector.applyExternalPrediction.calls.count() === 2, 'yeni modelle yeniden çalıştı');
    });

    it('setAiModel_ModelActive_OnlyPersistsSelection', async () => {
      const source = create({ preferred: 'Model' });
      await waitUntil(() => source.available(), 'modeller');

      source.setAiModel(OPUS.id);

      expect(source.aiModelId()).toBe(OPUS.id);
      expect(localStorage.getItem(MODEL_KEY)).toBe(OPUS.id);
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
    });

    it('setAiModel_UnknownId_DoesNothing', async () => {
      const source = create({ preferred: 'Ai' });
      await waitUntil(() => source.available(), 'modeller');
      const before = source.aiModelId();

      source.setAiModel('bilinmeyen-model');

      expect(source.aiModelId()).toBe(before);
      expect(localStorage.getItem(MODEL_KEY)).toBeNull();
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
    });
  });
});
