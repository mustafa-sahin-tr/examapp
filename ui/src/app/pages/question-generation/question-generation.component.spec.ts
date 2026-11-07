import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, Subject as RxSubject, throwError } from 'rxjs';

import { QuestionGenerationComponent } from './question-generation.component';
import { LearningOutcome } from '../../models/learning-outcome';
import { GradesService } from '../../services/grades.service';
import { LearningOutcomeService } from '../../services/learning-outcome.service';
import { QuestionDraftsResponse, QuestionGenerationService } from '../../services/question-generation.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import en from '../../../../public/i18n/question-generation/en.json';
import tr from '../../../../public/i18n/question-generation/tr.json';

const OUTCOMES: LearningOutcome[] = [
  { id: 11, code: 'MAT.5.1.1', description: 'Sayıları okuyabilme', curriculumVersion: 'Maarif', gradeId: 5, subjectId: 7, subTopicId: null },
  { id: 12, code: 'MAT.5.1.2', description: 'Sayıları yazabilme', curriculumVersion: 'Maarif', gradeId: 5, subjectId: 7, subTopicId: null },
];

function response(overrides: Partial<QuestionDraftsResponse> = {}): QuestionDraftsResponse {
  return {
    learningOutcomeId: 11,
    outcomeCode: 'MAT.5.1.1',
    outcomeDescription: 'Sayıları okuyabilme',
    rejected: 0,
    drafts: [
      { text: 'Birinci?', options: ['a', 'b', 'c', 'd'], correctIndex: 2, explanation: 'Çünkü c.' },
      { text: 'İkinci?', options: ['e', 'f', 'g', 'h'], correctIndex: 0 },
    ],
    ...overrides,
  };
}

describe('QuestionGenerationComponent', () => {
  let fixture: ComponentFixture<QuestionGenerationComponent>;
  let component: QuestionGenerationComponent;
  let generation: MockedObject<QuestionGenerationService>;
  let outcomes: MockedObject<LearningOutcomeService>;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function setup(): void {
    generation = {
      generate: vi.fn().mockReturnValue(of(response())),
      save: vi.fn().mockReturnValue(of({ questionIds: [100, 101] })),
    } as unknown as MockedObject<QuestionGenerationService>;
    outcomes = { search: vi.fn().mockReturnValue(of(OUTCOMES)) } as unknown as MockedObject<LearningOutcomeService>;

    TestBed.configureTestingModule({
      imports: [QuestionGenerationComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'question-generation/tr': tr } })],
      providers: [
        provideRouter([]),
        { provide: QuestionGenerationService, useValue: generation },
        { provide: LearningOutcomeService, useValue: outcomes },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 5, name: '5. Sınıf' }]) } },
        { provide: SubjectService, useValue: { getSubjectsByGrade: () => of([{ id: 7, name: 'Matematik' }]) } },
      ],
    });
    fixture = TestBed.createComponent(QuestionGenerationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function chooseOutcome(id = 11): void {
    component.onGradeChange(5);
    component.onSubjectChange(7);
    component.outcomeId.set(id);
    fixture.detectChanges();
  }

  function generated(): void {
    setup();
    chooseOutcome();
    component.generate();
    fixture.detectChanges();
  }

  it('translations_HaveTheSameKeysInBothLanguages', () => {
    const keys = (node: object, prefix = ''): string[] => Object.entries(node).flatMap(([k, v]) =>
      typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]);

    expect(keys(en).sort()).toEqual(keys(tr).sort());
  });

  it('generateButton_NoOutcome_IsDisabledAndCallsNothing', () => {
    setup();

    expect((el().querySelector('.qg-generate') as HTMLButtonElement).disabled).toBe(true);
    component.generate();
    expect(generation.generate).not.toHaveBeenCalled();
  });

  it('generateButton_SaysWhichFieldIsMissing_InOrder (#866)', () => {
    setup();
    const missing = () => el().querySelector('[data-testid="qg-missing"]')?.textContent?.trim();

    expect(missing()).toBe(tr.missing.grade);
    expect(el().querySelector('.qg-generate')?.getAttribute('aria-describedby')).toBe('qg-missing');
    component.onGradeChange(5);
    fixture.detectChanges();
    expect(missing()).toBe(tr.missing.subject);
    component.onSubjectChange(7);
    fixture.detectChanges();
    expect(missing()).toBe(tr.missing.outcome);
    component.outcomeId.set(11);
    fixture.detectChanges();
    expect(missing()).toBeUndefined();
    expect(el().querySelector('.qg-generate')?.textContent).toContain('3 taslak üret');
  });

  it('draftsArea_ShowsAnExampleUntilDraftsArrive (#866)', () => {
    setup();
    expect(el().querySelector('.qg-drafts [data-testid="qg-sample"]')?.textContent).toContain(tr.sampleTag);

    chooseOutcome();
    component.generate();
    fixture.detectChanges();
    expect(el().querySelector('[data-testid="qg-sample"]')).toBeNull();
    expect(el().querySelector('.qg-drafts .qg-draft')).not.toBeNull();
  });

  it('subjectChange_LoadsOutcomesOfThatGradeAndSubject', () => {
    setup();

    chooseOutcome();

    expect(outcomes.search).toHaveBeenCalledWith(5, 7, '');
    expect(component.outcomes().length).toBe(2);
  });

  it('gradeChange_ClearsSubjectAndOutcome', () => {
    setup();
    chooseOutcome();

    component.onGradeChange(5);

    expect(component.subjectId()).toBeNull();
    expect(component.outcomeId()).toBeNull();
    expect(component.outcomes()).toEqual([]);
  });

  it('search_WaitsForTypingToStopAndClearsTheChosenOutcome', async () => {
    vi.useFakeTimers();
    try {
      setup();
      chooseOutcome();
      outcomes.search.mockClear();

      component.onSearchChange('say');
      component.onSearchChange('sayı');
      await vi.advanceTimersByTimeAsync(299);
      expect(outcomes.search).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(1);
      expect(outcomes.search).toHaveBeenCalledTimes(1);
      expect(outcomes.search).toHaveBeenCalledWith(5, 7, 'sayı');
      expect(component.outcomeId()).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('lateResponseOfAnOldSubject_DoesNotOverwriteTheList', () => {
    setup();
    const slow = new RxSubject<LearningOutcome[]>();
    outcomes.search.mockReturnValueOnce(slow);
    component.onGradeChange(5);
    component.onSubjectChange(7);
    outcomes.search.mockReturnValueOnce(of([OUTCOMES[1]]));

    component.onSubjectChange(8);
    slow.next(OUTCOMES);

    expect(component.outcomes().map((o) => o.id)).toEqual([12]);
  });

  it('outcomesLoadFailure_ShowsRetry', () => {
    setup();
    outcomes.search.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.onGradeChange(5);
    component.onSubjectChange(7);
    fixture.detectChanges();

    expect(text()).toContain(tr.outcomesFailed);
  });

  it('noOutcomes_PointsToTheCatalog', () => {
    setup();
    outcomes.search.mockReturnValue(of([]));
    component.onGradeChange(5);
    component.onSubjectChange(7);
    fixture.detectChanges();

    expect(text()).toContain(tr.noOutcomes);
  });

  it('generate_SendsTheChoiceAndShowsEditableDraftsWithTheReviewNote', () => {
    setup();
    chooseOutcome();
    component.count.set(2);
    component.difficulty.set(8);

    component.generate();
    fixture.detectChanges();

    expect(generation.generate).toHaveBeenCalledWith(11, 2, 8);
    expect(el().querySelectorAll('.qg-draft').length).toBe(2);
    expect(text()).toContain(tr.reviewNote);
    expect(text()).toContain('Çünkü c.');
    expect(component.selectedCount()).toBe(2);
  });

  it('generate_WhileRunning_DoesNotSendASecondPaidCall', () => {
    setup();
    chooseOutcome();
    generation.generate.mockReturnValue(new RxSubject<QuestionDraftsResponse>());

    component.generate();
    component.generate();

    expect(generation.generate).toHaveBeenCalledTimes(1);
  });

  it('rejectedDrafts_AreReported', () => {
    setup();
    chooseOutcome();
    generation.generate.mockReturnValue(of(response({ rejected: 2 })));

    component.generate();
    fixture.detectChanges();

    expect(text()).toContain(tr.rejected.replace('{{count}}', '2'));
  });

  it('generateAgain_SameOutcome_AddsDrafts', () => {
    generated();

    component.generate();

    expect(component.drafts().length).toBe(4);
  });

  it('otherOutcomeWithDraftsOpen_BlocksGeneratingSoEditsAreNotLost', () => {
    generated();
    component.setText(0, 'Düzeltilmiş?');
    generation.generate.mockClear();
    component.outcomeId.set(12);
    fixture.detectChanges();

    expect((el().querySelector('.qg-generate') as HTMLButtonElement).disabled).toBe(true);
    expect(text()).toContain(tr.finishDraftsFirst);
    component.generate();
    expect(generation.generate).not.toHaveBeenCalled();
    expect(component.drafts()[0].text).toBe('Düzeltilmiş?');
  });

  it('otherDifficultyWithDraftsOpen_BlocksGeneratingToo', () => {
    generated();

    component.difficulty.set(8);

    expect(component.selectionDiffersFromDrafts()).toBe(true);
  });

  it('draftsSaveToTheirOwnOutcomeEvenIfTheSelectionMoved', () => {
    generated();
    component.outcomeId.set(12);

    component.save();

    expect(generation.save.mock.calls[0][0]).toBe(11);
  });

  it('clearDrafts_AllowsGeneratingForTheNewOutcome', () => {
    generated();
    component.outcomeId.set(12);
    generation.generate.mockReturnValue(of(response({ learningOutcomeId: 12, outcomeCode: 'MAT.5.1.2' })));

    component.clearDrafts();
    component.generate();

    expect(generation.generate).toHaveBeenLastCalledWith(12, 3, 5);
    expect(component.draftOutcome()?.id).toBe(12);
    expect(component.drafts().length).toBe(2);
  });

  it('generateReturningNothing_KeepsExistingDrafts', () => {
    generated();
    generation.generate.mockReturnValue(of(response({ drafts: [], rejected: 2 })));

    component.generate();

    expect(component.drafts().length).toBe(2);
  });

  it('generateFinishingDuringSave_DoesNotLoseFreshDrafts', () => {
    generated();
    const pendingSave = new RxSubject<{ questionIds: number[] }>();
    generation.save.mockReturnValue(pendingSave);
    component.save();
    component.setSelected(0, false);

    pendingSave.next({ questionIds: [1, 2] });

    expect(component.drafts()).toEqual([]);
  });

  it('saveAndGenerate_CannotRunTogether', () => {
    generated();
    generation.save.mockReturnValue(new RxSubject<{ questionIds: number[] }>());
    generation.generate.mockClear();

    component.save();
    component.generate();

    expect(generation.generate).not.toHaveBeenCalled();
  });

  it('correctOptionRadios_HaveNames', () => {
    generated();

    const radio = el().querySelector('.qg-option input[type=radio]') as HTMLInputElement;

    expect(radio.getAttribute('aria-label')).toBe(tr.correctOptionFor.replace('{{label}}', 'A'));
  });

  it('gradeChange_ClearsTheLoadFailure', () => {
    setup();
    outcomes.search.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.onGradeChange(5);
    component.onSubjectChange(7);

    component.onGradeChange(5);

    expect(component.outcomesFailed()).toBe(false);
  });

  it.each([
    [503, null, 'unavailable'],
    [429, 'RateLimited', 'limit'],
    [429, 'BudgetExceeded', 'budget'],
    [502, 'GenerationFailed', 'failed'],
  ])('generateFailure_%s_%s_ShowsTheReason', (status, errorCode, key) => {
    setup();
    chooseOutcome();
    generation.generate.mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { errorCode } })));

    component.generate();
    fixture.detectChanges();

    expect(text()).toContain(tr.errors[key as keyof typeof tr.errors]);
    expect(component.generating()).toBe(false);
  });

  it('save_SendsOnlySelectedDraftsWithEditsAndWithoutTheReasoning', () => {
    generated();
    component.setText(0, 'Düzeltilmiş?');
    component.setOption(0, 1, 'yeni');
    component.setCorrect(0, 1);
    component.setSelected(1, false);

    component.save();

    expect(generation.save).toHaveBeenCalledWith(11, 5, [{ text: 'Düzeltilmiş?', options: ['a', 'yeni', 'c', 'd'], correctIndex: 1 }]);
  });

  it('save_UsesTheDifficultyTheDraftsWereGeneratedWith', () => {
    generated();
    component.difficulty.set(8);

    component.save();

    expect(generation.save.mock.calls[0][1]).toBe(5);
  });

  it('save_Success_RemovesSavedDraftsSoTheyCannotBeSavedTwice', () => {
    generated();
    component.setSelected(1, false);

    component.save();
    fixture.detectChanges();

    expect(component.drafts().map((d) => d.text)).toEqual(['İkinci?']);
    expect(text()).toContain(tr.saved.replace('{{count}}', '2'));
  });

  it('save_Failure_KeepsTheDrafts', () => {
    generated();
    generation.save.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'the question refers to a visual' } })));

    component.save();
    fixture.detectChanges();

    expect(component.drafts().length).toBe(2);
    expect(text()).toContain('the question refers to a visual');
  });

  it.each([
    ['empty question', (c: QuestionGenerationComponent) => c.setText(0, '  ')],
    ['empty option', (c: QuestionGenerationComponent) => c.setOption(0, 0, ' ')],
    ['same options', (c: QuestionGenerationComponent) => c.setOption(0, 0, 'B')],
  ])('save_%s_IsBlockedWithAMessage', (_name, breakDraft) => {
    generated();

    breakDraft(component);
    fixture.detectChanges();

    expect(component.canSave()).toBe(false);
    expect((el().querySelector('.qg-save') as HTMLButtonElement).disabled).toBe(true);
    expect(text()).toContain(tr.incomplete);
    component.save();
    expect(generation.save).not.toHaveBeenCalled();
  });

  it('brokenDraftThatIsNotSelected_DoesNotBlockTheOthers', () => {
    generated();
    component.setText(0, '');
    component.setSelected(0, false);

    expect(component.canSave()).toBe(true);
  });

  it('nothingSelected_CannotSave', () => {
    generated();
    component.setSelected(0, false);
    component.setSelected(1, false);

    expect(component.canSave()).toBe(false);
  });

  it('removeDraft_DropsItFromTheList', () => {
    generated();

    component.removeDraft(0);

    expect(component.drafts().map((d) => d.text)).toEqual(['İkinci?']);
  });
});
