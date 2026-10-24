import type { MockedObject } from "vitest";
import { Component, EventEmitter, Input, Output, forwardRef, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';
import { MatButtonToggleChange } from '@angular/material/button-toggle';
import { MatSelectChange } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';

import { LabelingComponent } from './labeling.component';
import { pageIdOf } from './labeling-pool';
import { LabelingPromptDialogComponent } from './labeling-prompt-dialog/labeling-prompt-dialog.component';
import { ImageSelectorComponent, TrainedPage } from '../image-selector/image-selector.component';
import { QuestionDetectorService } from '../../services/question-detector.service';
import { LabelingService } from '../../services/labeling.service';
import { DetectionSource, LabelingAiModel, LabelingPage, LabelingPromptState, LabelingResult } from '../../models/labeling';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import labelingTr from '../../../../public/i18n/labeling/tr.json';

interface StubAnswer {
    label: string;
    isCorrect: boolean;
    x: number;
    y: number;
    width: number;
    height: number;
}
interface StubRegion {
    name: string;
    x?: number;
    y?: number;
    width?: number;
    height?: number;
    answers?: StubAnswer[];
}

/** Gerçek tuval yerine; `@ViewChild(ImageSelectorComponent)` bu stub'ı DI token'ı üzerinden bulur. */
@Component({
    selector: 'app-image-selector',
    standalone: true,
    template: '',
    changeDetection: ChangeDetectionStrategy.Eager,
    providers: [{ provide: ImageSelectorComponent, useExisting: forwardRef(() => ImageSelectorStub) }],
})
class ImageSelectorStub {
    @Input()
    navigationLocked = false;
    @Input()
    advanceOnTrain = true;
    @Input()
    externalDetection = false;
    @Output()
    trained = new EventEmitter<TrainedPage>();
    @Output()
    pageReady = new EventEmitter<string>();
    @Output()
    manualSaved = new EventEmitter<string>();
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
    readonly regions = signal<StubRegion[]>([]);
    /** Soru tipi (ad → tip); tanımsız = mcq. */
    interactionTypes: Record<string, string> = {};
    getInteractionType = vi.fn().mockName('getInteractionType').mockImplementation((name: string) => this.interactionTypes[name] ?? 'mcq');
    ensureQuestionsValid = vi.fn().mockName('ensureQuestionsValid').mockReturnValue(true);
    loaded: File[] = [];
    /** Gerçek bileşen gibi: yükleme anında eski sayfa durumu silinir, görsel okunmuş sayılır. */
    loadFile = vi.fn().mockName('loadFile').mockImplementation((file: File) => {
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
    beginExternalDetection = vi.fn().mockName('beginExternalDetection').mockImplementation(() => ++this.seq);
    applyExternalPrediction = vi.fn().mockName('applyExternalPrediction');
    failExternalDetection = vi.fn().mockName('failExternalDetection');
    hasUnsavedEdits = vi.fn().mockName('hasUnsavedEdits').mockImplementation(() => this.unsaved);
    alignAllAnswers = vi.fn().mockName('alignAllAnswers');
    sendToFix = vi.fn().mockName('sendToFix');
    predict = vi.fn().mockName('predict');
    recomputeWarnings = vi.fn().mockName('recomputeWarnings');
    inspector: unknown = null;
    getActiveInspector = vi.fn().mockName('getActiveInspector').mockImplementation(() => this.inspector);
    selectNextQuestion = vi.fn().mockName('selectNextQuestion');
    selectPreviousQuestion = vi.fn().mockName('selectPreviousQuestion');
    removeAnswer = vi.fn().mockName('removeAnswer');
    setCorrectAnswer = vi.fn().mockName('setCorrectAnswer');
    alignAnswers = vi.fn().mockName('alignAnswers');
    selectAnswerMode = vi.fn().mockName('selectAnswerMode');
    removeQuestion = vi.fn().mockName('removeQuestion');
    lockSelectionMode = vi.fn().mockName('lockSelectionMode').mockImplementation((mode: string | null) => {
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
    const hash = (file: File) => pendingNames.includes(file.name)
        ? new Promise<string>((resolve) => resolvers.set(file.name, resolve))
        : Promise.resolve(`id-${file.name}`);
    return { hash, release: (name: string) => resolvers.get(name)?.(`id-${name}`) };
}

/**
 * Sürmekte olan gerçek tarayıcı async'leri (dosya okuma + SHA-256). Sayfa açılışı `void` ile başlar ve `predict`'ten
 * önce iki kez hash'lenir; tam koşunun yükü altında bunlar sabit sayıda turda bitmeyebilir (flaky predict testi).
 */
const pendingNative = new Set<Promise<unknown>>();

function trackNative<T>(promise: Promise<T>): Promise<T> {
    pendingNative.add(promise);
    const done = () => pendingNative.delete(promise);
    promise.then(done, done);
    return promise;
}

/** Tüm bekleyen mikro görevler + gerçek async (dosya okuma, crypto.subtle) tamamlansın. */
async function settle(fixture: ComponentFixture<LabelingComponent>): Promise<void> {
    for (let round = 0; round < 10; round++) {
        await Promise.allSettled([...pendingNative]);
        for (let i = 0; i < 20; i++) {
            await new Promise((resolve) => setTimeout(resolve, 0));
        }
        if (pendingNative.size === 0) {
            break;
        }
    }
    fixture.detectChanges();
}

describe('LabelingComponent', () => {
    let fixture: ComponentFixture<LabelingComponent>;
    let component: LabelingComponent;
    let detector: MockedObject<QuestionDetectorService>;
    let labelingApi: MockedObject<LabelingService>;

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
        detector = {
            getLabeledPages: vi.fn().mockName("QuestionDetectorService.getLabeledPages")
        } as unknown as MockedObject<QuestionDetectorService>;
        detector.getLabeledPages.mockReturnValue(fail ? throwError(() => new Error('down')) : of({ pageIds: labeledIds }));
        labelingApi = {
            getAiModels: vi.fn().mockName("LabelingService.getAiModels"),
            getPage: vi.fn().mockName("LabelingService.getPage"),
            aiDetect: vi.fn().mockName("LabelingService.aiDetect"),
            saveManual: vi.fn().mockName("LabelingService.saveManual"),
            saveToBank: vi.fn().mockName("LabelingService.saveToBank")
        } as unknown as MockedObject<LabelingService>;
        labelingApi.getAiModels.mockReturnValue(of(aiModels));
        // Varsayılan: sayfanın kaydı yok (404 → null).
        labelingApi.getPage.mockReturnValue(throwError(() => ({ status: 404 })));
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
        pendingNative.clear();
        const digest = crypto.subtle.digest.bind(crypto.subtle);
        vi.spyOn(crypto.subtle, 'digest').mockImplementation((algorithm: AlgorithmIdentifier, data: BufferSource) =>
            trackNative(digest(algorithm, data)));
        const arrayBuffer = Blob.prototype.arrayBuffer;
        vi.spyOn(Blob.prototype, 'arrayBuffer').mockImplementation(function (this: Blob) {
            return trackNative(arrayBuffer.call(this));
        });
        localStorage.removeItem('labeling.answerCount');
        localStorage.removeItem('labeling.source');
        localStorage.removeItem('labeling.aiModel');
    });

    afterEach(() => {
        vi.mocked(crypto.subtle.digest).mockRestore();
        vi.mocked(Blob.prototype.arrayBuffer).mockRestore();
    });

    it('init_LoadsLabeledPagesAndPassesTrainingInputsToSelector', async () => {
        await setup();

        expect(detector.getLabeledPages).toHaveBeenCalled();
        const stub = selector();
        expect(stub.navigationLocked).toBe(true);
        expect(stub.advanceOnTrain).toBe(false);
    });

    it('folderPick_KeepsOnlyImagesAndLoadsFirstPageByName', async () => {
        await setup();
        const json = new File(['{}'], 'question-coordinates.json', { type: 'application/json' });

        await pickFolder([image('p2.webp'), json, image('p1.webp')]);

        expect(component.totalImages()).toBe(2);
        expect(selector().loaded.length).toBe(1);
        expect(component.currentPath()).toBe('kitap/p1.webp');
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

        expect(component.finished()).toBe(true);
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
        expect(component.finished()).toBe(true);
    });

    it('train_SendsCompletePageThenOnTrainedCountsAndAdvances', async () => {
        await setup();
        await pickFolder([image('a.webp'), image('b.webp')]);
        const firstId = component.current()!.pageId;

        component.train();
        expect(selector().sendToFix).toHaveBeenCalledTimes(1);
        expect(selector().sendToFix).toHaveBeenCalledWith(true);

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
        expect(component.labeledError()).toBe(true);
        expect(fixture.nativeElement.textContent).toContain(labelingTr.retry);

        await pickFolder([image('a.webp')]);
        expect(selector().loadFile).not.toHaveBeenCalled();

        detector.getLabeledPages.mockReturnValue(of({ pageIds: [] }));
        await component.retryLabeledPages();
        await settle(fixture);

        expect(component.labeledError()).toBe(false);
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
        await pickFolder([image('a1.webp'), image('a2.webp')]); // a1 açık, a2'nin hash'i bekliyor

        await pickFolder([image('b1.webp')]);
        control.release('a2.webp');
        await settle(fixture);

        expect(component.current()?.file.name).toBe('b1.webp');
        component.skip();
        await settle(fixture);
        expect(selector().loaded.map((f) => f.name)).toEqual(['a1.webp', 'b1.webp']);
        expect(component.finished()).toBe(true);
    });

    it('remaining_DoesNotDropWhilePrefetchHashInFlight', async () => {
        await setup();
        const control = controllableHash(['a2.webp', 'a3.webp']);
        component.hashFile = control.hash;

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
        expect(component.finished()).toBe(true);
    });

    it('pageLoadFailure_CanTrainFalseAndHintShown', async () => {
        await setup();
        await pickFolder([image('a.webp')]);
        expect(component.canTrain()).toBe(true);

        selector().pageLoadFailed.set(true);
        selector().imageData.set(null);
        fixture.detectChanges();

        expect(component.canTrain()).toBe(false);
        component.train();
        expect(selector().sendToFix).not.toHaveBeenCalled();
        expect(fixture.nativeElement.textContent).toContain(labelingTr.pageLoadFailedHint);
        expect(component.canSkip()).toBe(true);
    });

    it('predictFailure_EmptyRegions_CanTrainFalseUntilBoxesDrawn', async () => {
        await setup();
        await pickFolder([image('a.webp')]);

        selector().predictFailed.set(true);
        fixture.detectChanges();

        expect(component.canTrain()).toBe(false);
        expect(fixture.nativeElement.textContent).toContain(labelingTr.predictFailedHint);

        selector().regions.set([{ name: 'Soru 1' }]);
        fixture.detectChanges();
        expect(component.canTrain()).toBe(true);
    });

    it('onTrained_ServerPageIdMismatch_NotCountedNoAdvanceShowsError', async () => {
        await setup();
        const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
        await pickFolder([image('a.webp'), image('b.webp')]);
        const candidate = component.current()!;

        component.train();
        selector().trained.emit({ pageId: 'ffffffffffffffff', emptyPage: true, added: 0 });
        await settle(fixture);

        expect(component.trainedThisSession()).toBe(0);
        expect(component.current()).toBe(candidate);
        expect(selector().loaded.length).toBe(1);
        expect(snackBar).toHaveBeenCalledWith(labelingTr.pageIdMismatch, labelingTr.ok, expect.any(Object));
    });

    it('align_OneShotAlignmentAndAutoAlignStaysOff', async () => {
        await setup();
        await pickFolder([image('a.webp')]);
        selector().autoAlign.set(true);
        component.setAnswerCount(3); // effect yeniden çalışır
        fixture.detectChanges();
        // Sayfa açılışındaki varsayılan YOLO tespiti (predict) sayılmaz; yalnız hizalama sırasında çağrı aranır.
        selector().predict.mockClear();

        component.align();

        expect(selector().alignAllAnswers).toHaveBeenCalledTimes(1);
        expect(selector().predict).not.toHaveBeenCalled();
        expect(selector().autoAlign()).toBe(false);
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
                promptVersion: null,
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
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);
            return action;
        }

        function toggle(value: string, groupValue: string | null): MatButtonToggleChange {
            return { value, source: { buttonToggleGroup: { value: groupValue } } } as unknown as MatButtonToggleChange;
        }

        it('onPageReady_ManualRecordExists_AppliesManualAndSkipsYoloAndClaude', async () => {
            await setup([], false, [model]);
            labelingApi.getPage.mockReturnValue(of(pageWith([manualResult(1)])));
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));

            await pickFolder([image('a.webp')]);

            expect(selector().applyExternalPrediction).toHaveBeenCalledWith(expect.any(Number), prediction(1), { kind: 'Manual', base: { source: 'Model', modelName: null, promptVersion: null } });
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
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));

            await pickFolder([image('a.webp')]);

            expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);

            expect(labelingApi.aiDetect).toHaveBeenCalledWith(expect.objectContaining({ model: 'claude-a', force: false }));
            expect(selector().applyExternalPrediction).toHaveBeenCalledWith(expect.any(Number), prediction(7), { kind: 'Ai', modelName: 'claude-a', promptVersion: null });
            expect(selector().predict).not.toHaveBeenCalled();
        });

        it('onPageReady_PreferredAiFails_ShowsServerMessageAndFallsBackToYolo', async () => {
            localStorage.setItem('labeling.source', 'Ai');
            await setup([], false, [model]);
            labelingApi.aiDetect.mockReturnValue(throwError(() => ({ error: { message: 'Claude kullanılamıyor' } })));
            const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
            vi.spyOn(console, 'error').mockReturnValue(undefined);

            await pickFolder([image('a.webp')]);

            expect(selector().failExternalDetection).toHaveBeenCalled();
            expect(selector().predict).toHaveBeenCalledTimes(1);
            expect(snackBar).toHaveBeenCalledWith('Claude kullanılamıyor', expect.any(String), expect.any(Object));
        });

        it('onSourceToggle_NoUnsavedEdits_AppliesImmediatelyAndRestoresGroupValue', async () => {
            await setup([], false, [model]);
            await pickFolder([image('a.webp')]);
            selector().detectionSource.set({ kind: 'Manual', base: null });
            selector().predict.mockClear();

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
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));
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
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));
            const action = controllableSnackBar();
            const dismiss = vi.spyOn(TestBed.inject(MatSnackBar), 'dismiss').mockReturnValue(undefined);
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
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-b')));
            const action = controllableSnackBar();
            const event = { value: 'claude-b', source: { value: 'claude-a' } } as unknown as MatSelectChange;

            component.setAiModel(event);

            expect(event.source.value).toBe('claude-a');
            expect(component.aiModelId()).toBe('claude-a');
            expect(labelingApi.aiDetect).not.toHaveBeenCalled();

            action.next();
            await settle(fixture);

            expect(component.aiModelId()).toBe('claude-b');
            expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);
            expect(labelingApi.aiDetect).toHaveBeenCalledWith(expect.objectContaining({ model: 'claude-b', force: false }));
            expect(localStorage.getItem('labeling.aiModel')).toBe('claude-b');
        });

        it('setAiModel_NoUnsavedEdits_ReRunsClaudeWhenClaudeShown', async () => {
            await setup([], false, [model, other]);
            await pickFolder([image('a.webp')]);
            selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a' });
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-b')));

            component.setAiModel({ value: 'claude-b', source: { value: 'claude-a' } } as unknown as MatSelectChange);
            await settle(fixture);

            expect(component.aiModelId()).toBe('claude-b');
            expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);
            expect(labelingApi.aiDetect).toHaveBeenCalledWith(expect.objectContaining({ model: 'claude-b', force: false }));
        });

        it('askClaudeAgain_Confirmed_RunsForcedClaudeCall', async () => {
            await setup([], false, [model]);
            await pickFolder([image('a.webp')]);
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));
            const action = controllableSnackBar();

            component.askClaudeAgain();
            expect(labelingApi.aiDetect).not.toHaveBeenCalled();

            action.next();
            await settle(fixture);

            expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);

            expect(labelingApi.aiDetect).toHaveBeenCalledWith(expect.objectContaining({ model: 'claude-a', force: true }));
        });

        it('askClaudeAgain_PageChangedBeforeConfirm_NoPaidCall', async () => {
            await setup([], false, [model]);
            await pickFolder([image('a.webp'), image('b.webp')]);
            labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));
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
            labelingApi.aiDetect.mockReturnValue(late.asObservable());
            const action = controllableSnackBar();
            component.askClaudeAgain();
            action.next();
            await settle(fixture);
            expect(component.aiBusy()).toBe(true);

            component.skip();
            await settle(fixture);
            late.next(aiResponse('claude-a'));
            late.complete();
            await settle(fixture);

            expect(selector().applyExternalPrediction).not.toHaveBeenCalledWith(expect.anything(), expect.anything(), expect.objectContaining({ kind: 'Ai' }));
            expect(component.aiBusy()).toBe(false);
        });

        it('onPageReady_LateRecordAfterPageChange_IsIgnored', async () => {
            await setup([], false, [model]);
            const lateA = new Subject<LabelingPage>();
            const pageB = pageWith([manualResult(2)]);
            labelingApi.getPage.mockReturnValueOnce(lateA.asObservable()).mockReturnValueOnce(of(pageB));

            await pickFolder([image('a.webp'), image('b.webp')]);
            component.skip();
            await settle(fixture);
            // A'nın kaydı B açıkken geç gelir: uygulanmamalı.
            lateA.next(pageWith([manualResult(1)]));
            lateA.complete();
            await settle(fixture);

            expect(selector().applyExternalPrediction).toHaveBeenCalledTimes(1);
            expect(selector().applyExternalPrediction).toHaveBeenCalledWith(expect.any(Number), prediction(2), expect.anything());
        });

        it('claudeResult_CarriesPromptVersionIntoDetectionSource', async () => {
            await setup([], false, [model]);
            await pickFolder([image('a.webp')]);
            labelingApi.aiDetect.mockReturnValue(of({ ...aiResponse('claude-a'), result: { ...aiResult('claude-a'), promptVersion: 3 } }));

            // Özel yöntem: onay diyaloğu olmadan doğrudan Claude çağrısı (sayfa + model zaten seçili).
            await component['runClaude'](false, false);
            await settle(fixture);

            expect(selector().applyExternalPrediction).toHaveBeenCalledWith(expect.any(Number), prediction(7), expect.objectContaining({ kind: 'Ai', modelName: 'claude-a', promptVersion: 3 }));
        });

        describe('Claude prompt diyaloğu (openPromptDialog)', () => {
            const promptState: LabelingPromptState = {
                active: {
                    version: 2,
                    questionInstruction: 'q {width}x{height}',
                    optionInstruction: 'o {width}x{height}',
                    questionPadding: 6,
                    optionPadding: 4,
                    expandQuestionToOptions: true,
                    createdByName: 'Eğitmen',
                    createdAt: '2026-10-22T00:00:00Z',
                },
                defaults: {
                    version: 0,
                    questionInstruction: 'dq',
                    optionInstruction: 'do',
                    questionPadding: 6,
                    optionPadding: 4,
                    expandQuestionToOptions: true,
                    createdByName: null,
                    createdAt: null,
                },
                history: [],
            };

            /** MatDialog.open → afterClosed(result); dönen değer çağrı sayısı için spy olarak tutulur. */
            function dialogClosesWith(result: LabelingPromptState | undefined) {
                const dialog = TestBed.inject(MatDialog);
                return vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of(result) } as never);
            }

            it('openPromptDialog_OpensDialogWithoutAutoFocus', async () => {
                await setup([], false, [model]);
                const open = dialogClosesWith(undefined);

                component.openPromptDialog();

                expect(open).toHaveBeenCalledTimes(1);

                expect(open).toHaveBeenCalledWith(LabelingPromptDialogComponent, expect.objectContaining({ autoFocus: false }));
            });

            it('openPromptDialog_Cancelled_NoSnackbarAndNoClaudeCall', async () => {
                await setup([], false, [model]);
                await pickFolder([image('a.webp')]);
                selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a' });
                dialogClosesWith(undefined);
                const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

                component.openPromptDialog();
                await settle(fixture);

                expect(snackBar).not.toHaveBeenCalled();
                expect(labelingApi.aiDetect).not.toHaveBeenCalled();
            });

            it('openPromptDialog_SavedWithoutClaudeResult_ShowsSnackbarWithoutAction', async () => {
                await setup([], false, [model]);
                await pickFolder([image('a.webp')]);
                selector().detectionSource.set({ kind: 'Manual' });
                dialogClosesWith(promptState);
                const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({
                    onAction: () => new Subject<void>().asObservable(),
                } as never);

                component.openPromptDialog();
                await settle(fixture);

                expect(snackBar).toHaveBeenCalledTimes(1);
                expect(labelingApi.aiDetect).not.toHaveBeenCalled();
            });

            it('openPromptDialog_SavedWhileClaudeShown_ActionReAsksClaudeWithoutForce', async () => {
                await setup([], false, [model]);
                await pickFolder([image('a.webp')]);
                selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a', promptVersion: 1 });
                labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));
                dialogClosesWith(promptState);
                const action = controllableSnackBar();

                component.openPromptDialog();
                expect(labelingApi.aiDetect).not.toHaveBeenCalled();

                action.next();
                await settle(fixture);

                // Yeni sürüm ile yeniden soruş: önbellek yok sayılmaz (force=false), yalnız mevcut sayfa için çağrılır.
                expect(labelingApi.aiDetect).toHaveBeenCalledTimes(1);

                // Yeni sürüm ile yeniden soruş: önbellek yok sayılmaz (force=false), yalnız mevcut sayfa için çağrılır.
                expect(labelingApi.aiDetect).toHaveBeenCalledWith(expect.objectContaining({ model: 'claude-a', force: false }));
            });

            it('openPromptDialog_SavedWhileClaudeShown_PageChangedBeforeAction_NoClaudeCall', async () => {
                await setup([], false, [model]);
                await pickFolder([image('a.webp'), image('b.webp')]);
                selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a' });
                labelingApi.aiDetect.mockReturnValue(of(aiResponse('claude-a')));
                dialogClosesWith(promptState);
                const action = controllableSnackBar();

                component.openPromptDialog();
                component.skip();
                await settle(fixture);
                action.next();
                await settle(fixture);

                expect(labelingApi.aiDetect).not.toHaveBeenCalled();
            });
        });
    });
    describe('soru bankası (#470)', () => {
        const savedBank = { savedAt: '2026-10-22T09:00:00Z', savedByName: 'Eğitmen', questionCount: 1 };
        const pageWithBank: LabelingPage = {
            imageSha256: 'abc', width: 320, height: 200, results: [],
            bank: { savedAt: '2026-10-22T08:00:00Z', savedByName: 'Önceki', questionCount: 2 },
        };

        function region(name: string, correct: number, x = 10.4, y = 9.6): StubRegion {
            return {
                name, x, y, width: 100, height: 80,
                answers: [0, 1, 2].map((i) => ({
                    label: String.fromCharCode(65 + i), isCorrect: i === correct, x: 12 + i * 25, y: 12, width: 20, height: 20,
                })),
            };
        }

        /** Sayfayı açar (isteğe bağlı mevcut bank kaydıyla) ve tuvalde verilen soruları hazır bırakır. */
        async function openPageWith(regions: StubRegion[], page: LabelingPage | null = null): Promise<void> {
            await setup();
            if (page) {
                labelingApi.getPage.mockReturnValue(of(page));
            }
            await pickFolder([image('p1.webp')]);
            selector().regions.set(regions);
            fixture.detectChanges();
        }

        /** Snackbar'daki "Yine de kaydet" eylemi; `next()` ile tetiklenir. */
        function snackbarWithAction(): Subject<void> {
            const action = new Subject<void>();
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);
            return action;
        }

        it('saveToBank_McqRegions_PostsRoundedBoxesWithCorrectIndexAndForceFalse', async () => {
            await openPageWith([region('S1', 1)]);
            labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));

            component.saveToBank();

            expect(labelingApi.saveToBank).toHaveBeenCalledTimes(1);
            const request = vi.mocked(labelingApi.saveToBank).mock.lastCall![0];
            expect(request.force).toBe(false);
            expect(request.imageBase64).toMatch(/^data:image\/webp;base64,/);
            expect(request.questions).toEqual([
                {
                    x: 10, y: 10, width: 100, height: 80, correctOptionIndex: 1,
                    options: [
                        { x: 12, y: 12, width: 20, height: 20 },
                        { x: 37, y: 12, width: 20, height: 20 },
                        { x: 62, y: 12, width: 20, height: 20 },
                    ],
                },
            ]);
            expect(selector().ensureQuestionsValid).toHaveBeenCalledWith({ requireCorrectAnswer: true });
            expect(component.bankRecord()?.questionCount).toBe(1);
            expect(component.bankSaving()).toBe(false);
        });

        it('saveToBank_Success_SendsPageToTraining', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(selector().sendToFix).toHaveBeenCalledTimes(1);

            expect(selector().sendToFix).toHaveBeenCalledWith(true);
            // Birleşik mesaj eğitim bitince gösterilir.
            expect(snack).not.toHaveBeenCalledWith(expect.stringMatching(/./), labelingTr.ok, expect.any(Object));
        });

        it('saveToBank_ThenTrained_ShowsCombinedMessageAndAdvances', async () => {
            await setup();
            await pickFolder([image('p1.webp'), image('p2.webp')]);
            const first = component.current()!;
            selector().regions.set([region('S1', 0)]);
            fixture.detectChanges();
            labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: { ...savedBank, questionCount: 2 } }));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();
            selector().trained.emit({ pageId: first.pageId, emptyPage: false, added: 2 });
            await settle(fixture);

            expect(snack).toHaveBeenCalledWith(labelingTr.bank.savedAndTrained.replace('{{count}}', '2'), labelingTr.ok, expect.any(Object));
            expect(component.trainedThisSession()).toBe(1);
            expect(component.current()).not.toBe(first);
            expect(selector().loaded.length).toBe(2);
        });

        it('train_WithoutBankSave_DoesNotShowBankMessage', async () => {
            await setup();
            await pickFolder([image('p1.webp'), image('p2.webp')]);
            const first = component.current()!;
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.train();
            selector().trained.emit({ pageId: first.pageId, emptyPage: false, added: 1 });
            await settle(fixture);

            expect(snack).not.toHaveBeenCalledWith(expect.stringMatching(/bank|banka/i), expect.anything(), expect.anything());
        });

        it('saveToBank_Failure_DoesNotSendToTraining', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 500 })));
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(selector().sendToFix).not.toHaveBeenCalled();
        });

        it('saveToBank_InProgress_DisablesTrain', async () => {
            await openPageWith([region('S1', 0)]);
            const pending = new Subject<never>();
            labelingApi.saveToBank.mockReturnValue(pending.asObservable());

            component.saveToBank();

            expect(component.bankSaving()).toBe(true);
            expect(component.canTrain()).toBe(false);
            component.train();
            expect(selector().sendToFix).not.toHaveBeenCalled();
        });

        it('saveToBank_DragDropOnly_ShowsNoMcqAndSendsNothing', async () => {
            await openPageWith([region('S1', 0)]);
            selector().interactionTypes['S1'] = 'dragDropLabeling';
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(labelingApi.saveToBank).not.toHaveBeenCalled();
            expect(snack).toHaveBeenCalledWith(labelingTr.bank.noMcq, labelingTr.ok, expect.any(Object));
        });

        it('saveToBank_InvalidQuestions_SendsNothing', async () => {
            await openPageWith([region('S1', 0)]);
            selector().ensureQuestionsValid.mockReturnValue(false);

            component.saveToBank();

            expect(selector().ensureQuestionsValid).toHaveBeenCalledWith({ requireCorrectAnswer: true });
            expect(labelingApi.saveToBank).not.toHaveBeenCalled();
        });

        // Issue #545 (Material 20): MatButton ikonu ayrı slota alıyor; @else kökünde ikon + metin birlikte durunca
        // ikon slota düşmüyordu (NG8011). İkon tek başına @else'te, etiket dışarıda.
        function bankButton(): HTMLButtonElement {
            return fixture.nativeElement.querySelector('button.lb-bank') as HTMLButtonElement;
        }

        it('bankButton_Idle_ProjectsIconIntoMaterialIconSlot', async () => {
            await openPageWith([region('S1', 0)]);

            const button = bankButton();
            expect(button.querySelector('mat-icon')?.textContent?.trim()).toBe('library_add');
            expect(button.querySelector('.mdc-button__label mat-icon')).toBeNull();
            expect(button.querySelector('.mdc-button__label')?.textContent?.trim()).toBe(labelingTr.bank.save);
            expect(button.querySelector('mat-spinner')).toBeNull();
        });

        it('bankButton_Saving_ShowsSpinnerAndSavingLabel', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(new Subject<never>().asObservable());

            component.saveToBank();
            fixture.detectChanges();

            const button = bankButton();
            expect(button.querySelector('mat-spinner')).not.toBeNull();
            expect(button.querySelector('mat-icon')).toBeNull();
            expect(button.textContent).toContain(labelingTr.bank.saving);
            expect(button.getAttribute('aria-busy')).toBe('true');
        });

        it('canSaveToBank_NoQuestionsOnCanvas_IsFalseAndSendsNothing', async () => {
            await openPageWith([]);

            expect(component.canSaveToBank()).toBe(false);
            component.saveToBank();
            expect(labelingApi.saveToBank).not.toHaveBeenCalled();
        });

        it('saveToBank_ExistingRecord_AsksConfirmThenSendsForceTrue', async () => {
            await openPageWith([region('S1', 0)], pageWithBank);
            expect(component.bankRecord()?.questionCount).toBe(2);
            const action = snackbarWithAction();
            labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));

            component.saveToBank();
            expect(labelingApi.saveToBank).not.toHaveBeenCalled();

            action.next();
            expect(labelingApi.saveToBank).toHaveBeenCalledTimes(1);
            expect(vi.mocked(labelingApi.saveToBank).mock.lastCall![0].force).toBe(true);
            expect(selector().sendToFix).toHaveBeenCalledTimes(1);
            expect(selector().sendToFix).toHaveBeenCalledWith(true);
        });

        it('saveToBank_409WithExisting_ShowsRecordAndAsksAgainBeforeForce', async () => {
            await openPageWith([region('S1', 0)]);
            const existing = { savedAt: '2026-10-22T07:00:00Z', savedByName: 'Diğer', questionCount: 3 };
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 409, error: { message: 'x', errorCode: 'LabelingPageAlreadyInBank', existing } })));
            const action = snackbarWithAction();

            component.saveToBank();

            expect(labelingApi.saveToBank).toHaveBeenCalledTimes(1);
            expect(component.bankRecord()?.questionCount).toBe(3);
            expect(component.bankSaving()).toBe(false);

            labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));
            action.next();
            expect(labelingApi.saveToBank).toHaveBeenCalledTimes(2);
            expect(vi.mocked(labelingApi.saveToBank).mock.lastCall![0].force).toBe(true);
        });

        it('saveToBank_Forbidden_ShowsForbiddenMessageAndKeepsNoRecord', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 403, error: {} })));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(snack).toHaveBeenCalledWith(labelingTr.bank.forbidden, labelingTr.ok, expect.any(Object));
            expect(component.bankRecord()).toBeNull();
            expect(component.bankSaving()).toBe(false);
        });

        it('saveToBank_ServerErrorWithMessage_ShowsServerMessage', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 500, error: { message: 'sunucu metni' } })));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(snack).toHaveBeenCalledWith('sunucu metni', labelingTr.ok, expect.any(Object));
            expect(component.bankRecord()).toBeNull();
        });

        it('saveToBank_ServerErrorWithoutBody_ShowsGenericFailure', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 500 })));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(snack).toHaveBeenCalledWith(labelingTr.bank.failed, labelingTr.ok, expect.any(Object));
        });
    });

});
