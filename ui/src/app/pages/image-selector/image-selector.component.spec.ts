import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject, throwError } from 'rxjs';
import { MatSnackBar } from '@angular/material/snack-bar';
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
            providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideNoopAnimations()],
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
            const spy = vi.fn().mockName('selectedQuestionChange');
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
            vi.spyOn((component as any).questionService, 'getAll').mockReturnValue(of([]));
            vi.spyOn((component as any).testService, 'convertQuestionsToRegions').mockReturnValue([]);
            const files = [new File(['x'], 'p1.png', { type: 'image/png' })];
            component.imageFiles = files;

            component.togglePreviewMode(7);

            expect(component.previewMode()).toBe(true);
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

        let predictSpy: Mock;

        beforeEach(() => {
            predictSpy = vi.spyOn(component.questionDetectorService, 'predict').mockReturnValue(of({ predictions: [] }) as any);
            vi.spyOn(component.questionDetectorService, 'readQrData').mockReturnValue(of({}) as any);
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
            expect(component.coordinateJsonMode()).toBe(true);
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
            expect(component.coordinateJsonMode()).toBe(true);

            component.handleFilesInput2(fileEvent([png('page_1.png')]));
            await waitFor(() => vi.mocked(predictSpy).mock.calls.length > 0);

            expect(component.coordinateJsonMode()).toBe(false);
            expect(predictSpy).toHaveBeenCalledTimes(1);
        });

        it('handleFilesInput2_UnusableJson_FallsBackToDetector', async () => {
            const broken = new File(['{oops'], 'question-coordinates.json', { type: 'application/json' });

            component.handleFilesInput2(fileEvent([png('page_1.png'), broken]));
            await waitFor(() => vi.mocked(predictSpy).mock.calls.length > 0);

            expect(component.coordinateJsonMode()).toBe(false);
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
            vi.spyOn(slowJson, 'text').mockReturnValue(new Promise<string>((resolve) => (resolveText = resolve)));

            component.handleFilesInput2(fileEvent([png('page_1.png'), slowJson]));
            component.handleFilesInput2(fileEvent([png('page_9.png')])); // daha yeni seçim, JSON'suz
            resolveText(JSON.stringify([jsonEntry('page_1.png', '1')]));
            await waitFor(() => vi.mocked(predictSpy).mock.calls.length > 0);
            await new Promise((resolve) => setTimeout(resolve, 50));

            expect(component.coordinateJsonMode()).toBe(false);
            expect(component.imageFiles.map((f) => f.name)).toEqual(['page_9.png']);
        });

        it('handleFilesInput2_JsonModeWithAnswerLock_KeepsWholePageRegionAndSkipsJson', async () => {
            component.selectionModeLocked.set('answer');

            component.handleFilesInput2(fileEvent([png('page_1.png'), coordinatesJson([jsonEntry('page_1.png', '1')])]));
            await waitFor(() => component.regions().length > 0);

            expect(component.coordinateJsonMode()).toBe(true);
            expect(predictSpy).not.toHaveBeenCalled();
            expect(component.regions().length).toBe(1);
            expect(component.regions()[0].name).toBe('Soru');
            expect(component.regions()[0].classification).toBeUndefined();
        });

        it('handleFilesInput2_JsonLargerThanCap_FallsBackToNormalModeWithoutReading', async () => {
            const big = coordinatesJson([jsonEntry('page_1.png', '1')]);
            Object.defineProperty(big, 'size', { value: COORDINATE_JSON_MAX_BYTES + 1 });
            const textSpy = vi.spyOn(big, 'text');

            component.handleFilesInput2(fileEvent([png('page_1.png'), big]));
            await waitFor(() => vi.mocked(predictSpy).mock.calls.length > 0);

            expect(textSpy).not.toHaveBeenCalled();
            expect(component.coordinateJsonMode()).toBe(false);
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
            return vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: false, message: 'x' }));
        }

        it('sendToFix_DefaultArg_PostsPageCompleteTrue', () => {
            const sendtoFix = arrangeSendToFix();

            component.sendToFix();

            expect(sendtoFix).toHaveBeenCalledTimes(1);
            expect(vi.mocked(sendtoFix).mock.lastCall![0].pageComplete).toBe(true);
        });

        it('sendToFix_PageCompleteFalse_PostsPageCompleteFalse', () => {
            const sendtoFix = arrangeSendToFix();

            component.sendToFix(false);

            expect(vi.mocked(sendtoFix).mock.lastCall![0].pageComplete).toBe(false);
        });

        it('sendToFix_AdvanceFalse_SuccessDoesNotCallNextImage', () => {
            arrangeSendToFix().mockReturnValue(of({ success: true, added: 1, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);

            component.sendToFix(true, false);

            expect(nextImage).not.toHaveBeenCalled();
        });

        it('sendToFix_DefaultAdvance_SuccessCallsNextImage', () => {
            arrangeSendToFix().mockReturnValue(of({ success: true, added: 1, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);

            component.sendToFix();

            expect(nextImage).toHaveBeenCalledTimes(1);
        });

        it('sendToFix_Success_EmitsTrainedWithPageId', () => {
            arrangeSendToFix().mockReturnValue(of({ success: true, added: 1, imageFile: 'abcdef0123456789' }));
            const emitted: unknown[] = [];
            component.trained.subscribe((e) => emitted.push(e));

            component.sendToFix();

            expect(emitted).toEqual([{ pageId: 'abcdef0123456789', emptyPage: false, added: 1 }]);
        });

        it('sendToFix_Failure_DoesNotEmitTrained', () => {
            arrangeSendToFix();
            const emitted: unknown[] = [];
            component.trained.subscribe((e) => emitted.push(e));

            component.sendToFix();

            expect(emitted).toEqual([]);
        });

        it('sendToFix_AdvanceOnTrainFalse_SuccessEmitsButDoesNotCallNextImage', () => {
            arrangeSendToFix().mockReturnValue(of({ success: true, added: 1, imageFile: 'abcdef0123456789' }));
            fixture.componentRef.setInput('advanceOnTrain', false);
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            const emitted: unknown[] = [];
            component.trained.subscribe((e) => emitted.push(e));

            component.sendToFix();

            expect(nextImage).not.toHaveBeenCalled();
            expect(emitted.length).toBe(1);
        });
    });

    describe('loadFile', () => {
        it('loadFile_ReplacesPagesWithSingleFileAndLoadsIt', () => {
            component.imageFiles = [new File(['a'], 'old.png', { type: 'image/png' })];
            component.currentImageIndex = 0;
            component.coordinateJsonMode.set(true);
            const loadCurrentImage = vi.spyOn(component, 'loadCurrentImage').mockReturnValue(undefined);
            const file = new File(['b'], 'p7.webp', { type: 'image/webp' });

            component.loadFile(file);

            expect(component.imageFiles).toEqual([file]);
            expect(component.currentImageIndex).toBe(0);
            expect(component.coordinateJsonMode()).toBe(false);
            expect(loadCurrentImage).toHaveBeenCalledTimes(1);
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
            expect(component.ensureQuestionsValid({ requireCorrectAnswer: true })).toBe(true);
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
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            const select = vi.spyOn(component, 'selectQuestion').mockReturnValue(undefined);

            const ok = component.ensureQuestionsValid({ requireCorrectAnswer: false });

            expect(ok).toBe(false);
            const msg = vi.mocked(open).mock.lastCall![0] as string;
            expect(msg).toContain('1. soru: Şık sayısı 4 olmalı');
            expect(msg).toContain('3. soru');
            expect(msg).not.toContain('4. soru');
            expect(msg).toContain('ve 2 soru daha');
            expect(select).toHaveBeenCalledTimes(1);
            expect(select).toHaveBeenCalledWith(0);
        });
    });

    describe('sendToFix validation', () => {
        beforeEach(() => component.imageData.set('data:image/png;base64,AAAA'));

        it('sendToFix_AnswerCountMismatch_DoesNotPostOrAdvance', () => {
            component.answerCount.set(4);
            component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 1, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

            component.sendToFix();

            expect(sendtoFix).not.toHaveBeenCalled();
            expect(nextImage).not.toHaveBeenCalled();
            expect(vi.mocked(open).mock.lastCall![0]).toContain('1. soru: Şık sayısı 4 olmalı');
        });

        it('sendToFix_ValidWithoutCorrectAnswer_PostsAndAdvances', () => {
            component.answerCount.set(2);
            component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 1, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);

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
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: false, message: 'x' }));

            component.sendToFix();

            expect(sendtoFix).toHaveBeenCalledTimes(1);
            expect(vi.mocked(sendtoFix).mock.lastCall![0].questions.length).toBe(1);
        });

        it('sendToFix_NoRegions_PostsEmptyQuestionsAndAdvances', () => {
            component.answerCount.set(4);
            component.regions.set([]);
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 0, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

            component.sendToFix();

            expect(sendtoFix).toHaveBeenCalledTimes(1);
            const payload = vi.mocked(sendtoFix).mock.lastCall![0];
            expect(payload.questions).toEqual([]);
            expect(payload.pageComplete).toBe(true);
            expect(payload.answerCount).toBe(4);
            expect(nextImage).toHaveBeenCalledTimes(1);
            expect(vi.mocked(open).mock.lastCall![0]).toBe('Boş sayfa eğitime eklendi.');
        });

        it('sendToFix_NoImageNoRegions_DoesNotPostOrAdvance', () => {
            component.imageData.set(null);
            component.regions.set([]);
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 0, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

            component.sendToFix();

            expect(sendtoFix).not.toHaveBeenCalled();
            expect(nextImage).not.toHaveBeenCalled();
            expect(vi.mocked(open).mock.lastCall![0]).toBe('Önce bir sayfa yükleyin.');
        });

        it('sendToFix_NoRegionsPageCompleteFalse_DoesNotPostOrAdvance', () => {
            component.regions.set([]);
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 0, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

            component.sendToFix(false);

            expect(sendtoFix).not.toHaveBeenCalled();
            expect(nextImage).not.toHaveBeenCalled();
            expect(open).toHaveBeenCalledTimes(1);
        });

        it('sendToFix_OnlyDragDropQuestions_DoesNotPostAndShowsMessage', () => {
            component.regions.set([region('Soru 1', [])] as any);
            component.interactionTypes.set('Soru 1', 'dragDropLabeling');
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 0, imageFile: 'p.png' }));
            const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

            component.sendToFix();

            expect(sendtoFix).not.toHaveBeenCalled();
            expect(nextImage).not.toHaveBeenCalled();
            expect(vi.mocked(open).mock.lastCall![0]).toContain('sürükle-bırak');
        });

        it('sendToFix_OnlyDragDropQuestionsAdvanceFalse_DoesNotPostOrShowMessage', () => {
            component.regions.set([region('Soru 1', [])] as any);
            component.interactionTypes.set('Soru 1', 'dragDropLabeling');
            const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 0, imageFile: 'p.png' }));
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

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

        // issue #570: doğru yerleşimler plandan ayrı alanda gider; plan öğrenciye çözüm sızdırmamalı.
        it('getRegions_DragDropQuestion_SendsSolutionSeparatelyAndPlanWithoutSolution', () => {
            component.regions.set([region('Soru 1', [])] as any);
            component.interactionTypes.set('Soru 1', 'dragDropLabeling');
            (component as any).addDropZone('Soru 1', 10, 10, 50, 20);
            (component as any).addDropZone('Soru 1', 70, 10, 50, 20);
            component.setLabelCount('Soru 1', '2');
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.generateDraggables('Soru 1');
            component.setZoneSolution('Soru 1', 'z1', 'd2');
            component.setZoneSolution('Soru 1', 'z2', 'd1');

            const q = component.getRegions(null).questions[0];

            const plan = JSON.parse(q.interactionPlan!);
            expect(plan.solution).toBeUndefined();
            expect(plan.dropZones.map((z: { id: string }) => z.id)).toEqual(['z1', 'z2']);
            expect(plan.draggables.map((d: { id: string }) => d.id)).toEqual(['d1', 'd2']);
            expect(JSON.parse(q.interactionSolution!)).toEqual({
                placements: [
                    { dropZoneId: 'z1', draggableId: 'd2' },
                    { dropZoneId: 'z2', draggableId: 'd1' },
                ],
            });
        });

        it('getRegions_DragDropWithoutPlacements_SendsEmptyPlacements', () => {
            component.regions.set([region('Soru 1', [])] as any);
            component.interactionTypes.set('Soru 1', 'dragDropLabeling');

            const q = component.getRegions(null).questions[0];

            expect(JSON.parse(q.interactionSolution!)).toEqual({ placements: [] });
        });

        it('getRegions_McqQuestion_SendsNullPlanAndSolution', () => {
            component.regions.set([region('Soru 1', [answer(0, 0)])] as any);

            const q = component.getRegions(null).questions[0];

            expect(q.interactionPlan).toBeNull();
            expect(q.interactionSolution).toBeNull();
        });

        // "Örnek soruda çözüm yok" uyarısı plan yerine yazarlık durumundaki yerleşimlere bakar.
        it('dragDropIssues_ExampleWithoutPlacements_WarnsAndWithPlacements_DoesNot', () => {
            const r = { ...region('Soru 1', []), isExample: true };
            component.interactionTypes.set('Soru 1', 'dragDropLabeling');
            (component as any).addDropZone('Soru 1', 10, 10, 50, 20);
            component.setLabelCount('Soru 1', '1');
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.generateDraggables('Soru 1');
            const noSolution = (component as any).tr('warnings.noSolutionPlacements');

            expect((component as any).dragDropIssues(r)).toContain(noSolution);

            component.setZoneSolution('Soru 1', 'z1', 'd1');
            expect((component as any).dragDropIssues(r)).not.toContain(noSolution);
        });
    });

    // Bekleme anlarının görünürlüğü: analiz örtüsü, eğitime gönderim meşgul durumu ve hata yolları.
    describe('busy states (predict / sendToFix)', () => {
        let snackBarOpen: Mock;

        /** 1x1 PNG; FileReader + Image.onload gerçekten tetiklensin diye çözülebilir bir görsel. */
        function png(name: string): File {
            const b64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=';
            return new File([Uint8Array.from(atob(b64), (c) => c.charCodeAt(0))], name, { type: 'image/png' });
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

        beforeEach(() => {
            snackBarOpen = vi.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);
        });

        describe('predict', () => {
            let predict$: Subject<any>;
            let readQr$: Subject<any>;
            let predictSpy: Mock;

            beforeEach(() => {
                predict$ = new Subject<any>();
                readQr$ = new Subject<any>();
                predictSpy = vi.spyOn(component.questionDetectorService, 'predict').mockImplementation(() => predict$.asObservable());
                vi.spyOn(component.questionDetectorService, 'readQrData').mockImplementation(() => readQr$.asObservable());
                component.imageData.set('data:image/png;base64,AAAA');
            });

            it('predict_NoImage_DoesNotCallDetectorOrSetInProgress', () => {
                component.imageData.set(null);

                component.predict();

                expect(predictSpy).not.toHaveBeenCalled();
                expect(component.inProgress()).toBe(false);
            });

            it('predict_InProgressUntilResponse_AndNoStartToast', () => {
                component.predict();

                expect(component.inProgress()).toBe(true);
                expect(snackBarOpen).not.toHaveBeenCalled();

                predict$.next({ predictions: [] });

                expect(component.inProgress()).toBe(false);
                expect(snackBarOpen).toHaveBeenCalledTimes(1);
            });

            it('predict_ErrorThenSuccess_TogglesPredictFailed', () => {
                component.predict();
                predict$.error(new Error('timeout'));
                expect(component.predictFailed()).toBe(true);

                predict$ = new Subject<any>();
                component.predict();
                expect(component.predictFailed()).toBe(false);
                predict$.next({ predictions: [] });
                expect(component.predictFailed()).toBe(false);
            });

            it('predict_SuccessFalseWithoutPredictions_MarksFailedWithoutThrowing', () => {
                component.predict();

                expect(() => predict$.next({ success: false, error: 'Prediction failed' })).not.toThrow();

                expect(component.predictFailed()).toBe(true);
                expect(component.inProgress()).toBe(false);
                expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.messages.analysisError, expect.any(String), expect.any(Object));
            });

            it('predict_Error_ClearsInProgressAndShowsError', () => {
                component.predict();

                predict$.error(new Error('boom'));

                expect(component.inProgress()).toBe(false);
                expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.messages.analysisError, expect.any(String), expect.any(Object));
            });

            // question-detector sayfada QR yoksa 404 döner (main.py read-qr): olağan durum, bildirim yok.
            it('predict_ReadQrNotFound404_IsSilentAndDoesNotEndAnalysis', () => {
                const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
                component.predict();

                readQr$.error({ status: 404 });

                expect(snackBarOpen).not.toHaveBeenCalled();
                expect(consoleError).not.toHaveBeenCalled();
                expect(component.inProgress()).toBe(true);
                predict$.next({ predictions: [] });
                expect(component.inProgress()).toBe(false);
            });

            it('predict_ReadQrOtherError_LogsWithoutNotifyingOrEndingAnalysis', () => {
                const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
                component.predict();

                readQr$.error({ status: 500 });

                expect(consoleError).toHaveBeenCalled();
                expect(snackBarOpen).not.toHaveBeenCalled();
                expect(component.inProgress()).toBe(true);
            });

            it('predict_NoResponseWithinTimeout_ClearsInProgressAndShowsError', () => {
                vi.useFakeTimers();
                try {
                    component.predict();
                    vi.advanceTimersByTime(119999);
                    expect(component.inProgress()).toBe(true);

                    vi.advanceTimersByTime(1);

                    expect(component.inProgress()).toBe(false);
                    expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.messages.analysisError, expect.any(String), expect.any(Object));
                }
                finally {
                    vi.useRealTimers();
                }
            });

            it('predict_PageChangedWhileAnalyzing_DropsStaleResponse', async () => {
                const first$ = new Subject<any>();
                const second$ = new Subject<any>();
                predictSpy.mockReturnValueOnce(first$.asObservable()).mockReturnValueOnce(second$.asObservable());
                component.imageFiles = [png('p1.png'), png('p2.png')];
                component.predict();

                component.nextImage(); // loadCurrentImage → cancelPendingPredict
                expect(component.inProgress()).toBe(false);
                expect(component.pageLoading()).toBe(true);
                first$.next({ predictions: [{ class_id: 0, x: 0, y: 0, width: 10, height: 10, subpredictions: [] }] });

                expect(component.regions().length).toBe(0);
                await waitFor(() => vi.mocked(predictSpy).mock.calls.length === 2);
                expect(component.pageLoading()).toBe(false);
                expect(component.inProgress()).toBe(true);
                second$.next({ predictions: [] });
                expect(component.inProgress()).toBe(false);
            });

            it('predict_StaleResponseAfterNewerAnalysis_IsIgnored', () => {
                const first$ = new Subject<any>();
                const second$ = new Subject<any>();
                predictSpy.mockReturnValueOnce(first$.asObservable()).mockReturnValueOnce(second$.asObservable());

                component.predict();
                component.predict();
                first$.next({ predictions: [{ class_id: 0, x: 0, y: 0, width: 10, height: 10, subpredictions: [] }] });

                expect(component.regions().length).toBe(0);
                expect(component.inProgress()).toBe(true);

                second$.next({ predictions: [] });
                expect(component.inProgress()).toBe(false);
            });

            it('overlay_ShownOnlyWhileAnalyzing_LiveRegionAlwaysPresent', () => {
                component.imageSrc.set('data:image/png;base64,AAAA');
                fixture.detectChanges();
                const el: HTMLElement = fixture.nativeElement;
                const liveRegion = () => el.querySelector('[role="status"][aria-live="polite"]');
                expect(el.querySelector('.ms-analysis-overlay')).toBeNull();
                expect(liveRegion()).not.toBeNull();
                expect(liveRegion()!.textContent!.trim()).toBe('');

                component.predict();
                fixture.detectChanges();
                const overlay = el.querySelector('.ms-analysis-overlay');
                expect(overlay).not.toBeNull();
                expect(overlay!.getAttribute('aria-hidden')).toBe('true');
                expect(overlay!.textContent).toContain(imageSelectorTr.messages.analysisInProgress);
                expect(overlay!.querySelector('mat-spinner')).not.toBeNull();
                expect(liveRegion()!.textContent).toContain(imageSelectorTr.messages.analysisInProgress);
                expect(liveRegion()!.hasAttribute('aria-busy')).toBe(false);

                predict$.error(new Error('boom'));
                fixture.detectChanges();
                expect(el.querySelector('.ms-analysis-overlay')).toBeNull();
                expect(liveRegion()).not.toBeNull();
                expect(liveRegion()!.textContent!.trim()).toBe('');
            });
        });

        describe('sendToFix', () => {
            let send$: Subject<any>;
            let sendtoFix: Mock;

            beforeEach(() => {
                send$ = new Subject<any>();
                sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockImplementation(() => send$.asObservable());
                component.imageData.set('data:image/png;base64,AAAA');
                component.answerCount.set(2);
                component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
            });

            it('sendToFix_DoubleClick_PostsOnce', () => {
                component.sendToFix();
                component.sendToFix();

                expect(sendtoFix).toHaveBeenCalledTimes(1);
                expect(component.sendingToFix()).toBe(true);
            });

            it('sendToFix_Success_ClearsBusyAndAllowsNextSend', () => {
                const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
                component.sendToFix();

                send$.next({ success: true, added: 1, imageFile: 'p.png' });

                expect(component.sendingToFix()).toBe(false);
                expect(nextImage).toHaveBeenCalledTimes(1);
                component.sendToFix();
                expect(sendtoFix).toHaveBeenCalledTimes(2);
            });

            it('sendToFix_HttpError_ClearsBusyAndShowsError', () => {
                const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
                component.sendToFix();

                send$.error(new Error('boom'));

                expect(component.sendingToFix()).toBe(false);
                expect(nextImage).not.toHaveBeenCalled();
                expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.training.sendError, expect.any(String), expect.any(Object));
            });

            it('sendToFix_FailureWithoutMessage_ClearsBusyAndShowsFallbackError', () => {
                component.sendToFix();

                send$.next({ success: false, status: 500 });

                expect(component.sendingToFix()).toBe(false);
                expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.training.sendError, expect.any(String), expect.any(Object));
            });

            it('sendToFix_EarlyReturnWithoutImage_DoesNotStayBusy', () => {
                component.imageData.set(null);

                component.sendToFix();

                expect(sendtoFix).not.toHaveBeenCalled();
                expect(component.sendingToFix()).toBe(false);
            });

            it('sendToFix_PageChangedBeforeResponse_DoesNotAdvanceAgain', () => {
                const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
                component.sendToFix();

                component.currentImageIndex = 3; // yanıt gelmeden başka sayfaya geçildi
                send$.next({ success: true, added: 1, imageFile: 'p.png' });

                expect(component.sendingToFix()).toBe(false);
                expect(nextImage).not.toHaveBeenCalled();
            });

            it('sendToFix_ComponentDestroyedBeforeResponse_RequestNotCancelledAndNoSideEffects', () => {
                const nextImage = vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
                component.sendToFix();

                fixture.destroy();

                // Eğitim isteği iptal edilmez (abonelik sürer) ...
                expect(send$.observed).toBe(true);
                send$.next({ success: true, added: 1, imageFile: 'p.png' });
                // ... ama yok edilmiş komponent snackbar açmaz, sayfa ilerletmez.
                expect(snackBarOpen).not.toHaveBeenCalled();
                expect(nextImage).not.toHaveBeenCalled();
            });

            it('sendToFix_EarlyReturnOnInvalidQuestions_DoesNotStayBusy', () => {
                component.answerCount.set(4);

                component.sendToFix();

                expect(sendtoFix).not.toHaveBeenCalled();
                expect(component.sendingToFix()).toBe(false);
            });
        });

        describe('page loading', () => {
            beforeEach(() => {
                vi.spyOn(component.questionDetectorService, 'predict').mockReturnValue(new Subject<any>().asObservable() as any);
                vi.spyOn(component.questionDetectorService, 'readQrData').mockReturnValue(new Subject<any>().asObservable() as any);
            });

            it('loadCurrentImage_UndecodableImage_ClearsPageLoadingAndNotifies', async () => {
                component.imageFiles = [new File(['not an image'], 'bad.png', { type: 'image/png' })];

                component.loadCurrentImage();
                expect(component.pageLoading()).toBe(true);

                await waitFor(() => !component.pageLoading());
                expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.messages.pageLoadError, expect.any(String), expect.any(Object));
            });

            it('loadCurrentImage_UndecodableAfterTrainedPage_DoesNotResendPreviousImage', async () => {
                // Önceki sayfa yüklenmiş ve eğitilmiş; sıradaki görsel çözülemiyor.
                component.imageData.set('data:image/png;base64,PREVIOUS');
                component.imageSrc.set('data:image/png;base64,PREVIOUS');
                component.imageFiles = [new File(['not an image'], 'bad.png', { type: 'image/png' })];
                component.currentImageIndex = 0;
                const sendtoFix = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(undefined as never);

                component.loadCurrentImage();
                await waitFor(() => !component.pageLoading());

                expect(component.pageLoadFailed()).toBe(true);
                expect(component.imageData()).toBeNull();
                expect(component.imageSrc()).toBeNull();
                component.sendToFix(true);
                expect(sendtoFix).not.toHaveBeenCalled();
            });

            it('loadCurrentImage_NextLoadStarts_ClearsPageLoadFailed', async () => {
                component.imageFiles = [new File(['not an image'], 'bad.png', { type: 'image/png' }), png('good.png')];
                component.loadCurrentImage();
                await waitFor(() => !component.pageLoading());
                expect(component.pageLoadFailed()).toBe(true);

                component.nextImage();

                expect(component.pageLoadFailed()).toBe(false);
                await waitFor(() => !component.pageLoading());
                expect(component.pageLoadFailed()).toBe(false);
                expect(component.imageData()).not.toBeNull();
            });

            it('handleFileInput_UndecodableImage_ClearsPageLoadingAndNotifies', async () => {
                const bad = new File(['not an image'], 'bad.png', { type: 'image/png' });

                component.handleFileInput({ target: { files: [bad] } } as unknown as Event);
                expect(component.pageLoading()).toBe(true);

                await waitFor(() => !component.pageLoading());
                expect(snackBarOpen).toHaveBeenCalledWith(imageSelectorTr.messages.pageLoadError, expect.any(String), expect.any(Object));
            });

            it('loadCurrentImage_NewerLoadStarted_IgnoresOlderLoadEvents', async () => {
                component.imageFiles = [new File(['not an image'], 'bad.png', { type: 'image/png' }), png('good.png')];

                component.loadCurrentImage(); // eski (bozuk) yükleme
                component.currentImageIndex = 1;
                component.loadCurrentImage(); // yeni yükleme

                await waitFor(() => !component.pageLoading());
                await new Promise((resolve) => setTimeout(resolve, 50));
                expect(component.imagName()).toBe('good.png');
                expect(snackBarOpen).not.toHaveBeenCalledWith(imageSelectorTr.messages.pageLoadError, expect.any(String), expect.any(Object));
            });
        });

        it('navigationLocked_DisablesEdgeNavAreas', () => {
            component.imageSrc.set('data:image/png;base64,AAAA');
            fixture.detectChanges();
            const el: HTMLElement = fixture.nativeElement;
            expect(el.querySelector('.ms-nav-prev')!.classList).not.toContain('ms-nav-disabled');

            fixture.componentRef.setInput('navigationLocked', true);
            fixture.detectChanges();

            expect(el.querySelector('.ms-nav-prev')!.classList).toContain('ms-nav-disabled');
            expect(el.querySelector('.ms-nav-next')!.classList).toContain('ms-nav-disabled');
        });
    });

    describe('labeling mode (externalDetection)', () => {
        const PNG_1X1 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';

        function pngFile(): File {
            const bytes = Uint8Array.from(atob(PNG_1X1), (c) => c.charCodeAt(0));
            return new File([bytes], 'p.png', { type: 'image/png' });
        }

        async function waitFor(condition: () => boolean, timeoutMs = 3000): Promise<void> {
            const start = Date.now();
            while (!condition()) {
                if (Date.now() - start > timeoutMs) {
                    throw new Error('waitFor timeout');
                }
                await new Promise((resolve) => setTimeout(resolve, 10));
            }
        }

        function prediction(x = 10) {
            return {
                success: true,
                predictions: [
                    {
                        class_id: 0,
                        x,
                        y: 10,
                        width: 200,
                        height: 100,
                        subpredictions: [
                            { class_id: 0, x: x + 2, y: 12, width: 20, height: 20, subpredictions: [] },
                            { class_id: 0, x: x + 2, y: 50, width: 20, height: 20, subpredictions: [] },
                        ],
                    },
                ],
            };
        }

        it('loadFile_ExternalDetection_EmitsPageReadyAndDoesNotPredict', async () => {
            fixture.componentRef.setInput('externalDetection', true);
            fixture.detectChanges();
            const predict = vi.spyOn(component, 'predict').mockReturnValue(undefined);
            const ready: string[] = [];
            component.pageReady.subscribe((data) => ready.push(data));

            component.loadFile(pngFile());
            await waitFor(() => ready.length > 0);

            expect(ready[0]).toMatch(/^data:image\/png;base64,/);
            expect(predict).not.toHaveBeenCalled();
        });

        it('loadFile_DefaultMode_PredictsAndDoesNotEmitPageReady', async () => {
            const predict = vi.spyOn(component, 'predict').mockReturnValue(undefined);
            const ready = vi.spyOn(component.pageReady, 'emit').mockReturnValue(undefined);

            component.loadFile(pngFile());
            await waitFor(() => vi.mocked(predict).mock.calls.length > 0);

            expect(ready).not.toHaveBeenCalled();
        });

        it('beginExternalDetection_OpensOverlayAndIssuesIncreasingSequence', () => {
            component.predictFailed.set(true);

            const first = component.beginExternalDetection();
            const second = component.beginExternalDetection();

            expect(second).toBeGreaterThan(first);
            expect(component.inProgress()).toBe(true);
            expect(component.predictFailed()).toBe(false);
        });

        it('applyExternalPrediction_CurrentSequence_SetsRegionsAndDetectionSource', () => {
            const seq = component.beginExternalDetection();

            component.applyExternalPrediction(seq, prediction() as any, { kind: 'Ai', modelName: 'claude-a' });

            expect(component.regions().length).toBe(1);
            expect(component.regions()[0].answers.length).toBe(2);
            expect(component.detectionSource()).toEqual({ kind: 'Ai', modelName: 'claude-a' });
            expect(component.inProgress()).toBe(false);
        });

        it('applyExternalPrediction_StaleSequence_IgnoredForBoxesAndSource', () => {
            const stale = component.beginExternalDetection();
            component.beginExternalDetection();

            component.applyExternalPrediction(stale, prediction() as any, { kind: 'Ai', modelName: 'claude-a' });

            expect(component.regions().length).toBe(0);
            expect(component.detectionSource()).toBeNull();
        });

        it('failExternalDetection_CurrentSequence_MarksFailedAndStaleIsIgnored', () => {
            const stale = component.beginExternalDetection();
            const current = component.beginExternalDetection();

            component.failExternalDetection(stale);
            expect(component.inProgress()).toBe(true);
            expect(component.predictFailed()).toBe(false);

            component.failExternalDetection(current);
            expect(component.inProgress()).toBe(false);
            expect(component.predictFailed()).toBe(true);
        });

        it('hasUnsavedEdits_FalseAfterApply_TrueOnceBoxesChange', () => {
            const seq = component.beginExternalDetection();
            component.applyExternalPrediction(seq, prediction() as any, { kind: 'Model' });
            expect(component.hasUnsavedEdits()).toBe(false);

            component.regions.update((rs) => rs.map((r) => ({ ...r, x: r.x + 5 })));

            expect(component.hasUnsavedEdits()).toBe(true);
        });

        describe('sendToFix → manual record', () => {
            beforeEach(() => component.imageData.set('data:image/png;base64,AAAA'));

            function arrangeSend(source: unknown) {
                component.answerCount.set(2);
                component.regions.set([region('Soru 1', [answer(10, 10), answer(10, 50)])] as any);
                component.detectionSource.set(source as any);
                vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
                return vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 1, imageFile: 'abcdef0123456789' }));
            }

            it('sendToFix_Success_SavesManualRecordWithAiBaseAndEmitsManualSaved', () => {
                arrangeSend({ kind: 'Ai', modelName: 'claude-a' });
                const saveManual = vi.spyOn(component['labelingService'], 'saveManual').mockReturnValue(of({ imageSha256: 'f'.repeat(64), width: 1, height: 1, results: [] }));
                const saved: string[] = [];
                component.manualSaved.subscribe((data) => saved.push(data));

                component.sendToFix();

                expect(saveManual).toHaveBeenCalledTimes(1);
                const request = vi.mocked(saveManual).mock.lastCall![0];
                expect(request.imageBase64).toBe('data:image/png;base64,AAAA');
                expect(request.baseSource).toBe('Ai');
                expect(request.baseModelName).toBe('claude-a');
                expect(request.modelSnapshot).toBeNull();
                expect(request.predictions.predictions.length).toBe(1);
                expect(request.predictions.predictions[0].class_id).toBe(0);
                expect(request.predictions.predictions[0].subpredictions.length).toBe(2);
                expect(component.detectionSource()).toEqual({ kind: 'Manual', base: { source: 'Ai', modelName: 'claude-a', promptVersion: null } });
                expect(saved).toEqual(['data:image/png;base64,AAAA']);
            });

            function saveManualSpy() {
                return vi.spyOn(component['labelingService'], 'saveManual').mockReturnValue(of({ imageSha256: 'f'.repeat(64), width: 1, height: 1, results: [] }));
            }

            it('sendToFix_AiSourceWithPromptVersion_SendsBasePromptVersion', () => {
                arrangeSend({ kind: 'Ai', modelName: 'claude-a', promptVersion: 4 });
                const saveManual = saveManualSpy();

                component.sendToFix();

                expect(vi.mocked(saveManual).mock.lastCall![0].basePromptVersion).toBe(4);
            });

            it('sendToFix_ManualFromAiBase_KeepsBasePromptVersionOfOriginalResult', () => {
                arrangeSend({ kind: 'Manual', base: { source: 'Ai', modelName: 'claude-a', promptVersion: 2 } });
                const saveManual = saveManualSpy();

                component.sendToFix();

                const request = vi.mocked(saveManual).mock.lastCall![0];
                expect(request.baseSource).toBe('Ai');
                expect(request.basePromptVersion).toBe(2);
            });

            it('sendToFix_ModelSourceWithPromptVersion_SendsNullBasePromptVersion', () => {
                // Sürüm yalnız Claude kaynağına aittir; YOLO kaynağında bir sürüm bilgisi taşınmamalı.
                arrangeSend({ kind: 'Model', modelName: 'yolo', promptVersion: 9 });
                const saveManual = saveManualSpy();

                component.sendToFix();

                const request = vi.mocked(saveManual).mock.lastCall![0];
                expect(request.baseSource).toBe('Model');
                expect(request.basePromptVersion).toBeNull();
            });

            it('sendToFix_AiSourceWithoutPromptVersion_SendsNullBasePromptVersion', () => {
                arrangeSend({ kind: 'Ai', modelName: 'claude-a' });
                const saveManual = saveManualSpy();

                component.sendToFix();

                expect(vi.mocked(saveManual).mock.lastCall![0].basePromptVersion).toBeNull();
            });

            it('sendToFix_ManualFromModelBase_SendsNullBasePromptVersion', () => {
                arrangeSend({ kind: 'Manual', base: { source: 'Model', modelName: null } });
                const saveManual = saveManualSpy();

                component.sendToFix();

                expect(vi.mocked(saveManual).mock.lastCall![0].basePromptVersion).toBeNull();
            });

            it('sendToFix_ManualSaveFails_OnlyWarnsAndStillEmitsTrained', () => {
                arrangeSend({ kind: 'Model' });
                vi.spyOn(component['labelingService'], 'saveManual').mockReturnValue(throwError(() => new Error('down')));
                const snackBar = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
                vi.spyOn(console, 'error').mockReturnValue(undefined);
                const trained: unknown[] = [];
                component.trained.subscribe((e) => trained.push(e));

                component.sendToFix();

                expect(trained.length).toBe(1);
                expect(snackBar).toHaveBeenCalled();
                expect(component.detectionSource()).toEqual({ kind: 'Model' });
            });
        });
    });
});
