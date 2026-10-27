import type { Mock } from "vitest";
import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import { DIFFICULTY_RANGES, QuestionBankPanelComponent, } from './question-bank-panel.component';
import { QuestionBankPreviewDialogComponent } from './question-bank-preview-dialog.component';
import { ClassificationSelection, ClassificationSelectorComponent } from '../classification-selector/classification-selector.component';
import { QuestionBankService } from '../../../services/question-bank.service';
import { QuestionBankItem, QuestionBankPage } from '../../../models/question-bank';
import { ClassificationSource } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import qbTr from '../../../../../public/i18n/question-bank/tr.json';
import qbEn from '../../../../../public/i18n/question-bank/en.json';

/** Gerçek seçici kendi servislerinden HTTP çağırır; panel testinde yalnız çıktısı taklit edilir. */
@Component({
    selector: 'app-classification-selector',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.Eager,
    template: '',
})
class ClassificationSelectorStub {
    @Input()
    layout: 'chips' | 'selects' = 'chips';
    @Input()
    initialSelection: ClassificationSelection | null = null;
    @Output()
    selectionChange = new EventEmitter<ClassificationSelection>();
}

function makeItem(id: number, overrides: Partial<QuestionBankItem> = {}): QuestionBankItem {
    return {
        id,
        imageUrl: `https://img.test/${id}.webp`,
        subjectId: 1,
        subjectName: 'Matematik',
        topicId: 2,
        topicName: 'Kesirler',
        gradeId: 5,
        subTopics: [],
        difficultyLevel: 5,
        classificationSource: ClassificationSource.Human,
        isClassified: true,
        hasCorrectAnswer: true,
        testCount: 0,
        isOwn: true,
        canLink: true,
        createTime: '2026-10-01T00:00:00Z',
        ...overrides,
    };
}

function makePage(items: QuestionBankItem[], total = items.length): QuestionBankPage {
    return { items, total, skip: 0, take: 24 };
}

const EMPTY_PAGE = makePage([]);

describe('QuestionBankPanelComponent (issue #471)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<QuestionBankPanelComponent>;
    let component: QuestionBankPanelComponent;
    let search: Mock;
    let approveSolution: Mock;
    let dialogOpen: Mock;

    beforeEach(() => {
        search = vi.fn().mockName('search').mockReturnValue(of(EMPTY_PAGE));
        approveSolution = vi.fn().mockName('approveSolution');
        dialogOpen = vi.fn().mockName('open');

        TestBed.overrideComponent(QuestionBankPanelComponent, {
            remove: { imports: [ClassificationSelectorComponent] },
            add: { imports: [ClassificationSelectorStub] },
        });
        TestBed.configureTestingModule({
            imports: [QuestionBankPanelComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr, 'question-bank/en': qbEn } })],
            providers: [
                provideNoopAnimations(),
                { provide: QuestionBankService, useValue: { search, approveSolution } },
                { provide: MatDialog, useValue: { open: dialogOpen } },
            ],
        });
        fixture = TestBed.createComponent(QuestionBankPanelComponent);
        component = fixture.componentInstance;
    });

    /** Signal değişikliğini (toObservable effect) işletir, 250 ms debounce'u ilerletir ve sonucu render eder. */
    async function settle(): Promise<void> {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(250);
        fixture.detectChanges();
    }

    function lastFilter(): Record<string, unknown> {
        return vi.mocked(search).mock.lastCall![0] as Record<string, unknown>;
    }

    describe('filterLayout', () => {
        const selector = () =>
            fixture.debugElement.query(By.directive(ClassificationSelectorStub)).componentInstance as ClassificationSelectorStub;

        it('filterLayout_Default_PassesChipsToClassificationSelector', async () => {
            await settle();

            expect(selector().layout).toBe('chips');
        });

        it('filterLayout_Selects_PassesSelectsToClassificationSelector', async () => {
            fixture.componentRef.setInput('filterLayout', 'selects');
            await settle();

            expect(selector().layout).toBe('selects');
        });
    });

    describe('initialClassification', () => {
        const selector = () =>
            fixture.debugElement.query(By.directive(ClassificationSelectorStub)).componentInstance as ClassificationSelectorStub;
        const INITIAL: ClassificationSelection = { gradeId: 5, subjectId: 10, topicId: 100, subtopicIds: [1000] };

        it('initialClassification_Set_FirstSearchUsesItAndSelectorReceivesIt', async () => {
            fixture.componentRef.setInput('initialClassification', INITIAL);
            await settle();

            expect(search).toHaveBeenCalledTimes(1);
            expect(lastFilter()).toMatchObject({ gradeId: 5, subjectId: 10, topicId: 100, subTopicIds: [1000], skip: 0 });
            expect(selector().initialSelection).toEqual(INITIAL);
        });

        it('initialClassification_UserChangesSelection_OverridesInitialValue', async () => {
            fixture.componentRef.setInput('initialClassification', INITIAL);
            await settle();

            selector().selectionChange.emit({ gradeId: 6, subjectId: null, topicId: null, subtopicIds: [] });
            await settle();

            expect(lastFilter()).toMatchObject({ gradeId: 6, subjectId: null, topicId: null, subTopicIds: [] });
        });
    });

    describe('filter computed (pure mapping)', () => {
        it('filter_DefaultState_ReturnsMineScopeAndNoConstraints', () => {
            expect(component.filter()).toEqual({
                scope: 'mine',
                gradeId: null,
                subjectId: null,
                topicId: null,
                subTopicIds: [],
                minDifficulty: null,
                maxDifficulty: null,
                source: null,
                classified: null,
                hasCorrectAnswer: null,
                inTest: null,
                solutionStatus: null,
                skip: 0,
                take: 24,
            });
        });

        it('filter_EasyDifficulty_MapsToOneToThree', () => {
            component.setDifficulty('easy');
            expect(component.filter().minDifficulty).toBe(1);
            expect(component.filter().maxDifficulty).toBe(3);
        });

        it('filter_MediumDifficulty_MapsToFourToSeven', () => {
            component.setDifficulty('medium');
            expect(component.filter().minDifficulty).toBe(4);
            expect(component.filter().maxDifficulty).toBe(7);
        });

        it('filter_HardDifficulty_MapsToEightToTen', () => {
            component.setDifficulty('hard');
            expect(component.filter().minDifficulty).toBe(8);
            expect(component.filter().maxDifficulty).toBe(10);
        });

        it('filter_DifficultyBackToAny_ClearsRange', () => {
            component.setDifficulty('hard');
            component.setDifficulty('any');
            expect(component.filter().minDifficulty).toBeNull();
            expect(component.filter().maxDifficulty).toBeNull();
        });

        it('DIFFICULTY_RANGES_GroupsCoverTenPointScaleWithoutGaps', () => {
            expect(DIFFICULTY_RANGES.easy).toEqual([1, 3]);
            expect(DIFFICULTY_RANGES.medium).toEqual([4, 7]);
            expect(DIFFICULTY_RANGES.hard).toEqual([8, 10]);
        });

        it('filter_SourceAi_SendsAiAndAnyClearsIt', () => {
            component.setSource('AI');
            expect(component.filter().source).toBe('AI');
            component.setSource('any');
            expect(component.filter().source).toBeNull();
        });

        it('filter_SourceHuman_SendsHuman', () => {
            component.setSource('Human');
            expect(component.filter().source).toBe('Human');
        });

        it('filter_StatusClassifiedAndUnclassified_MapToBooleans', () => {
            component.setStatus('classified');
            expect(component.filter().classified).toBe(true);
            component.setStatus('unclassified');
            expect(component.filter().classified).toBe(false);
            component.setStatus('any');
            expect(component.filter().classified).toBeNull();
        });

        it('filter_AnswerYesAndNo_MapToBooleans', () => {
            component.setAnswer('yes');
            expect(component.filter().hasCorrectAnswer).toBe(true);
            component.setAnswer('no');
            expect(component.filter().hasCorrectAnswer).toBe(false);
            component.setAnswer('any');
            expect(component.filter().hasCorrectAnswer).toBeNull();
        });

        it('filter_InTestYesNoAny_MapToThreeStates', () => {
            component.setInTest('yes');
            expect(component.filter().inTest).toBe(true);
            component.setInTest('no');
            expect(component.filter().inTest).toBe(false);
            component.setInTest('any');
            expect(component.filter().inTest).toBeNull();
        });

        it('setInTest_ResetsToFirstPage', () => {
            component.onPage({ pageIndex: 2, pageSize: 24, length: 100, previousPageIndex: 0 });
            component.setInTest('no');
            expect(component.pageIndex()).toBe(0);
            expect(component.filter().skip).toBe(0);
        });

        it('filter_ScopeAll_SendsAll', () => {
            component.setScope('all');
            expect(component.filter().scope).toBe('all');
        });

        it('filter_ClassificationSelection_MapsIdsAndSubTopics', () => {
            component.onClassificationChange({ gradeId: 5, subjectId: 7, topicId: 9, subtopicIds: [11, 12] });
            const f = component.filter();
            expect(f.gradeId).toBe(5);
            expect(f.subjectId).toBe(7);
            expect(f.topicId).toBe(9);
            expect(f.subTopicIds).toEqual([11, 12]);
        });

        it('filter_ClassificationCleared_ResetsIdsToNullAndEmptyList', () => {
            component.onClassificationChange({ gradeId: 5, subjectId: 7, topicId: 9, subtopicIds: [11] });
            component.onClassificationChange({ gradeId: null, subjectId: null, topicId: null, subtopicIds: [] });
            const f = component.filter();
            expect(f.gradeId).toBeNull();
            expect(f.subjectId).toBeNull();
            expect(f.topicId).toBeNull();
            expect(f.subTopicIds).toEqual([]);
        });
    });

    describe('search pipeline', () => {
        it('search_AfterInit_CallsServiceOnceWithDefaultFilterAfterDebounce', async () => {
            fixture.detectChanges();
            expect(search).not.toHaveBeenCalled();

            await vi.advanceTimersByTimeAsync(250);
            expect(search).toHaveBeenCalledTimes(1);
            expect(lastFilter()['scope']).toBe('mine');
            expect(lastFilter()['skip']).toBe(0);
            expect(lastFilter()['take']).toBe(24);
            await vi.advanceTimersByTimeAsync(0);
            fixture.destroy();
        });

        it('search_RapidFilterChanges_DebouncedIntoSingleRequest', async () => {
            await settle();
            search.mockClear();

            component.setSource('AI');
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(100);
            component.setStatus('classified');
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(100);
            component.setAnswer('yes');
            fixture.detectChanges();

            await vi.advanceTimersByTimeAsync(249);
            expect(search).not.toHaveBeenCalled();

            await vi.advanceTimersByTimeAsync(1);
            expect(search).toHaveBeenCalledTimes(1);
            expect(lastFilter()['source']).toBe('AI');
            expect(lastFilter()['classified']).toBe(true);
            expect(lastFilter()['hasCorrectAnswer']).toBe(true);
            fixture.destroy();
        });

        it('search_ClassificationSelection_SendsIdsThroughService', async () => {
            await settle();
            search.mockClear();

            component.onClassificationChange({ gradeId: 5, subjectId: 7, topicId: 9, subtopicIds: [11, 12] });
            await settle();

            expect(lastFilter()['gradeId']).toBe(5);
            expect(lastFilter()['subjectId']).toBe(7);
            expect(lastFilter()['topicId']).toBe(9);
            expect(lastFilter()['subTopicIds']).toEqual([11, 12]);
            fixture.destroy();
        });

        it('search_SwitchMap_StaleResponseIgnoredWhenNewerSearchStarted', async () => {
            const first = new Subject<QuestionBankPage>();
            const second = new Subject<QuestionBankPage>();
            search.mockReturnValueOnce(first).mockReturnValueOnce(second);

            await settle();
            component.setSource('AI');
            await settle();

            second.next(makePage([makeItem(2)]));
            first.next(makePage([makeItem(1)]));

            expect(component.page().items.map((i) => i.id)).toEqual([2]);
            expect(first.observed).toBe(false);
            fixture.destroy();
        });

        it('loading_RequestInFlight_TrueUntilResponse', async () => {
            const pending = new Subject<QuestionBankPage>();
            search.mockReturnValue(pending);

            await settle();
            expect(component.loading()).toBe(true);
            fixture.detectChanges();
            expect(fixture.nativeElement.querySelector('mat-progress-bar')).not.toBeNull();

            pending.next(makePage([makeItem(1)]));
            pending.complete();
            expect(component.loading()).toBe(false);
            fixture.detectChanges();
            expect(fixture.nativeElement.querySelector('mat-progress-bar')).toBeNull();
            fixture.destroy();
        });
    });

    describe('paging and filter reset', () => {
        it('onPage_NewPage_UpdatesSkipAndTakeInRequest', async () => {
            await settle();
            search.mockClear();

            component.onPage({ pageIndex: 2, pageSize: 12, length: 100, previousPageIndex: 0 });
            await settle();

            expect(component.pageIndex()).toBe(2);
            expect(component.pageSize()).toBe(12);
            expect(lastFilter()['skip']).toBe(24);
            expect(lastFilter()['take']).toBe(12);
            fixture.destroy();
        });

        const resetters: [
            string,
            () => void
        ][] = [
            ['setScope', () => component.setScope('all')],
            ['setSource', () => component.setSource('Human')],
            ['setStatus', () => component.setStatus('unclassified')],
            ['setAnswer', () => component.setAnswer('no')],
            ['setDifficulty', () => component.setDifficulty('easy')],
            ['onClassificationChange', () => component.onClassificationChange({ gradeId: 1, subjectId: null, topicId: null, subtopicIds: [] })],
        ];

        for (const [name, change] of resetters) {
            it(`${name}_OnSecondPage_ResetsPageIndexToZero`, () => {
                component.pageIndex.set(3);
                change();
                expect(component.pageIndex()).toBe(0);
                expect(component.filter().skip).toBe(0);
            });
        }
    });

    describe('error state', () => {
        it('search_ServiceError_ShowsErrorMessageAndEmptyPage', async () => {
            search.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

            await settle();

            expect(component.error()).toBe(true);
            expect(component.loading()).toBe(false);
            expect(component.page().items).toEqual([]);
            const alert = fixture.nativeElement.querySelector('.qb-message--error') as HTMLElement;
            expect(alert).not.toBeNull();
            expect(alert.textContent?.trim()).toBe(qbTr.results.error);
            expect(fixture.nativeElement.querySelectorAll('.qb-card').length).toBe(0);
            fixture.destroy();
        });

        it('search_RecoversAfterError_ClearsErrorOnNextSearch', async () => {
            search.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
            await settle();
            expect(component.error()).toBe(true);

            search.mockReturnValue(of(makePage([makeItem(1)])));
            component.setSource('AI');
            await settle();

            expect(component.error()).toBe(false);
            expect(component.page().items.length).toBe(1);
            fixture.destroy();
        });
    });

    describe('results rendering', () => {
        it('template_ItemsReturned_RendersOneCardPerItem', async () => {
            search.mockReturnValue(of(makePage([makeItem(1), makeItem(2), makeItem(3)])));
            await settle();

            expect(fixture.nativeElement.querySelectorAll('.qb-card').length).toBe(3);
            fixture.destroy();
        });

        it('template_EmptyResult_ShowsEmptyMessage', async () => {
            await settle();

            const message = fixture.nativeElement.querySelector('.qb-message') as HTMLElement;
            expect(message.textContent?.trim()).toBe(qbTr.results.empty);
            fixture.destroy();
        });

        it('template_AiClassifiedItem_ShowsAiIndicator', async () => {
            search.mockReturnValue(of(makePage([makeItem(1, { classificationSource: ClassificationSource.AI }), makeItem(2)])));
            await settle();

            expect(fixture.nativeElement.querySelectorAll('.qb-card__ai').length).toBe(1);
            fixture.destroy();
        });

        it('template_BookClassifiedItem_ShowsBookIndicator', async () => {
            search.mockReturnValue(of(makePage([makeItem(1, { classificationSource: ClassificationSource.Book }), makeItem(2)])));
            await settle();

            const icons = fixture.nativeElement.querySelectorAll('.qb-card__book') as NodeListOf<HTMLElement>;
            expect(icons.length).toBe(1);
            expect(icons[0].textContent?.trim()).toBe('menu_book');
            fixture.destroy();
        });

        it('template_UnclassifiedAndNoAnswerItem_ShowsWarningTags', async () => {
            search.mockReturnValue(of(makePage([makeItem(1, { isClassified: false, hasCorrectAnswer: false })])));
            await settle();

            const tags = Array.from(fixture.nativeElement.querySelectorAll('.qb-tag--warning') as NodeListOf<HTMLElement>).map((t) => t.textContent?.trim());
            expect(tags).toEqual([qbTr.card.unclassified, qbTr.card.noAnswer]);
            fixture.destroy();
        });

        it('template_TotalCount_ShowsTranslatedTotal', async () => {
            search.mockReturnValue(of(makePage([makeItem(1)], 42)));
            await settle();

            const summary = fixture.nativeElement.querySelector('.qb-summary span') as HTMLElement;
            expect(summary.textContent?.trim()).toBe('42 soru');
            fixture.destroy();
        });
    });

    describe('selection (selectable mode)', () => {
        beforeEach(() => {
            search.mockReturnValue(of(makePage([makeItem(1), makeItem(2)])));
        });

        it('toggleSelection_NotSelectable_DoesNothing', async () => {
            await settle();
            const emit = vi.spyOn(component.selectionChange, 'emit').mockReturnValue(undefined);

            component.toggleSelection(makeItem(1));

            expect(emit).not.toHaveBeenCalled();
            expect(component.isSelected(makeItem(1))).toBe(false);
            fixture.destroy();
        });

        it('toggleSelection_Selectable_AddsThenRemovesAndEmitsIds', async () => {
            fixture.componentRef.setInput('selectable', true);
            await settle();
            const emit = vi.spyOn(component.selectionChange, 'emit').mockReturnValue(undefined);
            const one = makeItem(1);
            const two = makeItem(2);

            component.toggleSelection(one);
            expect(emit).toHaveBeenCalledWith([1]);

            component.toggleSelection(two);
            expect(emit).toHaveBeenCalledWith([1, 2]);

            component.toggleSelection(one);
            expect(emit).toHaveBeenCalledWith([2]);
            expect(component.isSelected(one)).toBe(false);
            expect(component.isSelected(two)).toBe(true);
            fixture.destroy();
        });

        it('toggleSelection_DisabledItem_IsIgnored', async () => {
            fixture.componentRef.setInput('selectable', true);
            fixture.componentRef.setInput('disabledIds', [2]);
            await settle();
            const emit = vi.spyOn(component.selectionChange, 'emit').mockReturnValue(undefined);

            component.toggleSelection(makeItem(2));

            expect(emit).not.toHaveBeenCalled();
            expect(component.isDisabled(makeItem(2))).toBe(true);
            expect(component.isSelected(makeItem(2))).toBe(false);
            fixture.destroy();
        });

        it('clearSelection_WithSelection_EmitsEmptyListAndHidesSummary', async () => {
            fixture.componentRef.setInput('selectable', true);
            await settle();
            component.toggleSelection(makeItem(1));
            fixture.detectChanges();
            expect(fixture.nativeElement.querySelector('.qb-summary__selection')).not.toBeNull();

            const emit = vi.spyOn(component.selectionChange, 'emit').mockReturnValue(undefined);
            component.clearSelection();
            fixture.detectChanges();

            expect(emit).toHaveBeenCalledWith([]);
            expect(component.selectedIds().size).toBe(0);
            expect(fixture.nativeElement.querySelector('.qb-summary__selection')).toBeNull();
            fixture.destroy();
        });

        it('template_CheckboxClick_TogglesSelectionOfThatCard', async () => {
            fixture.componentRef.setInput('selectable', true);
            await settle();
            const emit = vi.spyOn(component.selectionChange, 'emit').mockReturnValue(undefined);

            const input = fixture.nativeElement.querySelector('.qb-card__actions input[type="checkbox"]') as HTMLInputElement;
            input.click();
            fixture.detectChanges();

            expect(emit).toHaveBeenCalledWith([1]);
            fixture.destroy();
        });

        it('template_DisabledItem_CheckboxCheckedAndDisabled', async () => {
            fixture.componentRef.setInput('selectable', true);
            fixture.componentRef.setInput('disabledIds', [1]);
            await settle();

            const input = fixture.nativeElement.querySelector('.qb-card__actions input[type="checkbox"]') as HTMLInputElement;
            expect(input.checked).toBe(true);
            expect(input.disabled).toBe(true);
            fixture.destroy();
        });

        it('toggleSelection_NotLinkableItem_IsIgnored (issue #472)', async () => {
            fixture.componentRef.setInput('selectable', true);
            await settle();
            const emit = vi.spyOn(component.selectionChange, 'emit').mockReturnValue(undefined);
            const legacy = makeItem(3, { canLink: false, isOwn: false });

            component.toggleSelection(legacy);

            expect(emit).not.toHaveBeenCalled();
            expect(component.isLinkBlocked(legacy)).toBe(true);
            fixture.destroy();
        });

        it('isLinkBlocked_NotSelectableOrAlreadyInTest_IsFalse (issue #472)', async () => {
            await settle();
            expect(component.isLinkBlocked(makeItem(3, { canLink: false }))).toBe(false);

            fixture.componentRef.setInput('selectable', true);
            fixture.componentRef.setInput('disabledIds', [3]);
            expect(component.isLinkBlocked(makeItem(3, { canLink: false }))).toBe(false);
            fixture.destroy();
        });

        it('template_NotLinkableItem_CheckboxUncheckedDisabledWithLabel (issue #472)', async () => {
            search.mockReturnValue(of(makePage([makeItem(1, { canLink: false, isOwn: false })])));
            fixture.componentRef.setInput('selectable', true);
            await settle();

            const input = fixture.nativeElement.querySelector('.qb-card__actions input[type="checkbox"]') as HTMLInputElement;
            expect(input.checked).toBe(false);
            expect(input.disabled).toBe(true);
            expect((fixture.nativeElement.querySelector('.qb-card__actions') as HTMLElement).textContent).toContain(qbTr.card.cannotLink);
            expect(fixture.nativeElement.querySelector('.qb-card--disabled')).not.toBeNull();
            fixture.destroy();
        });

        it('template_NotSelectable_HidesCheckboxes', async () => {
            await settle();

            expect(fixture.nativeElement.querySelectorAll('mat-checkbox').length).toBe(0);
            fixture.destroy();
        });
    });

    describe('preview', () => {
        it('preview_Item_OpensPreviewDialogWithItemData', () => {
            const item = makeItem(7);

            component.preview(item);

            expect(dialogOpen).toHaveBeenCalledTimes(1);
            const [dialogComponent, config] = vi.mocked(dialogOpen).mock.lastCall!;
            expect(dialogComponent).toBe(QuestionBankPreviewDialogComponent);
            expect(config.data).toBe(item);
            expect(config.autoFocus).toBe(false);
        });

        it('template_CardImageClick_OpensPreview', async () => {
            search.mockReturnValue(of(makePage([makeItem(5)])));
            await settle();

            (fixture.nativeElement.querySelector('.qb-card__image') as HTMLButtonElement).click();

            expect(dialogOpen).toHaveBeenCalledTimes(1);
            expect(vi.mocked(dialogOpen).mock.lastCall![1].data.id).toBe(5);
            fixture.destroy();
        });
    });

    describe('çözüm onayı (#580)', () => {
        const pending = (id: number, source: QuestionBankItem['solutionSource'] = 'AI') =>
            makeItem(id, { solutionStatus: 'pending', solutionSource: source, solutionPromptVersion: 4 });

        function snack(): Mock {
            return vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never) as unknown as Mock;
        }

        it('pendingChip_Selected_SendsSolutionStatusPendingAndResetsPage', async () => {
            await settle();
            component.onPage({ pageIndex: 2, pageSize: 24, length: 100 });
            await settle();

            component.setPendingSolutionsOnly(true);
            await settle();

            expect(lastFilter()).toMatchObject({ solutionStatus: 'pending', skip: 0 });
            component.setPendingSolutionsOnly(false);
            await settle();
            expect(lastFilter()['solutionStatus']).toBeNull();
            fixture.destroy();
        });

        it('template_PendingChip_ClickTogglesFilter', async () => {
            await settle();

            (fixture.nativeElement.querySelector('.qb-pending-filter__chip .mdc-evolution-chip__action--primary') as HTMLElement).click();
            await settle();

            expect(component.pendingSolutionsOnly()).toBe(true);
            expect(lastFilter()).toMatchObject({ solutionStatus: 'pending' });
            fixture.destroy();
        });

        it('template_PendingBadgeWithSource_ApprovedTagOnlyForGeneratedSolutions', async () => {
            search.mockReturnValue(of(makePage([
                pending(1, 'Key'),
                makeItem(2, { solutionStatus: 'approved', solutionSource: 'AI' }),
                makeItem(3, { solutionStatus: 'approved', solutionSource: 'Human' }),
                makeItem(4),
            ])));
            await settle();

            const cards = fixture.nativeElement.querySelectorAll('.qb-card') as NodeListOf<HTMLElement>;
            expect(cards[0].querySelector('.qb-card__solution-pending')?.textContent?.replace(/\s+/g, ' ').trim())
                .toBe(`${qbTr.card.solutionPending} · ${qbTr.card.solutionSources.Key} · v4`);
            expect(cards[1].querySelector('.qb-card__solution-approved')?.textContent).toContain(qbTr.card.solutionApprovedTag);
            expect(cards[2].querySelector('.qb-card__solution-approved')).toBeNull();
            expect(cards[3].querySelector('.qb-card__solution-pending, .qb-card__solution-approved')).toBeNull();
            fixture.destroy();
        });

        it('approveAction_OnlyWhenAllowedPendingOwnOrAdminAndNotSelecting', async () => {
            search.mockReturnValue(of(makePage([pending(1), makeItem(2), makeItem(3, { solutionStatus: 'pending', solutionSource: 'AI', isOwn: false })])));
            await settle();
            expect(fixture.nativeElement.querySelector('.qb-card__approve')).toBeNull();

            fixture.componentRef.setInput('canApproveSolutions', true);
            await settle();
            // QuestionTrainer yalnız kendi sorusunu onaylar.
            expect(fixture.nativeElement.querySelectorAll('.qb-card__approve').length).toBe(1);

            fixture.componentRef.setInput('canApproveOthersSolutions', true);
            await settle();
            expect(fixture.nativeElement.querySelectorAll('.qb-card__approve').length).toBe(2);

            fixture.componentRef.setInput('selectable', true);
            await settle();
            expect(fixture.nativeElement.querySelector('.qb-card__approve')).toBeNull();
            fixture.destroy();
        });

        it('approveClick_OpensPreviewWithApprove_ApprovesOnlyWhenConfirmedThere', async () => {
            fixture.componentRef.setInput('canApproveSolutions', true);
            search.mockReturnValue(of(makePage([pending(1)])));
            approveSolution.mockReturnValue(of({ questionId: 1, solutionSource: 'AI', solutionStatus: 'approved' }));
            snack();
            await settle();
            dialogOpen.mockReturnValueOnce({ afterClosed: () => of(undefined) });

            (fixture.nativeElement.querySelector('.qb-card__approve') as HTMLButtonElement).click();

            const [dialogComponent, config] = vi.mocked(dialogOpen).mock.lastCall!;
            expect(dialogComponent).toBe(QuestionBankPreviewDialogComponent);
            expect(config.data).toEqual({ item: component.page().items[0], approve: true });
            expect(approveSolution).not.toHaveBeenCalled();

            dialogOpen.mockReturnValueOnce({ afterClosed: () => of('approve') });
            (fixture.nativeElement.querySelector('.qb-card__approve') as HTMLButtonElement).click();
            expect(approveSolution).toHaveBeenCalledWith(1);
            fixture.destroy();
        });

        it('approve_Success_ItemApprovedAndSnackbar', async () => {
            fixture.componentRef.setInput('canApproveSolutions', true);
            search.mockReturnValue(of(makePage([pending(1)])));
            approveSolution.mockReturnValue(of({
                questionId: 1, solutionSource: 'AI', solutionStatus: 'approved', solutionPromptVersion: 3,
                solutionApprovedByName: 'Ayşe', solutionApprovedAt: '2026-10-27T00:00:00Z',
            }));
            const open = snack();
            await settle();

            dialogOpen.mockReturnValueOnce({ afterClosed: () => of('approve') });
            (fixture.nativeElement.querySelector('.qb-card__approve') as HTMLButtonElement).click();
            fixture.detectChanges();

            expect(approveSolution).toHaveBeenCalledWith(1);
            expect(component.page().items[0].solutionStatus).toBe('approved');
            expect(fixture.nativeElement.querySelector('.qb-card__approve')).toBeNull();
            expect(fixture.nativeElement.querySelector('.qb-card__solution-approved')).not.toBeNull();
            expect(open).toHaveBeenCalledWith(qbTr.card.solutionApproved, qbTr.card.close, expect.any(Object));
            fixture.destroy();
        });

        it('approve_WithPendingFilter_RemovesItemAndDecrementsTotal', async () => {
            fixture.componentRef.setInput('canApproveSolutions', true);
            component.setPendingSolutionsOnly(true);
            search.mockReturnValue(of(makePage([pending(1), pending(2)], 5)));
            approveSolution.mockReturnValue(of({ questionId: 1, solutionSource: 'AI', solutionStatus: 'approved' }));
            snack();
            await settle();

            component.approveSolution(component.page().items[0]);

            expect(component.page().items.map((i) => i.id)).toEqual([2]);
            expect(component.page().total).toBe(4);
            fixture.destroy();
        });

        it('approve_Errors_ShowServerMessageOrForbidden_ItemStaysPending', async () => {
            fixture.componentRef.setInput('canApproveSolutions', true);
            search.mockReturnValue(of(makePage([pending(1)])));
            const open = snack();
            await settle();

            approveSolution.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Onaylanacak çözüm yok.' } })));
            component.approveSolution(component.page().items[0]);
            expect(open).toHaveBeenLastCalledWith('Onaylanacak çözüm yok.', qbTr.card.close, expect.any(Object));

            approveSolution.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 403 })));
            component.approveSolution(component.page().items[0]);
            expect(open).toHaveBeenLastCalledWith(qbTr.card.solutionForbidden, qbTr.card.close, expect.any(Object));

            expect(component.page().items[0].solutionStatus).toBe('pending');
            expect(component.isApproving(component.page().items[0])).toBe(false);
            fixture.destroy();
        });

        it('approve_NotAllowed_NoCall', async () => {
            search.mockReturnValue(of(makePage([pending(1)])));
            await settle();

            component.approveSolution(component.page().items[0]);

            expect(approveSolution).not.toHaveBeenCalled();
            fixture.destroy();
        });
    });
});

describe('QuestionBankPreviewDialogComponent (#580 onay)', () => {
    function create(data: unknown) {
        TestBed.configureTestingModule({
            imports: [QuestionBankPreviewDialogComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr } })],
            providers: [provideNoopAnimations(), { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close: vi.fn() } }],
        });
        const fixture = TestBed.createComponent(QuestionBankPreviewDialogComponent);
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    it('plainPreview_NoApproveButtonOrSolutionLine', () => {
        const el = create(makeItem(3));

        expect(el.querySelector('.qb-preview__approve')).toBeNull();
        expect(el.querySelector('.qb-preview__solution')).toBeNull();
    });

    it('approvePreview_ShowsSourcePromptVersionAndApproveButton', () => {
        const el = create({ item: makeItem(3, { solutionStatus: 'pending', solutionSource: 'Key', solutionPromptVersion: 7 }), approve: true });

        expect(el.querySelector('.qb-preview__solution')?.textContent).toContain(qbTr.card.solutionSources.Key);
        expect(el.querySelector('.qb-preview__prompt')?.textContent).toContain('v7');
        expect(el.querySelector('.qb-preview__approve')?.textContent).toContain(qbTr.preview.approve);
    });
});
