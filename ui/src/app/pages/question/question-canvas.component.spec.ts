import { HttpErrorResponse } from '@angular/common/http';
import type { MockedObject } from "vitest";
import { Component, EventEmitter, Input, Output, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject, throwError } from 'rxjs';
import { MatSnackBar } from '@angular/material/snack-bar';

import { QuestionCanvasComponent } from './question-canvas.component';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { SubjectService } from '../../services/subject.service';
import { BookService } from '../../services/book.service';
import { AuthService } from '../../services/auth.service';
import { ClassificationSource } from '../../models/draws';
import { TestCreateEnhancedComponent } from '../test-create-enhanced/test-create-enhanced.component';

/** Şablon testlerinde ağır test oluşturma formunun yerine geçen boş bileşen. */
@Component({ selector: 'app-test-create-enhanced', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '' })
class TestCreateEnhancedStubComponent {
    @Input()
    mode = '';
    @Output()
    createdAndContinue = new EventEmitter<number>();
}

/**
 * These tests exercise the redesigned shell logic of QuestionCanvasComponent:
 * pure getters, inspector proxies and the quick-create flow.
 * The heavy child (`ImageSelectorComponent`) is replaced by a lightweight fake
 * so no canvas / HTTP work happens — fixture.detectChanges() is intentionally
 * NOT called.
 */
import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import questionTr from '../../../../public/i18n/question/tr.json';

/** Gercek sozluk yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { 'question/tr': questionTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        // Uygulama config'i ile ayni: scope oneki klasor adiyla birebir ayni kalsin (issue #183).
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

/** Claude model kimliği (tespit kaynağı testleri, issue #473). */
const HAIKU_ID = 'claude-haiku-5-5';

describe('QuestionCanvasComponent', () => {
    let fixture: ComponentFixture<QuestionCanvasComponent>;
    let component: QuestionCanvasComponent;
    let testService: MockedObject<TestService>;

    function makeImageSelectorFake(overrides: Partial<any> = {}) {
        return {
            previewMode: vi.fn().mockName('previewMode').mockReturnValue(false),
            togglePreviewMode: vi.fn().mockName('togglePreviewMode'),
            answerCount: Object.assign(vi.fn().mockName('answerCount').mockReturnValue(4), {
                set: vi.fn().mockName('answerCount.set'),
            }),
            recomputeWarnings: vi.fn().mockName('recomputeWarnings'),
            ...overrides,
        };
    }

    beforeEach(async () => {
        window.history.replaceState({}, '');

        testService = {
            search: vi.fn().mockName("TestService.search"),
            get: vi.fn().mockName("TestService.get")
        } as unknown as MockedObject<TestService>;
        testService.search.mockReturnValue(of({ items: [] }) as any);
        testService.get.mockReturnValue(of({ subtitle: 'T-1', id: 99 }) as any);

        const questionService = {
            getAll: vi.fn().mockName("QuestionService.getAll"),
            saveBulk: vi.fn().mockName("QuestionService.saveBulk")
        };
        questionService.getAll.mockReturnValue(of([]) as any);
        questionService.saveBulk.mockReturnValue(of({}) as any);

        const bookService = {
            getAll: vi.fn().mockName("BookService.getAll"),
            getTestsByBook: vi.fn().mockName("BookService.getTestsByBook")
        };
        bookService.getAll.mockReturnValue(of([]));
        bookService.getTestsByBook.mockReturnValue(of([]));

        const subjectService = {
            loadCategories: vi.fn().mockName("SubjectService.loadCategories")
        };

        const router = {
            navigate: vi.fn().mockName("Router.navigate"),
            currentNavigation: vi.fn().mockName("Router.currentNavigation")
        };
        router.currentNavigation.mockReturnValue(null as any);

        await TestBed.configureTestingModule({
            imports: [QuestionCanvasComponent, translocoTesting],
            providers: [
                provideNoopAnimations(),
                provideHttpClient(withXhr()),
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

    // Issue #574 (E2E): hedef sayfa menüsü boş aramada da listeler, adla eşleşir ve etkinlik sayfalarını gösterir.
    describe('target worksheet search (#574)', () => {
        const wait = () => new Promise((resolve) => setTimeout(resolve, 350));
        const sheets = [
            { id: 1, name: 'Pilot Matris Etkinlikleri #574', subtitle: '', kind: 1 },
            { id: 2, name: 'Deneme', subtitle: 'Kesirler', kind: 0 },
        ];

        it('emptySearch_RequestsRecentWorksheetsAndListsBothKinds', async () => {
            testService.search.mockReturnValue(of({ items: sheets }) as any);
            fixture = TestBed.createComponent(QuestionCanvasComponent);
            component = fixture.componentInstance;
            await wait();

            expect(testService.search).toHaveBeenCalledWith('', [], [], 1, 100);
            expect(component.filteredTestListSignal().map((t) => t.id)).toEqual([1, 2]);
        });

        it('search_MatchesNameEvenWithoutSubtitle', async () => {
            testService.search.mockReturnValue(of({ items: sheets }) as any);
            fixture = TestBed.createComponent(QuestionCanvasComponent);
            component = fixture.componentInstance;
            component.questionForm.get('testId')!.setValue('pilot matris');
            await wait();

            expect(testService.search).toHaveBeenLastCalledWith('pilot matris', [], [], 1, 100);
            expect(component.filteredTestListSignal().map((t) => t.id)).toEqual([1]);
        });
    });

    // Issue #576: müfettişin sürükle-yerleştir / eşleştirme düğmeleri tuvale (ImageSelector) aktarılır.
    describe('dragPlace + match inspector bridge (#576)', () => {
        it('placeAndMatchActions_DelegateWithActiveQuestion', () => {
            const sel = {
                currentRegion: { name: 'Soru 2' },
                activeRegionIndex: 1,
                selectDragPlaceMode: vi.fn(),
                setDragPlaceMode: vi.fn(),
                addDragPlaceCardsFromText: vi.fn(),
                setDragPlaceTargetCapacity: vi.fn(),
                ensureDragPlaceState: vi.fn(() => ({ targets: [{ id: 't1', capacity: 30 }] })),
                toggleDragPlaceAssignment: vi.fn(),
                selectMatchMode: vi.fn(),
                setMatchPair: vi.fn(),
                toggleMatchPrelinked: vi.fn(),
            };
            component.imageSelector = sel as any;

            component.insPlaceDrawMode('placeCard');
            component.insPlaceMode('order');
            component.insPlaceAddCards('A\nB');
            const capacityInput = { value: '99' };
            component.insPlaceCapacity('t1', { target: capacityInput } as unknown as Event);
            component.insPlaceToggle('t1', 'c2');
            component.insMatchDrawMode('matchRight');
            component.insMatchPair('l1', { target: { value: '' } } as unknown as Event);
            component.insMatchPrelinked('l1');

            expect(sel.selectDragPlaceMode).toHaveBeenCalledWith(1, 'placeCard');
            expect(sel.setDragPlaceMode).toHaveBeenCalledWith('Soru 2', 'order');
            expect(sel.addDragPlaceCardsFromText).toHaveBeenCalledWith('Soru 2', 'A\nB');
            expect(sel.setDragPlaceTargetCapacity).toHaveBeenCalledWith('Soru 2', 't1', '99');
            expect(capacityInput.value).toBe('30');
            expect(sel.toggleDragPlaceAssignment).toHaveBeenCalledWith('Soru 2', 't1', 'c2');
            expect(sel.selectMatchMode).toHaveBeenCalledWith(1, 'matchRight');
            expect(sel.setMatchPair).toHaveBeenCalledWith('Soru 2', 'l1', null);
            expect(sel.toggleMatchPrelinked).toHaveBeenCalledWith('Soru 2', 'l1');
        });
    });

    describe('textSelect inspector bridge (#582)', () => {
        it('textActions_DelegateWithActiveQuestion_AddLineClearsFieldOnlyOnSuccess', () => {
            const sel = {
                currentRegion: { name: 'Soru 2' },
                activeRegionIndex: 1,
                selectTextLineMode: vi.fn(),
                setTextSelectMode: vi.fn(),
                setTextSelectStyle: vi.fn(),
                setTextSelectPalette: vi.fn(),
                addTextSelectLine: vi.fn().mockReturnValueOnce(true).mockReturnValueOnce(false),
                clickTextSelectToken: vi.fn(),
                setTextSelectTokenText: vi.fn(),
                toggleTextSelectGapSymbol: vi.fn(),
                splitTextSelectTarget: vi.fn(),
                setTextSelectTargetAccept: vi.fn(),
            };
            component.imageSelector = sel as any;

            component.insTextLineMode();
            component.insTextMode('punctuation');
            component.insTextStyle('circle');
            component.insTextPalette({ target: { value: '. , !' } } as unknown as Event);
            const field = { value: 'Bir gün' } as HTMLTextAreaElement;
            component.insTextAddLine(field);
            expect(field.value).toBe('');
            field.value = 'tekrar';
            component.insTextAddLine(field);
            expect(field.value).toBe('tekrar');
            component.insTextTokenClick('t3');
            component.insTextTokenText('t3', { target: { value: 'dolaşmış' } } as unknown as Event);
            component.insTextGapSymbol('g1', '!');
            component.insTextSplit(0);
            component.insTextAccept(0, { target: { value: 'Sonunda' } } as unknown as Event);

            expect(sel.selectTextLineMode).toHaveBeenCalledWith(1);
            expect(sel.setTextSelectMode).toHaveBeenCalledWith('Soru 2', 'punctuation');
            expect(sel.setTextSelectStyle).toHaveBeenCalledWith('Soru 2', 'circle');
            expect(sel.setTextSelectPalette).toHaveBeenCalledWith('Soru 2', '. , !');
            expect(sel.addTextSelectLine).toHaveBeenCalledWith('Soru 2', 'Bir gün');
            expect(sel.clickTextSelectToken).toHaveBeenCalledWith('Soru 2', 't3');
            expect(sel.setTextSelectTokenText).toHaveBeenCalledWith('Soru 2', 't3', 'dolaşmış');
            expect(sel.toggleTextSelectGapSymbol).toHaveBeenCalledWith('Soru 2', 'g1', '!');
            expect(sel.splitTextSelectTarget).toHaveBeenCalledWith('Soru 2', 0);
            expect(sel.setTextSelectTargetAccept).toHaveBeenCalledWith('Soru 2', 0, 'Sonunda');
        });
    });

    // Issue #581: hedef sayfanın türü (etkinlik bağlamı), etkinlik bloğunda tip seçimi, maske köprüsü, öğrenci gibi çöz.
    describe('activity context (#581)', () => {
        it('onOptionSelected_ActivityWorksheet_UsesRowKindWithoutFetching', () => {
            component.onOptionSelected({ id: 7, name: 'Etkinlik', subtitle: 'E', kind: 1 } as any);

            expect(component.activityContext()).toBe(true);
            expect(testService.get).not.toHaveBeenCalled();

            component.onOptionSelected({ id: 8, name: 'Test', subtitle: 'T', kind: 0 } as any);
            expect(component.activityContext()).toBe(false);
        });

        it('ngOnInit_WorksheetFromNavigationState_FetchesKind', () => {
            window.history.replaceState({ testValue: 42, testId: 'Etkinlik' }, '');
            testService.get.mockReturnValue(of({ id: 42, subtitle: 'E', kind: 1 }) as any);

            component.ngOnInit();

            expect(testService.get).toHaveBeenCalledWith(42);
            expect(testService.get).toHaveBeenCalledTimes(1);
            expect(component.activityContext()).toBe(true);
        });

        it('ngOnInit_TestWorksheetOrNoContext_StaysTestBehaviour', () => {
            component.ngOnInit();
            expect(component.activityContext()).toBe(false);

            window.history.replaceState({ testValue: 43 }, '');
            testService.get.mockReturnValue(of({ id: 43, subtitle: 'T' }) as any);
            component.ngOnInit();

            expect(component.worksheetKind()).toBe(0);
            expect(component.activityContext()).toBe(false);
        });

        it('insSetInteractionType_ActivityBlock_UsesSetRegionInteraction', () => {
            const sel = {
                currentRegion: { name: 'Soru 2' },
                activeRegionIndex: 1,
                getBlockKind: vi.fn((name: string) => (name === 'Soru 2' ? 'activity' : 'mcq_question')),
                setRegionInteraction: vi.fn(),
                onInteractionTypeChange: vi.fn(),
            };
            component.imageSelector = sel as any;

            component.insSetInteractionType('match');
            expect(sel.setRegionInteraction).toHaveBeenCalledWith(1, 'match');
            expect(sel.onInteractionTypeChange).not.toHaveBeenCalled();

            sel.currentRegion = { name: 'Soru 1' };
            sel.activeRegionIndex = 0;
            component.insSetInteractionType('slotFill');
            expect(sel.onInteractionTypeChange).toHaveBeenCalledTimes(1);
        });

        it('maskActions_DelegateWithActiveQuestion', () => {
            const sel = {
                currentRegion: { name: 'Soru 3' },
                activeRegionIndex: 2,
                selectMaskMode: vi.fn(),
                removeMask: vi.fn(),
                clearMasks: vi.fn(),
            };
            component.imageSelector = sel as any;

            component.insMaskMode();
            component.insRemoveMask(1);
            component.insClearMasks();

            expect(sel.selectMaskMode).toHaveBeenCalledWith(2);
            expect(sel.removeMask).toHaveBeenCalledWith('Soru 3', 1);
            expect(sel.clearMasks).toHaveBeenCalledWith('Soru 3');
        });

        it('solveAsStudent_SavedWorksheet_OpensPreview', () => {
            const fake = makeImageSelectorFake({ pageLoading: () => false, inProgress: () => false, sendingToFix: () => false });
            component.imageSelector = fake as any;
            component.questionForm.get('testValue')?.setValue(55 as any, { emitEvent: false });

            component.solveAsStudent();

            expect(fake.togglePreviewMode).toHaveBeenCalledWith(55);
        });

        it('insHasDraftCard_OnlyTypedActivityInActivityContext', () => {
            const d = { blockKind: 'activity', interactionType: 'matrixChoice' } as any;
            expect(component.insHasDraftCard(d)).toBe(false);

            component.onOptionSelected({ id: 7, name: 'Etkinlik', subtitle: 'E', kind: 1 } as any);

            expect(component.insHasDraftCard(d)).toBe(true);
            expect(component.insHasDraftCard({ ...d, interactionType: 'mcq' })).toBe(false);
            expect(component.insHasDraftCard({ ...d, blockKind: 'mcq_question' })).toBe(false);
        });
    });

    describe('canPreview', () => {
        it('canPreview_NoIdAndNoTestValue_ReturnsFalse', () => {
            component.id = null;
            component.questionForm.patchValue({ testValue: 0 });
            expect(component.canPreview).toBe(false);
        });

        it('canPreview_HasRouteId_ReturnsTrue', () => {
            component.id = 5;
            expect(component.canPreview).toBe(true);
        });

        it('canPreview_HasPositiveTestValue_ReturnsTrue', () => {
            component.id = null;
            component.questionForm.patchValue({ testValue: 12 });
            expect(component.canPreview).toBe(true);
        });
    });

    describe('previewOn', () => {
        it('previewOn_NoImageSelector_ReturnsFalse', () => {
            expect(component.previewOn).toBe(false);
        });

        it('previewOn_ReflectsImageSelectorPreviewMode', () => {
            component.imageSelector = makeImageSelectorFake({
                previewMode: vi.fn().mockReturnValue(true),
            }) as any;
            expect(component.previewOn).toBe(true);
        });
    });

    describe('togglePreviewMode', () => {
        it('togglePreviewMode_PreviewOff_UsesRouteId_WhenIdPresent', () => {
            const sel = makeImageSelectorFake();
            component.imageSelector = sel as any;
            component.id = 5;
            component.questionForm.patchValue({ testValue: 12 });

            component.togglePreviewMode();

            expect(sel.togglePreviewMode).toHaveBeenCalledTimes(1);

            expect(sel.togglePreviewMode).toHaveBeenCalledWith(5);
        });

        it('togglePreviewMode_PreviewOff_FallsBackToTestValue_WhenNoId', () => {
            const sel = makeImageSelectorFake();
            component.imageSelector = sel as any;
            component.id = null;
            component.questionForm.patchValue({ testValue: 12 });

            component.togglePreviewMode();

            expect(sel.togglePreviewMode).toHaveBeenCalledTimes(1);

            expect(sel.togglePreviewMode).toHaveBeenCalledWith(12);
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
            const sel = makeImageSelectorFake({ previewMode: vi.fn().mockReturnValue(true) });
            component.imageSelector = sel as any;
            component.id = 5;

            component.togglePreviewMode();

            expect(sel.togglePreviewMode).toHaveBeenCalledTimes(1);

            expect(sel.togglePreviewMode).toHaveBeenCalledWith(0);
        });
    });

    describe('answerCount / setAnswerCount', () => {
        it('answerCount_NoImageSelector_DefaultsToFour', () => {
            expect(component.answerCount).toBe(4);
        });

        it('answerCount_DelegatesToImageSelectorSignal', () => {
            component.imageSelector = makeImageSelectorFake({
                answerCount: vi.fn().mockReturnValue(5),
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
                    patchValue: vi.fn().mockName('patchValue'),
                },
                onSubjectChange: vi.fn().mockName('onSubjectChange'),
                onTopicChange: vi.fn().mockName('onTopicChange'),
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

        // Issue #454: JSON modunda panel değişikliği seçili sorunun soru bazındaki sınıflandırmasına yazılır.
        function jsonSelectorFake(jsonMode: boolean) {
            const updateRegionClassification = vi.fn().mockName('updateRegionClassification');
            const fake = makeImageSelectorFake({
                coordinateJsonMode: () => jsonMode,
                activeRegionIndex: 0,
                regions: () => [{ name: 'Soru 1', subjectId: 3, topicId: 7, subtopicIds: [9, 10] }],
                updateRegionClassification,
            });
            return { ...fake, updateRegionClassification };
        }

        it('insSubtopicId_Set_JsonMode_UpdatesActiveRegionClassification', () => {
            const tce = tceFake();
            tce.testForm.value = { subjectId: 3, topicId: 7, subtopicId: 11 };
            component.testCreateEnhancedComponent = tce as any;
            const sel = jsonSelectorFake(true);
            component.imageSelector = sel as any;

            component.insSubtopicId = 11;

            expect(sel.updateRegionClassification).toHaveBeenCalledWith(0, {
                subjectId: 3,
                topicId: 7,
                subtopicIds: [11],
            });
        });

        it('insSubjectId_Set_JsonModeNewSubject_ResetsTopicAndSubtopics', () => {
            component.testCreateEnhancedComponent = tceFake() as any;
            const sel = jsonSelectorFake(true);
            component.imageSelector = sel as any;

            component.insSubjectId = 5;

            expect(sel.updateRegionClassification).toHaveBeenCalledWith(0, { subjectId: 5, topicId: null, subtopicIds: [] });
        });

        it('insSubjectId_Set_JsonModeSameSubject_KeepsRegionClassification', () => {
            component.testCreateEnhancedComponent = tceFake() as any;
            const sel = jsonSelectorFake(true);
            component.imageSelector = sel as any;

            component.insSubjectId = 3;

            expect(sel.updateRegionClassification).not.toHaveBeenCalled();
        });

        it('detectorActions_JsonMode_NeverCallPredict', () => {
            const predict = vi.fn().mockName('predict');
            const alignAllAnswers = vi.fn().mockName('alignAllAnswers');
            const autoAlignSet = vi.fn().mockName('autoAlign.set');
            component.imageSelector = makeImageSelectorFake({
                coordinateJsonMode: () => true,
                predict,
                alignAllAnswers,
                autoAlign: Object.assign(() => false, { set: autoAlignSet }),
            }) as any;

            component.triggerPredict();
            component.askAi();
            component.setAutoAlign(true);
            component.triggerAlign();

            expect(predict).not.toHaveBeenCalled();
            expect(autoAlignSet).not.toHaveBeenCalled();
            expect(alignAllAnswers).toHaveBeenCalledTimes(1);
        });

        it('detectorActions_NormalMode_StillCallPredict', () => {
            const predict = vi.fn().mockName('predict');
            component.imageSelector = makeImageSelectorFake({
                coordinateJsonMode: () => false,
                predict,
                autoAlign: Object.assign(() => false, { set: vi.fn().mockName('autoAlign.set') }),
            }) as any;

            component.triggerPredict();
            component.askAi();
            component.setAutoAlign(true);
            component.triggerAlign();

            expect(predict).toHaveBeenCalledTimes(4);
        });

        it('insSubtopicId_Set_NormalMode_DoesNotTouchRegion', () => {
            component.testCreateEnhancedComponent = tceFake() as any;
            const sel = jsonSelectorFake(false);
            component.imageSelector = sel as any;

            component.insSubtopicId = 11;

            expect(sel.updateRegionClassification).not.toHaveBeenCalled();
        });
    });

    describe('onPreviewQuestionChange (issue #76 — unclassified region must not wipe test selection)', () => {
        function tceSyncFake() {
            return {
                syncClassification: vi.fn().mockName('syncClassification'),
            };
        }

        it('onPreviewQuestionChange_NullSubjectId_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onPreviewQuestionChange({
                index: 0,
                questionId: 1,
                subjectId: null,
                topicId: null,
                subtopicId: null,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });

        it('onPreviewQuestionChange_ZeroSubjectId_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onPreviewQuestionChange({
                index: 0,
                questionId: 1,
                subjectId: 0 as any,
                topicId: 0 as any,
                subtopicId: 0 as any,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });

        it('onPreviewQuestionChange_UndefinedSubjectId_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onPreviewQuestionChange({
                index: 0,
                questionId: 1,
                subjectId: undefined as any,
                topicId: undefined as any,
                subtopicId: undefined as any,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });

        it('onPreviewQuestionChange_RealSubjectId_PatchesFormAndSyncsClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;

            component.onPreviewQuestionChange({
                index: 0,
                questionId: 1,
                subjectId: 11,
                topicId: 22,
                subtopicId: 33,
            });

            expect(component.questionForm.value.subjectId).toBe(11);
            expect(component.questionForm.value.topicId).toBe(22);
            expect(component.questionForm.value.subtopicId).toBe(33);
            expect(tce.syncClassification).toHaveBeenCalledTimes(1);
            expect(tce.syncClassification).toHaveBeenCalledWith(11, 22, 33);
        });

        it('onPreviewQuestionChange_NullSubjectId_HumanSource_PatchesFormAndSyncsClassification', () => {
            // User picked a new Grade in classification-selector -> subjectId is
            // intentionally null with Human source. Guard must NOT swallow it.
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onPreviewQuestionChange({
                index: 0,
                questionId: 1,
                subjectId: null,
                topicId: null,
                subtopicId: null,
                classificationSource: ClassificationSource.Human,
            });

            expect(component.questionForm.value.subjectId).toBe(0);
            expect(component.questionForm.value.topicId).toBe(0);
            expect(component.questionForm.value.subtopicId).toBe(0);
            expect(tce.syncClassification).toHaveBeenCalledTimes(1);
            expect(tce.syncClassification).toHaveBeenCalledWith(null, null, null);
        });

        it('onPreviewQuestionChange_NullSubjectId_AISource_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onPreviewQuestionChange({
                index: 0,
                questionId: 1,
                subjectId: null,
                topicId: null,
                subtopicId: null,
                classificationSource: ClassificationSource.AI,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });
    });

    describe('onInspectorQuestionChange (issue #76 — unclassified region must not wipe test selection)', () => {
        function tceSyncFake() {
            return {
                syncClassification: vi.fn().mockName('syncClassification'),
            };
        }

        it('onInspectorQuestionChange_NullRegion_DoesNothing', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onInspectorQuestionChange({ index: 0, region: null });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });

        it('onInspectorQuestionChange_NullSubjectIdOnRegion_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onInspectorQuestionChange({
                index: 0,
                region: { subjectId: null, topicId: null, subtopicId: null } as any,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });

        it('onInspectorQuestionChange_ZeroOrUndefinedSubjectIdOnRegion_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onInspectorQuestionChange({
                index: 0,
                region: { subjectId: 0, topicId: undefined, subtopicId: undefined } as any,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });

        it('onInspectorQuestionChange_RealSubjectIdOnRegion_PatchesFormAndSyncsClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;

            component.onInspectorQuestionChange({
                index: 0,
                region: { subjectId: 11, topicId: 22, subtopicId: 33 } as any,
            });

            expect(component.questionForm.value.subjectId).toBe(11);
            expect(component.questionForm.value.topicId).toBe(22);
            expect(component.questionForm.value.subtopicId).toBe(33);
            expect(tce.syncClassification).toHaveBeenCalledTimes(1);
            expect(tce.syncClassification).toHaveBeenCalledWith(11, 22, 33);
        });

        it('onInspectorQuestionChange_NullSubjectId_HumanSource_PatchesFormAndSyncsClassification', () => {
            // Deliberate human reset (e.g. Grade changed) -> guard must NOT apply.
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onInspectorQuestionChange({
                index: 0,
                region: {
                    subjectId: null,
                    topicId: null,
                    subtopicId: null,
                    classificationSource: ClassificationSource.Human,
                } as any,
            });

            expect(component.questionForm.value.subjectId).toBe(0);
            expect(component.questionForm.value.topicId).toBe(0);
            expect(component.questionForm.value.subtopicId).toBe(0);
            expect(tce.syncClassification).toHaveBeenCalledTimes(1);
            expect(tce.syncClassification).toHaveBeenCalledWith(null, null, null);
        });

        it('onInspectorQuestionChange_NullSubjectId_AISource_DoesNotPatchFormOrSyncClassification', () => {
            const tce = tceSyncFake();
            component.testCreateEnhancedComponent = tce as any;
            component.questionForm.patchValue({ subjectId: 3, topicId: 7, subtopicId: 9 });

            component.onInspectorQuestionChange({
                index: 0,
                region: {
                    subjectId: null,
                    topicId: null,
                    subtopicId: null,
                    classificationSource: ClassificationSource.AI,
                } as any,
            });

            expect(component.questionForm.value.subjectId).toBe(3);
            expect(component.questionForm.value.topicId).toBe(7);
            expect(component.questionForm.value.subtopicId).toBe(9);
            expect(tce.syncClassification).not.toHaveBeenCalled();
        });
    });

    describe('quick create flow', () => {
        it('openQuickCreate_OpensPanelAndSidePanel', () => {
            component.showQuickCreate.set(false);
            component.showSidePanel.set(false);

            component.openQuickCreate();

            expect(component.showQuickCreate()).toBe(true);
            expect(component.showSidePanel()).toBe(true);
        });

        it('onQuickCreated_ClosesPanel_SetsId_AndSyncsFormFromTest', () => {
            component.showQuickCreate.set(true);

            component.onQuickCreated(42);

            expect(component.showQuickCreate()).toBe(false);
            expect(component.id).toBe(42);
            expect(testService.get).toHaveBeenCalledWith(42);
            expect(component.questionForm.get('testId')?.value).toBe('T-1');
            expect(component.questionForm.get('testValue')?.value).toBe(99);
        });
    });

    /**
     * `canTrain` is resolved once at field-initialization time from `AuthService.hasRealmRole('Admin' |
     * 'QuestionTrainer')` only (profile role `hasRole` is ignored, same source as the gateway),
     * so it can't be flipped after a component instance already exists — each scenario below spins
     * up its own TestBed module with a stubbed AuthService instead of reusing the outer beforeEach.
     * `saveBulk` is driven by a manually-controlled Subject so the saving() true/false transition
     * around the async emission can be asserted, rather than only the settled end-state.
     */
    describe('onSave', () => {
        /** `isAdmin` → profil rolü Admin (`hasRole`); `realmRoles` → token realm rolleri (`hasRealmRole`). */
        async function setup(opts: {
            isAdmin: boolean;
            realmRoles?: string[];
            saveBulk$?: Subject<any>;
            /** Şablon render edilecekse test oluşturma formunu boş bileşenle değiştir. */
            stubTestCreate?: boolean;
        }) {
            const saveBulk$ = opts.saveBulk$ ?? new Subject<any>();

            const testServiceLocal = {
                search: vi.fn().mockName("TestService.search"),
                get: vi.fn().mockName("TestService.get")
            };
            testServiceLocal.search.mockReturnValue(of({ items: [] }) as any);
            testServiceLocal.get.mockReturnValue(of({ subtitle: 'T-1', id: 7 }) as any);

            const questionServiceLocal = {
                getAll: vi.fn().mockName("QuestionService.getAll"),
                saveBulk: vi.fn().mockName("QuestionService.saveBulk")
            };
            questionServiceLocal.saveBulk.mockReturnValue(saveBulk$.asObservable() as any);

            const bookServiceLocal = {
                getAll: vi.fn().mockName("BookService.getAll"),
                getTestsByBook: vi.fn().mockName("BookService.getTestsByBook")
            };
            bookServiceLocal.getAll.mockReturnValue(of([]));
            bookServiceLocal.getTestsByBook.mockReturnValue(of([]));

            const subjectServiceLocal = {
                loadCategories: vi.fn().mockName("SubjectService.loadCategories")
            };

            const routerLocal = {
                navigate: vi.fn().mockName("Router.navigate"),
                currentNavigation: vi.fn().mockName("Router.currentNavigation")
            };
            routerLocal.currentNavigation.mockReturnValue(null as any);

            const authServiceLocal = {
                hasRole: vi.fn().mockName("AuthService.hasRole"),
                hasRealmRole: vi.fn().mockName("AuthService.hasRealmRole")
            };
            authServiceLocal.hasRole.mockImplementation((role: string) => opts.isAdmin && role === 'Admin');
            const realmRoles = opts.realmRoles ?? [];
            authServiceLocal.hasRealmRole.mockImplementation((role: string) => realmRoles.includes(role));

            TestBed.resetTestingModule();
            TestBed.configureTestingModule({
                imports: [QuestionCanvasComponent, translocoTesting],
                providers: [
                    provideNoopAnimations(),
                    provideHttpClient(withXhr()),
                    provideHttpClientTesting(),
                    { provide: TestService, useValue: testServiceLocal },
                    { provide: QuestionService, useValue: questionServiceLocal },
                    { provide: BookService, useValue: bookServiceLocal },
                    { provide: SubjectService, useValue: subjectServiceLocal },
                    { provide: Router, useValue: routerLocal },
                    { provide: AuthService, useValue: authServiceLocal },
                    { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
                ],
            });
            if (opts.stubTestCreate) {
                TestBed.overrideComponent(QuestionCanvasComponent, {
                    remove: { imports: [TestCreateEnhancedComponent] },
                    add: { imports: [TestCreateEnhancedStubComponent] },
                });
            }
            await TestBed.compileComponents();

            const localFixture = TestBed.createComponent(QuestionCanvasComponent);
            const localComponent = localFixture.componentInstance;

            const imageSelectorFake = {
                getRegions: vi.fn().mockName('getRegions').mockImplementation((payload: any) => payload),
                sendToFix: vi.fn().mockName('sendToFix'),
                nextImage: vi.fn().mockName('nextImage'),
                ensureQuestionsValid: vi.fn().mockName('ensureQuestionsValid').mockReturnValue(true),
            };
            localComponent.imageSelector = imageSelectorFake as any;

            const tceFake = {
                reloadComponent: vi.fn().mockName('reloadComponent'),
                onCreateAsync: vi.fn().mockName('onCreateAsync').mockReturnValue(of({ examId: 7 })),
            };
            localComponent.testCreateEnhancedComponent = tceFake as any;

            return { fixture: localFixture, component: localComponent, questionServiceLocal, imageSelectorFake, tceFake, saveBulk$ };
        }

        /** Kayıt sonrası 2 sn'lik "sonraki görsel" geçişi sahte saatle sürülür (#394). */
        async function withMockClock(body: () => void | Promise<void>): Promise<void> {
            vi.useFakeTimers();
            try {
                await body();
            }
            finally {
                vi.useRealTimers();
            }
        }

        it('onSave_Success_AdvancesToNextImageAfterDelay', async () => {
            const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false });

            await withMockClock(() => {
                component.onSave();
                saveBulk$.next({});

                vi.advanceTimersByTime(1999);
                expect(imageSelectorFake.nextImage).not.toHaveBeenCalled();

                vi.advanceTimersByTime(1);
                expect(imageSelectorFake.nextImage).toHaveBeenCalledTimes(1);
            });
        });

        it('onSave_ComponentDestroyedBeforeDelay_DoesNotAdvanceToNextImage', async () => {
            const { fixture: localFixture, component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false });

            await withMockClock(() => {
                component.onSave();
                saveBulk$.next({});

                localFixture.destroy();
                vi.advanceTimersByTime(2000);

                expect(imageSelectorFake.nextImage).not.toHaveBeenCalled();
            });
        });

        it('onSave_ResponseArrivesAfterDestroy_StillNotifiesButDoesNotAdvanceToNextImage', async () => {
            const { fixture: localFixture, component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false });
            // MatSnackBarModule bileşenin kendi ortam enjektöründe ayrı bir örnek sağlar; onu izleriz.
            const snackBarOpen = vi.spyOn(localFixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);

            await withMockClock(() => {
                component.onSave();
                localFixture.destroy();
                saveBulk$.next({});
                vi.advanceTimersByTime(2000);

                expect(snackBarOpen).toHaveBeenCalled();
                expect(imageSelectorFake.nextImage).not.toHaveBeenCalled();
            });
        });

        // Issue #573: yönergede kontrol karakteri → 400 `questions.passage.invalidInstruction`; sunucu mesajı gösterilir.
        it('onSave_InvalidPassageInstruction400_ShowsServerMessage', async () => {
            const { fixture: localFixture, component, saveBulk$ } = await setup({ isAdmin: false });
            const snackBarOpen = vi.spyOn(localFixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.onSave();
            saveBulk$.error(
                new HttpErrorResponse({
                    status: 400,
                    error: { success: false, invalidInput: true, message: 'Yönerge geçersiz: kontrol karakteri içermemeli.' },
                })
            );

            expect(snackBarOpen).toHaveBeenCalledWith('Yönerge geçersiz: kontrol karakteri içermemeli.', 'Tamam', expect.any(Object));
            expect(component.saving()).toBe(false);
        });

        it('onSave_ModelValidation400_ShowsFieldMessage', async () => {
            const { fixture: localFixture, component, saveBulk$ } = await setup({ isAdmin: false });
            const snackBarOpen = vi.spyOn(localFixture.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.onSave();
            saveBulk$.error(
                new HttpErrorResponse({ status: 400, error: { errors: { 'Passages[0].Instruction': ['Yönerge geçersiz.'] } } })
            );

            expect(snackBarOpen).toHaveBeenCalledWith('Yönerge geçersiz.', 'Tamam', expect.any(Object));
        });

        it('onSave_SavingAlreadyTrue_DoesNotCallSaveBulkAgain', async () => {
            const { component, questionServiceLocal } = await setup({ isAdmin: false });
            component.saving.set(true);

            component.onSave();

            expect(questionServiceLocal.saveBulk).not.toHaveBeenCalled();
        });

        it('onSave_TogglesSavingTrueThenFalse_AroundSuccessfulSaveBulkEmission', async () => {
            const { component, saveBulk$ } = await setup({ isAdmin: false });

            component.onSave();

            expect(component.saving()).toBe(true);

            saveBulk$.next({});

            expect(component.saving()).toBe(false);
        });

        it('onSave_SavingReturnsFalse_WhenSaveBulkErrors', async () => {
            const { component, saveBulk$ } = await setup({ isAdmin: false });

            component.onSave();

            expect(component.saving()).toBe(true);

            saveBulk$.error(new Error('boom'));

            expect(component.saving()).toBe(false);
        });

        it('onSave_AdminUser_DefaultSendToFix_DoesNotCallImageSelectorSendToFix_OnSuccess', async () => {
            const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: true, realmRoles: ['Admin'] });

            component.onSave();
            saveBulk$.next({});

            expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
        });

        it('onSave_AdminUser_ExplicitSendToFixTrue_CallsImageSelectorSendToFix_OnSuccess', async () => {
            const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: true, realmRoles: ['Admin'] });

            component.onSave(true);
            saveBulk$.next({});

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);
        });

        it('onSave_NonAdminUser_NeverCallsImageSelectorSendToFix_OnSuccess_RegardlessOfSendToFixArg', async () => {
            const subjectTrue = new Subject<any>();
            const { component, imageSelectorFake } = await setup({ isAdmin: false, saveBulk$: subjectTrue });

            component.onSave(true);
            subjectTrue.next({});

            expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
        });

        it('onSave_AdminUser_PageNotExcluded_SendsPageCompleteTrue', async () => {
            const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: true, realmRoles: ['Admin'] });

            component.onSave(true);
            saveBulk$.next({});

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledWith(true, false);
        });

        it('onSave_AdminUser_ExcludeFromTrainingChecked_SendsPageCompleteFalse', async () => {
            const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: true, realmRoles: ['Admin'] });
            component.excludeFromTraining.set(true);

            component.onSave(true);
            saveBulk$.next({});

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledWith(false, false);
        });

        it('sendToFix_AdminUser_ExcludeFromTrainingChecked_SendsPageCompleteFalse', async () => {
            const { component, imageSelectorFake } = await setup({ isAdmin: true, realmRoles: ['Admin'] });
            component.excludeFromTraining.set(true);

            component.sendToFix();

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);

            expect(imageSelectorFake.sendToFix).toHaveBeenCalledWith(false);
        });

        it('sendToFix_NonAdmin_DoesNotCallImageSelector', async () => {
            const { component, imageSelectorFake } = await setup({ isAdmin: false });

            component.sendToFix();

            expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
        });

        it('onImageChange_ResetsExcludeFromTraining', async () => {
            const { component } = await setup({ isAdmin: true, realmRoles: ['Admin'] });
            component.excludeFromTraining.set(true);

            component.onImageChange();

            expect(component.excludeFromTraining()).toBe(false);
        });

        it('excludeFromTraining_DefaultsToFalse', async () => {
            const { component } = await setup({ isAdmin: true, realmRoles: ['Admin'] });

            expect(component.excludeFromTraining()).toBe(false);
        });

        it('onSave_AdminUser_ExplicitSendToFixFalse_DoesNotCallImageSelectorSendToFix_OnSuccess', async () => {
            const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: true, realmRoles: ['Admin'] });

            component.onSave(false);
            saveBulk$.next({});

            expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
        });

        describe('canTrain (Admin / QuestionTrainer realm rolü)', () => {
            it('canTrain_QuestionTrainerRealmRole_ReturnsTrue', async () => {
                const { component } = await setup({ isAdmin: false, realmRoles: ['Teacher', 'QuestionTrainer'] });

                expect(component.canTrain).toBe(true);
            });

            it('canTrain_AdminRealmRole_ReturnsTrue', async () => {
                const { component } = await setup({ isAdmin: false, realmRoles: ['Admin'] });

                expect(component.canTrain).toBe(true);
            });

            it('canTrain_ProfileAdminWithoutRealmRole_ReturnsFalse', async () => {
                const { component } = await setup({ isAdmin: true, realmRoles: [] });

                expect(component.canTrain).toBe(false);
            });

            it('sendToFix_ProfileAdminWithoutRealmRole_DoesNotCallImageSelector', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: true, realmRoles: [] });

                component.sendToFix();

                expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
            });

            it('canTrain_TeacherOnly_ReturnsFalse', async () => {
                const { component } = await setup({ isAdmin: false, realmRoles: ['Teacher'] });

                expect(component.canTrain).toBe(false);
            });

            it('onSaveAndNew_QuestionTrainer_Success_DoesNotCallImageSelectorSendToFix', async () => {
                const { component, imageSelectorFake, questionServiceLocal, saveBulk$ } = await setup({
                    isAdmin: false,
                    realmRoles: ['Teacher', 'QuestionTrainer'],
                });

                component.onSaveAndNew();
                saveBulk$.next({});

                expect(questionServiceLocal.saveBulk).toHaveBeenCalledTimes(1);
                expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
            });

            it('onSaveAndFix_QuestionTrainer_Success_CallsImageSelectorSendToFix', async () => {
                const { component, imageSelectorFake, saveBulk$ } = await setup({
                    isAdmin: false,
                    realmRoles: ['Teacher', 'QuestionTrainer'],
                });

                component.onSaveAndFix();
                saveBulk$.next({});

                expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);

                expect(imageSelectorFake.sendToFix).toHaveBeenCalledWith(true, false);
            });

            it('onSaveAndFix_TeacherOnly_Success_SavesButDoesNotCallImageSelectorSendToFix', async () => {
                const { component, imageSelectorFake, questionServiceLocal, saveBulk$ } = await setup({
                    isAdmin: false,
                    realmRoles: ['Teacher'],
                });

                component.onSaveAndFix();
                saveBulk$.next({});

                expect(questionServiceLocal.saveBulk).toHaveBeenCalledTimes(1);
                expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
            });

            it('sendToFix_QuestionTrainer_CallsImageSelector', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['QuestionTrainer'] });

                component.sendToFix();

                expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);

                expect(imageSelectorFake.sendToFix).toHaveBeenCalledWith(true);
            });
        });

        // Sonraki sayfa hazır olana kadar işlem düğmeleri pasif (kayıt, eğitime gönderim, geçiş, analiz).
        describe('busy (meşgul durumu)', () => {
            /** image-selector'ın meşgul sinyallerini taşıyan sahte; sendToFix çağrısı gönderimi başlatır. */
            function withBusySignals(fake: any) {
                fake.sendingToFix = signal(false);
                fake.pageLoading = signal(false);
                fake.inProgress = signal(false);
                fake.sendToFix.mockImplementation(() => fake.sendingToFix.set(true));
                return fake;
            }

            it('busy_ReflectsSavingAndImageSelectorSignals', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                const fake = withBusySignals(imageSelectorFake);
                // Setter signal'i güncellesin diye yeniden atanır (setup sahteyi sinyaller eklenmeden atadı).
                component.imageSelector = fake;

                expect(component.busy()).toBe(false);
                component.saving.set(true);
                expect(component.busy()).toBe(true);
                component.saving.set(false);

                fake.inProgress.set(true);
                expect(component.busy()).toBe(true);
                fake.inProgress.set(false);

                fake.pageLoading.set(true);
                expect(component.busy()).toBe(true);
                fake.pageLoading.set(false);

                fake.sendingToFix.set(true);
                expect(component.busy()).toBe(true);
                expect(component.sendingToFix()).toBe(true);
            });

            it('sendToFix_DoubleClick_CallsImageSelectorOnce', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['QuestionTrainer'] });
                component.imageSelector = withBusySignals(imageSelectorFake);

                component.sendToFix();
                component.sendToFix();

                expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(1);
            });

            it('sendToFix_AfterSendFails_CanSendAgain', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['QuestionTrainer'] });
                const fake = withBusySignals(imageSelectorFake);
                component.imageSelector = fake;

                component.sendToFix();
                fake.sendingToFix.set(false); // image-selector hata yolunda meşgul durumunu kapatır

                component.sendToFix();

                expect(imageSelectorFake.sendToFix).toHaveBeenCalledTimes(2);
            });

            it('onSave_Success_BusyThroughAdvanceWindowUntilNextImage', async () => {
                const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false });

                await withMockClock(() => {
                    component.onSave();
                    saveBulk$.next({});

                    expect(component.saving()).toBe(false);
                    expect(component.advancing()).toBe(true);
                    expect(component.busy()).toBe(true);

                    vi.advanceTimersByTime(2000);

                    expect(component.advancing()).toBe(false);
                    expect(imageSelectorFake.nextImage).toHaveBeenCalledTimes(1);
                });
            });

            it('onSave_Error_DoesNotStartAdvanceWindow', async () => {
                const { component, saveBulk$ } = await setup({ isAdmin: false });

                component.onSave();
                saveBulk$.error({ status: 500 });

                expect(component.advancing()).toBe(false);
                expect(component.busy()).toBe(false);
            });

            it('saveEntryPoints_WhileBusy_DoNothing', async () => {
                const { component, tceFake, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                (imageSelectorFake as any).sendToStudyPage = vi.fn().mockName('sendToStudyPage');
                component.advancing.set(true);

                component.onSaveAndNew();
                component.onSaveAndFix();
                component.onSubmit();
                component.sendToWorkPages();

                expect(tceFake.onCreateAsync).not.toHaveBeenCalled();
                expect((imageSelectorFake as any).sendToStudyPage).not.toHaveBeenCalled();
            });

            it('navigation_WhileSavingCreatingAdvancingOrSending_IsBlocked', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                const fake = withBusySignals(imageSelectorFake);
                fake.previousImage = vi.fn().mockName('previousImage');
                fake.goToPage = vi.fn().mockName('goToPage');
                component.imageSelector = fake;
                const tryAll = () => {
                    component.previousImage();
                    component.nextImage();
                    component.goToPage('2');
                };

                for (const lock of [component.saving, component.creating, component.advancing, fake.sendingToFix]) {
                    lock.set(true);
                    tryAll();
                    lock.set(false);
                }

                expect(fake.previousImage).not.toHaveBeenCalled();
                expect(fake.nextImage).not.toHaveBeenCalled();
                expect(fake.goToPage).not.toHaveBeenCalled();
            });

            it('navigation_WhileAnalyzingOrPageLoading_IsAllowed', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false });
                const fake = withBusySignals(imageSelectorFake);
                fake.previousImage = vi.fn().mockName('previousImage');
                fake.goToPage = vi.fn().mockName('goToPage');
                component.imageSelector = fake;
                fake.inProgress.set(true);
                fake.pageLoading.set(true);

                component.previousImage();
                component.nextImage();
                component.goToPage('2');

                expect(component.busy()).toBe(true);
                expect(component.navigationBusy()).toBe(false);
                expect(fake.previousImage).toHaveBeenCalledTimes(1);
                expect(fake.nextImage).toHaveBeenCalledTimes(1);
                expect(fake.goToPage).toHaveBeenCalledTimes(1);
                expect(fake.goToPage).toHaveBeenCalledWith('2');
            });

            for (const action of ['onSaveAndNew', 'onSaveAndFix', 'onSubmit'] as const) {
                it(`${action}_DoubleClick_CreatesTestOnce`, async () => {
                    const { component, tceFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                    const create$ = new Subject<any>();
                    tceFake.onCreateAsync.mockReturnValue(create$.asObservable());

                    component[action]();
                    component[action]();

                    expect(tceFake.onCreateAsync).toHaveBeenCalledTimes(1);
                    expect(component.creating()).toBe(true);
                    expect(component.busy()).toBe(true);
                });
            }

            // Issue #599: hedef sayfa seçiliyse kayıt doğrudan ona gider, gömülü formdan test oluşturulmaz.
            for (const action of ['onSaveAndNew', 'onSaveAndFix', 'onSubmit'] as const) {
                it(`${action}_TargetSelected_SavesToTargetWithoutCreatingTest`, async () => {
                    const { component, tceFake, questionServiceLocal, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                    component.questionForm.get('testValue')?.setValue(3 as any, { emitEvent: false });

                    await withMockClock(() => {
                        component[action]();
                        component[action](); // çift tık: kayıt sürerken ikinci istek yok

                        expect(tceFake.onCreateAsync).not.toHaveBeenCalled();
                        expect(questionServiceLocal.saveBulk).toHaveBeenCalledTimes(1);
                        expect(imageSelectorFake.getRegions).toHaveBeenCalledWith(expect.objectContaining({ testId: 3 }));

                        saveBulk$.next({});
                        expect(tceFake.reloadComponent).toHaveBeenCalledWith(3);
                        vi.advanceTimersByTime(2000);
                        expect(imageSelectorFake.nextImage).toHaveBeenCalledTimes(1);
                    });
                });
            }

            it('onSaveAndNew_RouteIdWithoutFormTarget_SavesToRouteWorksheet', async () => {
                const { component, tceFake, imageSelectorFake } = await setup({ isAdmin: false });
                component.id = 12;

                component.onSaveAndNew();

                expect(tceFake.onCreateAsync).not.toHaveBeenCalled();
                expect(imageSelectorFake.getRegions).toHaveBeenCalledWith(expect.objectContaining({ testId: 12 }));
            });

            it('onSaveAndNew_NoTarget_CreatesTestThenSavesToIt', async () => {
                const { component, tceFake, imageSelectorFake } = await setup({ isAdmin: false });

                component.onSaveAndNew();

                expect(tceFake.onCreateAsync).toHaveBeenCalledTimes(1);
                expect(imageSelectorFake.getRegions).toHaveBeenCalledWith(expect.objectContaining({ testId: 7 }));
            });

            it('onSaveAndNew_CreateFails_ClearsCreating', async () => {
                const { component, tceFake, questionServiceLocal } = await setup({ isAdmin: false });
                const create$ = new Subject<any>();
                tceFake.onCreateAsync.mockReturnValue(create$.asObservable());

                component.onSaveAndNew();
                create$.error(new Error('boom'));

                expect(component.creating()).toBe(false);
                expect(component.busy()).toBe(false);
                expect(questionServiceLocal.saveBulk).not.toHaveBeenCalled();
            });

            it('onSaveAndNew_FetchCreatedTestFails_ClearsCreatingAndNotifies', async () => {
                const { fixture: f, component, questionServiceLocal } = await setup({ isAdmin: false });
                const snackBarOpen = vi.spyOn(f.debugElement.injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);
                const testServiceLocal = f.debugElement.injector.get(TestService) as MockedObject<TestService>;
                testServiceLocal.get.mockReturnValue(throwError(() => new Error('fetch')));

                component.onSaveAndNew();

                expect(component.creating()).toBe(false);
                expect(component.busy()).toBe(false);
                expect(questionServiceLocal.saveBulk).not.toHaveBeenCalled();
                expect(snackBarOpen).toHaveBeenCalledWith(questionTr.canvas.testFetchError, expect.any(String), expect.any(Object));
            });

            it('onSaveAndFix_SaveSucceedsTrainingFails_StillAdvancesAndBusyClears', async () => {
                const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false, realmRoles: ['QuestionTrainer'] });
                const fake = withBusySignals(imageSelectorFake);
                component.imageSelector = fake;

                await withMockClock(() => {
                    component.onSaveAndFix();
                    saveBulk$.next({});
                    expect(fake.sendToFix).toHaveBeenCalledTimes(1);
                    expect(fake.sendToFix).toHaveBeenCalledWith(true, false);
                    expect(component.busy()).toBe(true);

                    fake.sendingToFix.set(false); // image-selector eğitim hatasında meşgul durumunu kapatır

                    vi.advanceTimersByTime(2000);
                    expect(fake.nextImage).toHaveBeenCalledTimes(1);
                    expect(component.busy()).toBe(false);
                });
            });

            it('onSaveAndFix_TrainingStillRunningAfterDelay_StillAdvances', async () => {
                const { component, imageSelectorFake, saveBulk$ } = await setup({ isAdmin: false, realmRoles: ['QuestionTrainer'] });
                const fake = withBusySignals(imageSelectorFake);
                component.imageSelector = fake;

                await withMockClock(() => {
                    component.onSaveAndFix();
                    saveBulk$.next({});

                    vi.advanceTimersByTime(2000);

                    // Geçişi kayıt zamanlayıcısı yapar; süren eğitim isteğinin gezinme kilidi onu engellemez.
                    expect(fake.nextImage).toHaveBeenCalledTimes(1);
                    expect(component.navigationBusy()).toBe(true);
                });
            });

            describe('şablon', () => {
                async function render() {
                    const ctx = await setup({ isAdmin: false, realmRoles: ['Admin'], stubTestCreate: true });
                    // Gerçek alt bileşenler oluşur; ilk geçişte sahte kullanılmasın, ViewChild gerçek image-selector'ı atar.
                    ctx.component.imageSelector = undefined as any;
                    ctx.fixture.detectChanges();
                    await ctx.fixture.whenStable();
                    const el: HTMLElement = ctx.fixture.nativeElement;
                    const trainButton = () => Array.from(el.querySelectorAll<HTMLButtonElement>('button.qc-btn--ghost')).find((b) => b.textContent!.includes(questionTr.canvas.train) || b.textContent!.includes(questionTr.canvas.sendingToFix))!;
                    const saveButton = () => el.querySelector<HTMLButtonElement>('.qc-split__main')!;
                    const caretButton = () => el.querySelector<HTMLButtonElement>('.qc-split__caret')!;
                    return { ...ctx, el, trainButton, saveButton, caretButton };
                }

                // E2E (#581): kutu 3'e yazılan değer, boş kutu 4'e geçince DOM'da kalıp onunkine ekleniyordu ("29023").
                it('slotEditor_SwitchToEmptySlot_ShowsOnlyThatSlotsValue', async () => {
                    const { fixture: f, component, el } = await render();
                    const sel = component.imageSelector;
                    vi.spyOn(sel, 'drawImage').mockReturnValue(undefined);
                    sel.regions.set([{ name: 'Soru 1', x: 0, y: 0, width: 400, height: 300, answers: [], passageId: '0', imageId: '', imageUrl: '', id: 0, isExample: false, exampleAnswer: null }] as any);
                    sel.interactionTypes.set('Soru 1', 'slotFill');
                    (sel as any).addSlotFromDrag(0, 10, 10, 60, 40);
                    (sel as any).addSlotFromDrag(0, 10, 60, 60, 90);
                    const [a, b] = sel.ensureSlotFillState('Soru 1').slots.map((x) => x.id);
                    sel.selectSlot('Soru 1', a);
                    f.detectChanges();
                    const valueInput = () => el.querySelector<HTMLInputElement>('[data-testid="slot-value"]')!;
                    const type = (text: string) => {
                        const input = valueInput();
                        input.value = input.value + text;
                        input.dispatchEvent(new Event('change'));
                    };

                    type('290');
                    // Yazma ve kutu değişimi aynı değişiklik algılama turunda (olay birleştirme): arada yeniden çizim yok.
                    sel.selectSlot('Soru 1', b);
                    f.detectChanges();
                    expect(valueInput().value).toBe('');
                    type('23');
                    f.detectChanges();

                    const slots = sel.ensureSlotFillState('Soru 1').slots;
                    expect(slots.find((x) => x.id === a)!.value).toBe('290');
                    expect(slots.find((x) => x.id === b)!.value).toBe('23');
                });

                // Issue #583: "Açık uçlu" yalnız etkinlik sayfasında; sınırlar + örnek cevap / rubrik plana / çözüme gider.
                it('openText_TypeOnlyInActivityContext_InspectorBuildsPlanAndSolution', async () => {
                    const { fixture: f, component, el } = await render();
                    const sel = component.imageSelector;
                    vi.spyOn(sel, 'drawImage').mockReturnValue(undefined);
                    sel.regions.set([{ name: 'Soru 1', x: 0, y: 0, width: 400, height: 300, answers: [], passageId: '0', imageId: '', imageUrl: '', id: 0, isExample: false, exampleAnswer: null }] as any);
                    (sel as any).selectedRegion = 0;
                    f.detectChanges();
                    expect(el.querySelector('[data-testid="type-open-text"]')).toBeNull();

                    component.onOptionSelected({ id: 7, name: 'Etkinlik', subtitle: 'E', kind: 1 } as any);
                    f.detectChanges();
                    const typeButton = el.querySelector<HTMLButtonElement>('[data-testid="type-open-text"]');
                    expect(typeButton).not.toBeNull();
                    typeButton!.click();
                    f.detectChanges();

                    expect(sel.getInteractionType('Soru 1')).toBe('openText');
                    expect(el.querySelector('[data-testid="inspector-open-text"]')).not.toBeNull();
                    expect(el.querySelector('[data-testid="open-text-activity-only"]')).toBeNull();
                    const change = (testId: string, value: string) => {
                        const field = el.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[data-testid="${testId}"]`)!;
                        field.value = value;
                        field.dispatchEvent(new Event('change'));
                        f.detectChanges();
                    };
                    change('open-text-min', '20');
                    change('open-text-max', '120');
                    change('open-text-lines', '6');
                    change('open-text-sample', '  Kitaptaki örnek cevap.  ');
                    change('open-text-rubric', 'İki neden yazmalı.');

                    expect(JSON.parse((sel as any).buildInteractionPlanJson('Soru 1'))).toEqual({
                        version: 1, type: 'openText', minWords: 20, maxWords: 120, lines: 6,
                    });
                    expect(JSON.parse((sel as any).buildInteractionSolutionJson('Soru 1'))).toEqual({
                        sampleAnswer: 'Kitaptaki örnek cevap.', rubric: 'İki neden yazmalı.',
                    });
                    expect(sel.validateQuestions({ requireCorrectAnswer: true })).toEqual([]);

                    // Geçersiz sınır kaydı engeller; test sayfasına dönülürse uyarı görünür.
                    change('open-text-min', '500');
                    const issues = sel.validateQuestions({ requireCorrectAnswer: true });
                    expect(issues).toHaveLength(1);
                    expect(issues[0].messages).toHaveLength(1);
                    component.onOptionSelected({ id: 8, name: 'Test', subtitle: 'T', kind: 0 } as any);
                    f.detectChanges();
                    expect(el.querySelector('[data-testid="open-text-activity-only"]')).not.toBeNull();
                });

                // Aynı kolon id'si (c1) iki soruda: soru değişince ilk sorunun DOM değeri ikinciye taşınmamalı.
                it('inspector_SwitchQuestion_MatrixColumnLabelShowsOwnValue', async () => {
                    const { fixture: f, component, el } = await render();
                    const sel = component.imageSelector;
                    vi.spyOn(sel, 'drawImage').mockReturnValue(undefined);
                    const base = { y: 0, width: 400, height: 300, answers: [], passageId: '0', imageId: '', imageUrl: '', id: 0, isExample: false, exampleAnswer: null };
                    sel.regions.set([{ ...base, name: 'Soru 1', x: 0 }, { ...base, name: 'Soru 2', x: 500 }] as any);
                    for (const name of ['Soru 1', 'Soru 2']) {
                        sel.interactionTypes.set(name, 'matrixChoice');
                        sel.ensureMatrixState(name);
                        sel.setMatrixColumnLabel(name, 'c1', '');
                    }
                    (sel as any).selectedRegion = 0;
                    f.detectChanges();
                    const firstLabel = () => el.querySelector<HTMLInputElement>('.qc-matrix__col-input')!;
                    firstLabel().value = 'Doğru';
                    firstLabel().dispatchEvent(new Event('change'));

                    (sel as any).selectedRegion = 1;
                    f.detectChanges();

                    expect(firstLabel().value).toBe('');
                    expect(sel.ensureMatrixState('Soru 1').columns[0].label).toBe('Doğru');
                    expect(sel.ensureMatrixState('Soru 2').columns[0].label).toBe('');
                });

                it('buttons_Idle_AreEnabled', async () => {
                    const { trainButton, saveButton, caretButton } = await render();

                    expect(trainButton().disabled).toBe(false);
                    expect(saveButton().disabled).toBe(false);
                    expect(caretButton().disabled).toBe(false);
                });

                it('buttons_WhileAnalyzing_AreDisabled', async () => {
                    const { fixture: f, component, trainButton, saveButton, caretButton } = await render();

                    component.imageSelector.inProgress.set(true);
                    f.detectChanges();

                    expect(trainButton().disabled).toBe(true);
                    expect(saveButton().disabled).toBe(true);
                    expect(caretButton().disabled).toBe(true);
                });

                it('buttons_DuringAdvanceWindow_AreDisabled', async () => {
                    const { fixture: f, component, saveButton, caretButton } = await render();

                    component.advancing.set(true);
                    f.detectChanges();

                    expect(saveButton().disabled).toBe(true);
                    expect(caretButton().disabled).toBe(true);
                });

                it('trainButton_WhileSending_ShowsSpinnerTextAndAriaBusy', async () => {
                    const { fixture: f, component, trainButton } = await render();

                    component.imageSelector.sendingToFix.set(true);
                    f.detectChanges();

                    const button = trainButton();
                    expect(button.disabled).toBe(true);
                    expect(button.getAttribute('aria-busy')).toBe('true');
                    expect(button.querySelector('mat-spinner')).not.toBeNull();
                    expect(button.textContent).toContain(questionTr.canvas.sendingToFix);
                });

                it('pageNavigation_DuringAdvanceWindow_IsDisabled_ButNotDuringAnalysis', async () => {
                    const { fixture: f, component, el } = await render();
                    component.imageSelector.imageFiles = [new File(['a'], 'p1.png'), new File(['b'], 'p2.png')];
                    f.detectChanges();
                    const navControls = () => [
                        ...Array.from(el.querySelectorAll<HTMLButtonElement>('.qc-thumb')),
                        ...Array.from(el.querySelectorAll<HTMLButtonElement | HTMLInputElement>('.qc-pagejump button, .qc-pagejump input')),
                    ];
                    expect(navControls().length).toBe(5);

                    component.imageSelector.inProgress.set(true);
                    f.detectChanges();
                    expect(navControls().every((c) => !c.disabled)).toBe(true);

                    component.advancing.set(true);
                    f.detectChanges();
                    expect(navControls().every((c) => c.disabled)).toBe(true);
                    expect(component.imageSelector.navigationLocked).toBe(true);
                });
            });
        });

        describe('tespit kaynağı (issue #473)', () => {
            /** image-selector'ın meşgul / JSON sinyallerini taşıyan sahte; varsayılan: boşta, normal mod. */
            function withIdleSignals(fake: any) {
                fake.sendingToFix = signal(false);
                fake.pageLoading = signal(false);
                fake.inProgress = signal(false);
                fake.coordinateJsonMode = () => false;
                return fake;
            }

            it('selectDetectionSource_IdleNormalMode_DelegatesToDetection', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                component.imageSelector = withIdleSignals(imageSelectorFake);
                const selectSource = vi.spyOn(component.detection, 'selectSource').mockReturnValue(undefined);

                component.selectDetectionSource('Model');

                expect(selectSource).toHaveBeenCalledTimes(1);

                expect(selectSource).toHaveBeenCalledWith('Model');
            });

            it('selectDetectionSource_WhileBusy_DoesNothing', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                const fake = withIdleSignals(imageSelectorFake);
                component.imageSelector = fake;
                fake.inProgress.set(true);
                const selectSource = vi.spyOn(component.detection, 'selectSource').mockReturnValue(undefined);

                component.selectDetectionSource('Ai');

                expect(component.busy()).toBe(true);
                expect(selectSource).not.toHaveBeenCalled();
            });

            it('selectDetectionSource_CoordinateJsonMode_DoesNothing', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                const fake = withIdleSignals(imageSelectorFake);
                fake.coordinateJsonMode = () => true;
                component.imageSelector = fake;
                const selectSource = vi.spyOn(component.detection, 'selectSource').mockReturnValue(undefined);

                component.selectDetectionSource('Model');

                expect(selectSource).not.toHaveBeenCalled();
            });

            it('selectAiModel_WhileBusy_DoesNothing', async () => {
                const { component, imageSelectorFake } = await setup({ isAdmin: false, realmRoles: ['Admin'] });
                const fake = withIdleSignals(imageSelectorFake);
                component.imageSelector = fake;
                fake.pageLoading.set(true);
                const setAiModel = vi.spyOn(component.detection, 'setAiModel').mockReturnValue(undefined);

                component.selectAiModel(HAIKU_ID);

                expect(setAiModel).not.toHaveBeenCalled();
            });

            it('onImageChange_CallsDetectionResetPage', async () => {
                const { component } = await setup({ isAdmin: false, realmRoles: ['Teacher'] });
                const resetPage = vi.spyOn(component.detection, 'resetPage').mockReturnValue(undefined);

                component.onImageChange();

                expect(resetPage).toHaveBeenCalledTimes(1);
            });

            describe('şablon', () => {
                it('sourceGroup_UserWithoutClaudeModels_IsNotRendered', async () => {
                    const ctx = await setup({ isAdmin: false, realmRoles: ['Teacher'], stubTestCreate: true });
                    ctx.component.imageSelector = undefined as any;
                    // Kaynak grubu sayfa araç çubuğunda: yalnız sayfa yüklüyken render edilir.
                    Object.defineProperty(ctx.component, 'pageFiles', { get: () => [new File([''], 'p1.png')] });
                    ctx.fixture.detectChanges();
                    await ctx.fixture.whenStable();

                    expect(ctx.fixture.nativeElement.querySelector('.qc-source')).toBeNull();
                });

                it('sourceGroup_ModelsAvailable_IsRendered', async () => {
                    // Kontrol (positive control): model listesi varken grup görünmeli; yoksa negatif test anlamsız olur.
                    const ctx = await setup({ isAdmin: false, realmRoles: ['QuestionTrainer'], stubTestCreate: true });
                    ctx.component.detection.aiModels.set([
                        { id: HAIKU_ID, displayName: 'Haiku 5.5', inputUsdPerMTok: 1, outputUsdPerMTok: 5, isDefault: true },
                    ]);
                    ctx.component.imageSelector = undefined as any;
                    // Kaynak grubu sayfa araç çubuğunda: yalnız sayfa yüklüyken render edilir.
                    Object.defineProperty(ctx.component, 'pageFiles', { get: () => [new File([''], 'p1.png')] });
                    ctx.fixture.detectChanges();
                    await ctx.fixture.whenStable();

                    expect(ctx.fixture.nativeElement.querySelector('.qc-source')).not.toBeNull();
                });
            });
        });

        describe('kayıt öncesi soru doğrulaması', () => {
            for (const action of ['onSaveAndNew', 'onSaveAndFix'] as const) {
                it(`${action}_InvalidQuestions_DoesNotCreateTestOrSave`, async () => {
                    const { component, imageSelectorFake, tceFake, questionServiceLocal } = await setup({
                        isAdmin: false,
                        realmRoles: ['QuestionTrainer'],
                    });
                    imageSelectorFake.ensureQuestionsValid.mockReturnValue(false);

                    component[action]();

                    expect(imageSelectorFake.ensureQuestionsValid).toHaveBeenCalledTimes(1);

                    expect(imageSelectorFake.ensureQuestionsValid).toHaveBeenCalledWith({ requireCorrectAnswer: true });
                    expect(tceFake.onCreateAsync).not.toHaveBeenCalled();
                    expect(questionServiceLocal.saveBulk).not.toHaveBeenCalled();
                    expect(imageSelectorFake.sendToFix).not.toHaveBeenCalled();
                });

                it(`${action}_ValidQuestions_CreatesTestAndSaves`, async () => {
                    const { component, imageSelectorFake, tceFake, questionServiceLocal, saveBulk$ } = await setup({
                        isAdmin: false,
                        realmRoles: ['Teacher'],
                    });

                    component[action]();
                    saveBulk$.next({});

                    expect(imageSelectorFake.ensureQuestionsValid).toHaveBeenCalledWith({ requireCorrectAnswer: true });
                    expect(tceFake.onCreateAsync).toHaveBeenCalledTimes(1);
                    expect(questionServiceLocal.saveBulk).toHaveBeenCalledTimes(1);
                });
            }

            it('onSave_InvalidQuestions_DoesNotSave', async () => {
                const { component, imageSelectorFake, questionServiceLocal } = await setup({ isAdmin: false });
                imageSelectorFake.ensureQuestionsValid.mockReturnValue(false);

                component.onSave(false);

                expect(questionServiceLocal.saveBulk).not.toHaveBeenCalled();
                expect(component.saving()).toBe(false);
            });
        });
    });
});
