import type { Mock, MockedObject } from "vitest";
import { Component, EventEmitter, Input, Output, forwardRef, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Observable, Subject, of, throwError } from 'rxjs';
import { MatButtonToggleChange } from '@angular/material/button-toggle';
import { MatSelect, MatSelectChange } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';

import { LabelingComponent, normalizeLabel } from './labeling.component';
import { pageIdOf } from './labeling-pool';
import { LabelingPromptDialogComponent } from './labeling-prompt-dialog/labeling-prompt-dialog.component';
import { ImageSelectorComponent, TrainedPage } from '../image-selector/image-selector.component';
import { QuestionDetectorService } from '../../services/question-detector.service';
import { LabelingService } from '../../services/labeling.service';
import { GradesService } from '../../services/grades.service';
import { AuthService } from '../../services/auth.service';
import { LabelingBookService } from '../../services/labeling-book.service';
import { LabelingBook, LabelingBookPageInfo } from '../../models/labeling-book';
import { LabelingCropDuplicateDialogComponent } from './labeling-crop-duplicate-dialog/labeling-crop-duplicate-dialog.component';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { Grade } from '../../models/student';
import {
    DetectionSource,
    LabelingActivitySolutionResponse,
    LabelingActivityStructureResponse,
    LabelingActivityStructureResult,
    LabelingAnswerKeyEntry,
    LabelingAnswerKeyReadResponse,
    LabelingAiModel,
    LabelingPage,
    LabelingPromptState,
    LabelingResult,
} from '../../models/labeling';
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
    /** Issue #578: blok türü (ad → tür); tanımsız = mcq_question. */
    blockKinds: Record<string, string> = {};
    isMcqRegion = vi.fn().mockName('isMcqRegion').mockImplementation(
        (name: string) => (this.blockKinds[name] ?? 'mcq_question') === 'mcq_question' && (this.interactionTypes[name] ?? 'mcq') === 'mcq'
    );
    getBlockKind = vi.fn().mockName('getBlockKind').mockImplementation((name: string) => this.blockKinds[name] ?? 'mcq_question');
    setBlockKind = vi.fn().mockName('setBlockKind');
    setActivityComponent = vi.fn().mockName('setActivityComponent');
    /** Issue #579: yeni tespitte artar; taslaklar sıfırlanır. */
    readonly detectionRevision = signal(0);
    setActivityInteraction = vi.fn().mockName('setActivityInteraction');
    applyInteractionDraft = vi.fn().mockName('applyInteractionDraft').mockReturnValue('applied');
    /** Issue #580: çözüm uygulama, tuvaldeki çözüm / plan ve cevap anahtarı kutuları. */
    applyInteractionSolution = vi.fn().mockName('applyInteractionSolution').mockReturnValue('applied');
    /** Tuvaldeki çözüm (ad → çözüm); test elle değiştirir. */
    canvasSolutions: Record<string, Record<string, unknown> | null> = {};
    getInteractionSolution = vi.fn().mockName('getInteractionSolution').mockImplementation((name: string) => this.canvasSolutions[name] ?? null);
    /** Tuvaldeki plan (ad → plan); tanımsız ise ada göre sabit bir plan. */
    canvasPlans: Record<string, Record<string, unknown>> = {};
    getInteractionPlan = vi.fn().mockName('getInteractionPlan').mockImplementation(
        (name: string) => this.canvasPlans[name] ?? ({ version: 1, type: this.interactionTypes[name], plan: name }));
    setDraftSolution = vi.fn().mockName('setDraftSolution');
    /** Issue #580: tespitin blok etiketleri (ad → etiket) ve sayfanın bölüm başlığı. */
    blockLabels: Record<string, string> = {};
    getBlockLabel = vi.fn().mockName('getBlockLabel').mockImplementation((name: string) => this.blockLabels[name] ?? null);
    readonly sectionLabel = signal<string | null>(null);
    setAnswerKeyOverlays = vi.fn().mockName('setAnswerKeyOverlays');
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
    let gradesApi: MockedObject<GradesService>;
    const GRADES: Grade[] = [{ id: 4, name: '4. Sınıf' }, { id: 5, name: '5. Sınıf' }];
    /** `setup` öncesi değiştirilebilir: sınıf listesi yanıtı. */
    let gradesResponse: Observable<Grade[]>;
    let booksApi: MockedObject<LabelingBookService>;
    /** `setup` öncesi değiştirilebilir: kitap listesi yanıtı (varsayılan boş → kitap seçimi gizli). */
    let booksResponse: Observable<LabelingBook[]>;
    /** `setup` öncesi değiştirilebilir: token'daki realm rolleri (issue #580 onay düğmesi). */
    let realmRoles: string[];

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
            saveToBank: vi.fn().mockName("LabelingService.saveToBank"),
            activityStructure: vi.fn().mockName("LabelingService.activityStructure"),
            readAnswerKey: vi.fn().mockName("LabelingService.readAnswerKey"),
            getAnswerKey: vi.fn().mockName("LabelingService.getAnswerKey"),
            lookupAnswerKey: vi.fn().mockName("LabelingService.lookupAnswerKey"),
            updateAnswerKeyEntry: vi.fn().mockName("LabelingService.updateAnswerKeyEntry"),
            deleteAnswerKeyEntry: vi.fn().mockName("LabelingService.deleteAnswerKeyEntry"),
            solveActivity: vi.fn().mockName("LabelingService.solveActivity"),
            updateActivitySolution: vi.fn().mockName("LabelingService.updateActivitySolution"),
            approveActivitySolution: vi.fn().mockName("LabelingService.approveActivitySolution"),
            getActivitySolutions: vi.fn().mockName("LabelingService.getActivitySolutions")
        } as unknown as MockedObject<LabelingService>;
        // Varsayılan: kırpmanın kayıtlı çözümü yok.
        labelingApi.getActivitySolutions.mockReturnValue(of({ solutions: [] }));
        labelingApi.getAnswerKey.mockReturnValue(of({ entries: [] }));
        labelingApi.lookupAnswerKey.mockReturnValue(of({ results: [] }));
        labelingApi.getAiModels.mockReturnValue(of(aiModels));
        gradesApi = {
            getGrades: vi.fn().mockName("GradesService.getGrades")
        } as unknown as MockedObject<GradesService>;
        gradesApi.getGrades.mockReturnValue(gradesResponse);
        booksApi = {
            list: vi.fn().mockName("LabelingBookService.list"),
            getPage: vi.fn().mockName("LabelingBookService.getPage")
        } as unknown as MockedObject<LabelingBookService>;
        booksApi.list.mockReturnValue(booksResponse);
        booksApi.getPage.mockReturnValue(throwError(() => ({ status: 404 })));
        // Varsayılan: sayfanın kaydı yok (404 → null).
        labelingApi.getPage.mockReturnValue(throwError(() => ({ status: 404 })));
        TestBed.configureTestingModule({
            imports: [LabelingComponent, translocoTestingModule({ langs: { 'labeling/tr': labelingTr } })],
            providers: [
                provideNoopAnimations(),
                provideRouter([]),
                { provide: QuestionDetectorService, useValue: detector },
                { provide: LabelingBookService, useValue: booksApi },
                { provide: LabelingService, useValue: labelingApi },
                { provide: GradesService, useValue: gradesApi },
                { provide: AuthService, useValue: { hasRealmRole: (role: string) => realmRoles.includes(role) } },
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
        localStorage.removeItem('labeling.gradeId');
        localStorage.removeItem('labeling.bookId');
        gradesResponse = of(GRADES);
        booksResponse = of([]);
        realmRoles = ['Teacher', 'QuestionTrainer'];
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

        async function openPageWithQuestion(
            answerLabels: string[],
            blockKind = 'mcq_question',
            activityComponent: string | null = null
        ): Promise<void> {
            await setup();
            await pickFolder([image('a.webp')]);
            selector().regions.set([{ name: 'q1' }, { name: 'q2' }]);
            selector().inspector = {
                index: 1,
                no: 2,
                name: 'q2',
                blockKind,
                activityComponent,
                answers: answerLabels.map((label, i) => ({ id: i + 1, label, isCorrect: i === 1 })),
            };
            fixture.detectChanges();
        }

        describe('blok türü (#578)', () => {
            function selectValue(css: string): unknown {
                return fixture.debugElement.query(By.css(css)).injector.get(MatSelect).value;
            }

            it('kindSelect_ShowsCurrentKindAndForwardsChange', async () => {
                await openPageWithQuestion(['A', 'B', 'C', 'D']);
                const el = inspectorEl()!;
                expect(selectValue('.lb-kind-select')).toBe('mcq_question');
                // Soru: şıklar görünür, bileşen seçimi yok.
                expect(el.querySelectorAll('.lb-choice').length).toBe(4);
                expect(el.querySelector('.lb-component-select')).toBeNull();

                component.insSetKind('info_box');

                expect(selector().setBlockKind).toHaveBeenCalledWith(1, 'info_box');
            });

            it('nonQuestionKind_HidesAnswersAndShowsHint', async () => {
                await openPageWithQuestion([], 'noise');
                const el = inspectorEl()!;
                expect(el.querySelector('.lb-choice')).toBeNull();
                expect(el.querySelector('.lb-activity-label')).toBeNull();
                expect(el.querySelector('.lb-kind__hint')?.textContent).toContain(labelingTr.inspector.kind.hint.noise);
            });

            it('activityKind_ShowsComponentSelectAndForwardsChange', async () => {
                await openPageWithQuestion([], 'activity', 'A');
                const el = inspectorEl()!;
                expect(el.querySelector('.lb-component-select')).not.toBeNull();
                expect(selectValue('.lb-component-select')).toBe('A');

                component.insSetComponent('C');

                expect(selector().setActivityComponent).toHaveBeenCalledWith(1, 'C');
            });

            it('legend_VisibleOnlyForClaudeSourceOrClaudeBasedManual', async () => {
                await openPageWithQuestion(['A']);
                const legend = () => fixture.nativeElement.querySelector('.lb-legend') as HTMLElement | null;
                expect(legend()).toBeNull();

                selector().detectionSource.set({ kind: 'Ai', modelName: 'claude-a' });
                fixture.detectChanges();
                expect(legend()!.querySelectorAll('.lb-legend__item').length).toBe(5);
                expect(legend()!.textContent).toContain(labelingTr.inspector.kind.types.info_box);

                selector().detectionSource.set({ kind: 'Model' });
                fixture.detectChanges();
                expect(legend()).toBeNull();

                selector().detectionSource.set({ kind: 'Manual', base: { source: 'Ai', modelName: 'claude-a' } });
                fixture.detectChanges();
                expect(legend()).not.toBeNull();
            });

            it('whileSending_KindChangeIgnored', async () => {
                await openPageWithQuestion(['A'], 'activity');
                selector().sendingToFix.set(true);

                component.insSetKind('mcq_question');
                component.insSetComponent('A');

                expect(selector().setBlockKind).not.toHaveBeenCalled();
                expect(selector().setActivityComponent).not.toHaveBeenCalled();
            });
        });

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
        async function openPageWith(regions: StubRegion[], page: LabelingPage | null = null, gradeId: string | null = '5'): Promise<void> {
            if (gradeId != null) {
                localStorage.setItem('labeling.gradeId', gradeId);
            }
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
            expect(selector().ensureQuestionsValid).toHaveBeenCalledWith({ requireCorrectAnswer: true, skipInteractive: true });
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
            localStorage.setItem('labeling.gradeId', '5');
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

        it('saveToBank_NonQuestionBlocks_SendsOnlyMcqQuestions', async () => {
            // Issue #578: etkinlik, anlatım, cevap anahtarı ve gürültü bloğu bankaya gitmez.
            await openPageWith([region('S1', 1), region('S2', 0, 10, 120), region('S3', 0, 10, 240), region('S4', 0, 10, 360), region('S5', 2, 10, 480)]);
            selector().blockKinds = { S2: 'activity', S3: 'info_box', S4: 'answer_key', S5: 'noise' };
            labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));

            component.saveToBank();

            const request = vi.mocked(labelingApi.saveToBank).mock.lastCall![0];
            expect(request.questions.length).toBe(1);
            expect(request.questions[0].correctOptionIndex).toBe(1);
        });

        it('saveToBank_OnlyNonQuestionBlocks_ShowsNoMcqAndSendsNothing', async () => {
            await openPageWith([region('S1', 0)]);
            selector().blockKinds = { S1: 'info_box' };
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(labelingApi.saveToBank).not.toHaveBeenCalled();
            expect(snack).toHaveBeenCalledWith(labelingTr.bank.noMcq, labelingTr.ok, expect.any(Object));
        });

        it('saveToBank_InvalidQuestions_SendsNothing', async () => {
            await openPageWith([region('S1', 0)]);
            selector().ensureQuestionsValid.mockReturnValue(false);

            component.saveToBank();

            expect(selector().ensureQuestionsValid).toHaveBeenCalledWith({ requireCorrectAnswer: true, skipInteractive: true });
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

        describe('sınıf seçimi (#565)', () => {
            function gradeSelect(): HTMLElement {
                return fixture.nativeElement.querySelector('.lb-grade mat-select') as HTMLElement;
            }

            it('grades_Loaded_RendersOptionsInToolbarSelect', async () => {
                await openPageWith([region('S1', 0)], null, null);

                expect(gradesApi.getGrades).toHaveBeenCalledTimes(1);
                expect(component.grades()).toEqual(GRADES);
                expect(fixture.nativeElement.querySelector('.lb-toolbar').textContent).toContain(labelingTr.grade.label);
                gradeSelect().click();
                fixture.detectChanges();
                await fixture.whenStable();
                // CDK 21+: panel tetikleyicinin yanında native popover; document üzerinden aranır.
                const options = Array.from(document.querySelectorAll('mat-option')).map((o) => o.textContent?.trim());
                expect(options).toEqual(['4. Sınıf', '5. Sınıf']);
            });

            it('bankButton_NoGrade_DisabledWithHint_EnabledAfterSelection', async () => {
                await openPageWith([region('S1', 0)], null, null);

                expect(component.gradeId()).toBeNull();
                expect(component.canSaveToBank()).toBe(false);
                expect(bankButton().getAttribute('aria-disabled')).toBe('true');
                component.saveToBank();
                expect(labelingApi.saveToBank).not.toHaveBeenCalled();

                component.setGrade({ value: 4 } as MatSelectChange);
                fixture.detectChanges();

                expect(component.canSaveToBank()).toBe(true);
                expect(bankButton().getAttribute('aria-disabled')).not.toBe('true');
                expect(localStorage.getItem('labeling.gradeId')).toBe('4');
            });

            it('saveToBank_SelectedGrade_RequestCarriesGradeId', async () => {
                await openPageWith([region('S1', 0)], null, null);
                component.setGrade({ value: 4 } as MatSelectChange);
                labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));

                component.saveToBank();

                expect(vi.mocked(labelingApi.saveToBank).mock.lastCall![0].gradeId).toBe(4);
            });

            it('init_StoredGradeInList_IsRestored', async () => {
                await openPageWith([region('S1', 0)], null, '5');

                expect(component.gradeId()).toBe(5);
                expect(component.canSaveToBank()).toBe(true);
            });

            it('init_StoredGradeNotInList_IsIgnored', async () => {
                await openPageWith([region('S1', 0)], null, '99');

                expect(component.gradeId()).toBeNull();
                expect(component.canSaveToBank()).toBe(false);
            });

            it('init_GradesFail_NoGradeAndBankDisabled', async () => {
                gradesResponse = throwError(() => ({ status: 500 }));
                await openPageWith([region('S1', 0)], null, '5');

                expect(component.gradesError()).toBe(true);
                expect(component.gradeId()).toBeNull();
                expect(component.canSaveToBank()).toBe(false);
            });
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

        it('saveToBank_GradeRequired400_ShowsLocalizedServerMessage', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 400, error: { message: 'Sayfanın sınıfı seçilmeli.' } })));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(snack).toHaveBeenCalledWith('Sayfanın sınıfı seçilmeli.', labelingTr.ok, expect.any(Object));
            expect(selector().sendToFix).not.toHaveBeenCalled();
            expect(component.bankSaving()).toBe(false);
        });

        it('saveToBank_ServerErrorWithoutBody_ShowsGenericFailure', async () => {
            await openPageWith([region('S1', 0)]);
            labelingApi.saveToBank.mockReturnValue(throwError(() => ({ status: 500 })));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.saveToBank();

            expect(snack).toHaveBeenCalledWith(labelingTr.bank.failed, labelingTr.ok, expect.any(Object));
        });

        describe('kaynak kitap (#577)', () => {
            const BOOK: LabelingBook = {
                id: 7, name: 'TT MatLab', canonicalBookId: null, canonicalBookName: null, notes: null,
                offsets: [{ fileFrom: 1, fileTo: 100, offset: -1 }], answerKeyRanges: [{ fileFrom: 200, fileTo: 210 }], toc: [],
            };
            const PAGE_INFO: LabelingBookPageInfo = {
                filePage: 40, printedPage: 39, isAnswerKey: false,
                toc: {
                    fileFrom: 30, fileTo: 50, printedFrom: 29, printedTo: 49, gradeId: 4, subjectId: 3, topicId: 11, subTopicId: null,
                    gradeName: '4. Sınıf', subjectName: 'Fen Bilimleri', topicName: 'Maddenin Hâlleri', subTopicName: null,
                },
            };

            /** Kitap listesi + (isteğe bağlı) kayıtlı kitap seçimiyle `page_040.webp` sayfasını açar. */
            async function openBookPage(regions: StubRegion[], storedBookId: string | null = '7', gradeId: string | null = '5'): Promise<void> {
                if (storedBookId != null) {
                    localStorage.setItem('labeling.bookId', storedBookId);
                }
                if (gradeId != null) {
                    localStorage.setItem('labeling.gradeId', gradeId);
                }
                booksResponse = of([BOOK]);
                await setup();
                booksApi.getPage.mockReturnValue(of(PAGE_INFO));
                await pickFolder([image('page_040.webp')]);
                selector().regions.set(regions);
                fixture.detectChanges();
                await settle(fixture);
            }

            it('bookSelected_PageInfoWithToc_SetsGradeDefaultAndShowsPrintedAndTocChip', async () => {
                await openBookPage([region('S1', 0)]);

                expect(component.bookId()).toBe(7);
                expect(component.filePage()).toBe(40);
                expect(booksApi.getPage).toHaveBeenCalledWith(7, 40);
                // Kayıtlı sınıf 5'ti; içindekiler 4 diyor → varsayılan 4 (localStorage'a yazılmaz).
                expect(component.gradeId()).toBe(4);
                expect(localStorage.getItem('labeling.gradeId')).toBe('5');
                expect(component.tocLabel()).toBe('Fen Bilimleri › Maddenin Hâlleri');
                const el = fixture.nativeElement as HTMLElement;
                expect(el.querySelector('.lb-book__page')?.textContent?.trim()).toBe('s. 39 (dosya 40)');
                expect(el.querySelector('.lb-book__toc')?.textContent).toContain('Fen Bilimleri › Maddenin Hâlleri');
                expect(el.querySelector('.lb-book__answer-key')).toBeNull();
            });

            it('bookSelected_UserChangesGradeAfterDefault_KeepsUserChoice_WarnsAndStrikesToc', async () => {
                await openBookPage([region('S1', 0)]);
                expect(component.tocGradeMismatch()).toBe(false);

                component.setGrade({ value: 5 } as MatSelectChange);
                fixture.detectChanges();

                expect(component.gradeId()).toBe(5);
                expect(component.tocGradeMismatch()).toBe(true);
                const el = fixture.nativeElement as HTMLElement;
                expect(el.querySelector('.lb-book__toc')?.classList.contains('lb-pill--off')).toBe(true);
                expect(el.querySelector('.lb-book__mismatch')?.textContent).toContain(labelingTr.book.tocGradeMismatch);
            });

            it('tocGrade_DoesNotLeakToNextPageWithoutToc_RestoresManualGrade', async () => {
                localStorage.setItem('labeling.bookId', '7');
                localStorage.setItem('labeling.gradeId', '5');
                booksResponse = of([BOOK]);
                await setup();
                booksApi.getPage.mockImplementation((_id: number, filePage: number) =>
                    of(filePage === 40 ? PAGE_INFO : { filePage, printedPage: filePage - 1, isAnswerKey: false, toc: null }));
                await pickFolder([image('page_040.webp'), image('page_041.webp')]);
                expect(component.gradeId()).toBe(4);

                component.skip();
                await settle(fixture);

                expect(component.filePage()).toBe(41);
                expect(component.gradeId()).toBe(5);
                expect(component.tocGradeMismatch()).toBe(false);
            });

            it('tocStale_ShowsWarningNoChip_KeepsManualGrade', async () => {
                localStorage.setItem('labeling.bookId', '7');
                localStorage.setItem('labeling.gradeId', '5');
                booksResponse = of([BOOK]);
                await setup();
                booksApi.getPage.mockReturnValue(of({ filePage: 40, printedPage: 39, isAnswerKey: false, toc: null, tocStale: true }));
                await pickFolder([image('page_040.webp')]);
                await settle(fixture);

                const el = fixture.nativeElement as HTMLElement;
                expect(el.querySelector('.lb-book__stale')?.textContent).toContain(labelingTr.book.tocStale);
                expect(el.querySelector('.lb-book__toc')).toBeNull();
                expect(component.gradeId()).toBe(5);
            });

            it('bookSelected_AnswerKeyPage_ShowsBadge', async () => {
                booksResponse = of([BOOK]);
                localStorage.setItem('labeling.bookId', '7');
                await setup();
                booksApi.getPage.mockReturnValue(of({ filePage: 205, printedPage: null, isAnswerKey: true, toc: null }));
                await pickFolder([image('page_205.webp')]);
                await settle(fixture);

                const el = fixture.nativeElement as HTMLElement;
                expect(el.querySelector('.lb-book__answer-key')).not.toBeNull();
                expect(el.querySelector('.lb-book__page')?.textContent).toContain('dosya 205');
            });

            it('storedBookNotInList_NoBookSelected_NoPageLookup', async () => {
                await openBookPage([region('S1', 0)], '99');

                expect(component.bookId()).toBeNull();
                expect(booksApi.getPage).not.toHaveBeenCalled();
                expect(component.gradeId()).toBe(5);
            });

            it('booksListFails_SelectorHidden_BankSaveWithoutBook', async () => {
                booksResponse = throwError(() => ({ status: 403 }));
                localStorage.setItem('labeling.gradeId', '5');
                await setup();
                await pickFolder([image('page_040.webp')]);
                selector().regions.set([region('S1', 0)]);
                fixture.detectChanges();
                labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));

                component.saveToBank();

                expect((fixture.nativeElement as HTMLElement).querySelector('.lb-book')).toBeNull();
                const request = vi.mocked(labelingApi.saveToBank).mock.lastCall![0];
                expect('bookId' in request).toBe(false);
                expect('filePage' in request).toBe(false);
            });

            it('saveToBank_BookSelected_RequestCarriesBookIdFilePageAndTrimmedLabels', async () => {
                await openBookPage([region('S1', 0), region('S2', 1)]);
                labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));
                component.setQuestionLabel('S1', 'activityLabel', { value: '  Etkinlik 9 ' } as unknown as EventTarget);
                component.setQuestionLabel('S1', 'itemLabel', { value: '3' } as unknown as EventTarget);

                component.saveToBank();

                const request = vi.mocked(labelingApi.saveToBank).mock.lastCall![0];
                expect(request.bookId).toBe(7);
                expect(request.filePage).toBe(40);
                expect(request.gradeId).toBe(4);
                expect(request.questions[0].activityLabel).toBe('Etkinlik 9');
                expect(request.questions[0].itemLabel).toBe('3');
                expect('activityLabel' in request.questions[1]).toBe(false);
                expect('itemLabel' in request.questions[1]).toBe(false);
            });

            it('setQuestionLabel_LongerThanLimit_IsTruncatedTo50', async () => {
                await openBookPage([region('S1', 0)]);

                component.setQuestionLabel('S1', 'itemLabel', { value: 'x'.repeat(80) } as unknown as EventTarget);

                expect(component.labelsOf('S1').itemLabel.length).toBe(50);
            });

            it('pageChange_ClearsQuestionLabels', async () => {
                await openBookPage([region('S1', 0)]);
                component.setQuestionLabel('S1', 'itemLabel', { value: '3' } as unknown as EventTarget);

                component.skip();
                await settle(fixture);

                expect(component.labelsOf('S1').itemLabel).toBe('');
            });

            it('saveToBank_FileNameWithoutNumber_SendsNoBookSource', async () => {
                booksResponse = of([BOOK]);
                localStorage.setItem('labeling.bookId', '7');
                localStorage.setItem('labeling.gradeId', '5');
                await setup();
                await pickFolder([image('kapak.webp')]);
                selector().regions.set([region('S1', 0)]);
                fixture.detectChanges();
                labelingApi.saveToBank.mockReturnValue(of({ imageSha256: 'abc', bank: savedBank }));

                component.saveToBank();

                expect(component.filePage()).toBeNull();
                expect(booksApi.getPage).not.toHaveBeenCalled();
                expect((fixture.nativeElement as HTMLElement).querySelector('.lb-book__info')?.textContent)
                    .toContain(labelingTr.book.noPageNumber);
                const request = vi.mocked(labelingApi.saveToBank).mock.lastCall![0];
                expect(request.bookId).toBeUndefined();
                expect(request.filePage).toBeUndefined();
            });

            describe('cevap anahtarı sayfası (409 LabelingAnswerKeyPage)', () => {
                const conflict = () => throwError(() => ({
                    status: 409, error: { message: 'Sayfa cevap anahtarında.', errorCode: 'LabelingAnswerKeyPage' },
                }));

                it('conflict_ConfirmWithServerMessage_ForceResubmits', async () => {
                    await openBookPage([region('S1', 0)]);
                    labelingApi.saveToBank.mockReturnValueOnce(conflict())
                        .mockReturnValueOnce(of({ imageSha256: 'abc', bank: savedBank }));
                    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(true) } as never);

                    component.saveToBank();

                    expect(open).toHaveBeenCalledWith(ConfirmDialogComponent, expect.objectContaining({
                        data: expect.objectContaining({ message: 'Sayfa cevap anahtarında.', confirmText: labelingTr.bank.answerKey.force }),
                    }));
                    expect(labelingApi.saveToBank).toHaveBeenCalledTimes(2);
                    expect(vi.mocked(labelingApi.saveToBank).mock.lastCall![0].force).toBe(true);
                    expect(component.bankRecord()?.questionCount).toBe(1);
                });

                it('conflict_Declined_NothingSent', async () => {
                    await openBookPage([region('S1', 0)]);
                    labelingApi.saveToBank.mockReturnValue(conflict());
                    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(false) } as never);

                    component.saveToBank();

                    expect(labelingApi.saveToBank).toHaveBeenCalledTimes(1);
                    expect(selector().sendToFix).not.toHaveBeenCalled();
                    expect(component.bankRecord()).toBeNull();
                });
            });

            describe('aynı kırpma (409 LabelingCropAlreadyInBank)', () => {
                const existing = [
                    { questionIndex: 0, questionId: 501, bookName: 'TT MatLab', filePage: 40, printedPage: 39, activityLabel: null, itemLabel: '3' },
                ];
                const conflict = () => throwError(() => ({
                    status: 409, error: { message: 'x', errorCode: 'LabelingCropAlreadyInBank', existing },
                }));

                function dialogResult(result: boolean | undefined) {
                    return vi.spyOn(TestBed.inject(MatDialog), 'open')
                        .mockReturnValue({ afterClosed: () => of(result) } as never);
                }

                it('conflict_OpensDialogWithExistingRecords_ForceResubmits', async () => {
                    await openBookPage([region('S1', 0)]);
                    labelingApi.saveToBank.mockReturnValueOnce(conflict())
                        .mockReturnValueOnce(of({ imageSha256: 'abc', bank: savedBank }));
                    const open = dialogResult(true);

                    component.saveToBank();

                    expect(open).toHaveBeenCalledWith(LabelingCropDuplicateDialogComponent, expect.objectContaining({ data: { existing } }));
                    expect(labelingApi.saveToBank).toHaveBeenCalledTimes(2);
                    const retry = vi.mocked(labelingApi.saveToBank).mock.lastCall![0];
                    expect(retry.force).toBe(true);
                    expect(retry.bookId).toBe(7);
                    // Sayfa-düzeyi kayıt kartı kırpma çakışmasında yazılmaz.
                    expect(component.bankRecord()?.questionCount).toBe(1);
                });

                it('conflict_DialogCancelled_DoesNotResubmitOrTrain', async () => {
                    await openBookPage([region('S1', 0)]);
                    labelingApi.saveToBank.mockReturnValue(conflict());
                    dialogResult(false);

                    component.saveToBank();

                    expect(labelingApi.saveToBank).toHaveBeenCalledTimes(1);
                    expect(selector().sendToFix).not.toHaveBeenCalled();
                    expect(component.bankRecord()).toBeNull();
                    expect(component.bankSaving()).toBe(false);
                });
            });
        });
    });


    describe('etkinlik yapısı (#579)', () => {
        const model: LabelingAiModel = {
            id: 'claude-a',
            displayName: 'Claude A',
            inputUsdPerMTok: 3,
            outputUsdPerMTok: 15,
            isDefault: true,
        };

        interface Block {
            name: string;
            kind?: string;
            type?: string;
        }

        /** Sırayla: MCQ, A (matris), B (kutu), C (sürükle), tipsiz etkinlik D, anlatım, gürültü. */
        const MIXED: Block[] = [
            { name: 'Soru 1' },
            { name: 'Soru 2', kind: 'activity', type: 'matrixChoice' },
            { name: 'Soru 3', kind: 'activity', type: 'slotFill' },
            { name: 'Soru 4', kind: 'activity', type: 'dragPlace' },
            { name: 'Soru 5', kind: 'activity' },
            { name: 'Soru 6', kind: 'info_box' },
            { name: 'Soru 7', kind: 'noise' },
        ];

        function result(key: string, interactionType: string, overrides: Partial<LabelingActivityStructureResult> = {}) {
            return {
                key,
                interactionType,
                status: 'ok',
                origin: 'cached',
                plan: { version: 1, type: interactionType },
                instruction: null,
                valid: true,
                validationErrors: [],
                promptVersion: 2,
                inputTokens: 100,
                outputTokens: 50,
                estimatedCostUsd: 0.0012,
                error: null,
                ...overrides,
            } as LabelingActivityStructureResult;
        }

        /** İstenen blokların hepsi için başarılı yanıt. */
        function echoOk() {
            structure.mockImplementation((request) =>
                of({ results: request.blocks.map((b) => result(b.key, b.interactionType)) })
            );
        }

        /** Tuvale tespit uygulanmış gibi: türler, tipler, bölgeler ve kaynak. */
        async function applyDetection(blocks: Block[], source: DetectionSource = { kind: 'Ai', modelName: 'claude-a' }) {
            const stub = selector();
            stub.blockKinds = {};
            stub.interactionTypes = {};
            blocks.forEach((b) => {
                if (b.kind) stub.blockKinds[b.name] = b.kind;
                if (b.type) stub.interactionTypes[b.name] = b.type;
            });
            stub.detectionSource.set(source);
            stub.regions.set(blocks.map((b, i) => ({ name: b.name, x: 10.4, y: i * 100, width: 200, height: 80.6 })));
            stub.detectionRevision.update((n) => n + 1);
            TestBed.tick();
            await settle(fixture);
        }

        /** `setup` hizmet taklitlerini yeniden kurar; yapı ucunun taklidi testten önce hazırlanıp buraya bağlanır. */
        let structure: Mock<LabelingService['activityStructure']>;
        beforeEach(() => {
            structure = vi.fn().mockName('LabelingService.activityStructure');
        });

        async function openFolder(files = [image('a.webp')]) {
            await setup([], false, [model]);
            (labelingApi as unknown as { activityStructure: unknown }).activityStructure = structure;
            component.structureDebounceMs = 0;
            await pickFolder(files);
        }

        async function openPage(blocks: Block[] = MIXED, source?: DetectionSource, files = [image('a.webp')]) {
            await openFolder(files);
            await applyDetection(blocks, source);
        }

        /** Bölgenin tipini müfettişten değiştirmiş gibi (gerçek bileşen `regions` sinyalini yeniler). */
        async function changeType(name: string, type: string | null) {
            const stub = selector();
            if (type) stub.interactionTypes[name] = type;
            else delete stub.interactionTypes[name];
            stub.regions.set([...stub.regions()]);
            TestBed.tick();
            await settle(fixture);
        }

        function requestedKeys(call: number): string[] {
            return structure.mock.calls[call][0].blocks.map((b) => b.key);
        }

        it('claudePage_TypedActivities_OneBatchWithOnlyTypedBlocksAndForceFalse', async () => {
            echoOk();
            await openPage();

            expect(structure).toHaveBeenCalledTimes(1);
            const request = structure.mock.calls[0][0];
            expect(request.model).toBe('claude-a');
            expect(request.force).toBe(false);
            expect(request.imageBase64).toContain('data:image/webp;base64,');
            // MCQ, tipsiz etkinlik, anlatım ve gürültü istenmez; kutular yuvarlanır.
            expect(request.blocks).toEqual([
                { key: 'Soru 2', x: 10, y: 100, width: 200, height: 81, interactionType: 'matrixChoice' },
                { key: 'Soru 3', x: 10, y: 200, width: 200, height: 81, interactionType: 'slotFill' },
                { key: 'Soru 4', x: 10, y: 300, width: 200, height: 81, interactionType: 'dragPlace' },
            ]);
        });

        it('claudePage_NoTypedActivities_NoRequest', async () => {
            echoOk();
            await openPage([{ name: 'Soru 1' }, { name: 'Soru 2', kind: 'activity' }, { name: 'Soru 3', kind: 'answer_key' }]);

            expect(structure).not.toHaveBeenCalled();
        });

        it('modelSource_TypedActivities_NoRequest', async () => {
            echoOk();
            await openPage(MIXED, { kind: 'Model' });

            expect(structure).not.toHaveBeenCalled();
        });

        it('manualRecordFromClaude_TypedActivities_RequestsWithRecordModel', async () => {
            echoOk();
            await openPage(MIXED, { kind: 'Manual', base: { source: 'Ai', modelName: 'claude-a' } });

            expect(structure).toHaveBeenCalledTimes(1);
            expect(structure.mock.calls[0][0].force).toBe(false);
        });

        it('okResults_AppliedToAuthoringStatePerTypeAndKeptAsDrafts', async () => {
            structure.mockReturnValue(of({
                results: [
                    result('Soru 2', 'matrixChoice', { origin: 'live' }),
                    result('Soru 3', 'slotFill', { valid: false, validationErrors: ['Kutu 2 boş'] }),
                    result('Soru 4', 'dragPlace', { status: 'error', plan: null, error: 'Claude yanıtı okunamadı' }),
                ],
            } as LabelingActivityStructureResponse));
            await openPage();
            const stub = selector();

            expect(stub.applyInteractionDraft).toHaveBeenCalledTimes(2);
            expect(stub.applyInteractionDraft).toHaveBeenCalledWith('Soru 2', 'matrixChoice', { version: 1, type: 'matrixChoice' });
            expect(stub.applyInteractionDraft).toHaveBeenCalledWith('Soru 3', 'slotFill', { version: 1, type: 'slotFill' });
            const drafts = component.activityDrafts();
            expect(drafts['Soru 2']).toEqual(expect.objectContaining({ status: 'ok', apply: 'applied', interactionType: 'matrixChoice' }));
            expect(drafts['Soru 3'].result?.validationErrors).toEqual(['Kutu 2 boş']);
            expect(drafts['Soru 4']).toEqual(expect.objectContaining({ status: 'error', error: 'Claude yanıtı okunamadı' }));
        });

        it('typeChange_ReRequestsOnlyThatBlock', async () => {
            echoOk();
            await openPage();

            await changeType('Soru 4', 'match');

            expect(structure).toHaveBeenCalledTimes(2);
            expect(structure.mock.calls[1][0].blocks).toEqual([
                expect.objectContaining({ key: 'Soru 4', interactionType: 'match' }),
            ]);
            expect(structure.mock.calls[1][0].force).toBe(false);
            expect(selector().applyInteractionDraft).toHaveBeenLastCalledWith('Soru 4', 'match', { version: 1, type: 'match' });
        });

        it('typeRemovedThenRestored_DraftDroppedThenReRequested', async () => {
            echoOk();
            await openPage();

            await changeType('Soru 2', null);
            expect(component.activityDrafts()['Soru 2']).toBeUndefined();
            expect(structure).toHaveBeenCalledTimes(1);

            await changeType('Soru 2', 'matrixChoice');
            expect(structure).toHaveBeenCalledTimes(2);
            expect(requestedKeys(1)).toEqual(['Soru 2']);
        });

        it('unrelatedRegionUpdate_NoNewRequest', async () => {
            echoOk();
            await openPage();

            selector().regions.set([...selector().regions()]);
            TestBed.tick();
            await settle(fixture);

            expect(structure).toHaveBeenCalledTimes(1);
        });

        it('staleResponse_AfterPageChange_IsIgnored', async () => {
            const pending = new Subject<LabelingActivityStructureResponse>();
            structure.mockReturnValue(pending.asObservable());
            await openPage(MIXED, undefined, [image('a.webp'), image('b.webp')]);

            component.skip();
            await settle(fixture);
            pending.next({ results: [result('Soru 2', 'matrixChoice')] });

            expect(selector().applyInteractionDraft).not.toHaveBeenCalled();
            expect(component.activityDrafts()).toEqual({});
        });

        it('staleResponse_AfterNewDetection_IsIgnored', async () => {
            const pending = new Subject<LabelingActivityStructureResponse>();
            structure.mockReturnValueOnce(pending.asObservable());
            await openPage();
            structure.mockReturnValue(new Subject<LabelingActivityStructureResponse>().asObservable());

            await applyDetection(MIXED);
            pending.next({ results: [result('Soru 2', 'matrixChoice')] });

            expect(selector().applyInteractionDraft).not.toHaveBeenCalled();
            expect(component.activityDrafts()['Soru 2'].status).toBe('loading');
        });

        it('staleResponse_BlockTypeChangedWhileLoading_OldResultIgnoredForThatBlock', async () => {
            const first = new Subject<LabelingActivityStructureResponse>();
            const second = new Subject<LabelingActivityStructureResponse>();
            structure.mockReturnValueOnce(first.asObservable()).mockReturnValueOnce(second.asObservable());
            await openPage();

            await changeType('Soru 4', 'match');
            first.next({ results: [result('Soru 2', 'matrixChoice'), result('Soru 3', 'slotFill'), result('Soru 4', 'dragPlace')] });

            expect(selector().applyInteractionDraft).not.toHaveBeenCalledWith('Soru 4', 'dragPlace', expect.anything());
            expect(component.activityDrafts()['Soru 4']).toEqual(expect.objectContaining({ status: 'loading', interactionType: 'match' }));
            expect(component.activityDrafts()['Soru 2'].status).toBe('ok');

            second.next({ results: [result('Soru 4', 'match')] });
            expect(component.activityDrafts()['Soru 4']).toEqual(expect.objectContaining({ status: 'ok', interactionType: 'match' }));
        });

        it('rateLimited429_ShowsServerMessageAndMarksDraftsFailed', async () => {
            vi.spyOn(console, 'error').mockReturnValue(undefined);
            structure.mockReturnValue(throwError(() => ({
                status: 429,
                error: { errorCode: 'LabelingBudgetExceeded', message: 'Günlük Claude bütçesi doldu.' },
            })));
            await openFolder();
            const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            await applyDetection(MIXED);

            expect(snackBar).toHaveBeenCalledWith('Günlük Claude bütçesi doldu.', expect.any(String), expect.any(Object));
            expect(component.activityDrafts()['Soru 2']).toEqual(expect.objectContaining({ status: 'error', error: 'Günlük Claude bütçesi doldu.' }));
            // Hatalı taslak kendiliğinden yeniden istenmez.
            selector().regions.set([...selector().regions()]);
            TestBed.tick();
            await settle(fixture);
            expect(structure).toHaveBeenCalledTimes(1);
        });

        it('noKey503WithoutMessage_ShowsUnavailable', async () => {
            vi.spyOn(console, 'error').mockReturnValue(undefined);
            structure.mockReturnValue(throwError(() => ({ status: 503 })));
            await openPage();

            expect(component.activityDrafts()['Soru 3'].error).toBe(labelingTr.structure.unavailable);
        });

        const MANY = Array.from({ length: 23 }, (_, i) => ({ name: `Soru ${i + 1}`, kind: 'activity', type: 'slotFill' }));

        it('moreThan20Blocks_BatchesSentSequentiallyAllLoadingUpfront', async () => {
            const first = new Subject<LabelingActivityStructureResponse>();
            structure.mockReturnValueOnce(first.asObservable()).mockReturnValueOnce(of({ results: [] }));
            await openPage(MANY);

            // İkinci istek ilki bitmeden gitmez; tüm bloklar baştan yükleniyor.
            expect(structure).toHaveBeenCalledTimes(1);
            expect(requestedKeys(0).length).toBe(20);
            expect(component.activityDrafts()['Soru 23'].status).toBe('loading');

            first.next({ results: [] });
            first.complete();

            expect(structure).toHaveBeenCalledTimes(2);
            expect(requestedKeys(1).length).toBe(3);
        });

        it('moreThan20Blocks_FirstBatchFails_RestNotSentAndMarkedFailed', async () => {
            vi.spyOn(console, 'error').mockReturnValue(undefined);
            structure.mockReturnValue(throwError(() => ({ status: 429, error: { message: 'Bütçe doldu' } })));
            await openPage(MANY);

            expect(structure).toHaveBeenCalledTimes(1);
            expect(component.activityDrafts()['Soru 23']).toEqual(expect.objectContaining({ status: 'error', error: 'Bütçe doldu' }));
        });

        it('liveResult_ShowsCostWithoutFirstCallLabel', async () => {
            structure.mockReturnValue(of({ results: [result('Soru 2', 'matrixChoice', { origin: 'live' })] } as LabelingActivityStructureResponse));
            await openPage([{ name: 'Soru 2', kind: 'activity', type: 'matrixChoice' }]);
            selector().inspector = { index: 0, no: 1, name: 'Soru 2', blockKind: 'activity', activityComponent: 'A', interactionType: 'matrixChoice', answers: [] };
            fixture.detectChanges();

            const cost = fixture.nativeElement.querySelector('.lb-structure__cost')!.textContent!.trim();
            expect(cost).toBe('~$0.0012');
        });

        it('deletedRegion_DraftDroppedEvenWhenNotClaude_NewRegionWithSameNameDoesNotInherit', async () => {
            echoOk();
            await openPage();
            // Kaynak YOLO'ya döndü sayılmaz (yeni tespit yok); yalnız yapı kapalı: model listesi boşaldı.
            component.aiModels.set([]);
            await changeType('Soru 2', null);

            expect(component.activityDrafts()['Soru 2']).toBeUndefined();

            component.aiModels.set([model]);
            await changeType('Soru 2', 'matrixChoice');
            expect(requestedKeys(1)).toEqual(['Soru 2']);
        });

        it('regionMoved_DraftStaleThenReRequestedAfterDebounceWithNewBox', async () => {
            echoOk();
            await openFolder();
            component.structureDebounceMs = 30;
            await applyDetection(MIXED);
            const stub = selector();

            stub.regions.set(stub.regions().map((r) => (r.name === 'Soru 3' ? { ...r, x: 50, width: 150 } : r)));
            TestBed.tick();
            fixture.detectChanges();

            expect(component.activityDrafts()['Soru 3'].stale).toBe(true);
            expect(structure).toHaveBeenCalledTimes(1);

            await new Promise((resolve) => setTimeout(resolve, 60));
            await settle(fixture);

            expect(structure).toHaveBeenCalledTimes(2);
            expect(structure.mock.calls[1][0]).toEqual(expect.objectContaining({ force: false }));
            expect(structure.mock.calls[1][0].blocks).toEqual([
                expect.objectContaining({ key: 'Soru 3', x: 50, width: 150, interactionType: 'slotFill' }),
            ]);
            expect(component.activityDrafts()['Soru 3']).toEqual(expect.objectContaining({ status: 'ok', stale: false }));
        });

        it('regionResizedRepeatedly_OnlyOneRequestAfterLastChange', async () => {
            echoOk();
            await openFolder();
            component.structureDebounceMs = 40;
            await applyDetection(MIXED);
            const stub = selector();

            for (const width of [190, 180, 170]) {
                stub.regions.set(stub.regions().map((r) => (r.name === 'Soru 2' ? { ...r, width } : r)));
                TestBed.tick();
                await new Promise((resolve) => setTimeout(resolve, 10));
            }
            await new Promise((resolve) => setTimeout(resolve, 80));
            await settle(fixture);

            expect(structure).toHaveBeenCalledTimes(2);
            expect(structure.mock.calls[1][0].blocks).toEqual([expect.objectContaining({ key: 'Soru 2', width: 170 })]);
        });

        it('keptDraft_ApplyConfirmed_ReplacesWithReplaceOption', async () => {
            echoOk();
            await openFolder();
            selector().applyInteractionDraft.mockReturnValue('kept');
            await applyDetection([{ name: 'Soru 2', kind: 'activity', type: 'matrixChoice' }]);
            selector().inspector = { index: 0, no: 1, name: 'Soru 2', blockKind: 'activity', activityComponent: 'A', interactionType: 'matrixChoice', answers: [] };
            fixture.detectChanges();
            const el = fixture.nativeElement.querySelector('.lb-structure') as HTMLElement;
            expect(el.querySelector('.lb-structure__kept')).not.toBeNull();
            expect(el.querySelector('.lb-structure__apply')).not.toBeNull();
            const action = new Subject<void>();
            const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);
            selector().applyInteractionDraft.mockReturnValue('applied');

            component.applyDraft('Soru 2');
            expect(snackBar).toHaveBeenCalledWith(labelingTr.structure.applyConfirm, labelingTr.structure.applyAction, expect.any(Object));
            expect(selector().applyInteractionDraft).toHaveBeenCalledTimes(1);
            action.next();

            expect(selector().applyInteractionDraft).toHaveBeenLastCalledWith('Soru 2', 'matrixChoice', { version: 1, type: 'matrixChoice' }, { replace: true });
            expect(component.activityDrafts()['Soru 2'].apply).toBe('applied');
        });

        it('reExtractConfirm_SaysEditsReplacedOnlyViaApplyDraft', () => {
            expect(labelingTr.structure.reExtractConfirm).toContain(labelingTr.structure.apply);
            expect(labelingTr.structure.promptSaved).toContain(labelingTr.structure.apply);
        });

        it('reExtract_Confirmed_SendsSingleBlockWithForceTrue', async () => {
            echoOk();
            await openPage();
            const action = new Subject<void>();
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);

            component.reExtractStructure('Soru 3');
            expect(structure).toHaveBeenCalledTimes(1);
            action.next();

            expect(structure).toHaveBeenCalledTimes(2);
            expect(structure.mock.calls[1][0]).toEqual(expect.objectContaining({ force: true }));
            expect(requestedKeys(1)).toEqual(['Soru 3']);
        });

        it('reExtract_PageChangedBeforeConfirm_NoPaidCall', async () => {
            echoOk();
            await openPage(MIXED, undefined, [image('a.webp'), image('b.webp')]);
            const action = new Subject<void>();
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);

            component.reExtractStructure('Soru 3');
            component.skip();
            await settle(fixture);
            action.next();

            expect(structure.mock.calls.every(([request]) => request.force === false)).toBe(true);
        });

        it('retryAfterError_UsesCache', async () => {
            vi.spyOn(console, 'error').mockReturnValue(undefined);
            structure.mockReturnValueOnce(throwError(() => ({ status: 500 })));
            await openPage();
            echoOk();

            component.retryStructure('Soru 2');

            expect(structure.mock.calls[1][0]).toEqual(expect.objectContaining({ force: false }));
            expect(component.activityDrafts()['Soru 2'].status).toBe('ok');
        });

        it('insSetInteraction_ForwardsToSelector', async () => {
            echoOk();
            await openPage();
            selector().inspector = { index: 3, no: 4, name: 'Soru 4', blockKind: 'activity', activityComponent: 'C', interactionType: 'dragPlace', answers: [] };

            component.insSetInteraction('match');

            expect(selector().setActivityInteraction).toHaveBeenCalledWith(3, 'match');
        });

        describe('müfettiş', () => {
            function structureEl(): HTMLElement | null {
                return fixture.nativeElement.querySelector('.lb-structure');
            }

            function inspect(name: string, interactionType: string, activityComponent = 'A') {
                selector().inspector = { index: 1, no: 2, name, blockKind: 'activity', activityComponent, interactionType, answers: [] };
                fixture.detectChanges();
            }

            it('okDraft_ShowsOriginCostPromptValidationErrorsAndInstruction', async () => {
                structure.mockReturnValue(of({
                    results: [
                        result('Soru 2', 'matrixChoice', {
                            valid: false,
                            validationErrors: ['Satır 3 kutusuz'],
                            instruction: 'Doğru ise D, yanlış ise Y yazınız.',
                        }),
                    ],
                } as LabelingActivityStructureResponse));
                await openPage([{ name: 'Soru 1' }, { name: 'Soru 2', kind: 'activity', type: 'matrixChoice' }]);

                inspect('Soru 2', 'matrixChoice');

                const text = structureEl()!.textContent!;
                expect(text).toContain(labelingTr.structure.ready);
                expect(text).toContain(labelingTr.structure.cached);
                expect(structureEl()!.querySelector('.lb-structure__cost')?.textContent).toContain('ilk çağrı ~$0.0012');
                expect(text).toContain('prompt v2');
                expect(text).toContain('Satır 3 kutusuz');
                expect(text).toContain('Doğru ise D, yanlış ise Y yazınız.');
                expect(structureEl()!.querySelector('.lb-structure__re-extract')).not.toBeNull();
            });

            it('blankInstruction_HidesInstructionLine', async () => {
                for (const instruction of [null, '', '   ']) {
                    structure.mockReturnValue(of({
                        results: [result('Soru 2', 'matrixChoice', { instruction })],
                    } as LabelingActivityStructureResponse));
                    await openPage([{ name: 'Soru 2', kind: 'activity', type: 'matrixChoice' }]);

                    inspect('Soru 2', 'matrixChoice');

                    expect(structureEl()!.querySelector('.lb-structure__instruction')).toBeNull();
                    expect(structureEl()!.textContent).not.toContain(labelingTr.structure.instruction);
                    TestBed.resetTestingModule();
                }
            });

            it('loadingDraft_ShowsSpinnerAndDisablesReExtract', async () => {
                structure.mockReturnValue(new Subject<LabelingActivityStructureResponse>().asObservable());
                await openPage();

                inspect('Soru 2', 'matrixChoice');

                expect(structureEl()!.textContent).toContain(labelingTr.structure.loading);
                expect((structureEl()!.querySelector('.lb-structure__re-extract') as HTMLButtonElement).disabled).toBe(true);
            });

            it('errorDraft_ShowsMessageAndRetry', async () => {
                structure.mockReturnValue(of({
                    results: [result('Soru 2', 'matrixChoice', { status: 'error', plan: null, error: 'Zaman aşımı' })],
                } as LabelingActivityStructureResponse));
                await openPage([{ name: 'Soru 2', kind: 'activity', type: 'matrixChoice' }]);

                inspect('Soru 2', 'matrixChoice');

                expect(structureEl()!.querySelector('.lb-structure__error')?.textContent).toContain('Zaman aşımı');
                expect(structureEl()!.querySelector('.lb-structure__retry')).not.toBeNull();
            });

            it('notApplied_ShowsWarning', async () => {
                echoOk();
                await openFolder();
                selector().applyInteractionDraft.mockReturnValue('invalid');
                await applyDetection([{ name: 'Soru 2', kind: 'activity', type: 'slotFill' }]);

                inspect('Soru 2', 'slotFill', 'B');

                expect(structureEl()!.querySelector('.lb-structure__not-applied')).not.toBeNull();
            });

            it('modelSource_ShowsClaudeOnlyHint', async () => {
                await openPage(MIXED, { kind: 'Model' });

                inspect('Soru 2', 'matrixChoice');

                expect(structureEl()!.textContent).toContain(labelingTr.structure.claudeOnly);
            });

            it('componentC_ShowsTypeSelectWithDragPlaceAndMatch', async () => {
                echoOk();
                await openPage();

                inspect('Soru 4', 'dragPlace', 'C');

                const select = fixture.debugElement.query(By.css('.lb-interaction-select'));
                expect(select).not.toBeNull();
                expect(select.injector.get(MatSelect).value).toBe('dragPlace');

                inspect('Soru 2', 'matrixChoice', 'A');
                expect(fixture.debugElement.query(By.css('.lb-interaction-select'))).toBeNull();
            });
        });

        describe('etkinlik talimatı kaydı (prompt diyaloğu)', () => {
            function dialogClosesWith(result: unknown) {
                vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(result) } as never);
            }

            const saved = {
                kind: 'activity',
                interactionType: 'dragPlace',
                prompt: { version: 3, instruction: 'x', createdByName: 'Eğitmen', createdAt: '2026-10-27T00:00:00Z' },
            };

            it('activityPromptSaved_ActionReExtractsOnlyThatTypeWithoutForce', async () => {
                echoOk();
                await openPage();
                dialogClosesWith(saved);
                const action = new Subject<void>();
                const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);

                component.openPromptDialog();
                expect(snackBar).toHaveBeenCalledWith(expect.stringContaining('v3'), labelingTr.structure.promptAskPage, expect.any(Object));
                action.next();

                expect(structure).toHaveBeenCalledTimes(2);
                expect(structure.mock.calls[1][0]).toEqual(expect.objectContaining({ force: false }));
                expect(requestedKeys(1)).toEqual(['Soru 4']);
                expect(labelingApi.aiDetect).not.toHaveBeenCalled();
            });

            it('promptUnchanged_SnackbarWithoutReExtractOffer', async () => {
                echoOk();
                await openPage();
                dialogClosesWith({ kind: 'unchanged', version: 2 });
                const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => new Subject<void>() } as never);

                component.openPromptDialog();

                expect(snackBar).toHaveBeenCalledWith(expect.stringContaining('v2'), labelingTr.ok, expect.any(Object));
                expect(structure).toHaveBeenCalledTimes(1);
                expect(labelingApi.aiDetect).not.toHaveBeenCalled();
            });

            it('activityPromptSaved_NoBlockOfThatType_SnackbarWithoutAction', async () => {
                echoOk();
                await openPage([{ name: 'Soru 2', kind: 'activity', type: 'matrixChoice' }]);
                dialogClosesWith(saved);
                const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => new Subject<void>() } as never);

                component.openPromptDialog();

                expect(snackBar).toHaveBeenCalledWith(expect.any(String), labelingTr.ok, expect.any(Object));
            });
        });

        describe('cevap anahtarı ve çözüm (#580)', () => {
            const BOOK: LabelingBook = {
                id: 7, name: 'TT MatLab', canonicalBookId: null, canonicalBookName: null, notes: null,
                offsets: [{ fileFrom: 1, fileTo: 300, offset: -1 }], answerKeyRanges: [{ fileFrom: 200, fileTo: 210 }], toc: [],
            };
            const ACTIVITY_PAGE: LabelingBookPageInfo = { filePage: 40, printedPage: 39, isAnswerKey: false, toc: null };
            const KEY_PAGE: LabelingBookPageInfo = { filePage: 201, printedPage: 200, isAnswerKey: true, toc: null };

            function entry(id: number, overrides: Partial<LabelingAnswerKeyEntry> = {}): LabelingAnswerKeyEntry {
                return {
                    id, filePage: 201, printedPage: 39, activityLabel: 'Etkinlik 3', itemLabel: null, answerText: `cevap ${id}`,
                    visual: false, box: null, ...overrides,
                };
            }

            function solutionResponse(overrides: Partial<LabelingActivitySolutionResponse> = {}): LabelingActivitySolutionResponse {
                return {
                    draftSolutionId: 55, solution: { r1: ['c2'] }, source: 'Key', status: 'pending', promptVersion: 4,
                    origin: 'cached', valid: true, validationErrors: [], estimatedCostUsd: 0.002, error: null, ...overrides,
                };
            }

            const ACTIVITY: Block[] = [{ name: 'Soru 1', kind: 'activity', type: 'matrixChoice' }];

            /** Kitap seçili, `page_040.webp` (basılı 39) ya da anahtar sayfası; Claude tespiti + etkinlik taslağı hazır. */
            async function openBookActivity(blocks: Block[] = ACTIVITY, pageInfo: LabelingBookPageInfo = ACTIVITY_PAGE, file = 'page_040.webp') {
                localStorage.setItem('labeling.bookId', '7');
                booksResponse = of([BOOK]);
                await setup([], false, [model]);
                (labelingApi as unknown as { activityStructure: unknown }).activityStructure = structure;
                component.structureDebounceMs = 0;
                component.keyLookupDebounceMs = 0;
                booksApi.getPage.mockReturnValue(of(pageInfo));
                echoOk();
                await pickFolder([image(file)]);
                await applyDetection(blocks);
            }

            function inspect(name: string, interactionType: string) {
                selector().inspector = { index: 0, no: 1, name, blockKind: 'activity', activityComponent: 'A', interactionType, answers: [] };
                fixture.detectChanges();
            }

            async function setLabel(name: string, field: 'activityLabel' | 'itemLabel', value: string) {
                component.setQuestionLabel(name, field, { value } as unknown as EventTarget);
                await settle(fixture);
            }

            function el(selectorText: string): HTMLElement | null {
                return fixture.nativeElement.querySelector(selectorText);
            }

            describe('cevap anahtarı sayfası', () => {
                it('readButton_OnlyOnAnswerKeyPage', async () => {
                    await openBookActivity(ACTIVITY, ACTIVITY_PAGE);
                    expect(component.isAnswerKeyPage()).toBe(false);
                    expect(el('.lb-read-key')).toBeNull();
                    expect(el('.lb-answer-key')).toBeNull();
                });

                it('readButton_NoBookSelected_Hidden', async () => {
                    echoOk();
                    await openPage(ACTIVITY);
                    expect(el('.lb-read-key')).toBeNull();
                });

                it('read_SendsBookFilePageImageModel_ListsEntriesAndDrawsVisualBoxes', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    expect(el('.lb-read-key')).not.toBeNull();
                    expect(el('.lb-answer-key')?.textContent).toContain(labelingTr.answerKey.notRead);
                    labelingApi.readAnswerKey.mockReturnValue(of({
                        origin: 'cached', promptVersion: 2, estimatedCostUsd: 0.01,
                        entries: [entry(1), entry(2, { visual: true, answerText: null, itemLabel: 'b', box: { x: 5, y: 6, width: 70, height: 80 } })],
                    }));

                    (el('.lb-read-key') as HTMLButtonElement).click();
                    await settle(fixture);

                    const request = labelingApi.readAnswerKey.mock.calls[0][0];
                    expect(request).toEqual(expect.objectContaining({ bookId: 7, filePage: 201, model: 'claude-a', force: false }));
                    expect(request.imageBase64).toContain('data:image/webp;base64,');
                    const items = fixture.nativeElement.querySelectorAll('.lb-ak-entry') as NodeListOf<HTMLElement>;
                    expect(items.length).toBe(2);
                    expect(items[0].textContent).toContain('s. 39 · Etkinlik 3');
                    expect(items[0].textContent).toContain('cevap 1');
                    expect(items[1].textContent).toContain(labelingTr.answerKey.visual);
                    expect(el('.lb-answer-key__cost')?.textContent).toContain('ilk çağrı ~$0.0100');
                    expect(el('.lb-answer-key')?.textContent).toContain('prompt v2');
                    expect(selector().setAnswerKeyOverlays).toHaveBeenLastCalledWith([
                        { box: { x: 5, y: 6, width: 70, height: 80 }, tag: 's. 39 · Etkinlik 3 · b' },
                    ]);
                });

                it('read_NotAnswerKeyPage400_ShowsServerMessage', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(throwError(() => ({
                        status: 400, error: { message: 'Sayfa cevap anahtarı aralığında değil.', errorCode: 'LabelingNotAnswerKeyPage' },
                    })));
                    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

                    component.readAnswerKey();
                    fixture.detectChanges();

                    expect(snack).toHaveBeenCalledWith('Sayfa cevap anahtarı aralığında değil.', labelingTr.ok, expect.any(Object));
                    expect(el('.lb-answer-key__error')?.textContent).toContain('Sayfa cevap anahtarı aralığında değil.');
                    expect(component.answerKeyReading()).toBe(false);
                });

                it('reread_RequiresConfirmThenForceTrue', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(of({ origin: 'live', promptVersion: 1, estimatedCostUsd: 0.02, entries: [] }));
                    const action = new Subject<void>();
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);

                    component.readAnswerKey(true);
                    expect(labelingApi.readAnswerKey).not.toHaveBeenCalled();
                    action.next();

                    expect(labelingApi.readAnswerKey).toHaveBeenCalledWith(expect.objectContaining({ force: true }));
                });

                it('read_StaleStoredRows_ShowsStaleWarning', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(of({ origin: 'cached', promptVersion: 1, estimatedCostUsd: 0.01, entries: [entry(1)], stale: true }));

                    component.readAnswerKey();
                    fixture.detectChanges();

                    expect(el('.lb-answer-key__stale')?.textContent).toContain(labelingTr.answerKey.stale);
                    expect(component.answerKeyEntries().length).toBe(1);
                });

                it('reread409HasEdits_ConfirmSendsConfirmOverwrite_CancelSendsNothing', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    vi.spyOn(console, 'error').mockReturnValue(undefined);
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => of(undefined) } as never);
                    const dialogOpen = vi.spyOn(TestBed.inject(MatDialog), 'open');
                    labelingApi.readAnswerKey.mockReturnValueOnce(throwError(() => ({
                        status: 409, error: { errorCode: 'LabelingAnswerKeyHasEdits', message: 'Elle düzeltilmiş 2 satır silinecek.' },
                    })));
                    dialogOpen.mockReturnValueOnce({ afterClosed: () => of(false) } as never);

                    component.readAnswerKey(true);

                    expect(dialogOpen).toHaveBeenCalledWith(ConfirmDialogComponent, expect.objectContaining({
                        data: expect.objectContaining({ message: 'Elle düzeltilmiş 2 satır silinecek.' }),
                    }));
                    expect(labelingApi.readAnswerKey).toHaveBeenCalledTimes(1);

                    labelingApi.readAnswerKey.mockReturnValueOnce(throwError(() => ({ status: 409, error: { errorCode: 'LabelingAnswerKeyHasEdits' } })));
                    labelingApi.readAnswerKey.mockReturnValueOnce(of({ origin: 'live', promptVersion: 2, estimatedCostUsd: 0.02, entries: [] }));
                    dialogOpen.mockReturnValueOnce({ afterClosed: () => of(true) } as never);
                    component.readAnswerKey(true);

                    expect(labelingApi.readAnswerKey).toHaveBeenCalledTimes(3);
                    expect(labelingApi.readAnswerKey.mock.calls[1][0].confirmOverwrite).toBeUndefined();
                    expect(labelingApi.readAnswerKey.mock.calls[2][0]).toEqual(expect.objectContaining({ force: true, confirmOverwrite: true }));
                    expect(component.answerKeyError()).toBeNull();
                });

                it('modelChange_DoesNotReReadAnswerKey', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(of({ origin: 'cached', promptVersion: 1, estimatedCostUsd: 0, entries: [entry(1)] }));
                    component.readAnswerKey();
                    labelingApi.aiDetect.mockReturnValue(new Subject<never>().asObservable());

                    component.setAiModel({ value: 'claude-a', source: { value: 'claude-a' } } as unknown as MatSelectChange);
                    await settle(fixture);

                    expect(labelingApi.readAnswerKey).toHaveBeenCalledTimes(1);
                });

                it('staleRead_AfterPageChange_IsIgnored', async () => {
                    localStorage.setItem('labeling.bookId', '7');
                    booksResponse = of([BOOK]);
                    await setup([], false, [model]);
                    booksApi.getPage.mockImplementation((_id: number, filePage: number) => of({ ...KEY_PAGE, filePage }));
                    await pickFolder([image('page_201.webp'), image('page_202.webp')]);
                    const pending = new Subject<LabelingAnswerKeyReadResponse>();
                    labelingApi.readAnswerKey.mockReturnValue(pending.asObservable());
                    component.readAnswerKey();

                    component.skip();
                    await settle(fixture);
                    pending.next({ origin: 'live', promptVersion: 1, estimatedCostUsd: 0.02, entries: [entry(1)] });

                    expect(component.filePage()).toBe(202);
                    expect(component.answerKeyEntries()).toEqual([]);
                    expect(component.answerKeyMeta()).toBeNull();
                });

                it('editEntry_Save_PutsTrimmedFieldsAndUpdatesRow', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(of({ origin: 'cached', promptVersion: 1, estimatedCostUsd: 0, entries: [entry(1)] }));
                    component.readAnswerKey();
                    labelingApi.updateAnswerKeyEntry.mockImplementation((id, body) => of(entry(id, { ...body })));

                    component.editEntry(component.answerKeyEntries()[0]);
                    component.setEntryField('printedPage', { value: ' 41 ' } as unknown as EventTarget);
                    component.setEntryField('itemLabel', { value: ' c ' } as unknown as EventTarget);
                    component.setEntryField('answerText', { value: '   ' } as unknown as EventTarget);
                    component.saveEntry(component.answerKeyEntries()[0]);
                    fixture.detectChanges();

                    expect(labelingApi.updateAnswerKeyEntry).toHaveBeenCalledWith(1, {
                        printedPage: 41, activityLabel: 'Etkinlik 3', itemLabel: 'c', answerText: null,
                    });
                    expect(component.editingEntryId()).toBeNull();
                    expect(component.answerKeyEntries()[0]).toEqual(expect.objectContaining({ printedPage: 41, itemLabel: 'c', answerText: null }));
                    expect(el('.lb-ak-entry')?.textContent).toContain('s. 41 · Etkinlik 3 · c');
                });

                it('editEntry_InvalidPrintedPage_NotSent', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(of({ origin: 'cached', promptVersion: 1, estimatedCostUsd: 0, entries: [entry(1)] }));
                    component.readAnswerKey();
                    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

                    component.editEntry(component.answerKeyEntries()[0]);
                    component.setEntryField('printedPage', { value: '3.5' } as unknown as EventTarget);
                    component.saveEntry(component.answerKeyEntries()[0]);

                    expect(labelingApi.updateAnswerKeyEntry).not.toHaveBeenCalled();
                    expect(snack).toHaveBeenCalledWith(labelingTr.answerKey.invalidPage, labelingTr.ok, expect.any(Object));
                });

                it('deleteEntry_RemovesRow_FailureKeepsItAndShowsMessage', async () => {
                    await openBookActivity([], KEY_PAGE, 'page_201.webp');
                    labelingApi.readAnswerKey.mockReturnValue(of({
                        origin: 'cached', promptVersion: 1, estimatedCostUsd: 0,
                        entries: [entry(1), entry(2, { visual: true, box: { x: 1, y: 2, width: 3, height: 4 } })],
                    }));
                    component.readAnswerKey();
                    TestBed.tick();
                    expect(selector().setAnswerKeyOverlays).toHaveBeenLastCalledWith([
                        { box: { x: 1, y: 2, width: 3, height: 4 }, tag: 's. 39 · Etkinlik 3' },
                    ]);
                    labelingApi.deleteAnswerKeyEntry.mockReturnValueOnce(of(undefined));

                    component.deleteEntry(component.answerKeyEntries()[1]);
                    TestBed.tick();

                    expect(labelingApi.deleteAnswerKeyEntry).toHaveBeenCalledWith(2);
                    expect(component.answerKeyEntries().map((e) => e.id)).toEqual([1]);
                    expect(selector().setAnswerKeyOverlays).toHaveBeenLastCalledWith([]);

                    vi.spyOn(console, 'error').mockReturnValue(undefined);
                    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
                    labelingApi.deleteAnswerKeyEntry.mockReturnValueOnce(throwError(() => ({ status: 404, error: { message: 'Madde bulunamadı.' } })));
                    component.deleteEntry(component.answerKeyEntries()[0]);

                    expect(component.answerKeyEntries().map((e) => e.id)).toEqual([1]);
                    expect(snack).toHaveBeenCalledWith('Madde bulunamadı.', labelingTr.ok, expect.any(Object));
                    expect(component.entryBusyId()).toBeNull();
                });
            });

            describe('etkinlik çözümü', () => {
                it('labelsShownForActivityBlocks_NoLabel_ShowsNeedLabel_AiButton', async () => {
                    await openBookActivity();
                    inspect('Soru 1', 'matrixChoice');

                    expect(el('.lb-labels .lb-activity-label')).not.toBeNull();
                    expect(el('.lb-solution')?.textContent).toContain(labelingTr.solution.needLabel);
                    expect(el('.lb-solution__ai')).not.toBeNull();
                    expect(el('.lb-solution__from-key')).toBeNull();
                    expect(labelingApi.getAnswerKey).not.toHaveBeenCalled();
                });

                it('activityLabel_LooksUpKeyWithPrintedPage_Found_SolveFromKeySendsMatchingEntryIds', async () => {
                    await openBookActivity();
                    labelingApi.getAnswerKey.mockReturnValue(of({ entries: [entry(11, { itemLabel: 'a' }), entry(12, { itemLabel: 'B' })] }));
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));

                    await setLabel('Soru 1', 'activityLabel', 'Etkinlik 3');
                    inspect('Soru 1', 'matrixChoice');

                    expect(labelingApi.getAnswerKey).toHaveBeenCalledWith(7, { printedPage: 39, activityLabel: 'Etkinlik 3', filePage: 40 });
                    expect(el('.lb-solution')?.textContent).toContain('2 satır bulundu');
                    expect(el('.lb-solution__ai')).toBeNull();

                    // Madde etiketi yalnız istemcide süzer (büyük/küçük harf duyarsız), yeniden sorulmaz.
                    await setLabel('Soru 1', 'itemLabel', ' b ');
                    (el('.lb-solution__from-key') as HTMLButtonElement).click();

                    expect(labelingApi.getAnswerKey).toHaveBeenCalledTimes(1);
                    const request = labelingApi.solveActivity.mock.calls[0][0];
                    expect(request).toEqual(expect.objectContaining({
                        mode: 'key', key: { entryIds: [12] }, force: false, model: 'claude-a', interactionType: 'matrixChoice',
                        x: 10, y: 0, width: 200, height: 81,
                    }));
                    expect(request.plan).toEqual({ version: 1, type: 'matrixChoice', plan: 'Soru 1' });
                });

                it('keyNotFound_AiButton_SolveSendsModeAiWithoutKey', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse({ source: 'AI' })));
                    await setLabel('Soru 1', 'activityLabel', 'Etkinlik 9');
                    inspect('Soru 1', 'matrixChoice');

                    expect(el('.lb-solution')?.textContent).toContain(labelingTr.solution.keyNone);
                    (el('.lb-solution__ai') as HTMLButtonElement).click();

                    const request = labelingApi.solveActivity.mock.calls[0][0];
                    expect(request.mode).toBe('ai');
                    expect(request.key).toBeUndefined();
                });

                it('noBook_AiOnly_NoLookup', async () => {
                    echoOk();
                    await openPage(ACTIVITY);
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse({ source: 'AI' })));
                    await setLabel('Soru 1', 'activityLabel', 'Etkinlik 3');
                    inspect('Soru 1', 'matrixChoice');

                    expect(labelingApi.getAnswerKey).not.toHaveBeenCalled();
                    expect(el('.lb-solution')?.textContent).toContain(labelingTr.solution.noBook);
                    component.solve('Soru 1', 'ai');
                    expect(labelingApi.solveActivity).toHaveBeenCalledWith(expect.objectContaining({ mode: 'ai' }));
                });

                it('solutionAppliedPerType_IntoAuthoringStateOfThatRegion', async () => {
                    const blocks: Block[] = [
                        { name: 'Soru 1', kind: 'activity', type: 'matrixChoice' },
                        { name: 'Soru 2', kind: 'activity', type: 'slotFill' },
                        { name: 'Soru 3', kind: 'activity', type: 'dragPlace' },
                        { name: 'Soru 4', kind: 'activity', type: 'match' },
                    ];
                    const solutions: Record<string, Record<string, unknown>> = {
                        'Soru 1': { r1: ['c2'] },
                        'Soru 2': { s1: { value: '3/4' } },
                        'Soru 3': { t1: ['c1'] },
                        'Soru 4': { l1: 'r3' },
                    };
                    await openBookActivity(blocks);
                    labelingApi.solveActivity.mockImplementation((request) => {
                        const name = blocks.find((b) => b.type === request.interactionType)!.name;
                        return of(solutionResponse({ source: 'AI', solution: solutions[name] }));
                    });

                    for (const b of blocks) {
                        component.solve(b.name, 'ai');
                    }

                    const stub = selector();
                    for (const b of blocks) {
                        expect(stub.applyInteractionSolution).toHaveBeenCalledWith(b.name, b.type, solutions[b.name]);
                        expect(component.solutionOf(b.name)).toEqual(expect.objectContaining({ status: 'ok', apply: 'applied' }));
                    }
                });

                it('okSolution_ShowsSourcePendingPromptCostAndValidationErrors', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse({
                        source: 'AI', origin: 'live', estimatedCostUsd: 0.0031, valid: false, validationErrors: ['Satır 2 boş'],
                    })));
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');

                    const text = el('.lb-solution')!.textContent!;
                    expect(el('.lb-solution__source')?.textContent).toContain(labelingTr.solution.sources.AI);
                    expect(el('.lb-solution__status--pending')?.textContent).toContain(labelingTr.solution.pending);
                    expect(el('.lb-solution__prompt')?.textContent).toContain('prompt v4');
                    expect(el('.lb-solution__cost')?.textContent).toContain('~$0.0031');
                    expect(text).toContain('Satır 2 boş');
                    // Doğrulamadan geçmeyen çözüm düzeltilmeden onaylanmaz.
                    expect((el('.lb-solution__approve') as HTMLButtonElement).disabled).toBe(true);
                });

                it('keptSolution_ApplyConfirmed_ReplacesWithReplaceOption', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    selector().applyInteractionSolution.mockReturnValueOnce('kept');
                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');
                    expect(el('.lb-solution__kept')).not.toBeNull();
                    const action = new Subject<void>();
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);

                    (el('.lb-solution__apply') as HTMLButtonElement).click();
                    action.next();

                    expect(selector().applyInteractionSolution).toHaveBeenLastCalledWith('Soru 1', 'matrixChoice', { r1: ['c2'] }, { replace: true });
                    expect(component.solutionOf('Soru 1')?.apply).toBe('applied');
                });

                it('approveButton_HiddenWithoutTrainerOrAdminRealmRole', async () => {
                    realmRoles = ['Teacher'];
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');

                    expect(component.canApproveSolutions).toBe(false);
                    expect(el('.lb-solution__status--pending')).not.toBeNull();
                    expect(el('.lb-solution__approve')).toBeNull();
                    component.approveSolution('Soru 1');
                    expect(labelingApi.approveActivitySolution).not.toHaveBeenCalled();
                });

                it('approve_AdminRole_CallsApproveAndShowsApproverAndTime', async () => {
                    realmRoles = ['Admin'];
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    labelingApi.approveActivitySolution.mockReturnValue(of(solutionResponse({
                        status: 'approved', origin: null, approvedByName: 'Eğitmen Ayşe', approvedAt: '2026-10-27T09:30:00Z',
                    })));
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');

                    (el('.lb-solution__approve') as HTMLButtonElement).click();
                    fixture.detectChanges();

                    expect(labelingApi.approveActivitySolution).toHaveBeenCalledWith(55);
                    expect(el('.lb-solution__status--approved')?.textContent).toContain(labelingTr.solution.approved);
                    expect(el('.lb-solution__approver')?.textContent).toContain('Eğitmen Ayşe');
                    expect(el('.lb-solution__approve')).toBeNull();
                });

                it('approve_Forbidden403_ShowsForbiddenMessage', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    labelingApi.approveActivitySolution.mockReturnValue(throwError(() => ({ status: 403 })));
                    vi.spyOn(console, 'error').mockReturnValue(undefined);
                    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
                    component.solve('Soru 1', 'ai');

                    component.approveSolution('Soru 1');

                    expect(snack).toHaveBeenCalledWith(labelingTr.solution.forbidden, labelingTr.ok, expect.any(Object));
                    expect(component.solutionOf('Soru 1')?.busy).toBeNull();
                    expect(component.solutionOf('Soru 1')?.response?.status).toBe('pending');
                });

                it('canvasEdit_SaveEditPutsWithoutApprove_ApproveAfterEditPutsWithApprove', async () => {
                    await openBookActivity();
                    const stub = selector();
                    stub.canvasSolutions['Soru 1'] = { r1: ['c2'] };
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    labelingApi.updateActivitySolution.mockImplementation((_id, solution, approve) =>
                        of(solutionResponse({ solution, source: approve ? 'Human' : 'Key', status: approve ? 'approved' : 'pending', origin: null })));
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');
                    expect(component.solutionEdited('Soru 1')).toBe(false);

                    stub.canvasSolutions['Soru 1'] = { r1: ['c1'] };
                    fixture.detectChanges();
                    expect(el('.lb-solution__edited')).not.toBeNull();
                    (el('.lb-solution__save-edit') as HTMLButtonElement).click();

                    expect(labelingApi.updateActivitySolution).toHaveBeenLastCalledWith(55, { r1: ['c1'] }, false);
                    expect(component.solutionEdited('Soru 1')).toBe(false);

                    stub.canvasSolutions['Soru 1'] = { r1: ['c3'] };
                    component.approveSolution('Soru 1');
                    fixture.detectChanges();

                    expect(labelingApi.updateActivitySolution).toHaveBeenLastCalledWith(55, { r1: ['c3'] }, true);
                    expect(labelingApi.approveActivitySolution).not.toHaveBeenCalled();
                    expect(el('.lb-solution__source')?.textContent).toContain(labelingTr.solution.sources.Human);
                    expect(el('.lb-solution__status--approved')).not.toBeNull();
                });

                it('staleSolution_AfterPageChange_IsIgnored', async () => {
                    localStorage.setItem('labeling.bookId', '7');
                    booksResponse = of([BOOK]);
                    await setup([], false, [model]);
                    (labelingApi as unknown as { activityStructure: unknown }).activityStructure = structure;
                    component.structureDebounceMs = 0;
                    booksApi.getPage.mockReturnValue(of(ACTIVITY_PAGE));
                    echoOk();
                    await pickFolder([image('page_040.webp'), image('page_041.webp')]);
                    await applyDetection(ACTIVITY);
                    const pending = new Subject<LabelingActivitySolutionResponse>();
                    labelingApi.solveActivity.mockReturnValue(pending.asObservable());
                    component.solve('Soru 1', 'ai');
                    expect(component.solutionOf('Soru 1')?.status).toBe('loading');

                    component.skip();
                    await settle(fixture);
                    await applyDetection(ACTIVITY);
                    pending.next(solutionResponse());

                    expect(selector().applyInteractionSolution).not.toHaveBeenCalled();
                    expect(component.solutionOf('Soru 1')).toBeNull();
                });

                it('staleSolution_StructureReExtracted_SolutionDroppedAndLateResponseIgnored', async () => {
                    await openBookActivity();
                    const pending = new Subject<LabelingActivitySolutionResponse>();
                    labelingApi.solveActivity.mockReturnValue(pending.asObservable());
                    component.solve('Soru 1', 'ai');

                    const action = new Subject<void>();
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);
                    component.reExtractStructure('Soru 1');
                    action.next();
                    await settle(fixture);
                    pending.next(solutionResponse());

                    expect(component.solutionOf('Soru 1')).toBeNull();
                    expect(selector().applyInteractionSolution).not.toHaveBeenCalled();
                });

                it('rateLimited429_ShowsServerMessageAndMarksError', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(throwError(() => ({
                        status: 429, error: { message: 'Günlük bütçe doldu.', errorCode: 'LabelingBudgetExceeded' },
                    })));
                    vi.spyOn(console, 'error').mockReturnValue(undefined);
                    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');

                    expect(snack).toHaveBeenCalledWith('Günlük bütçe doldu.', labelingTr.ok, expect.any(Object));
                    expect(el('.lb-solution__error')?.textContent).toContain('Günlük bütçe doldu.');
                });

                it('claudeError200_ShowsErrorNothingApplied', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse({ draftSolutionId: null, solution: null, error: 'Claude çözemedi.' })));

                    component.solve('Soru 1', 'ai');

                    expect(component.solutionOf('Soru 1')).toEqual(expect.objectContaining({ status: 'error', error: 'Claude çözemedi.' }));
                    expect(selector().applyInteractionSolution).not.toHaveBeenCalled();
                });

                it('resolve_RequiresConfirmThenForceTrueSameMode', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse({ source: 'AI' })));
                    component.solve('Soru 1', 'ai');
                    const action = new Subject<void>();
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);

                    component.resolve('Soru 1');
                    expect(labelingApi.solveActivity).toHaveBeenCalledTimes(1);
                    action.next();

                    expect(labelingApi.solveActivity).toHaveBeenCalledTimes(2);
                    expect(labelingApi.solveActivity.mock.calls[1][0]).toEqual(expect.objectContaining({ mode: 'ai', force: true }));
                });

                it('reopen_RestoresStoredSolutionByCropPreferringSamePlan', async () => {
                    localStorage.setItem('labeling.bookId', '7');
                    booksResponse = of([BOOK]);
                    await setup([], false, [model]);
                    (labelingApi as unknown as { activityStructure: unknown }).activityStructure = structure;
                    structure.mockImplementation((request) =>
                        of({ results: request.blocks.map((b) => result(b.key, b.interactionType, { cropSha256: 'abc123' })) }));
                    booksApi.getPage.mockReturnValue(of(ACTIVITY_PAGE));
                    labelingApi.getActivitySolutions.mockReturnValue(of({
                        solutions: [
                            solutionResponse({ draftSolutionId: 1, interactionType: 'slotFill', solution: { s1: { value: '1' } } }),
                            solutionResponse({ draftSolutionId: 2, interactionType: 'matrixChoice', plan: { other: true }, solution: { r1: ['c9'] } }),
                            solutionResponse({
                                draftSolutionId: 3, interactionType: 'matrixChoice', status: 'approved', approvedByName: 'Ayşe',
                                plan: { type: 'matrixChoice', plan: 'Soru 1', version: 1 }, solution: { r1: ['c1'] },
                            }),
                        ],
                    }));
                    await pickFolder([image('page_040.webp')]);
                    await applyDetection(ACTIVITY);
                    inspect('Soru 1', 'matrixChoice');

                    expect(labelingApi.getActivitySolutions).toHaveBeenCalledWith('abc123');
                    expect(selector().applyInteractionSolution).toHaveBeenCalledWith('Soru 1', 'matrixChoice', { r1: ['c1'] });
                    expect(component.solutionOf('Soru 1')?.response?.draftSolutionId).toBe(3);
                    expect(el('.lb-solution__status--approved')).not.toBeNull();
                    expect(labelingApi.solveActivity).not.toHaveBeenCalled();
                });

                it('reopen_StoredSolutionsOnlyForOtherPlans_PlanChangedStateNothingApplied', async () => {
                    localStorage.setItem('labeling.bookId', '7');
                    booksResponse = of([BOOK]);
                    await setup([], false, [model]);
                    (labelingApi as unknown as { activityStructure: unknown }).activityStructure = structure;
                    structure.mockImplementation((request) =>
                        of({ results: request.blocks.map((b) => result(b.key, b.interactionType, { cropSha256: 'abc123' })) }));
                    booksApi.getPage.mockReturnValue(of(ACTIVITY_PAGE));
                    labelingApi.getActivitySolutions.mockReturnValue(of({
                        solutions: [
                            solutionResponse({ draftSolutionId: 2, interactionType: 'matrixChoice', plan: { other: true }, status: 'approved' }),
                            solutionResponse({ draftSolutionId: 4, interactionType: 'matrixChoice', plan: null }),
                        ],
                    }));
                    await pickFolder([image('page_040.webp')]);
                    await applyDetection(ACTIVITY);
                    inspect('Soru 1', 'matrixChoice');

                    expect(selector().applyInteractionSolution).not.toHaveBeenCalled();
                    expect(component.solutionOf('Soru 1')?.status).toBe('planChanged');
                    expect(el('.lb-solution__plan-changed')?.textContent).toContain('Plan değişti, yeniden çöz');
                    expect(el('.lb-solution__status')).toBeNull();
                    expect(el('.lb-solution__approve')).toBeNull();
                    // Yeniden çözülebilir.
                    expect(component.canSolve('Soru 1')).toBe(true);
                });

                it('canvasPlanChangedAfterSolve_SolutionStale_ApproveAndSaveDisabled', async () => {
                    await openBookActivity();
                    const stub = selector();
                    stub.canvasSolutions['Soru 1'] = { r1: ['c2'] };
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    component.solve('Soru 1', 'ai');
                    inspect('Soru 1', 'matrixChoice');
                    expect(component.solutionPlanStale('Soru 1')).toBe(false);
                    expect(component.canApproveSolution('Soru 1')).toBe(true);

                    stub.canvasPlans['Soru 1'] = { version: 1, type: 'matrixChoice', rows: ['yeni satır'] };
                    stub.canvasSolutions['Soru 1'] = { r1: ['c1'] };
                    fixture.detectChanges();

                    expect(component.solutionPlanStale('Soru 1')).toBe(true);
                    expect(el('.lb-solution__plan-stale')?.textContent).toContain('Plan değişti');
                    expect((el('.lb-solution__approve') as HTMLButtonElement).disabled).toBe(true);
                    expect(el('.lb-solution__save-edit')).toBeNull();
                    component.approveSolution('Soru 1');
                    component.saveSolutionEdit('Soru 1');
                    expect(labelingApi.approveActivitySolution).not.toHaveBeenCalled();
                    expect(labelingApi.updateActivitySolution).not.toHaveBeenCalled();
                });

                it('appliedSolution_LinksDraftSolutionIdOnCanvas_ResolveUnlinksFirst', async () => {
                    await openBookActivity();
                    labelingApi.solveActivity.mockReturnValue(of(solutionResponse()));
                    component.solve('Soru 1', 'ai');

                    expect(selector().setDraftSolution).toHaveBeenLastCalledWith('Soru 1', 55);

                    const pending = new Subject<LabelingActivitySolutionResponse>();
                    labelingApi.solveActivity.mockReturnValue(pending.asObservable());
                    const action = new Subject<void>();
                    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => action.asObservable() } as never);
                    component.resolve('Soru 1');
                    action.next();
                    expect(selector().setDraftSolution).toHaveBeenLastCalledWith('Soru 1', null);

                    // Tuvalde korunan elle yazarlık: çözüm uygulanmadı, bağ yok.
                    selector().applyInteractionSolution.mockReturnValueOnce('kept');
                    pending.next(solutionResponse({ draftSolutionId: 77 }));
                    expect(selector().setDraftSolution).toHaveBeenLastCalledWith('Soru 1', null);
                });

                it('itemLabelMatching_IgnoresPunctuation', async () => {
                    await openBookActivity();
                    labelingApi.getAnswerKey.mockReturnValue(of({ entries: [entry(11, { itemLabel: 'a)' }), entry(12, { itemLabel: 'B.' }), entry(13, { itemLabel: '1.' })] }));
                    await setLabel('Soru 1', 'activityLabel', 'Etkinlik 3');

                    await setLabel('Soru 1', 'itemLabel', 'b)');
                    expect(component.matchingKeyEntries('Soru 1').map((e) => e.id)).toEqual([12]);
                    await setLabel('Soru 1', 'itemLabel', '1');
                    expect(component.matchingKeyEntries('Soru 1').map((e) => e.id)).toEqual([13]);
                    expect(normalizeLabel(' (A) ')).toBe('a');
                    expect(normalizeLabel('Etkinlik  3.')).toBe('etkinlik 3');
                });

                it('solutionPromptSaved_SnackbarNamesKindWithoutReExtract', async () => {
                    echoOk();
                    await openPage(ACTIVITY);
                    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
                        afterClosed: () => of({
                            kind: 'activity', interactionType: 'solveAi',
                            prompt: { version: 5, instruction: 'x', createdByName: null, createdAt: null },
                        }),
                    } as never);
                    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({ onAction: () => new Subject<void>() } as never);

                    component.openPromptDialog();

                    expect(snack).toHaveBeenCalledWith(expect.stringContaining('AI çözüm talimatı v5'), labelingTr.ok, expect.any(Object));
                    expect(structure).toHaveBeenCalledTimes(1);
                });
            });
        });
    });
});
