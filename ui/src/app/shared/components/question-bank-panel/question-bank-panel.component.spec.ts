import { Component, EventEmitter, Output, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import {
  DIFFICULTY_RANGES,
  QuestionBankPanelComponent,
} from './question-bank-panel.component';
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
  @Output() selectionChange = new EventEmitter<ClassificationSelection>();
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
  let fixture: ComponentFixture<QuestionBankPanelComponent>;
  let component: QuestionBankPanelComponent;
  let search: jasmine.Spy;
  let dialogOpen: jasmine.Spy;

  beforeEach(() => {
    search = jasmine.createSpy('search').and.returnValue(of(EMPTY_PAGE));
    dialogOpen = jasmine.createSpy('open');

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
      ],
    });
    fixture = TestBed.createComponent(QuestionBankPanelComponent);
    component = fixture.componentInstance;
  });

  /** Signal değişikliğini (toObservable effect) işletir, 250 ms debounce'u ilerletir ve sonucu render eder. */
  function settle(): void {
    fixture.detectChanges();
    tick(250);
    fixture.detectChanges();
  }

  function lastFilter(): Record<string, unknown> {
    return search.calls.mostRecent().args[0] as Record<string, unknown>;
  }

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
      expect(component.filter().classified).toBeTrue();
      component.setStatus('unclassified');
      expect(component.filter().classified).toBeFalse();
      component.setStatus('any');
      expect(component.filter().classified).toBeNull();
    });

    it('filter_AnswerYesAndNo_MapToBooleans', () => {
      component.setAnswer('yes');
      expect(component.filter().hasCorrectAnswer).toBeTrue();
      component.setAnswer('no');
      expect(component.filter().hasCorrectAnswer).toBeFalse();
      component.setAnswer('any');
      expect(component.filter().hasCorrectAnswer).toBeNull();
    });

    it('filter_InTestYesNoAny_MapToThreeStates', () => {
      component.setInTest('yes');
      expect(component.filter().inTest).toBeTrue();
      component.setInTest('no');
      expect(component.filter().inTest).toBeFalse();
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
    it('search_AfterInit_CallsServiceOnceWithDefaultFilterAfterDebounce', fakeAsync(() => {
      fixture.detectChanges();
      expect(search).not.toHaveBeenCalled();

      tick(250);
      expect(search).toHaveBeenCalledTimes(1);
      expect(lastFilter()['scope']).toBe('mine');
      expect(lastFilter()['skip']).toBe(0);
      expect(lastFilter()['take']).toBe(24);
      tick(0);
      fixture.destroy();
    }));

    it('search_RapidFilterChanges_DebouncedIntoSingleRequest', fakeAsync(() => {
      settle();
      search.calls.reset();

      component.setSource('AI');
      fixture.detectChanges();
      tick(100);
      component.setStatus('classified');
      fixture.detectChanges();
      tick(100);
      component.setAnswer('yes');
      fixture.detectChanges();

      tick(249);
      expect(search).not.toHaveBeenCalled();

      tick(1);
      expect(search).toHaveBeenCalledTimes(1);
      expect(lastFilter()['source']).toBe('AI');
      expect(lastFilter()['classified']).toBeTrue();
      expect(lastFilter()['hasCorrectAnswer']).toBeTrue();
      fixture.destroy();
    }));

    it('search_ClassificationSelection_SendsIdsThroughService', fakeAsync(() => {
      settle();
      search.calls.reset();

      component.onClassificationChange({ gradeId: 5, subjectId: 7, topicId: 9, subtopicIds: [11, 12] });
      settle();

      expect(lastFilter()['gradeId']).toBe(5);
      expect(lastFilter()['subjectId']).toBe(7);
      expect(lastFilter()['topicId']).toBe(9);
      expect(lastFilter()['subTopicIds']).toEqual([11, 12]);
      fixture.destroy();
    }));

    it('search_SwitchMap_StaleResponseIgnoredWhenNewerSearchStarted', fakeAsync(() => {
      const first = new Subject<QuestionBankPage>();
      const second = new Subject<QuestionBankPage>();
      search.and.returnValues(first, second);

      settle();
      component.setSource('AI');
      settle();

      second.next(makePage([makeItem(2)]));
      first.next(makePage([makeItem(1)]));

      expect(component.page().items.map((i) => i.id)).toEqual([2]);
      expect(first.observed).toBeFalse();
      fixture.destroy();
    }));

    it('loading_RequestInFlight_TrueUntilResponse', fakeAsync(() => {
      const pending = new Subject<QuestionBankPage>();
      search.and.returnValue(pending);

      settle();
      expect(component.loading()).toBeTrue();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('mat-progress-bar')).not.toBeNull();

      pending.next(makePage([makeItem(1)]));
      pending.complete();
      expect(component.loading()).toBeFalse();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('mat-progress-bar')).toBeNull();
      fixture.destroy();
    }));
  });

  describe('paging and filter reset', () => {
    it('onPage_NewPage_UpdatesSkipAndTakeInRequest', fakeAsync(() => {
      settle();
      search.calls.reset();

      component.onPage({ pageIndex: 2, pageSize: 12, length: 100, previousPageIndex: 0 });
      settle();

      expect(component.pageIndex()).toBe(2);
      expect(component.pageSize()).toBe(12);
      expect(lastFilter()['skip']).toBe(24);
      expect(lastFilter()['take']).toBe(12);
      fixture.destroy();
    }));

    const resetters: [string, () => void][] = [
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
    it('search_ServiceError_ShowsErrorMessageAndEmptyPage', fakeAsync(() => {
      search.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      settle();

      expect(component.error()).toBeTrue();
      expect(component.loading()).toBeFalse();
      expect(component.page().items).toEqual([]);
      const alert = fixture.nativeElement.querySelector('.qb-message--error') as HTMLElement;
      expect(alert).not.toBeNull();
      expect(alert.textContent?.trim()).toBe(qbTr.results.error);
      expect(fixture.nativeElement.querySelectorAll('.qb-card').length).toBe(0);
      fixture.destroy();
    }));

    it('search_RecoversAfterError_ClearsErrorOnNextSearch', fakeAsync(() => {
      search.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      settle();
      expect(component.error()).toBeTrue();

      search.and.returnValue(of(makePage([makeItem(1)])));
      component.setSource('AI');
      settle();

      expect(component.error()).toBeFalse();
      expect(component.page().items.length).toBe(1);
      fixture.destroy();
    }));
  });

  describe('results rendering', () => {
    it('template_ItemsReturned_RendersOneCardPerItem', fakeAsync(() => {
      search.and.returnValue(of(makePage([makeItem(1), makeItem(2), makeItem(3)])));
      settle();

      expect(fixture.nativeElement.querySelectorAll('.qb-card').length).toBe(3);
      fixture.destroy();
    }));

    it('template_EmptyResult_ShowsEmptyMessage', fakeAsync(() => {
      settle();

      const message = fixture.nativeElement.querySelector('.qb-message') as HTMLElement;
      expect(message.textContent?.trim()).toBe(qbTr.results.empty);
      fixture.destroy();
    }));

    it('template_AiClassifiedItem_ShowsAiIndicator', fakeAsync(() => {
      search.and.returnValue(of(makePage([makeItem(1, { classificationSource: ClassificationSource.AI }), makeItem(2)])));
      settle();

      expect(fixture.nativeElement.querySelectorAll('.qb-card__ai').length).toBe(1);
      fixture.destroy();
    }));

    it('template_UnclassifiedAndNoAnswerItem_ShowsWarningTags', fakeAsync(() => {
      search.and.returnValue(of(makePage([makeItem(1, { isClassified: false, hasCorrectAnswer: false })])));
      settle();

      const tags = Array.from(fixture.nativeElement.querySelectorAll('.qb-tag--warning') as NodeListOf<HTMLElement>).map((t) => t.textContent?.trim());
      expect(tags).toEqual([qbTr.card.unclassified, qbTr.card.noAnswer]);
      fixture.destroy();
    }));

    it('template_TotalCount_ShowsTranslatedTotal', fakeAsync(() => {
      search.and.returnValue(of(makePage([makeItem(1)], 42)));
      settle();

      const summary = fixture.nativeElement.querySelector('.qb-summary span') as HTMLElement;
      expect(summary.textContent?.trim()).toBe('42 soru');
      fixture.destroy();
    }));
  });

  describe('selection (selectable mode)', () => {
    beforeEach(() => {
      search.and.returnValue(of(makePage([makeItem(1), makeItem(2)])));
    });

    it('toggleSelection_NotSelectable_DoesNothing', fakeAsync(() => {
      settle();
      const emit = spyOn(component.selectionChange, 'emit');

      component.toggleSelection(makeItem(1));

      expect(emit).not.toHaveBeenCalled();
      expect(component.isSelected(makeItem(1))).toBeFalse();
      fixture.destroy();
    }));

    it('toggleSelection_Selectable_AddsThenRemovesAndEmitsIds', fakeAsync(() => {
      fixture.componentRef.setInput('selectable', true);
      settle();
      const emit = spyOn(component.selectionChange, 'emit');
      const one = makeItem(1);
      const two = makeItem(2);

      component.toggleSelection(one);
      expect(emit).toHaveBeenCalledWith([1]);

      component.toggleSelection(two);
      expect(emit).toHaveBeenCalledWith([1, 2]);

      component.toggleSelection(one);
      expect(emit).toHaveBeenCalledWith([2]);
      expect(component.isSelected(one)).toBeFalse();
      expect(component.isSelected(two)).toBeTrue();
      fixture.destroy();
    }));

    it('toggleSelection_DisabledItem_IsIgnored', fakeAsync(() => {
      fixture.componentRef.setInput('selectable', true);
      fixture.componentRef.setInput('disabledIds', [2]);
      settle();
      const emit = spyOn(component.selectionChange, 'emit');

      component.toggleSelection(makeItem(2));

      expect(emit).not.toHaveBeenCalled();
      expect(component.isDisabled(makeItem(2))).toBeTrue();
      expect(component.isSelected(makeItem(2))).toBeFalse();
      fixture.destroy();
    }));

    it('clearSelection_WithSelection_EmitsEmptyListAndHidesSummary', fakeAsync(() => {
      fixture.componentRef.setInput('selectable', true);
      settle();
      component.toggleSelection(makeItem(1));
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.qb-summary__selection')).not.toBeNull();

      const emit = spyOn(component.selectionChange, 'emit');
      component.clearSelection();
      fixture.detectChanges();

      expect(emit).toHaveBeenCalledWith([]);
      expect(component.selectedIds().size).toBe(0);
      expect(fixture.nativeElement.querySelector('.qb-summary__selection')).toBeNull();
      fixture.destroy();
    }));

    it('template_CheckboxClick_TogglesSelectionOfThatCard', fakeAsync(() => {
      fixture.componentRef.setInput('selectable', true);
      settle();
      const emit = spyOn(component.selectionChange, 'emit');

      const input = fixture.nativeElement.querySelector('.qb-card__actions input[type="checkbox"]') as HTMLInputElement;
      input.click();
      fixture.detectChanges();

      expect(emit).toHaveBeenCalledWith([1]);
      fixture.destroy();
    }));

    it('template_DisabledItem_CheckboxCheckedAndDisabled', fakeAsync(() => {
      fixture.componentRef.setInput('selectable', true);
      fixture.componentRef.setInput('disabledIds', [1]);
      settle();

      const input = fixture.nativeElement.querySelector('.qb-card__actions input[type="checkbox"]') as HTMLInputElement;
      expect(input.checked).toBeTrue();
      expect(input.disabled).toBeTrue();
      fixture.destroy();
    }));

    it('toggleSelection_NotLinkableItem_IsIgnored (issue #472)', fakeAsync(() => {
      fixture.componentRef.setInput('selectable', true);
      settle();
      const emit = spyOn(component.selectionChange, 'emit');
      const legacy = makeItem(3, { canLink: false, isOwn: false });

      component.toggleSelection(legacy);

      expect(emit).not.toHaveBeenCalled();
      expect(component.isLinkBlocked(legacy)).toBeTrue();
      fixture.destroy();
    }));

    it('isLinkBlocked_NotSelectableOrAlreadyInTest_IsFalse (issue #472)', fakeAsync(() => {
      settle();
      expect(component.isLinkBlocked(makeItem(3, { canLink: false }))).toBeFalse();

      fixture.componentRef.setInput('selectable', true);
      fixture.componentRef.setInput('disabledIds', [3]);
      expect(component.isLinkBlocked(makeItem(3, { canLink: false }))).toBeFalse();
      fixture.destroy();
    }));

    it('template_NotLinkableItem_CheckboxUncheckedDisabledWithLabel (issue #472)', fakeAsync(() => {
      search.and.returnValue(of(makePage([makeItem(1, { canLink: false, isOwn: false })])));
      fixture.componentRef.setInput('selectable', true);
      settle();

      const input = fixture.nativeElement.querySelector('.qb-card__actions input[type="checkbox"]') as HTMLInputElement;
      expect(input.checked).toBeFalse();
      expect(input.disabled).toBeTrue();
      expect((fixture.nativeElement.querySelector('.qb-card__actions') as HTMLElement).textContent).toContain(
        qbTr.card.cannotLink,
      );
      expect(fixture.nativeElement.querySelector('.qb-card--disabled')).not.toBeNull();
      fixture.destroy();
    }));

    it('template_NotSelectable_HidesCheckboxes', fakeAsync(() => {
      settle();

      expect(fixture.nativeElement.querySelectorAll('mat-checkbox').length).toBe(0);
      fixture.destroy();
    }));
  });

  describe('preview', () => {
    it('preview_Item_OpensPreviewDialogWithItemData', () => {
      const item = makeItem(7);

      component.preview(item);

      expect(dialogOpen).toHaveBeenCalledTimes(1);
      const [dialogComponent, config] = dialogOpen.calls.mostRecent().args;
      expect(dialogComponent).toBe(QuestionBankPreviewDialogComponent);
      expect(config.data).toBe(item);
      expect(config.autoFocus).toBeFalse();
    });

    it('template_CardImageClick_OpensPreview', fakeAsync(() => {
      search.and.returnValue(of(makePage([makeItem(5)])));
      settle();

      (fixture.nativeElement.querySelector('.qb-card__image') as HTMLButtonElement).click();

      expect(dialogOpen).toHaveBeenCalledTimes(1);
      expect(dialogOpen.calls.mostRecent().args[1].data.id).toBe(5);
      fixture.destroy();
    }));
  });
});
