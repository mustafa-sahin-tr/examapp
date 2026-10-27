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

        // issue #573: kapsam türü + etkinlik yönergesi BulkPassageDto.Kind / Instruction olarak gider.
        it('getRegions_Passages_SendKindAndInstructionOnlyForActivity', () => {
            component.regions.set([]);
            component.passages.set([
                { id: 'p1', x: 0, y: 0, width: 10, height: 10 },
                { id: 'p2', x: 0, y: 0, width: 10, height: 10 },
            ]);
            component.setPassageKind('p2', 1);
            component.setPassageInstruction('p2', '  Resimleri eşleştir.  ');
            component.setPassageInstruction('p1', 'okuma metninde gönderilmez');

            const [p1, p2] = component.getRegions(null).passages;

            expect(p1).toEqual(expect.objectContaining({ id: 'p1', kind: 0, instruction: null }));
            expect(p2).toEqual(expect.objectContaining({ id: 'p2', kind: 1, instruction: 'Resimleri eşleştir.' }));
        });

        it('setPassageInstruction_TruncatesTo2000Characters', () => {
            component.passages.set([{ id: 'p1', x: 0, y: 0, width: 10, height: 10 }]);

            component.setPassageInstruction('p1', 'x'.repeat(2100));

            expect(component.passages()[0].instruction!.length).toBe(2000);
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

    // Issue #574: matris seçim yazarlığı — satır/hücre çizimi, kolonlar, çözüm, plan/çözüm JSON'u ve doğrulama.
    describe('matrixChoice authoring', () => {
        /** Soru bölgesi (100,100) konumunda; çizim koordinatları tuval (mutlak) pikselidir. */
        function setupMatrix() {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            component.interactionTypes.set('Soru 1', 'matrixChoice');
            component.ensureMatrixState('Soru 1');
        }
        function draw(mode: 'matrixRow' | 'matrixCell', x1: number, y1: number, x2: number, y2: number) {
            component.selectionMode.set(mode);
            (component as any).addMatrixBoxFromDrag(0, x1, y1, x2, y2);
        }

        it('drawRowsAndCells_BuildsRelativePlanAndSeparateSolution', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixRow', 110.4, 150, 410, 180.6);
            component.setMatrixRowLabel('Soru 1', 'r1', '  Kuşlar uçar.  ');
            draw('matrixCell', 330, 112, 360, 138); // aktif kolon c1 → sonra c2'ye geçer
            draw('matrixCell', 370, 112, 400, 138);
            component.toggleMatrixSolution('Soru 1', 'r1', 'c1');
            component.toggleMatrixSolution('Soru 1', 'r2', 'c2');

            const q = component.getRegions(null).questions[0];
            const plan = JSON.parse(q.interactionPlan!);

            expect(q.interactionType).toBe('matrixChoice');
            expect(plan).toEqual({
                version: 1,
                type: 'matrixChoice',
                multiPerRow: false,
                columns: [{ id: 'c1', label: 'D' }, { id: 'c2', label: 'Y' }],
                rows: [
                    {
                        id: 'r1',
                        label: 'Kuşlar uçar.',
                        box: { x: 10, y: 10, width: 300, height: 30 },
                        cells: { c1: { x: 230, y: 12, width: 30, height: 26 }, c2: { x: 270, y: 12, width: 30, height: 26 } },
                    },
                    { id: 'r2', box: { x: 10, y: 50, width: 300, height: 31 } },
                ],
            });
            expect(plan.solution).toBeUndefined();
            expect(JSON.parse(q.interactionSolution!)).toEqual({ r1: ['c1'], r2: ['c2'] });
            expect(q.answers).toEqual([]);
        });

        it('noSolutionMarked_SendsNullSolution', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);

            expect(component.getRegions(null).questions[0].interactionSolution).toBeNull();
        });

        it('drawOutsideQuestionOrCellOutsideRow_IsRejected', () => {
            setupMatrix();
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            draw('matrixRow', 50, 110, 300, 140);
            expect(component.ensureMatrixState('Soru 1').rows).toEqual([]);

            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixCell', 120, 250, 150, 280);
            expect(component.ensureMatrixState('Soru 1').rows[0].cells).toEqual({});
            expect(open).toHaveBeenCalledTimes(2);
        });

        it('solution_SingleReplacesMultiTogglesAndTurningMultiOffTrims', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            component.applyMatrixPreset('Soru 1', 'compare');
            component.toggleMatrixSolution('Soru 1', 'r1', 'c1');
            component.toggleMatrixSolution('Soru 1', 'r1', 'c3');
            expect(component.ensureMatrixState('Soru 1').solution).toEqual({ r1: ['c3'] });

            component.setMatrixMultiPerRow('Soru 1', true);
            component.toggleMatrixSolution('Soru 1', 'r1', 'c1');
            expect(component.ensureMatrixState('Soru 1').solution).toEqual({ r1: ['c1', 'c3'] });

            component.setMatrixMultiPerRow('Soru 1', false);
            expect(component.ensureMatrixState('Soru 1').solution).toEqual({ r1: ['c1'] });
        });

        it('columnCount_ClampsAndDropsRemovedColumnsFromCellsAndSolution', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            component.setMatrixColumnCount('Soru 1', 3);
            component.setMatrixActiveColumn('Soru 1', 'c3');
            draw('matrixCell', 330, 112, 360, 138);
            component.toggleMatrixSolution('Soru 1', 'r1', 'c3');

            component.setMatrixColumnCount('Soru 1', 1);

            const state = component.ensureMatrixState('Soru 1');
            expect(state.columns.map((c) => c.id)).toEqual(['c1', 'c2']);
            expect(state.rows[0].cells).toEqual({});
            expect(state.solution).toEqual({});
            component.setMatrixColumnCount('Soru 1', 99);
            expect(component.ensureMatrixState('Soru 1').columns.length).toBe(10);
        });

        it('copyCellsFromFirstRow_AlignsCellsToEachRowBox', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixRow', 110, 200, 410, 240);
            draw('matrixCell', 330, 112, 360, 138);
            draw('matrixCell', 370, 112, 400, 138);

            component.copyMatrixCellsFromFirstRow('Soru 1');

            const r2 = component.ensureMatrixState('Soru 1').rows[1];
            expect(r2.cells).toEqual({ c1: { x: 230, y: 102, width: 30, height: 26 }, c2: { x: 270, y: 102, width: 30, height: 26 } });
            expect(component.getActiveInspector()?.matrix?.allCells).toBe(true);
        });

        it('hitTest_CellBeforeRow_AndRemoveRowDropsItsSolution', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixCell', 330, 112, 360, 138);
            component.toggleMatrixSolution('Soru 1', 'r1', 'c1');

            expect(component.getRegionOrAnswerAtPosition(340, 120)).toEqual({
                type: 'matrixCell',
                value: { questionIndex: 0, rowId: 'r1', columnId: 'c1' },
            });
            expect(component.getRegionOrAnswerAtPosition(150, 120)).toEqual({
                type: 'matrixRow',
                value: { questionIndex: 0, rowId: 'r1' },
            });

            component.removeMatrixRow('Soru 1', 'r1');
            expect(component.ensureMatrixState('Soru 1').solution).toEqual({});
        });

        it('validateQuestions_Save_BlocksIncompleteMatrix_TrainAndSkipInteractiveDoNot', () => {
            setupMatrix();
            const tr = (key: string, params?: Record<string, unknown>) => (component as any).tr(key, params);

            expect(component.validateQuestions({ requireCorrectAnswer: true })[0].messages).toEqual([tr('warnings.matrixNoRows')]);

            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixRow', 110, 150, 410, 180);
            component.setMatrixColumnLabel('Soru 1', 'c2', '  ');
            component.toggleMatrixSolution('Soru 1', 'r1', 'c1');
            expect(component.validateQuestions({ requireCorrectAnswer: true })[0].messages).toEqual([
                tr('warnings.matrixEmptyColumnLabel'),
                tr('warnings.matrixMissingSolution', { count: 1 }),
            ]);
            expect(component.validateQuestions({ requireCorrectAnswer: false })).toEqual([]);
            expect(component.validateQuestions({ requireCorrectAnswer: true, skipInteractive: true })).toEqual([]);

            component.setMatrixColumnLabel('Soru 1', 'c2', 'Y');
            component.toggleMatrixSolution('Soru 1', 'r2', 'c2');
            expect(component.validateQuestions({ requireCorrectAnswer: true })).toEqual([]);
        });

        it('partialCells_IsWarningOnlyNotBlocking', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixCell', 330, 112, 360, 138);
            component.toggleMatrixSolution('Soru 1', 'r1', 'c1');
            const r = component.regions()[0];

            component.checkRegionWarning(r);

            expect(component.warningMarkers.find((w) => w.id === r.id)?.messages).toEqual([
                (component as any).tr('warnings.matrixPartialCells'),
            ]);
            expect(component.validateQuestions({ requireCorrectAnswer: true })).toEqual([]);
        });

        it('sendToFix_OnlyMatrixQuestions_DoesNotPost', () => {
            setupMatrix();
            component.imageData.set('data:image/png;base64,xx');
            const sendSpy = vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 0, imageFile: 'p.png' }));
            vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);

            component.sendToFix();

            expect(sendSpy).not.toHaveBeenCalled();
        });

        // E2E (Sosyal Bilgiler s.186): iki sütunlu düzende sağ sütundaki satırın hücreleri kendi kutusuna kaymalı.
        it('copyCellsFromFirstRow_TwoColumnLayout_OffsetsXAndClampsToRowBox', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 290, 140); // sol sütun: box x 10..190
            draw('matrixRow', 310, 110, 450, 140); // sağ sütun, daha dar: box x 210..350
            draw('matrixCell', 230, 112, 260, 138); // c1 göreli x 130 → sağ satırda 210+120=330
            draw('matrixCell', 260, 112, 290, 138); // c2 göreli x 160 → 370, kutuya sığdırılır

            component.copyMatrixCellsFromFirstRow('Soru 1');

            const r2 = component.ensureMatrixState('Soru 1').rows[1];
            expect(r2.cells['c1']).toEqual({ x: 330, y: 12, width: 20, height: 26 });
            expect(r2.cells['c2']).toEqual({ x: 349, y: 12, width: 1, height: 26 });
            Object.values(r2.cells).forEach((c) => {
                expect(c.x).toBeGreaterThanOrEqual(r2.box.x);
                expect(c.x + c.width).toBeLessThanOrEqual(r2.box.x + r2.box.width);
            });
        });

        it('toolbarMatrixMode_OnMcqQuestion_ConvertsToMatrixInsteadOfLosingRows', () => {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            expect(component.getInteractionType('Soru 1')).toBe('mcq');

            draw('matrixRow', 110, 110, 410, 140);

            expect(component.getInteractionType('Soru 1')).toBe('matrixChoice');
            expect(JSON.parse(component.getRegions(null).questions[0].interactionPlan!).rows.length).toBe(1);
        });

        it('lockedToolbarMode_SelectMatrixCellMode_UpdatesLockAndSurvivesQuestionSelect', () => {
            setupMatrix();
            component.lockSelectionMode('matrixRow');

            component.selectMatrixCellMode(0, 'c2');
            expect(component.selectionModeLocked()).toBe('matrixCell');
            expect(component.ensureMatrixState('Soru 1').activeColumnId).toBe('c2');

            component.selectQuestion(0);
            expect(component.selectionMode()).toBe('matrixCell');
        });

        it('cellClick_TogglesSolutionOnlyInMatrixModes', () => {
            setupMatrix();
            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixCell', 330, 112, 360, 138);
            (component as any).ctx = {};
            const click = (mode: 'answer' | 'matrixCell') => {
                component.selectionMode.set(mode);
                vi.spyOn(component as any, 'toCanvasPoint').mockReturnValue({ x: 340, y: 120 });
                (component as any).startX = 340;
                (component as any).startY = 120;
                (component as any).isDrawing = true;
                (component as any).pendingHit = component.getRegionOrAnswerAtPosition(340, 120);
                component.endSelection(new MouseEvent('mouseup'));
            };

            click('answer');
            expect(component.ensureMatrixState('Soru 1').solution).toEqual({});

            click('matrixCell');
            expect(component.ensureMatrixState('Soru 1').solution).toEqual({ r1: ['c1'] });
            expect(component.selectionMode()).toBe('matrixCell');
        });

        it('planBuilder_EmitsOnlyContractKeysAndRegexSafeIds', () => {
            setupMatrix();
            component.setMatrixColumnCount('Soru 1', 10);
            draw('matrixRow', 110, 110, 410, 140);
            draw('matrixCell', 330, 112, 360, 138);
            component.setMatrixRowLabel('Soru 1', 'r1', 'Metin');

            const plan = JSON.parse(component.getRegions(null).questions[0].interactionPlan!);

            expect(Object.keys(plan).sort()).toEqual(['columns', 'multiPerRow', 'rows', 'type', 'version']);
            plan.columns.forEach((c: object) => expect(Object.keys(c).sort()).toEqual(['id', 'label']));
            expect(Object.keys(plan.rows[0]).sort()).toEqual(['box', 'cells', 'id', 'label']);
            expect(Object.keys(plan.rows[0].box).sort()).toEqual(['height', 'width', 'x', 'y']);
            const ids = [...plan.columns.map((c: { id: string }) => c.id), ...plan.rows.map((r: { id: string }) => r.id)];
            ids.forEach((id: string) => expect(id).toMatch(/^[A-Za-z0-9_-]{1,32}$/));
        });

        it('removeQuestionAndReset_ClearMatrixState', () => {
            setupMatrix();
            component.resetRegions();
            expect(component.matrixState.size).toBe(0);
        });
    });

    // Issue #575: cevap kutusu yazarlığı — kutu çizimi, tür ayarları, plan/çözüm JSON'u, kopyalama ve doğrulama.
    describe('slotFill authoring', () => {
        function setupSlots() {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            component.interactionTypes.set('Soru 1', 'slotFill');
            component.ensureSlotFillState('Soru 1');
        }
        function draw(x1: number, y1: number, x2: number, y2: number) {
            component.selectionMode.set('slot');
            (component as any).addSlotFromDrag(0, x1, y1, x2, y2);
        }
        const state = () => component.ensureSlotFillState('Soru 1');
        const save = () => component.getRegions(null).questions[0];

        it('drawSlots_BuildsRelativePlanAndSeparateSolutionForEverySlot', () => {
            setupSlots();
            draw(110, 110, 160.4, 130.6);
            component.setSlotValue('Soru 1', 's1', ' 3/4 ');
            component.setSlotEntry('Soru 1', 's1', 'fraction');
            component.setSlotUnitHint('Soru 1', 's1', ' cm ');
            draw(200, 110, 300, 130);
            component.setSlotKind('Soru 1', 's2', 'text');
            component.setSlotLabel('Soru 1', 's2', '  Başkent ');
            component.setSlotAccept('Soru 1', 's2', 'İzmir\n izmir \n\nSmyrna');
            component.setSlotSuffixes('Soru 1', 's2', 'Bölgesi, ili');

            const q = save();
            const plan = JSON.parse(q.interactionPlan!);
            expect(q.interactionType).toBe('slotFill');
            expect(plan).toEqual({
                version: 1,
                type: 'slotFill',
                slots: [
                    { id: 's1', box: { x: 10, y: 10, width: 50, height: 21 }, input: { kind: 'number', entry: 'fraction', unit: 'cm' } },
                    { id: 's2', label: 'Başkent', box: { x: 100, y: 10, width: 100, height: 20 }, input: { kind: 'text' } },
                ],
            });
            expect(JSON.parse(q.interactionSolution!)).toEqual({
                s1: { value: '3/4', equivalence: true, requireSimplest: false, units: [], unitMode: 'none' },
                s2: { accept: ['İzmir', 'Smyrna'], optionalSuffixes: ['Bölgesi', 'ili'] },
            });
            expect(q.answers).toEqual([]);
        });

        it('newSlot_InheritsSelectedSlotSettingsButNotValues', () => {
            setupSlots();
            draw(110, 110, 160, 130);
            component.setSlotKind('Soru 1', 's1', 'select');
            component.setSlotOptionLabel('Soru 1', 's1', 'o1', 'artar');
            component.setSlotOptionLabel('Soru 1', 's1', 'o2', 'azalır');
            component.toggleSlotCorrectOption('Soru 1', 's1', 'o2');
            draw(200, 110, 260, 130);

            const s2 = state().slots[1];
            expect(s2.kind).toBe('select');
            expect(s2.options.map((o) => o.label)).toEqual(['artar', 'azalır']);
            expect(s2.correctOptionIds).toEqual([]);
            expect(state().selectedSlotId).toBe('s2');
        });

        it('applySettingsToAll_CopiesSettingsKeepsEachSlotsValue', () => {
            setupSlots();
            draw(110, 110, 160, 130);
            draw(200, 110, 260, 130);
            component.setSlotValue('Soru 1', 's2', '12');
            component.setSlotUnitHint('Soru 1', 's1', 'cm');
            component.setSlotUnitMode('Soru 1', 's1', 'required');
            component.setSlotEquivalence('Soru 1', 's1', false);

            component.applySlotSettingsToAll('Soru 1', 's1');

            const s2 = state().slots[1];
            expect(s2).toMatchObject({ unitHint: 'cm', unitMode: 'required', units: ['cm'], equivalence: false, value: '12' });
        });

        it('validation_BlocksSaveWhenSlotHasNoCorrectValue_AndMirrorsServerRules', () => {
            setupSlots();
            expect(component.validateQuestions({ requireCorrectAnswer: true })[0].messages).toEqual(['Cevap kutusu çizilmemiş']);

            draw(110, 110, 160, 130);
            expect(component.validateQuestions({ requireCorrectAnswer: true })[0].messages).toEqual(['Kutu 1: doğru sayı yazılmamış']);
            expect(component.validateQuestions({ requireCorrectAnswer: false })).toEqual([]);
            expect(component.validateQuestions({ requireCorrectAnswer: true, skipInteractive: true })).toEqual([]);
            expect(save().interactionSolution).toBeNull();

            component.setSlotValue('Soru 1', 's1', '5 cm');
            expect(component.validateQuestions({ requireCorrectAnswer: true })[0].messages[0]).toContain('birim');
            component.setSlotValue('Soru 1', 's1', '%40');
            component.setSlotUnitMode('Soru 1', 's1', 'required');
            const codes = () => component.getActiveInspector()!.slotFill!.slots[0].issues;
            expect(codes()).toEqual(['unitsMissing', 'percentWithUnit']);
            component.setSlotUnitMode('Soru 1', 's1', 'none');
            component.setSlotEntry('Soru 1', 's1', 'fraction');
            component.setSlotEquivalence('Soru 1', 's1', false);
            expect(codes()).toEqual(['entryFormMismatch']);
            component.setSlotValue('Soru 1', 's1', '6/8');
            component.setSlotRequireSimplest('Soru 1', 's1', true);
            expect(codes()).toEqual(['notSimplest']);
            component.setSlotValue('Soru 1', 's1', '3/4');
            expect(component.validateQuestions({ requireCorrectAnswer: true })).toEqual([]);
            component.setSlotUnits('Soru 1', 's1', 'cm');
            component.setSlotUnitMode('Soru 1', 's1', 'required');
            expect(codes()).toEqual(['entryWithRequiredUnit']);
            component.setSlotEntry('Soru 1', 's1', 'plain');
            component.setSlotEquivalence('Soru 1', 's1', true);
            component.setSlotUnits('Soru 1', 's1', 'cm, m², ₺');
            expect(codes()).toEqual([]);
            component.setSlotUnits('Soru 1', 's1', 'cm, ², 5cm');
            expect(codes()).toEqual(['invalidUnit']);
            // "Birim yok" kipinde gizli kalan birimler gönderilmez ve kaydı engellemez.
            component.setSlotUnitMode('Soru 1', 's1', 'none');
            expect(codes()).toEqual([]);
            expect(JSON.parse(save().interactionSolution!).s1.units).toEqual([]);
        });

        it('selectAndPalette_ValidationAndPresets', () => {
            setupSlots();
            draw(110, 110, 160, 130);
            component.setSlotKind('Soru 1', 's1', 'select');
            const codes = () => component.getActiveInspector()!.slotFill!.slots[0].issues;
            expect(codes()).toEqual(['emptyOptionLabel', 'missingCorrectOption']);
            component.removeSlotOption('Soru 1', 's1', 'o2');
            expect(codes()).toContain('optionCount');

            component.setSlotKind('Soru 1', 's1', 'palette');
            expect(codes()).toEqual(['symbolCount', 'missingPaletteAccept']);
            component.applySlotPalettePreset('Soru 1', 's1', 'geometry');
            component.applySlotPalettePreset('Soru 1', 's1', 'geometry');
            component.setSlotPaletteAccept('Soru 1', 's1', '∠ABC');
            expect(state().slots[0].symbols).toEqual(['∠', '⊥', '//', '[AB]', '(AB)', '|AB|']);
            expect(codes()).toEqual([]);
            expect(JSON.parse(save().interactionPlan!).slots[0].input).toEqual({ kind: 'palette', symbols: ['∠', '⊥', '//', '[AB]', '(AB)', '|AB|'] });
        });

        it('preview_UsesNormalizer', () => {
            setupSlots();
            draw(110, 110, 160, 130);
            component.setSlotValue('Soru 1', 's1', '3/4');
            expect(component.previewSlotAnswer('Soru 1', 's1', '%75')).toBe(true);
            component.setSlotEquivalence('Soru 1', 's1', false);
            expect(component.previewSlotAnswer('Soru 1', 's1', '0,75')).toBe(false);
            expect(component.previewSlotAnswer('Soru 1', 's1', '')).toBeNull();
        });

        it('drawOutsideQuestion_IsRejected_HitTestFindsSlot_RemoveAndReset', () => {
            setupSlots();
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            draw(50, 110, 300, 140);
            expect(state().slots).toEqual([]);
            expect(open).toHaveBeenCalledTimes(1);

            draw(110, 110, 160, 130);
            expect(component.getRegionOrAnswerAtPosition(120, 120)).toEqual({ type: 'slot', value: { questionIndex: 0, slotId: 's1' } });
            component.removeSlot('Soru 1', 's1');
            expect(state().slots).toEqual([]);
            component.resetRegions();
            expect(component.slotFillState.size).toBe(0);
        });

        it('slotModeOnMcqQuestion_TurnsItIntoSlotFill_SelectKeepsSlotMode', () => {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            draw(110, 110, 160, 130);

            expect(component.getInteractionType('Soru 1')).toBe('slotFill');
            component.selectQuestion(0);
            expect(component.selectionMode()).toBe('slot');
        });
    });

    describe('dragPlace + match authoring (#576)', () => {
        function setupRegion(type: 'dragPlace' | 'match') {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            component.interactionTypes.set('Soru 1', type);
        }
        function draw(mode: string, x1: number, y1: number, x2: number, y2: number) {
            component.selectionMode.set(mode as any);
            (component as any).addInteractionBoxFromDrag(mode, 0, x1, y1, x2, y2);
        }
        const place = () => component.ensureDragPlaceState('Soru 1');
        const match = () => component.ensureMatchState('Soru 1');
        const save = () => component.getRegions(null).questions[0];
        const messages = () => component.validateQuestions({ requireCorrectAnswer: true })[0]?.messages ?? [];

        it('dragPlace_DrawTargetsAndCards_BucketsTextCards_SolutionByClicks_PlanAndSolutionSeparate', () => {
            setupRegion('dragPlace');
            draw('placeTarget', 110, 110, 210.4, 160.6);
            component.setDragPlaceTargetLabel('Soru 1', 't1', ' Erime ');
            component.addDragPlaceBucket('Soru 1');
            component.setDragPlaceTargetLabel('Soru 1', 't2', 'Donma');
            component.setDragPlaceTargetCapacity('Soru 1', 't2', '3');
            component.addDragPlaceCardsFromText('Soru 1', 'Buz\n\n  Mum \nSu');
            draw('placeCard', 300, 300, 350, 330);

            // Tuvalde hedefe tık = seç, karta tık = seçili hedefe ata.
            expect(component.getRegionOrAnswerAtPosition(120, 120)).toEqual({ type: 'placeTarget', value: { questionIndex: 0, targetId: 't1' } });
            expect(component.getRegionOrAnswerAtPosition(310, 310)).toEqual({ type: 'placeCard', value: { questionIndex: 0, cardId: 'c4' } });
            (component as any).onAuthoringItemClick('Soru 1', { type: 'placeTarget', value: { questionIndex: 0, targetId: 't1' } });
            (component as any).onAuthoringItemClick('Soru 1', { type: 'placeCard', value: { questionIndex: 0, cardId: 'c4' } });
            component.toggleDragPlaceAssignment('Soru 1', 't2', 'c1');
            component.toggleDragPlaceAssignment('Soru 1', 't2', 'c2');

            const q = save();
            expect(q.interactionType).toBe('dragPlace');
            expect(JSON.parse(q.interactionPlan!)).toEqual({
                version: 1,
                type: 'dragPlace',
                mode: 'place',
                reuse: false,
                targets: [
                    { id: 't1', label: 'Erime', box: { x: 10, y: 10, width: 100, height: 51 }, capacity: 1 },
                    { id: 't2', label: 'Donma', capacity: 3 },
                ],
                cards: [
                    { id: 'c1', label: 'Buz' },
                    { id: 'c2', label: 'Mum' },
                    { id: 'c3', label: 'Su' },
                    { id: 'c4', box: { x: 200, y: 200, width: 50, height: 30 } },
                ],
            });
            expect(JSON.parse(q.interactionSolution!)).toEqual({ t1: ['c4'], t2: ['c1', 'c2'] });
            expect(component.getActiveInspector()!.dragPlace!.distractorCount).toBe(1);
            expect(q.answers).toEqual([]);
            expect(messages()).toEqual([]);
        });

        it('dragPlace_ValidationMirrorsServer_ReuseAndOrderMode', () => {
            setupRegion('dragPlace');
            expect(messages()).toEqual(['Hedef yok (görselde kutu çizin ya da kova ekleyin)', 'Kart yok']);
            component.addDragPlaceBucket('Soru 1');
            component.addDragPlaceCardsFromText('Soru 1', 'A\nB');
            expect(messages()).toEqual(['Hedef 1: kovanın adı yok', 'Hiçbir hedefe doğru kart atanmamış']);
            expect(save().interactionSolution).toBeNull();
            expect(component.validateQuestions({ requireCorrectAnswer: true, skipInteractive: true })).toEqual([]);

            component.setDragPlaceTargetLabel('Soru 1', 't1', 'Kova');
            component.addDragPlaceBucket('Soru 1', 'İkinci');
            component.toggleDragPlaceAssignment('Soru 1', 't1', 'c1');
            component.toggleDragPlaceAssignment('Soru 1', 't2', 'c1');
            // Tekrar kullanım kapalı: kart ikinci hedefe taşınır.
            expect({ ...place().solution }).toEqual({ t2: ['c1'] });
            component.setDragPlaceReuse('Soru 1', true);
            component.toggleDragPlaceAssignment('Soru 1', 't1', 'c1');
            expect({ ...place().solution }).toEqual({ t2: ['c1'], t1: ['c1'] });
            component.setDragPlaceReuse('Soru 1', false);
            expect({ ...place().solution }).toEqual({ t1: ['c1'] });

            component.fillDragPlaceOrderFromCards('Soru 1');
            const plan = JSON.parse(save().interactionPlan!);
            expect(plan.mode).toBe('order');
            expect(plan.targets.every((t: { capacity: number }) => t.capacity === 1)).toBe(true);
            expect(JSON.parse(save().interactionSolution!)).toEqual({ t1: ['c1'], t2: ['c2'] });
            component.addDragPlaceBucket('Soru 1');
            expect(messages()).toEqual(['3. sıranın doğru kartı yok']);
        });

        it('match_TextAndCropItems_PairsPrelinkedExample_IdsDoNotOverlap', () => {
            setupRegion('match');
            component.addMatchItemsFromText('Soru 1', 'left', 'Deprem\nSel');
            draw('matchLeft', 110, 110, 160, 160);
            component.addMatchItemsFromText('Soru 1', 'right', 'Fay\nYağmur\nÇığ\nKar');
            expect(messages()).toEqual(['3 sol öğenin eşi seçilmemiş']);

            component.setMatchPair('Soru 1', 'l1', 'r1');
            component.setMatchPair('Soru 1', 'l2', 'r2');
            (component as any).onAuthoringItemClick('Soru 1', { type: 'matchItem', value: { questionIndex: 0, side: 'left', itemId: 'l3' } });
            (component as any).onAuthoringItemClick('Soru 1', { type: 'matchItem', value: { questionIndex: 0, side: 'right', itemId: 'r2' } });
            // Sağ öğe bir kez kullanılır: r2 artık l3'ün; l2 eşsiz kalır.
            expect({ ...match().pairs }).toEqual({ l1: 'r1', l3: 'r2' });
            component.setMatchPair('Soru 1', 'l2', 'r3');
            component.toggleMatchPrelinked('Soru 1', 'l1');

            const q = save();
            const plan = JSON.parse(q.interactionPlan!);
            expect(q.interactionType).toBe('match');
            expect(plan.left).toEqual([{ id: 'l1', label: 'Deprem' }, { id: 'l2', label: 'Sel' }, { id: 'l3', box: { x: 10, y: 10, width: 50, height: 50 } }]);
            expect(plan.right.map((r: { id: string }) => r.id)).toEqual(['r1', 'r2', 'r3', 'r4']);
            expect(plan.prelinked).toEqual([{ leftId: 'l1', rightId: 'r1' }]);
            expect(JSON.parse(q.interactionSolution!)).toEqual({ l2: 'r3', l3: 'r2' });
            const ids = [...plan.left, ...plan.right].map((i: { id: string }) => i.id);
            expect(new Set(ids).size).toBe(ids.length);
            expect(component.getActiveInspector()!.match!.distractorCount).toBe(1);
            expect(messages()).toEqual([]);

            component.toggleMatchPrelinked('Soru 1', 'l2');
            component.toggleMatchPrelinked('Soru 1', 'l3');
            expect(messages()).toEqual(['Örnek dışında eşlenecek sol öğe kalmadı']);
        });

        it('drawOnMcqQuestion_TurnsTypeAndSelectKeepsMode_OutsideRejected_ResetClears', () => {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            const open = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            draw('placeTarget', 50, 110, 160, 160);
            expect(open).toHaveBeenCalledTimes(1);
            draw('matchRight', 110, 110, 160, 160);
            expect(component.getInteractionType('Soru 1')).toBe('match');
            component.selectQuestion(0);
            expect(component.selectionMode()).toBe('matchRight');
            component.selectDragPlaceMode(0, 'placeCard');
            expect(component.getInteractionType('Soru 1')).toBe('dragPlace');
            component.selectQuestion(0);
            expect(component.selectionMode()).toBe('placeCard');
            component.resetRegions();
            expect(component.dragPlaceState.size).toBe(0);
            expect(component.matchState.size).toBe(0);
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

    describe('block kinds (issue #578)', () => {
        function box(y: number, extra: Record<string, unknown> = {}, answers = 0) {
            return {
                class_id: 0,
                x: 10,
                y,
                width: 200,
                height: 80,
                subpredictions: Array.from({ length: answers }, (_, i) => ({
                    class_id: 0, x: 12, y: y + 2 + i * 30, width: 20, height: 20, subpredictions: [],
                })),
                ...extra,
            };
        }

        /** Sırayla: MCQ, etkinlik A (matrixChoice), tipsiz etkinlik D, anlatım, cevap anahtarı, gürültü. */
        function mixedPrediction() {
            return {
                success: true,
                predictions: [
                    box(10, {}, 2),
                    box(100, { kind: 'activity', component: 'A', interaction_type: 'matrixChoice' }),
                    box(200, { kind: 'activity', component: 'E' }),
                    box(300, { kind: 'info_box' }),
                    box(400, { kind: 'answer_key' }),
                    box(500, { kind: 'noise' }),
                ],
            };
        }

        function applyMixed() {
            component.applyExternalPrediction(component.beginExternalDetection(), mixedPrediction() as any, {
                kind: 'Ai',
                modelName: 'claude-a',
            });
        }

        it('applyExternalPrediction_KindsInPrediction_StoredPerRegionAndNotCopiedOntoRegion', () => {
            applyMixed();

            const names = component.regions().map((r) => r.name);
            expect(names.map((n) => component.getBlockKind(n))).toEqual([
                'mcq_question', 'activity', 'activity', 'info_box', 'answer_key', 'noise',
            ]);
            expect(component.getActivityComponent('Soru 2')).toBe('A');
            expect(component.getActivityComponent('Soru 3')).toBe('E');
            expect(component.getActivityComponent('Soru 4')).toBeNull();
            expect('kind' in component.regions()[1]).toBe(false);
            expect('interaction_type' in component.regions()[1]).toBe(false);
        });

        it('applyExternalPrediction_ActivityWithInteractionType_SetsRegionInteractionType', () => {
            applyMixed();

            expect(component.getInteractionType('Soru 2')).toBe('matrixChoice');
            expect(component.getActiveInspector()).toBeTruthy();
            // Tipsiz etkinlik: blok etkinlik olarak işaretli kalır, etkileşimi yok.
            expect(component.getInteractionType('Soru 3')).toBe('mcq');
            expect(component.getInteractionType('Soru 4')).toBe('mcq');
        });

        it('applyExternalPrediction_InconsistentInteractionType_Ignored', () => {
            const list = { success: true, predictions: [box(10, { kind: 'activity', component: 'B', interaction_type: 'matrixChoice' })] };
            component.applyExternalPrediction(component.beginExternalDetection(), list as any, { kind: 'Ai' });

            expect(component.getBlockKind('Soru 1')).toBe('activity');
            expect(component.getInteractionType('Soru 1')).toBe('mcq');
        });

        it('applyExternalPrediction_NewDetection_ClearsPreviousKindsAndInteractionTypes', () => {
            applyMixed();

            component.applyExternalPrediction(component.beginExternalDetection(), {
                success: true,
                predictions: [box(10, {}, 2), box(100, {}, 2)],
            } as any, { kind: 'Model' });

            expect(component.getBlockKind('Soru 2')).toBe('mcq_question');
            expect(component.getInteractionType('Soru 2')).toBe('mcq');
        });

        it('checkRegionWarning_TypedActivityBlock_NoAuthoringWarnings', () => {
            // Yazarlık işaretleme ekranında yapılmaz (#581): Claude'un tiplediği etkinlikte boş matris hata sayılmaz.
            applyMixed();
            const activity = component.regions()[1];

            component.checkRegionWarning(activity);

            expect(component.warningMarkers.some((m) => m.id === activity.id)).toBe(false);
        });

        it('checkRegionWarning_QuestionWithUserPickedType_KeepsAuthoringWarnings', () => {
            applyMixed();
            const question = component.regions()[0];
            component.onInteractionTypeChange({ target: { value: 'matrixChoice' } } as unknown as Event, question.name);

            component.checkRegionWarning(question);

            expect(component.warningMarkers.some((m) => m.id === question.id)).toBe(true);
        });

        it('getActiveInspector_ExposesKindAndComponent', () => {
            applyMixed();
            component.selectQuestion(1);

            const inspector = component.getActiveInspector()!;

            expect(inspector.blockKind).toBe('activity');
            expect(inspector.activityComponent).toBe('A');
        });

        it('validateQuestions_NonQuestionBlocksWithoutAnswers_NotReported', () => {
            component.answerCount.set(2);
            applyMixed();

            const issues = component.validateQuestions({ requireCorrectAnswer: false, skipInteractive: true });

            expect(issues).toEqual([]);
        });

        it('getRegions_ExcludesInfoAnswerKeyNoiseAndUntypedActivity', () => {
            applyMixed();

            const payload = component.getRegions({});

            expect(payload.questions.map((q) => q.name)).toEqual(['Soru 1', 'Soru 2']);
            expect(payload.questions[1].interactionType).toBe('matrixChoice');
        });

        describe('inspector kind change', () => {
            beforeEach(() => applyMixed());

            it('setBlockKind_ToInfoBox_ChangesKindAndCountsAsUnsavedEdit', () => {
                expect(component.hasUnsavedEdits()).toBe(false);

                component.setBlockKind(0, 'info_box');

                expect(component.getBlockKind('Soru 1')).toBe('info_box');
                expect(component.isMcqRegion('Soru 1')).toBe(false);
                expect(component.hasUnsavedEdits()).toBe(true);
            });

            it('setBlockKind_ActivityBackToQuestion_RestoresMcqBehaviour', () => {
                component.setBlockKind(1, 'mcq_question');

                expect(component.getBlockKind('Soru 2')).toBe('mcq_question');
                expect(component.getActivityComponent('Soru 2')).toBeNull();
                expect(component.getInteractionType('Soru 2')).toBe('mcq');
                expect(component.isMcqRegion('Soru 2')).toBe(true);
            });

            it('setActivityComponent_MapsComponentToInteractionType', () => {
                component.setBlockKind(3, 'activity');
                expect(component.getInteractionType('Soru 4')).toBe('mcq');

                component.setActivityComponent(3, 'B');
                expect(component.getInteractionType('Soru 4')).toBe('slotFill');

                component.setActivityComponent(3, 'C');
                expect(component.getInteractionType('Soru 4')).toBe('dragPlace');

                component.setActivityComponent(3, 'G');
                expect(component.getInteractionType('Soru 4')).toBe('mcq');
                expect(component.getActivityComponent('Soru 4')).toBe('G');
            });

            it('setActivityComponent_NotAnActivity_Ignored', () => {
                component.setActivityComponent(0, 'A');

                expect(component.getBlockKind('Soru 1')).toBe('mcq_question');
                expect(component.getInteractionType('Soru 1')).toBe('mcq');
            });
        });

        describe('sendToFix', () => {
            beforeEach(() => {
                component.imageData.set('data:image/png;base64,AAAA');
                component.answerCount.set(2);
                vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            });

            function sendSpy() {
                return vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 1, imageFile: 'abcdef0123456789' }));
            }

            function saveManualSpy() {
                return vi.spyOn(component['labelingService'], 'saveManual').mockReturnValue(of({ imageSha256: 'f'.repeat(64), width: 1, height: 1, results: [] }));
            }

            it('sendToFix_MixedKinds_TrainsOnlyMcqQuestions', () => {
                applyMixed();
                const send = sendSpy();
                saveManualSpy();

                component.sendToFix();

                expect(send).toHaveBeenCalledTimes(1);
                const data = vi.mocked(send).mock.lastCall![0];
                expect(data.questions.length).toBe(1);
                expect(data.questions[0].y).toBe(10);
            });

            it('sendToFix_OnlyExcludedBlocks_SendsEmptyPage', () => {
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, { kind: 'info_box' }), box(100, { kind: 'noise' }), box(200, { kind: 'answer_key' })],
                } as any, { kind: 'Ai' });
                const send = sendSpy();
                saveManualSpy();
                const trained: Array<{ emptyPage: boolean }> = [];
                component.trained.subscribe((e) => trained.push(e));

                component.sendToFix();

                expect(send).toHaveBeenCalledTimes(1);
                expect(vi.mocked(send).mock.lastCall![0].questions).toEqual([]);
                expect(trained[0].emptyPage).toBe(true);
            });

            it('sendToFix_OnlyActivityBlocks_NotSent', () => {
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, { kind: 'activity', component: 'H' })],
                } as any, { kind: 'Ai' });
                const send = sendSpy();
                vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

                component.sendToFix();

                expect(send).not.toHaveBeenCalled();
            });

            it('sendToFix_CarriedBlocksSamePage_AppendedUnchangedToManualRecord', () => {
                // Soru tuvali: Manuel / Claude sonucunun soru dışı blokları tuvale uygulanmaz ama kayıtta korunur.
                const carried = [box(300, { kind: 'info_box' }), box(400, { kind: 'activity', component: 'A', interaction_type: 'matrixChoice' })];
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, {}, 2)],
                } as any, { kind: 'Manual', base: null }, carried as any);
                expect(component.regions().length).toBe(1);
                sendSpy();
                const saveManual = saveManualSpy();

                component.sendToFix(true, false);

                const predictions = vi.mocked(saveManual).mock.lastCall![0].predictions.predictions;
                expect(predictions.length).toBe(3);
                expect(predictions[0].kind).toBeUndefined();
                expect(predictions.slice(1)).toEqual(carried);
            });

            it('sendToFix_CarriedBlocksAfterNewDetection_NotAppended', () => {
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, {}, 2)],
                } as any, { kind: 'Manual', base: null }, [box(300, { kind: 'noise' })] as any);
                // Yeni tespit (YOLO / Claude, düşen blok yok) saklananları siler.
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, {}, 2)],
                } as any, { kind: 'Model' });
                sendSpy();
                const saveManual = saveManualSpy();

                component.sendToFix(true, false);

                expect(vi.mocked(saveManual).mock.lastCall![0].predictions.predictions.length).toBe(1);
            });

            it('sendToFix_CarriedBlocksOtherPage_NotAppended', () => {
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, {}, 2)],
                } as any, { kind: 'Manual', base: null }, [box(300, { kind: 'noise' })] as any);
                component.imageData.set('data:image/png;base64,BBBB');
                sendSpy();
                const saveManual = saveManualSpy();

                component.sendToFix(true, false);

                expect(vi.mocked(saveManual).mock.lastCall![0].predictions.predictions.length).toBe(1);
            });

            it('resetRegions_ClearsCarriedBlocks', () => {
                component.applyExternalPrediction(component.beginExternalDetection(), {
                    success: true,
                    predictions: [box(10, {}, 2)],
                } as any, { kind: 'Manual', base: null }, [box(300, { kind: 'noise' })] as any);

                component.resetRegions();

                expect(component['carriedBlocks']).toBeNull();
            });

            it('sendToFix_ManualRecord_CarriesKindsAndRoundTrips', () => {
                applyMixed();
                sendSpy();
                const saveManual = saveManualSpy();

                component.sendToFix();

                const predictions = vi.mocked(saveManual).mock.lastCall![0].predictions;
                expect(predictions.predictions.map((p) => p.kind ?? null)).toEqual([
                    null, 'activity', 'activity', 'info_box', 'answer_key', 'noise',
                ]);
                expect(predictions.predictions[0].subpredictions.length).toBe(2);
                expect(predictions.predictions[1]).toEqual(expect.objectContaining({ component: 'A', interaction_type: 'matrixChoice', subpredictions: [] }));
                expect(predictions.predictions[2].component).toBe('E');
                expect('interaction_type' in predictions.predictions[2]).toBe(false);
                expect('component' in predictions.predictions[3]).toBe(false);

                // Manuel kaynağı açılınca türler (renkler) geri gelir.
                component.applyExternalPrediction(component.beginExternalDetection(), predictions, { kind: 'Manual', base: null });

                expect(component.regions().map((r) => component.getBlockKind(r.name))).toEqual([
                    'mcq_question', 'activity', 'activity', 'info_box', 'answer_key', 'noise',
                ]);
                expect(component.getInteractionType('Soru 2')).toBe('matrixChoice');
                expect(component.getActivityComponent('Soru 3')).toBe('E');
            });
        });
    });

    describe('activity drafts (issue #579)', () => {
        function activity(y: number, component: string, interaction_type: string) {
            return { class_id: 0, x: 10, y, width: 200, height: 120, subpredictions: [], kind: 'activity', component, interaction_type };
        }

        beforeEach(() => {
            component.applyExternalPrediction(component.beginExternalDetection(), {
                success: true,
                predictions: [
                    activity(10, 'A', 'matrixChoice'),
                    activity(200, 'B', 'slotFill'),
                    activity(400, 'C', 'dragPlace'),
                    activity(600, 'C', 'match'),
                ],
            } as any, { kind: 'Ai', modelName: 'claude-a' });
        });

        it('detectionRevision_NewDetection_Increments', () => {
            const before = component.detectionRevision();

            component.applyExternalPrediction(component.beginExternalDetection(), { success: true, predictions: [] } as any, { kind: 'Model' });

            expect(component.detectionRevision()).toBe(before + 1);
        });

        it('applyInteractionDraft_MatrixPlan_LoadsRowsCellsColumnsWithoutSolution', () => {
            const plan = {
                version: 1,
                type: 'matrixChoice',
                multiPerRow: false,
                columns: [{ id: 'c1', label: 'D' }, { id: 'c2', label: 'Y' }],
                rows: [
                    { id: 'r1', label: 'Su kaynar.', box: { x: 0, y: 0, width: 180, height: 20 }, cells: { c1: { x: 140, y: 0, width: 18, height: 18 } } },
                    // Kutusuz satır: hücrelerin kapsayan kutusu kullanılır.
                    { id: 'r2', cells: { c1: { x: 140, y: 30, width: 18, height: 18 }, c2: { x: 160, y: 30, width: 18, height: 18 } } },
                    // Kutusu da hücresi de yok: atlanır.
                    { id: 'r3', label: 'boş' },
                ],
            };

            expect(component.applyInteractionDraft('Soru 1', 'matrixChoice', plan)).toBe('applied');

            const state = component.matrixState.get('Soru 1')!;
            expect(state.columns).toEqual([{ id: 'c1', label: 'D' }, { id: 'c2', label: 'Y' }]);
            expect(state.rows.map((r) => r.id)).toEqual(['r1', 'r2']);
            expect(state.rows[0].label).toBe('Su kaynar.');
            expect(state.rows[1].box).toEqual({ x: 140, y: 30, width: 38, height: 18 });
            expect(Object.keys(state.solution)).toEqual([]);
            component.selectQuestion(0);
            expect(component.getActiveInspector()!.matrix!.rows.length).toBe(2);
        });

        it('applyInteractionDraft_SlotFillPlan_LoadsSlotsWithInputSettingsAndEmptyAnswers', () => {
            const plan = {
                version: 1,
                type: 'slotFill',
                slots: [
                    { id: 's1', label: 'a', box: { x: 5, y: 6, width: 40, height: 20 }, input: { kind: 'number', entry: 'fraction', unit: 'cm' } },
                    { id: 's2', box: { x: 5, y: 40, width: 40, height: 20 }, input: { kind: 'select', options: [{ id: 'o1', label: 'var' }, { id: 'o2', label: 'yok' }] } },
                    { id: 's3', box: { x: 5, y: 80, width: 40, height: 20 }, input: { kind: 'palette', symbols: ['<', '>'] } },
                ],
            };

            expect(component.applyInteractionDraft('Soru 2', 'slotFill', plan)).toBe('applied');

            const slots = component.slotFillState.get('Soru 2')!.slots;
            expect(slots.map((s) => s.kind)).toEqual(['number', 'select', 'palette']);
            expect(slots[0]).toEqual(expect.objectContaining({ label: 'a', entry: 'fraction', unitHint: 'cm', value: '', box: { x: 5, y: 6, width: 40, height: 20 } }));
            expect(slots[1].options).toEqual([{ id: 'o1', label: 'var' }, { id: 'o2', label: 'yok' }]);
            expect(slots[1].correctOptionIds).toEqual([]);
            expect(slots[2].symbols).toEqual(['<', '>']);
        });

        it('applyInteractionDraft_DragPlacePlan_LoadsTargetsAndCards', () => {
            const plan = {
                version: 1,
                type: 'dragPlace',
                mode: 'place',
                reuse: false,
                targets: [{ id: 't1', label: 'Canlı', capacity: 3 }, { id: 't2', box: { x: 10, y: 10, width: 50, height: 30 }, capacity: 1 }],
                cards: [{ id: 'c1', label: 'Kedi' }, { id: 'c2', box: { x: 70, y: 10, width: 40, height: 30 } }],
            };

            expect(component.applyInteractionDraft('Soru 3', 'dragPlace', plan)).toBe('applied');

            const state = component.dragPlaceState.get('Soru 3')!;
            expect(state.targets).toEqual([
                { id: 't1', label: 'Canlı', box: null, capacity: 3 },
                { id: 't2', label: '', box: { x: 10, y: 10, width: 50, height: 30 }, capacity: 1 },
            ]);
            expect(state.cards.map((c) => c.id)).toEqual(['c1', 'c2']);
            expect(Object.keys(state.solution)).toEqual([]);
        });

        it('applyInteractionDraft_MatchPlan_LoadsItemsAndPrelinkedPairOnly', () => {
            const plan = {
                version: 1,
                type: 'match',
                left: [{ id: 'l1', label: 'Elma' }, { id: 'l2', box: { x: 0, y: 30, width: 40, height: 20 } }],
                right: [{ id: 'r1', label: 'Meyve' }, { id: 'r2', label: 'Sebze' }],
                prelinked: [{ leftId: 'l1', rightId: 'r1' }],
            };

            expect(component.applyInteractionDraft('Soru 4', 'match', plan)).toBe('applied');

            const state = component.matchState.get('Soru 4')!;
            expect(state.left.map((i) => i.id)).toEqual(['l1', 'l2']);
            expect(state.right.map((i) => i.label)).toEqual(['Meyve', 'Sebze']);
            expect(state.pairs['l1']).toBe('r1');
            expect(state.prelinked).toEqual(['l1']);
        });

        it('applyInteractionDraft_WrongTypeOrEmptyPlan_ReturnsFalseAndKeepsState', () => {
            const before = component.matrixState.get('Soru 1');

            expect(component.applyInteractionDraft('Soru 1', 'matrixChoice', { version: 1, type: 'slotFill', slots: [] })).toBe('invalid');
            expect(component.applyInteractionDraft('Soru 1', 'matrixChoice', null)).toBe('invalid');
            expect(component.applyInteractionDraft('Yok', 'matrixChoice', {})).toBe('invalid');
            expect(component.matrixState.get('Soru 1')).toBe(before);
        });

        it('applyInteractionDraft_DoesNotCountAsUnsavedEdit', () => {
            component.applyInteractionDraft('Soru 2', 'slotFill', {
                version: 1, type: 'slotFill', slots: [{ id: 's1', box: { x: 1, y: 1, width: 10, height: 10 }, input: { kind: 'text' } }],
            });

            expect(component.hasUnsavedEdits()).toBe(false);
        });

        describe('el yazarlığı korunur (W2)', () => {
            const place = (targets: number) => ({
                version: 1, type: 'dragPlace', mode: 'place', reuse: false,
                targets: Array.from({ length: targets }, (_, i) => ({ id: `t${i + 1}`, label: `H${i + 1}`, capacity: 1 })),
                cards: [{ id: 'c1', label: 'Kart' }],
            });

            it('handDrawnState_DraftKeptAndStateUnchanged_ReplaceOptionOverwrites', () => {
                component.addDragPlaceBucket('Soru 3', 'Elle');
                const before = component.dragPlaceState.get('Soru 3');

                expect(component.applyInteractionDraft('Soru 3', 'dragPlace', place(2))).toBe('kept');
                expect(component.dragPlaceState.get('Soru 3')).toBe(before);

                expect(component.applyInteractionDraft('Soru 3', 'dragPlace', place(2), { replace: true })).toBe('applied');
                expect(component.dragPlaceState.get('Soru 3')!.targets.map((t) => t.label)).toEqual(['H1', 'H2']);
            });

            it('previousDraftUnedited_NewDraftReplacesIt', () => {
                expect(component.applyInteractionDraft('Soru 3', 'dragPlace', place(1))).toBe('applied');

                expect(component.applyInteractionDraft('Soru 3', 'dragPlace', place(3))).toBe('applied');
                expect(component.dragPlaceState.get('Soru 3')!.targets.length).toBe(3);
            });

            it('previousDraftEditedByHand_TypeSwitchedAwayAndBack_EditsKept', () => {
                component.applyInteractionDraft('Soru 3', 'dragPlace', place(1));
                component.setDragPlaceTargetLabel('Soru 3', 't1', 'Öğretmen düzeltti');
                component.setActivityInteraction(2, 'match');
                component.setActivityInteraction(2, 'dragPlace');

                expect(component.hasAuthoringEdits('Soru 3', 'dragPlace')).toBe(true);
                expect(component.applyInteractionDraft('Soru 3', 'dragPlace', place(1))).toBe('kept');
                expect(component.dragPlaceState.get('Soru 3')!.targets[0].label).toBe('Öğretmen düzeltti');
            });

            it('solutionEditedByHand_CountsAsEdit', () => {
                component.applyInteractionDraft('Soru 3', 'dragPlace', place(1));
                component.toggleDragPlaceAssignment('Soru 3', 't1', 'c1');

                expect(component.applyInteractionDraft('Soru 3', 'dragPlace', place(1))).toBe('kept');
            });

            it('removeQuestion_ForgetsDraftFingerprint', () => {
                component.applyInteractionDraft('Soru 4', 'match', {
                    version: 1, type: 'match', left: [{ id: 'l1', label: 'a' }], right: [{ id: 'r1', label: 'b' }], prelinked: [],
                });
                component.removeQuestion(3);

                expect(component.hasAuthoringEdits('Soru 4', 'match')).toBe(false);
            });
        });

        it('setActivityInteraction_ComponentC_SwitchesBetweenDragPlaceAndMatch', () => {
            const regionsBefore = component.regions();

            component.setActivityInteraction(2, 'match');

            expect(component.getInteractionType('Soru 3')).toBe('match');
            // Bölge listesi yenilenir (üst komponent tip değişimini buradan izler) ve düzeltme sayılır.
            expect(component.regions()).not.toBe(regionsBefore);
            expect(component.hasUnsavedEdits()).toBe(true);
        });

        it('setActivityInteraction_InconsistentWithComponent_Ignored', () => {
            component.setActivityInteraction(0, 'match');
            component.setActivityInteraction(1, 'dragPlace');

            expect(component.getInteractionType('Soru 1')).toBe('matrixChoice');
            expect(component.getInteractionType('Soru 2')).toBe('slotFill');
        });
    });

    describe('textSelect authoring (issue #582)', () => {
        /** Bölge (100,100) 400×300. Çizim koordinatları tuval pikselidir. */
        function setup() {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([{ ...region('Soru 1', []), x: 100, y: 100, width: 400, height: 300 }] as any);
            (component as any).ctx = {};
        }

        function pointer(x1: number, y1: number, x2 = x1, y2 = y1) {
            const point = vi.spyOn(component as any, 'toCanvasPoint').mockReturnValue({ x: x1, y: y1 });
            component.startSelection(new MouseEvent('mousedown'));
            point.mockReturnValue({ x: x2, y: y2 });
            component.endSelection(new MouseEvent('mouseup'));
            point.mockRestore();
        }

        const state = () => component.ensureTextSelectState('Soru 1');
        const save = () => component.getRegions(null).questions[0];
        const messages = () => component.validateQuestions({ requireCorrectAnswer: true })[0]?.messages ?? [];

        it('select_DrawLineBoxes_AddLines_ClickTokensToMarkTargets_PlanAndSolutionSeparate', () => {
            setup();
            component.selectTextLineMode(0);
            expect(component.getInteractionType('Soru 1')).toBe('textSelect');

            // İkinci satır önce çizilir; okuma sırası kutunun dikey konumundan gelir.
            pointer(110, 160, 260.4, 180);
            expect(state().pendingLine).toEqual({ x: 10, y: 60, width: 150, height: 20 });
            expect(component.addTextSelectLine('Soru 1', 'gökte ki kuş')).toBe(true);
            pointer(110, 110, 260, 130);
            expect(component.addTextSelectLine('Soru 1', 'Sonun da  dolaş-')).toBe(true);
            expect(component.addTextSelectLine('Soru 1', 'kutu yok')).toBe(false);

            expect(state().tokens.map((t) => [t.id, t.text, t.line])).toEqual([
                ['t4', 'Sonun', 0],
                ['t5', 'da', 0],
                ['t6', 'dolaş-', 0],
                ['t1', 'gökte', 1],
                ['t2', 'ki', 1],
                ['t3', 'kuş', 1],
            ]);
            expect(messages()).toContain('Doğru cevap işaretlenmedi (kelimeye tıklayıp hedef yapın)');

            // Tuvalde kelimeye tık = hedef; yan yana kelimeler tek hedef.
            pointer(115, 115); // Sonun
            pointer(175, 115); // da
            pointer(115, 165); // gökte
            expect(state().targets.map((t) => t.tokenIds)).toEqual([['t4', 't5'], ['t1']]);
            component.setTextSelectTokenText('Soru 1', 't6', 'dolaşmış');
            component.setTextSelectStyle('Soru 1', 'circle');

            const q = save();
            expect(q.interactionType).toBe('textSelect');
            const plan = JSON.parse(q.interactionPlan!);
            expect(plan).toEqual(expect.objectContaining({ version: 1, type: 'textSelect', mode: 'select', style: 'circle' }));
            expect('palette' in plan).toBe(false);
            expect(plan.tokens[2]).toEqual({ id: 't6', text: 'dolaşmış', line: 0, boxes: [expect.any(Object)] });
            expect(JSON.parse(q.interactionSolution!)).toEqual({ targets: [{ tokenIds: ['t4', 't5'] }, { tokenIds: ['t1'] }] });
            expect(messages()).toEqual([]);

            // Ayır: hedef kelime başına bölünür; aynı kelimeye yeniden tık hedeften çıkarır.
            component.splitTextSelectTarget('Soru 1', 0);
            expect(state().targets.map((t) => t.tokenIds)).toEqual([['t4'], ['t5'], ['t1']]);
            pointer(175, 115);
            expect(state().targets.map((t) => t.tokenIds)).toEqual([['t4'], ['t1']]);
        });

        it('correct_TargetsNeedAcceptedCorrection', () => {
            setup();
            component.selectTextLineMode(0);
            pointer(110, 110, 260, 130);
            component.addTextSelectLine('Soru 1', 'Sonun da geldi');
            component.setTextSelectMode('Soru 1', 'correct');
            component.toggleTextSelectTarget('Soru 1', 't1');
            component.toggleTextSelectTarget('Soru 1', 't2');

            expect(messages()).toEqual(['Hedef 1: kabul edilen düzeltme yazılmadı']);
            component.setTextSelectTargetAccept('Soru 1', 0, 'Sonunda, sonunda ,Nihayet');

            expect(JSON.parse(save().interactionSolution!)).toEqual({ targets: [{ tokenIds: ['t1', 't2'], accept: ['Sonunda', 'Nihayet'] }] });
            expect(component.getActiveInspector()?.textSelect?.targets[0].text).toBe('Sonun da');
            expect(messages()).toEqual([]);
        });

        it('punctuation_GapsGetSymbolsFromPalette_GapsNeedPunctuationOtherwise', () => {
            setup();
            component.selectTextLineMode(0);
            pointer(110, 110, 300, 130);
            component.addTextSelectLine('Soru 1', 'Eyvah( ) ne yaptın ()');

            expect(messages()).toContain('Metinde "( )" boşlukları var; modu noktalama yapın ya da boşlukları silin');
            component.setTextSelectMode('Soru 1', 'punctuation');
            expect(messages()).toEqual(['Boşluk 1: doğru işaret seçilmedi', 'Boşluk 2: doğru işaret seçilmedi']);

            // Boşluğa tık = seç (hedef olmaz); sembol paletten.
            const gap = state().tokens.find((t) => t.id === 'g1')!;
            pointer(100 + gap.boxes[0].x + 2, 112);
            expect(state().selectedTokenId).toBe('g1');
            expect(state().targets).toEqual([]);
            component.toggleTextSelectGapSymbol('Soru 1', 'g1', '!');
            component.toggleTextSelectGapSymbol('Soru 1', 'g2', '?');
            component.toggleTextSelectGapSymbol('Soru 1', 'g2', '.');
            component.toggleTextSelectGapSymbol('Soru 1', 'g2', 'x'); // palette yok: yok sayılır

            const q = save();
            const plan = JSON.parse(q.interactionPlan!);
            expect(plan.palette).toEqual(['.', ',', '?', '!', ':', ';', '...', '-']);
            expect('style' in plan).toBe(false);
            expect(plan.tokens.filter((t: { gap?: boolean }) => t.gap).map((t: { id: string; text: string }) => [t.id, t.text])).toEqual([
                ['g1', '( )'],
                ['g2', '( )'],
            ]);
            expect(JSON.parse(q.interactionSolution!)).toEqual({ gaps: { g1: { accept: ['!'] }, g2: { accept: ['.', '?'] } } });

            // Paletten çıkan sembol çözümden de düşer.
            component.setTextSelectPalette('Soru 1', '. , !');
            expect(JSON.parse(save().interactionSolution!)).toEqual({ gaps: { g1: { accept: ['!'] }, g2: { accept: ['.'] } } });
            component.removeTextSelectToken('Soru 1', 'g2');
            expect(messages()).toEqual([]);
        });

        it('activityD_NullInteractionType_BecomesTextSelect_AndDraftLoads', () => {
            component.applyExternalPrediction(component.beginExternalDetection(), {
                success: true,
                predictions: [{ class_id: 0, x: 10, y: 10, width: 300, height: 120, subpredictions: [], kind: 'activity', component: 'D' }],
            } as any, { kind: 'Ai', modelName: 'claude-a' });

            expect(component.getInteractionType('Soru 1')).toBe('textSelect');
            const plan = {
                version: 1,
                type: 'textSelect',
                mode: 'punctuation',
                palette: ['.', '!'],
                tokens: [
                    { id: 't1', text: 'Eyvah', line: 0, boxes: [{ x: 4, y: 4, width: 23, height: 10 }] },
                    { id: 'g1', text: '( )', line: 0, gap: true, boxes: [{ x: 31, y: 4, width: 14, height: 10 }] },
                ],
            };
            expect(component.applyInteractionDraft('Soru 1', 'textSelect', plan)).toBe('applied');
            expect(component.ensureTextSelectState('Soru 1').palette).toEqual(['.', '!']);
            expect(component.hasAuthoringEdits('Soru 1', 'textSelect')).toBe(false);
            component.toggleTextSelectGapSymbol('Soru 1', 'g1', '!');
            expect(component.hasAuthoringEdits('Soru 1', 'textSelect')).toBe(true);
            expect(component.applyInteractionDraft('Soru 1', 'textSelect', plan)).toBe('kept');
            expect(component.applyInteractionDraft('Soru 1', 'textSelect', { type: 'textSelect', mode: 'select', tokens: [] })).toBe('invalid');
        });
    });

    describe('print masks (issue #581)', () => {
        /** Bölge (100,100) 400×300; içinde (120,120)'de 40×20 şık. Çizim koordinatları tuval pikselidir. */
        function setup() {
            vi.spyOn(component, 'drawImage').mockReturnValue(undefined);
            component.regions.set([
                {
                    ...region('Soru 1', [{ label: 'A', x: 120, y: 120, width: 40, height: 20, isCorrect: false, id: 0 }]),
                    x: 100, y: 100, width: 400, height: 300,
                },
                { ...region('Soru 2', []), x: 100, y: 500, width: 400, height: 200 },
            ] as any);
            (component as any).ctx = {};
        }

        /** Gerçek fare akışı: mousedown → mouseup (→ click). */
        function drag(x1: number, y1: number, x2: number, y2: number) {
            const point = vi.spyOn(component as any, 'toCanvasPoint').mockReturnValue({ x: x1, y: y1 });
            component.startSelection(new MouseEvent('mousedown'));
            point.mockReturnValue({ x: x2, y: y2 });
            component.endSelection(new MouseEvent('mouseup'));
            component.tapCorrectAnswer(new MouseEvent('click'));
            point.mockRestore();
        }

        it('maskMode_DragOverChoice_StoresRegionRelativeMaskAndDoesNotMarkCorrect', () => {
            setup();
            component.lockSelectionMode('mask');

            drag(115, 115, 165.4, 145.6);

            expect(component.getMasks('Soru 1')).toEqual([{ x: 15, y: 15, width: 51, height: 31 }]);
            expect(component.regions()[0].answers[0].isCorrect).toBe(false);
            expect(component.getActiveInspector()!.masks).toHaveLength(1);
            expect(component.hasUnsavedEdits()).toBe(false); // appliedSnapshot yok (elle çizilen sayfa)
        });

        it('maskMode_DragPastRegionEdge_ClampedToRegion_StartOutsideIgnored', () => {
            setup();
            component.lockSelectionMode('mask');

            drag(450, 350, 600, 450);
            drag(10, 10, 60, 60);

            expect(component.getMasks('Soru 1')).toEqual([{ x: 350, y: 250, width: 50, height: 50 }]);
            expect(component.getMasks('Soru 2')).toEqual([]);
        });

        it('getRegions_SendsMasksOnlyForMaskedRegions', () => {
            setup();
            component.lockSelectionMode('mask');
            drag(110, 110, 150, 140);
            drag(200, 520, 260, 560);
            drag(300, 600, 340, 640);

            const [q1, q2] = component.getRegions(null).questions as any[];

            expect(q1.masks).toEqual([{ x: 10, y: 10, width: 40, height: 30 }]);
            expect(q2.masks).toEqual([
                { x: 100, y: 20, width: 60, height: 40 },
                { x: 200, y: 100, width: 40, height: 40 },
            ]);

            component.removeMask('Soru 2', 0);
            component.clearMasks('Soru 1');

            const after = component.getRegions(null).questions as any[];
            expect('masks' in after[0]).toBe(false);
            expect(after[1].masks).toEqual([{ x: 200, y: 100, width: 40, height: 40 }]);
        });

        it('maskLimit_TwentyPerRegion', () => {
            setup();
            const snack = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            component.lockSelectionMode('mask');
            for (let i = 0; i < 21; i++) {
                drag(110 + i * 10, 300, 118 + i * 10, 320);
            }

            expect(component.getMasks('Soru 1')).toHaveLength(20);
            expect(snack).toHaveBeenCalledTimes(1);
        });

        it('fractionalRegion_MaskRelativeToRoundedOriginAndCoversDrawnArea', () => {
            setup();
            component.regions.set([{ ...component.regions()[0], x: 100.6, y: 99.4 }, component.regions()[1]] as any);
            component.lockSelectionMode('mask');

            drag(200.7, 300.2, 240.3, 330.9);

            // Kayıtta bölge (101, 99); maske 200..241 × 300..331 sayfa pikselini kapsar.
            expect(component.getMasks('Soru 1')).toEqual([{ x: 99, y: 201, width: 41, height: 31 }]);
            expect((component.getRegions(null).questions[0] as any).x).toBe(101);
        });

        it('maskOverOptionBox_WarnsButKeepsMask', () => {
            setup();
            const snack = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            component.lockSelectionMode('mask');

            drag(300, 300, 340, 320); // şıktan uzak: uyarı yok
            expect(snack).not.toHaveBeenCalled();

            drag(118, 118, 165, 142); // A şıkkını (120,120 40×20) tamamen kapatır
            expect(snack).toHaveBeenCalledWith(imageSelectorTr.messages.maskOverAnswer, expect.any(String), expect.any(Object));
            expect(component.getMasks('Soru 1')).toHaveLength(2);
        });

        it('maskOverActivityItem_Warns', () => {
            setup();
            const snack = vi.spyOn((component as any).snackBar, 'open').mockReturnValue(undefined);
            component.interactionTypes.set('Soru 2', 'slotFill');
            (component as any).addSlotFromDrag(1, 150, 550, 190, 580);
            component.lockSelectionMode('mask');

            drag(148, 548, 192, 582);

            expect(snack).toHaveBeenCalledWith(imageSelectorTr.messages.maskOverAnswer, expect.any(String), expect.any(Object));
        });

        it('maskMode_ShortClickOnPlaceCard_DoesNotChangeSolution', () => {
            setup();
            component.interactionTypes.set('Soru 2', 'dragPlace');
            component.ensureDragPlaceState('Soru 2');
            (component as any).addInteractionBoxFromDrag('placeTarget', 1, 120, 520, 220, 600);
            (component as any).addInteractionBoxFromDrag('placeCard', 1, 300, 520, 360, 560);
            const state = component.ensureDragPlaceState('Soru 2');
            component.selectDragPlaceTarget('Soru 2', state.targets[0].id);
            const before = JSON.stringify(component.ensureDragPlaceState('Soru 2').solution);
            component.lockSelectionMode('mask');

            drag(320, 540, 321, 541);

            expect(JSON.stringify(component.ensureDragPlaceState('Soru 2').solution)).toBe(before);
            expect(component.getMasks('Soru 2')).toEqual([]);
        });

        it('manualRecord_SavesMasksAndRestoresThemWhenApplied', () => {
            setup();
            component.regions.set([component.regions()[0]]);
            component.imageData.set('data:image/png;base64,AAAA');
            component.lockSelectionMode('mask');
            drag(110, 110, 150, 140);
            component.detectionSource.set({ kind: 'Ai', modelName: 'claude-a' } as any);
            component.answerCount.set(1);
            vi.spyOn(component, 'nextImage').mockReturnValue(undefined);
            vi.spyOn(component.questionDetectorService, 'sendtoFix').mockReturnValue(of({ success: true, added: 1, imageFile: 'abcdef0123456789' }) as any);
            const saveManual = vi.spyOn(component['labelingService'], 'saveManual').mockReturnValue(of({ imageSha256: 'f'.repeat(64), width: 1, height: 1, results: [] }) as any);

            component.sendToFix();

            const predictions = vi.mocked(saveManual).mock.lastCall![0].predictions.predictions as any[];
            expect(predictions[0].masks).toEqual([{ x: 10, y: 10, width: 40, height: 30 }]);
            expect(predictions).toHaveLength(1);

            component.applyExternalPrediction(component.beginExternalDetection(), {
                success: true,
                predictions: [
                    { ...predictions[0], masks: [...predictions[0].masks, { x: -1, y: 0, width: 5, height: 5 }] },
                ],
            } as any, { kind: 'Manual', base: null } as any);

            expect(component.getMasks('Soru 1')).toEqual([{ x: 10, y: 10, width: 40, height: 30 }]);
            expect('masks' in component.regions()[0]).toBe(false);
        });

        it('removeQuestion_DropsItsMasks', () => {
            setup();
            component.lockSelectionMode('mask');
            drag(110, 110, 150, 140);

            component.removeQuestion(0);

            expect(component.getMasks('Soru 1')).toEqual([]);
        });
    });

    describe('activity authoring on the canvas (issue #581)', () => {
        function applyActivities() {
            component.applyExternalPrediction(component.beginExternalDetection(), {
                success: true,
                predictions: [
                    { class_id: 0, x: 10, y: 10, width: 200, height: 80, subpredictions: [], kind: 'activity', component: 'E' },
                    { class_id: 0, x: 10, y: 100, width: 200, height: 80, subpredictions: [], kind: 'activity', component: 'A', interaction_type: 'matrixChoice' },
                ],
            } as any, { kind: 'Ai', modelName: 'claude-a' });
        }

        it('setRegionInteraction_UntypedActivityToMatch_SetsComponentCAndBecomesSavable', () => {
            applyActivities();
            expect(component.isSavableRegion('Soru 1')).toBe(false);

            component.setRegionInteraction(0, 'match');

            expect(component.getActivityComponent('Soru 1')).toBe('C');
            expect(component.getInteractionType('Soru 1')).toBe('match');
            expect(component.isSavableRegion('Soru 1')).toBe(true);
        });

        it('setRegionInteraction_ActivityToMcq_TurnsBlockIntoQuestion', () => {
            applyActivities();

            component.setRegionInteraction(1, 'mcq');

            expect(component.getBlockKind('Soru 2')).toBe('mcq_question');
            expect(component.isMcqRegion('Soru 2')).toBe(true);
        });

        it('authorActivities_TypedActivityShowsAuthoringWarnings', () => {
            applyActivities();
            const region2 = component.regions()[1];

            component.checkRegionWarning(region2);
            expect(component.getActiveInspector() && (component as any).warningMarkers.length).toBe(0);

            component.authorActivities = true;
            component.checkRegionWarning(region2);
            expect((component as any).warningMarkers.length).toBe(1);
        });

        it('savedActivityItems_KeepReadingOrder', () => {
            applyActivities();
            component.setRegionInteraction(0, 'slotFill');

            expect(component.getRegions(null).questions.map((q: any) => q.name)).toEqual(['Soru 1', 'Soru 2']);
        });
    });
});
