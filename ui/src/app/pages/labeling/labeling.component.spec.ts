import { Component, EventEmitter, Input, Output, forwardRef, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';
import { MatButtonToggleChange } from '@angular/material/button-toggle';
import { MatSelectChange } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';

import { LabelingComponent } from './labeling.component';
import { pageIdOf } from './labeling-pool';
import { ImageSelectorComponent, TrainedPage } from '../image-selector/image-selector.component';
import { QuestionDetectorService } from '../../services/question-detector.service';
import { LabelingService } from '../../services/labeling.service';
import { DetectionSource, LabelingAiModel, LabelingPage, LabelingResult } from '../../models/labeling';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import labelingTr from '../../../../public/i18n/labeling/tr.json';

/** Gerçek tuval yerine; `@ViewChild(ImageSelectorComponent)` bu stub'ı DI token'ı üzerinden bulur. */
@Component({
  selector: 'app-image-selector',
  standalone: true,
  template: '',
  providers: [{ provide: ImageSelectorComponent, useExisting: forwardRef(() => ImageSelectorStub) }],
})
class ImageSelectorStub {
  @Input() navigationLocked = false;
  @Input() advanceOnTrain = true;
  @Input() externalDetection = false;
  @Output() trained = new EventEmitter<TrainedPage>();
  @Output() pageReady = new EventEmitter<string>();
  @Output() manualSaved = new EventEmitter<string>();
  readonly detectionSource = signal<DetectionSource | null>(null);
  /** Kaydedilmemiş elle düzeltme var mı (gerçek bileşende kutular ile son uygulanan tespit farkı). */
  unsaved = false;
  readonly sendingToFix = signal(false);
  readonly pageLoading = signal(false);
  readonly inProgress = signal(false);
  readonly selectionMode = signal<string | null>(null);
  readonly selectionModeLocked = signal<string | null>(null);
  readonly answerCount = signal(4);
  readonly autoAlign = signal(false);
  readonly imageData = signal<string | null>(null);
  readonly pageLoadFailed = signal(false);
  readonly predictFailed = signal(false);
  readonly regions = signal<unknown[]>([]);
  loaded: File[] = [];
  /** Gerçek bileşen gibi: yükleme anında eski sayfa durumu silinir, görsel okunmuş sayılır. */
  loadFile = jasmine.createSpy('loadFile').and.callFake((file: File) => {
    this.loaded.push(file);
    this.pageLoadFailed.set(false);
    this.predictFailed.set(false);
    this.regions.set([]);
    const data = `data:image/webp;base64,${btoa(file.name)}`;
    this.imageData.set(data);
    if (this.externalDetection) {
      // Gerçek bileşen: görsel okununca `pageReady` yayılır (tespit üst komponentin kararına kalır).
      queueMicrotask(() => this.pageReady.emit(data));
    }
  });
  private seq = 0;
  beginExternalDetection = jasmine.createSpy('beginExternalDetection').and.callFake(() => ++this.seq);
  applyExternalPrediction = jasmine.createSpy('applyExternalPrediction');
  failExternalDetection = jasmine.createSpy('failExternalDetection');
  hasUnsavedEdits = jasmine.createSpy('hasUnsavedEdits').and.callFake(() => this.unsaved);
  alignAllAnswers = jasmine.createSpy('alignAllAnswers');
  sendToFix = jasmine.createSpy('sendToFix');
  predict = jasmine.createSpy('predict');
  recomputeWarnings = jasmine.createSpy('recomputeWarnings');
  inspector: unknown = null;
  getActiveInspector = jasmine.createSpy('getActiveInspector').and.callFake(() => this.inspector);
  selectNextQuestion = jasmine.createSpy('selectNextQuestion');
  selectPreviousQuestion = jasmine.createSpy('selectPreviousQuestion');
  removeAnswer = jasmine.createSpy('removeAnswer');
  setCorrectAnswer = jasmine.createSpy('setCorrectAnswer');
  alignAnswers = jasmine.createSpy('alignAnswers');
  selectAnswerMode = jasmine.createSpy('selectAnswerMode');
  removeQuestion = jasmine.createSpy('removeQuestion');
  lockSelectionMode = jasmine.createSpy('lockSelectionMode').and.callFake((mode: string | null) => {
    this.selectionModeLocked.set(mode);
    this.selectionMode.set(mode);
  });
}

function image(name: string, content = name): File {
  const file = new File([content], name, { type: 'image/webp' });
  Object.defineProperty(file, 'webkitRelativePath', { value: `kitap/${name}` });
  return file;
}

/** Elle çözülen hash: dosya adına göre bekletilebilir. */
function controllableHash(pendingNames: string[] = []) {
  const resolvers = new Map<string, (id: string) => void>();
  const hash = (file: File) =>
    pendingNames.includes(file.name)
      ? new Promise<string>((resolve) => resolvers.set(file.name, resolve))
      : Promise.resolve(`id-${file.name}`);
  return { hash, release: (name: string) => resolvers.get(name)?.(`id-${name}`) };
}

/** Tüm bekleyen mikro görevler + crypto.subtle (gerçek async) tamamlansın. */
async function settle(fixture: ComponentFixture<LabelingComponent>): Promise<void> {
  for (let i = 0; i < 20; i++) {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

describe('LabelingComponent', () => {
  let fixture: ComponentFixture<LabelingComponent>;
  let component: LabelingComponent;
  let detector: jasmine.SpyObj<QuestionDetectorService>;
  let labelingApi: jasmine.SpyObj<LabelingService>;

  function selector(): ImageSelectorStub {
    return fixture.debugElement.query((de) => de.componentInstance instanceof ImageSelectorStub)
      .componentInstance as ImageSelectorStub;
  }

  async function pickFolder(files: File[]): Promise<void> {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: files });
    await component.onFolderSelected({ target: input } as unknown as Event);
    await settle(fixture);
  }

  async function setup(labeledIds: string[] = [], fail = false, aiModels: LabelingAiModel[] = []): Promise<void> {
    detector = jasmine.createSpyObj<QuestionDetectorService>('QuestionDetectorService', ['getLabeledPages']);
    detector.getLabeledPages.and.returnValue(
      fail ? throwError(() => new Error('down')) : of({ pageIds: labeledIds })
    );
    labelingApi = jasmine.createSpyObj<LabelingService>('LabelingService', ['getAiModels', 'getPage', 'aiDetect', 'saveManual']);
    labelingApi.getAiModels.and.returnValue(of(aiModels));
    // Varsayılan: sayfanın kaydı yok (404 → null).
    labelingApi.getPage.and.returnValue(throwError(() => ({ status: 404 })));
    TestBed.configureTestingModule({
      imports: [LabelingComponent, translocoTestingModule({ langs: { 'labeling/tr': labelingTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: QuestionDetectorService, useValue: detector },
        { provide: LabelingService, useValue: labelingApi },
      ],
    });
    TestBed.overrideComponent(LabelingComponent, {
      remove: { imports: [ImageSelectorComponent] },
      add: { imports: [ImageSelectorStub] },
    });
    fixture = TestBed.createComponent(LabelingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await settle(fixture);
  }

  beforeEach(() => {
    localStorage.removeItem('labeling.answerCount');
    localStorage.removeItem('labeling.source');
    localStorage.removeItem('labeling.aiModel');
  });

  it('init_LoadsLabeledPagesAndPassesTrainingInputsToSelector', async () => {
    await setup();

    expect(detector.getLabeledPages).toHaveBeenCalled();
    const stub = selector();
    expect(stub.navigationLocked).toBeTrue();
    expect(stub.advanceOnTrain).toBeFalse();
  });

  it('folderPick_KeepsOnlyImagesAndLoadsOneRandomPage', async () => {
    await setup();
    const json = new File(['{}'], 'question-coordinates.json', { type: 'application/json' });

    await pickFolder([image('p1.webp'), image('p2.webp'), json]);

    expect(component.totalImages()).toBe(2);
    expect(selector().loaded.length).toBe(1);
    expect(component.currentPath()).toMatch(/^kitap\/p[12]\.webp$/);
    // açık sayfa + arka planda bulunan aday: biri havuzda değil, biri önceden seçilmiş
    expect(component.remaining()).toBe(1);
  });

  it('folderPick_SkipsAlreadyLabeledPages', async () => {
    const labeledFile = image('done.webp');
    await setup([await pageIdOf(labeledFile)]);

    await pickFolder([labeledFile, image('todo.webp')]);

    expect(selector().loaded.map((f) => f.name)).toEqual(['todo.webp']);
    expect(component.skippedLabeled()).toBe(1);
  });

  it('folderPick_AllLabeled_ShowsAllDone', async () => {
    const a = image('a.webp');
    await setup([await pageIdOf(a)]);

    await pickFolder([a]);

    expect(component.finished()).toBeTrue();
    expect(selector().loadFile).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(labelingTr.allDone);
  });

  it('skip_LoadsAnotherPageWithoutTrainingAndNeverRepeats', async () => {
    await setup();
    await pickFolder([image('a.webp'), image('b.webp')]);

    component.skip();
    await settle(fixture);

    const names = selector().loaded.map((f) => f.name);
    expect(names.length).toBe(2);
    expect(new Set(names).size).toBe(2);
    expect(component.passedThisSession()).toBe(1);
    expect(selector().sendToFix).not.toHaveBeenCalled();

    component.skip();
    await settle(fixture);
    expect(component.finished()).toBeTrue();
  });

  it('train_SendsCompletePageThenOnTrainedCountsAndAdvances', async () => {
    await setup();
    await pickFolder([image('a.webp'), image('b.webp')]);
    const firstId = component.current()!.pageId;

    component.train();
    expect(selector().sendToFix).toHaveBeenCalledOnceWith(true);

    selector().trained.emit({ pageId: firstId, emptyPage: false, added: 3 });
    await settle(fixture);

    expect(component.trainedThisSession()).toBe(1);
    expect(selector().loaded.length).toBe(2);
    expect(component.current()!.pageId).not.toBe(firstId);
  });

  it('train_WhileSending_Ignored', async () => {
    await setup();
    await pickFolder([image('a.webp')]);
    selector().sendingToFix.set(true);
    fixture.detectChanges();

    component.train();
    component.skip();

    expect(selector().sendToFix).not.toHaveBeenCalled();
    expect(component.passedThisSession()).toBe(0);
  });

  it('labeledPagesError_ShowsRetryAndRetryStartsPicking', async () => {
    await setup([], true);
    expect(component.labeledError()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain(labelingTr.retry);

    await pickFolder([image('a.webp')]);
    expect(selector().loadFile).not.toHaveBeenCalled();

    detector.getLabeledPages.and.returnValue(of({ pageIds: [] }));
    await component.retryLabeledPages();
    await settle(fixture);

    expect(component.labeledError()).toBeFalse();
    expect(selector().loaded.length).toBe(1);
  });

  it('setAnswerCount_UpdatesSelectorAndPersists', async () => {
    await setup();

    component.setAnswerCount(5);
    fixture.detectChanges(); // effect tuvale aktarır

    expect(selector().answerCount()).toBe(5);
    expect(selector().recomputeWarnings).toHaveBeenCalled();
    expect(localStorage.getItem('labeling.answerCount')).toBe('5');
  });

  it('keyN_SkipsWhenNotTyping', async () => {
    await setup();
    await pickFolder([image('a.webp'), image('b.webp')]);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'n' }));
    await settle(fixture);

    expect(component.passedThisSession()).toBe(1);
  });

  // --- Kod incelemesi düzeltmeleri ---

  it('folderReselect_WhilePrefetchPending_OldCandidateDoesNotLeak', async () => {
    await setup();
    const control = controllableHash(['a2.webp']);
    component.hashFile = control.hash;
    spyOn(Math, 'random').and.returnValue(0);
    await pickFolder([image('a1.webp'), image('a2.webp')]); // a1 açık, a2'nin hash'i bekliyor

    await pickFolder([image('b1.webp')]);
    control.release('a2.webp');
    await settle(fixture);

    expect(component.current()?.file.name).toBe('b1.webp');
    component.skip();
    await settle(fixture);
    expect(selector().loaded.map((f) => f.name)).toEqual(['a1.webp', 'b1.webp']);
    expect(component.finished()).toBeTrue();
  });

  it('remaining_DoesNotDropWhilePrefetchHashInFlight', async () => {
    await setup();
    const control = controllableHash(['a2.webp', 'a3.webp']);
    component.hashFile = control.hash;
    spyOn(Math, 'random').and.returnValue(0);

    await pickFolder([image('a1.webp'), image('a2.webp'), image('a3.webp')]);

    expect(component.remaining()).toBe(2);
  });

  it('folderChange_WhileTrainingPending_ResponseDoesNotCountInNewSession', async () => {
    await setup();
    component.hashFile = controllableHash().hash;
    await pickFolder([image('a1.webp')]);
    component.train();

    await pickFolder([image('b1.webp'), image('b2.webp')]);
    const shownBefore = selector().loaded.length;
    selector().trained.emit({ pageId: 'id-a1.webp', emptyPage: false, added: 1 });
    await settle(fixture);

    expect(component.trainedThisSession()).toBe(0);
    expect(selector().loaded.length).toBe(shownBefore);

    // id yine de kümeye yazıldı: aynı dosya tekrar seçilirse atlanır.
    await pickFolder([image('a1.webp')]);
    expect(component.skippedLabeled()).toBe(1);
    expect(component.finished()).toBeTrue();
  });

  it('pageLoadFailure_CanTrainFalseAndHintShown', async () => {
    await setup();
    await pickFolder([image('a.webp')]);
    expect(component.canTrain()).toBeTrue();

    selector().pageLoadFailed.set(true);
    selector().imageData.set(null);
    fixture.detectChanges();

    expect(component.canTrain()).toBeFalse();
    component.train();
    expect(selector().sendToFix).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(labelingTr.pageLoadFailedHint);
    expect(component.canSkip()).toBeTrue();
  });

  it('predictFailure_EmptyRegions_CanTrainFalseUntilBoxesDrawn', async () => {
    await setup();
    await pickFolder([image('a.webp')]);

    selector().predictFailed.set(true);
    fixture.detectChanges();

    expect(component.canTrain()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain(labelingTr.predictFailedHint);

    selector().regions.set([{ name: 'Soru 1' }]);
    fixture.detectChanges();
    expect(component.canTrain()).toBeTrue();
  });

  it('onTrained_ServerPageIdMismatch_NotCountedNoAdvanceShowsError', async () => {
    await setup();
    const snackBar = spyOn(TestBed.inject(MatSnackBar), 'open');
    await pickFolder([image('a.webp'), image('b.webp')]);
    const candidate = component.current()!;

    component.train();
    selector().trained.emit({ pageId: 'ffffffffffffffff', emptyPage: true, added: 0 });
    await settle(fixture);

    expect(component.trainedThisSession()).toBe(0);
    expect(component.current()).toBe(candidate);
    expect(selector().loaded.length).toBe(1);
    expect(snackBar).toHaveBeenCalledWith(labelingTr.pageIdMismatch, labelingTr.ok, jasmine.any(Object));
  });

  it('align_OneShotAlignmentAndAutoAlignStaysOff', async () => {
    await setup();
    await pickFolder([image('a.webp')]);
    selector().autoAlign.set(true);
    component.setAnswerCount(3); // effect yeniden çalışır
    fixture.detectChanges();
    // Sayfa açılışındaki varsayılan YOLO tespiti (predict) sayılmaz; yalnız hizalama sırasında çağrı aranır.
    selector().predict.calls.reset();

    component.align();

    expect(selector().alignAllAnswers).toHaveBeenCalledTimes(1);
    expect(selector().predict).not.toHaveBeenCalled();
    expect(selector().autoAlign()).toBeFalse();
  });

  describe('inspector', () => {
    function inspectorEl(): HTMLElement | null {
      return fixture.nativeElement.querySelector('.lb-inspector');
    }

    async function openPageWithQuestion(answerLabels: string[]): Promise<void> {
      await setup();
      await pickFolder([image('a.webp')]);
      selector().regions.set([{ name: 'q1' }, { name: 'q2' }]);
      selector().inspector = {
        index: 1,
        no: 2,
        name: 'q2',
        answers: answerLabels.map((label, i) => ({ id: i + 1, label, isCorrect: i === 1 })),
      };
      fixture.detectChanges();
    }

    it('showsSelectedQuestionAnswersAndCountMismatch', async () => {
      await openPageWithQuestion(['A', 'B', 'C']);
      const el = inspectorEl()!;
      expect(el.querySelector('.lb-badge')?.textContent).toContain('S2');
      expect(el.querySelectorAll('.lb-choice').length).toBe(3);
      expect(el.querySelector('.lb-count--warn')?.textContent).toContain('3/4');
      // Tuvalde seçilen şık (isCorrect) müfettişte seçili görünür.
      const selected = el.querySelectorAll('.lb-choice.is-selected');
      expect(selected.length).toBe(1);
      expect(selected[0].textContent).toContain('B');
    });

    it('actionsTargetSelectedQuestion', async () => {
      await openPageWithQuestion(['A', 'B', 'C', 'D']);
      expect(inspectorEl()!.querySelector('.lb-count--warn')).toBeNull();
      component.insSelectAnswer(3);
      component.insRemoveAnswer(2);
      component.insAlignAnswers();
      component.insDrawAnswer();
      component.insRemoveQuestion();
      component.insSelectNext();
      expect(selector().setCorrectAnswer).toHaveBeenCalledWith(1, 3, 1);
      expect(selector().removeAnswer).toHaveBeenCalledWith(1, 2);
      expect(selector().alignAnswers).toHaveBeenCalledWith(1, true);
      expect(selector().selectAnswerMode).toHaveBeenCalledWith(1);
      expect(selector().removeQuestion).toHaveBeenCalledWith(1);
      expect(selector().selectNextQuestion).toHaveBeenCalled();
    });

    it('whileSending_EditsIgnored', async () => {
      await openPageWithQuestion(['A', 'B', 'C', 'D']);
      selector().sendingToFix.set(true);
      component.insRemoveQuestion();
      component.insRemoveAnswer(0);
      expect(selector().removeQuestion).not.toHaveBeenCalled();
      expect(selector().removeAnswer).not.toHaveBeenCalled();
    });

    it('toggleHidesPanel', async () => {
      await openPageWithQuestion(['A']);
      component.toggleInspector();
      fixture.detectChanges();
      expect(inspectorEl()).toBeNull();
    });
  });

  describe('kaynak akışı (Manuel / Model / Claude)', () => {
    const model: LabelingAiModel = {
      id: 'claude-a',
      displayName: 'Claude A',
      inputUsdPerMTok: 3,
      outputUsdPerMTok: 15,
      isDefault: true,
    };
    const other: LabelingAiModel = { ...model, id: 'claude-b', displayName: 'Claude B', isDefault: false };

    function prediction(x: number) {
      return { success: true, predictions: [{ class_id: 0, x, y: 2, width: 3, height: 4, subpredictions: [] }] };
    }

    function manualResult(x: number): LabelingResult {
      return {
        source: 'Manual',
        modelName: '',
        predictions: prediction(x),
        baseSource: 'Model',
        baseModelName: null,
        inputTokens: null,
        outputTokens: null,
        estimatedCostUsd: null,
        createdByName: 'Eğitmen',
        createdAt: '2026-10-22T00:00:00Z',
        updatedAt: '2026-10-22T00:00:00Z',
      };
    }

    function aiResult(modelName: string): LabelingResult {
      return { ...manualResult(7), source: 'Ai', modelName, baseSource: null };
    }

    function pageWith(results: LabelingResult[]): LabelingPage {
      return { imageSha256: 'f'.repeat(64), width: 100, height: 100, results };
    }

    function aiResponse(modelName: string, origin: 'live' | 'cached' = 'live') {
      return { origin, imageSha256: 'f'.repeat(64), result: aiResult(modelName) };
    }

    /** Snackbar eylemi test tarafından tetiklenir. */
    function controllableSnackBar(): Subject<void> {
      const action = new Subject<void>();
      spyOn(TestBed.inject(MatSnackBar), 'open').and.returnValue({ onAction: () => action.asObservable() } as never);
      return action;
    }

    function toggle(value: string, groupValue: string | null): MatButtonToggleChange {
      return { value, source: { buttonToggleGroup: { value: groupValue } } } as unknown as MatButtonToggleChange;
    }

    it('onPageReady_ManualRecordExists_AppliesManualAndSkipsYoloAndClaude', async () => {
      await setup([], false, [model]);
      labelingApi.getPage.and.returnValue(of(pageWith([manualResult(1)])));
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-a')));

      await pickFolder([image('a.webp')]);

      expect(selector().applyExternalPrediction).toHaveBeenCalledWith(
        jasmine.any(Number),
        prediction(1),
        { kind: 'Manual', base: { source: 'Model', modelName: null } }
      );
      expect(selector().predict).not.toHaveBeenCalled();
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
    });

    it('onPageReady_NoRecord_PreferredModel_RunsYoloPredict', async () => {
      await setup([], false, [model]);

      await pickFolder([image('a.webp')]);

      expect(selector().predict).toHaveBeenCalledTimes(1);
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
      expect(selector().applyExternalPrediction).not.toHaveBeenCalled();
    });

    it('onPageReady_NoRecord_PreferredAi_RunsClaudeWithoutForce', async () => {
      localStorage.setItem('labeling.source', 'Ai');
      await setup([], false, [model]);
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-a')));

      await pickFolder([image('a.webp')]);

      expect(labelingApi.aiDetect).toHaveBeenCalledOnceWith(
        jasmine.objectContaining({ model: 'claude-a', force: false })
      );
      expect(selector().applyExternalPrediction).toHaveBeenCalledWith(
        jasmine.any(Number),
        prediction(7),
        { kind: 'Ai', modelName: 'claude-a' }
      );
      expect(selector().predict).not.toHaveBeenCalled();
    });

    it('onPageReady_PreferredAiFails_ShowsServerMessageAndFallsBackToYolo', async () => {
      localStorage.setItem('labeling.source', 'Ai');
      await setup([], false, [model]);
      labelingApi.aiDetect.and.returnValue(throwError(() => ({ error: { message: 'Claude kullanılamıyor' } })));
      const snackBar = spyOn(TestBed.inject(MatSnackBar), 'open');
      spyOn(console, 'error');

      await pickFolder([image('a.webp')]);

      expect(selector().failExternalDetection).toHaveBeenCalled();
      expect(selector().predict).toHaveBeenCalledTimes(1);
      expect(snackBar).toHaveBeenCalledWith('Claude kullanılamıyor', jasmine.any(String), jasmine.any(Object));
    });

    it('onSourceToggle_NoUnsavedEdits_AppliesImmediatelyAndRestoresGroupValue', async () => {
      await setup([], false, [model]);
      await pickFolder([image('a.webp')]);
      selector().detectionSource.set({ kind: 'Manual', base: null });
      selector().predict.calls.reset();

      const change = toggle('Model', 'Manual');
      component.onSourceToggle(change);

      expect(selector().predict).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('labeling.source')).toBe('Model');
      // Grup, gösterilen kaynağı yansıtır (seçim onaya bağlı değil).
      expect(change.source.buttonToggleGroup.value).toBe('Manual');
    });

    it('onSourceToggle_UnsavedEdits_WaitsForConfirmThenAppliesAndSavesPreference', async () => {
      await setup([], false, [model]);
      await pickFolder([image('a.webp')]);
      selector().detectionSource.set({ kind: 'Model' });
      selector().unsaved = true;
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-a')));
      const action = controllableSnackBar();

      component.onSourceToggle(toggle('Ai', 'Model'));

      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
      expect(localStorage.getItem('labeling.source')).toBeNull();

      action.next();
      await settle(fixture);

      expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('labeling.source')).toBe('Ai');
    });

    it('confirmDiscard_PageChangedBeforeConfirm_ActionIgnored', async () => {
      await setup([], false, [model]);
      await pickFolder([image('a.webp'), image('b.webp')]);
      selector().detectionSource.set({ kind: 'Model' });
      selector().unsaved = true;
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-a')));
      const action = controllableSnackBar();
      const dismiss = spyOn(TestBed.inject(MatSnackBar), 'dismiss');
      component.onSourceToggle(toggle('Ai', 'Model'));

      component.skip();
      await settle(fixture);
      expect(dismiss).toHaveBeenCalled();

      action.next();
      await settle(fixture);

      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
      expect(localStorage.getItem('labeling.source')).toBeNull();
    });

    it('setAiModel_UnsavedEdits_RevertsSelectAndAppliesOnConfirm', async () => {
      await setup([], false, [model, other]);
      await pickFolder([image('a.webp')]);
      selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a' });
      selector().unsaved = true;
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-b')));
      const action = controllableSnackBar();
      const event = { value: 'claude-b', source: { value: 'claude-a' } } as unknown as MatSelectChange;

      component.setAiModel(event);

      expect(event.source.value).toBe('claude-a');
      expect(component.aiModelId()).toBe('claude-a');
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();

      action.next();
      await settle(fixture);

      expect(component.aiModelId()).toBe('claude-b');
      expect(labelingApi.aiDetect).toHaveBeenCalledOnceWith(jasmine.objectContaining({ model: 'claude-b', force: false }));
      expect(localStorage.getItem('labeling.aiModel')).toBe('claude-b');
    });

    it('setAiModel_NoUnsavedEdits_ReRunsClaudeWhenClaudeShown', async () => {
      await setup([], false, [model, other]);
      await pickFolder([image('a.webp')]);
      selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a' });
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-b')));

      component.setAiModel({ value: 'claude-b', source: { value: 'claude-a' } } as unknown as MatSelectChange);
      await settle(fixture);

      expect(component.aiModelId()).toBe('claude-b');
      expect(labelingApi.aiDetect).toHaveBeenCalledOnceWith(jasmine.objectContaining({ model: 'claude-b', force: false }));
    });

    it('askClaudeAgain_Confirmed_RunsForcedClaudeCall', async () => {
      await setup([], false, [model]);
      await pickFolder([image('a.webp')]);
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-a')));
      const action = controllableSnackBar();

      component.askClaudeAgain();
      expect(labelingApi.aiDetect).not.toHaveBeenCalled();

      action.next();
      await settle(fixture);

      expect(labelingApi.aiDetect).toHaveBeenCalledOnceWith(jasmine.objectContaining({ model: 'claude-a', force: true }));
    });

    it('askClaudeAgain_PageChangedBeforeConfirm_NoPaidCall', async () => {
      await setup([], false, [model]);
      await pickFolder([image('a.webp'), image('b.webp')]);
      labelingApi.aiDetect.and.returnValue(of(aiResponse('claude-a')));
      const action = controllableSnackBar();

      component.askClaudeAgain();
      component.skip();
      await settle(fixture);
      action.next();
      await settle(fixture);

      expect(labelingApi.aiDetect).not.toHaveBeenCalled();
    });

    it('staleClaudeResponse_AfterPageChange_IsIgnored', async () => {
      await setup([], false, [model]);
      await pickFolder([image('a.webp'), image('b.webp')]);
      const late = new Subject<ReturnType<typeof aiResponse>>();
      labelingApi.aiDetect.and.returnValue(late.asObservable());
      const action = controllableSnackBar();
      component.askClaudeAgain();
      action.next();
      await settle(fixture);
      expect(component.aiBusy()).toBeTrue();

      component.skip();
      await settle(fixture);
      late.next(aiResponse('claude-a'));
      late.complete();
      await settle(fixture);

      expect(selector().applyExternalPrediction).not.toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        jasmine.objectContaining({ kind: 'Ai' })
      );
      expect(component.aiBusy()).toBeFalse();
    });

    it('onPageReady_LateRecordAfterPageChange_IsIgnored', async () => {
      await setup([], false, [model]);
      const lateA = new Subject<LabelingPage>();
      const pageB = pageWith([manualResult(2)]);
      labelingApi.getPage.and.returnValues(lateA.asObservable(), of(pageB));

      await pickFolder([image('a.webp'), image('b.webp')]);
      component.skip();
      await settle(fixture);
      // A'nın kaydı B açıkken geç gelir: uygulanmamalı.
      lateA.next(pageWith([manualResult(1)]));
      lateA.complete();
      await settle(fixture);

      expect(selector().applyExternalPrediction).toHaveBeenCalledTimes(1);
      expect(selector().applyExternalPrediction).toHaveBeenCalledWith(
        jasmine.any(Number),
        prediction(2),
        jasmine.anything()
      );
    });
  });
});
