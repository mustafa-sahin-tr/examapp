import type { Mock } from "vitest";
import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import { DIFFICULTY_RANGES, QuestionBankPanelComponent, } from './question-bank-panel.component';
import { QuestionBankPreviewDialogComponent } from './question-bank-preview-dialog.component';
import { ClassificationSelection, ClassificationSelectorComponent } from '../classification-selector/classification-selector.component';
import { QuestionBankService } from '../../../services/question-bank.service';
import { SkillService } from '../../../services/skill.service';
import { Skill, SkillCategory } from '../../../models/skill';
import { ContextTagsDialogComponent } from './context-tags-dialog.component';
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

const SKILLS: Skill[] = [
    { id: 8, code: 'KB2.8', category: SkillCategory.Integrated, name: 'Sorgulama' },
    { id: 102, code: 'KB3.2', category: SkillCategory.HigherOrder, name: 'Problem çözme' },
];

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
    let dialogOpen: Mock;
    let afterClosed: Subject<unknown>;

    beforeEach(() => {
        search = vi.fn().mockName('search').mockReturnValue(of(EMPTY_PAGE));
        afterClosed = new Subject<unknown>();
        dialogOpen = vi.fn().mockName('open').mockReturnValue({ afterClosed: () => afterClosed });

        TestBed.overrideComponent(QuestionBankPanelComponent, {
            remove: { imports: [ClassificationSelectorComponent] },
            add: { imports: [ClassificationSelectorStub] },
        });
        TestBed.configureTestingModule({
            imports: [QuestionBankPanelComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr, 'question-bank/en': qbEn } })],
            providers: [
                provideNoopAnimations(),
                { provide: QuestionBankService, useValue: { search } },
                { provide: MatDialog, useValue: { open: dialogOpen } },
                { provide: SkillService, useValue: { list: () => of(SKILLS) } },
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

    // Issue #863: "Havuzdan soru ekle" satır listesi, cevap süzgeci varsayılanı ve seçilen öğeler.
    describe('list layout (issue #863)', () => {
        const el = () => fixture.nativeElement as HTMLElement;

        it('rows_ShowTextDifficultyAndWarnOnMissingAnswer', async () => {
            vi.mocked(search).mockReturnValue(of(makePage([
                makeItem(1, { imageUrl: null, text: 'Tümler iki açıdan biri 35° ise', difficultyLevel: 2, testCount: 1 }),
                makeItem(2, { hasCorrectAnswer: false, difficultyLevel: 5 }),
            ])));
            fixture.componentRef.setInput('layout', 'list');
            fixture.componentRef.setInput('selectable', true);
            await settle();

            const rows = el().querySelectorAll('[data-testid="bank-row"]');
            expect(rows.length).toBe(2);
            expect(el().querySelector('.qb-card')).toBeNull();
            expect(rows[0].textContent).toContain('Tümler iki açıdan biri 35° ise');
            expect(rows[0].textContent).toContain('Kolay');
            expect(rows[1].classList).toContain('qb-row--no-answer');
            expect(rows[1].querySelector('[data-testid="bank-row-no-answer"]')).not.toBeNull();

            (rows[1].querySelector('.qb-row__body') as HTMLElement).click();
            expect(component.isSelected(makeItem(2))).toBe(true);
            fixture.destroy();
        });

        it('initialAnswerFilter_SearchesAnsweredOnly', async () => {
            fixture.componentRef.setInput('initialAnswerFilter', 'yes');
            await settle();
            expect(lastFilter()['hasCorrectAnswer']).toBe(true);
            fixture.destroy();
        });

        it('selectedItems_EmittedAndRemovable', async () => {
            fixture.componentRef.setInput('selectable', true);
            await settle();
            const emit = vi.spyOn(component.selectedItemsChange, 'emit').mockReturnValue(undefined);

            component.toggleSelection(makeItem(1));
            component.toggleSelection(makeItem(2));
            expect(emit).toHaveBeenLastCalledWith([makeItem(1), makeItem(2)]);

            component.removeSelection(1);
            expect(emit).toHaveBeenLastCalledWith([makeItem(2)]);
            expect(component.isSelected(makeItem(1))).toBe(false);
            fixture.destroy();
        });
    });

    describe('more filters (issue #836)', () => {
        const el = () => fixture.nativeElement as HTMLElement;

        it('secondaryFiltersHiddenUntilToggled', async () => {
            await settle();

            expect(el().querySelector('[data-testid="more-filters-panel"]')).toBeNull();
            expect(el().querySelector('[data-testid="filter-context-based"]')).toBeNull();
            expect(el().querySelector('[data-testid="filter-kind"]')).not.toBeNull();

            (el().querySelector('[data-testid="more-filters"]') as HTMLButtonElement).click();
            fixture.detectChanges();

            expect(el().querySelector('[data-testid="more-filters"]')?.getAttribute('aria-expanded')).toBe('true');
            expect(el().querySelector('[data-testid="filter-context-based"]')).not.toBeNull();
        });

        it('activeSecondaryFilters_ShowAsRemovableChipsAndCountOnButton', async () => {
            component.setDifficulty('medium');
            component.setSkillIds([1, 2]);
            await settle();

            const chips = Array.from(el().querySelectorAll('[data-testid="active-extra-filters"] .qb-chip'));
            expect(chips.map((c) => c.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
                expect.stringContaining('Zorluk'),
                expect.stringContaining('2'),
            ]);
            expect(el().querySelector('[data-testid="more-filters"]')?.textContent).toContain('2');

            (chips[0] as HTMLButtonElement).click();
            await settle();

            expect(component.difficulty()).toBe('any');
            expect(lastFilter()['minDifficulty'] ?? null).toBeNull();

            component.clearExtraFilters();
            await settle();
            expect(component.skillIds()).toEqual([]);
            expect(el().querySelector('[data-testid="active-extra-filters"]')).toBeNull();
        });
    });

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
                contextBased: null,
                kind: null,
                skillIds: [],
                skip: 0,
                take: 24,
            });
        });

        it('filter_Kind_MapsActivityAndQuestionAndResetsPage_issue819', () => {
            component.pageIndex.set(2);
            component.setKind('activity');
            expect(component.filter().kind).toBe('activity');
            expect(component.pageIndex()).toBe(0);
            component.setKind('question');
            expect(component.filter().kind).toBe('question');
            component.setKind('any');
            expect(component.filter().kind).toBeNull();
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

        it('template_ActivityItem_ShowsActivityTag_issue819', async () => {
            search.mockReturnValue(of(makePage([
                makeItem(1, { interactionType: 'matchPairs' }),
                makeItem(2, { interactionType: 'MCQ' }),
                makeItem(3),
            ])));
            await settle();

            const tags = fixture.nativeElement.querySelectorAll('[data-testid="activity-tag"]') as NodeListOf<HTMLElement>;
            expect(tags.length).toBe(1);
            expect(tags[0].textContent?.trim()).toBe(qbTr.card.activity);
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

    describe('context tags (issue #703)', () => {
        it('filter_ContextBasedAndSkills_SendsFlagAndIdsAndResetsPage', () => {
            component.pageIndex.set(2);
            component.setContextBased('yes');
            expect(component.pageIndex()).toBe(0);
            component.pageIndex.set(1);
            component.setSkillIds([8, 102]);
            expect(component.pageIndex()).toBe(0);
            expect(component.filter().contextBased).toBe(true);
            expect(component.filter().skillIds).toEqual([8, 102]);

            component.setContextBased('no');
            expect(component.filter().contextBased).toBe(false);
            component.setContextBased('any');
            expect(component.filter().contextBased).toBeNull();
        });

        it('skillGroups_GroupsByCategoryInOrder', () => {
            expect(component.skillGroups().map((g) => [g.category, g.skills.map((s) => s.code)])).toEqual([
                [SkillCategory.Integrated, ['KB2.8']],
                [SkillCategory.HigherOrder, ['KB3.2']],
            ]);
        });

        it('template_ContextTags_ShowsFlagAndTranslatedSkillNames', async () => {
            search.mockReturnValue(of(makePage([makeItem(5, { isContextBased: true, skillCodes: ['KB2.8', 'KB3.2'] }), makeItem(6)])));
            await settle();

            const tags = fixture.nativeElement.querySelectorAll('[data-testid="context-tags"]') as NodeListOf<HTMLElement>;
            expect(tags.length).toBe(1);
            expect(tags[0].textContent).toContain(qbTr.card.contextBased);
            expect(tags[0].textContent).toContain(qbTr.skills.KB2['8']);
            expect(tags[0].textContent).toContain(qbTr.skills.KB3['2']);
            fixture.destroy();
        });

        it('template_EditButton_OnlyOnOwnQuestions', async () => {
            search.mockReturnValue(of(makePage([makeItem(5), makeItem(6, { isOwn: false })])));
            await settle();

            expect(fixture.nativeElement.querySelectorAll('[data-testid="edit-context-tags"]').length).toBe(1);
            fixture.destroy();
        });

        it('editContextTags_Saved_UpdatesCardInPlace', async () => {
            search.mockReturnValue(of(makePage([makeItem(5), makeItem(6)])));
            await settle();

            component.editContextTags(component.page().items[0]);
            expect(vi.mocked(dialogOpen).mock.lastCall![0]).toBe(ContextTagsDialogComponent);
            expect(vi.mocked(dialogOpen).mock.lastCall![1].data).toEqual({ questionId: 5 });

            afterClosed.next({ isContextBased: true, skills: [SKILLS[0]] });
            expect(component.page().items[0].isContextBased).toBe(true);
            expect(component.page().items[0].skillCodes).toEqual(['KB2.8']);
            expect(component.page().items[1].isContextBased).toBeUndefined();
            fixture.destroy();
        });

        it('editContextTags_Cancelled_LeavesCardUnchanged', async () => {
            search.mockReturnValue(of(makePage([makeItem(5)])));
            await settle();
            const before = component.page();

            component.editContextTags(before.items[0]);
            afterClosed.next(undefined);

            expect(component.page()).toBe(before);
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
});
