import { describe, expect, it } from 'vitest';
import { planPassageLayout, planQuestionLayout, QuestionLayoutInput } from './question-layout-planner';

const fourAnswers = (width: number, height: number) => Array.from({ length: 4 }, () => ({ width, height }));

function input(overrides: Partial<QuestionLayoutInput>): QuestionLayoutInput {
  return {
    question: { width: 1000, height: 500 },
    answers: fourAnswers(200, 100),
    width: 1100,
    height: 800,
    minScale: 0,
    ...overrides,
  };
}

describe('planQuestionLayout', () => {
  it('sameDisplayRatioAtDifferentSourceResolutions_ReturnsSameArrangement', () => {
    const small = planQuestionLayout(input({ question: { width: 1000, height: 1200 }, answers: fourAnswers(450, 80) }));
    const large = planQuestionLayout(
      input({ question: { width: 3000, height: 3600 }, answers: fourAnswers(1350, 240) }),
    );

    expect(large.arrangement).toBe(small.arrangement);
    expect(large.answerColumns).toBe(small.answerColumns);
  });

  it('narrowChoicesOnWideQuestion_UsesFourColumns', () => {
    const plan = planQuestionLayout(input({ question: { width: 2000, height: 600 }, answers: fourAnswers(300, 80) }));

    expect(plan).toMatchObject({ arrangement: 'top', answerColumns: 4, fits: true });
  });

  it('choicesNearQuestionWidth_UsesOneColumn', () => {
    const plan = planQuestionLayout(input({ question: { width: 1000, height: 300 }, answers: fourAnswers(950, 40) }));

    expect(plan).toMatchObject({ arrangement: 'top', answerColumns: 1 });
  });

  it('tallQuestion_MovesChoicesBesideItBeforeReducingScale', () => {
    const plan = planQuestionLayout(
      input({ question: { width: 800, height: 1400 }, answers: fourAnswers(400, 150), height: 700 }),
    );

    expect(plan.arrangement).toBe('side');
    expect(plan.fits).toBe(true);
    expect(plan.scale).toBeCloseTo(0.5, 1);
  });

  it('choiceHeight_ChangesTheDecision', () => {
    const short = planQuestionLayout(input({ answers: fourAnswers(450, 40), height: 500, width: 1000 }));
    const tall = planQuestionLayout(input({ answers: fourAnswers(450, 400), height: 500, width: 1000 }));

    expect(tall.scale).toBeLessThan(short.scale);
    expect(`${tall.arrangement}${tall.answerColumns}`).not.toBe(`${short.arrangement}${short.answerColumns}`);
  });

  it('heightBelowFloor_KeepsFloorAndReportsThatItDoesNotFit', () => {
    const plan = planQuestionLayout(
      input({ question: { width: 2000, height: 3000 }, answers: fourAnswers(900, 300), height: 400, minScale: 0.3 }),
    );

    expect(plan.scale).toBeCloseTo(0.3, 2);
    expect(plan.fits).toBe(false);
  });

  it('fittingOnlyBelowFloorBecauseOfNarrowColumns_PrefersReadableLayout', () => {
    const plan = planQuestionLayout(
      input({
        question: { width: 1760, height: 2400 },
        answers: fourAnswers(710, 60),
        width: 764,
        height: 610,
        minScale: 0.27,
      }),
    );

    expect(plan.scale).toBeGreaterThanOrEqual(0.27 * 0.95);
    expect(plan.answerColumns).toBeLessThan(4);
  });

  it('nothingFits_ChoosesLayoutThatNeedsLeastScrolling', () => {
    const plan = planQuestionLayout(
      input({ question: { width: 2000, height: 3000 }, answers: fourAnswers(800, 200), width: 1000, height: 600, minScale: 0.24 }),
    );

    expect(plan.arrangement).toBe('side');
    expect(plan.fits).toBe(false);
  });

  it('maxScale_LimitsScaleEvenWithRoomToGrow', () => {
    const plan = planQuestionLayout(input({ question: { width: 2000, height: 500 }, answers: fourAnswers(400, 80), maxScale: 0.3 }));

    expect(plan.scale).toBe(0.3);
    expect(plan.fits).toBe(true);
  });

  it('teacherChoice_OverridesTheProposedArrangement', () => {
    const tall = { question: { width: 800, height: 1400 }, answers: fourAnswers(400, 150), height: 700 };

    expect(planQuestionLayout(input(tall)).arrangement).toBe('side');
    expect(planQuestionLayout(input({ ...tall, arrangement: 'top' })).arrangement).toBe('top');
    expect(planQuestionLayout(input({ arrangement: 'side' })).arrangement).toBe('side');
  });

  it('sideChoiceOnPhone_StaysOnTop', () => {
    const plan = planQuestionLayout(input({ width: 358, height: null, arrangement: 'side' }));

    expect(plan.arrangement).toBe('top');
  });

  it('phoneWidth_NeverPlacesChoicesBesideQuestion', () => {
    const plan = planQuestionLayout(
      input({ question: { width: 800, height: 1600 }, answers: fourAnswers(300, 100), width: 358, height: null }),
    );

    expect(plan.arrangement).toBe('top');
    expect(plan.answerColumns).toBeLessThanOrEqual(2);
    expect(plan.scale).toBeCloseTo(358 / 800, 2);
  });

  it('wideChoicesOnPhone_UsesOneColumnSoQuestionKeepsItsWidth', () => {
    const plan = planQuestionLayout(
      input({ question: { width: 1000, height: 400 }, answers: fourAnswers(900, 60), width: 358, height: null }),
    );

    expect(plan.answerColumns).toBe(1);
    expect(plan.scale).toBeCloseTo(0.358, 2);
  });

  it('smallSourceImage_IsNotEnlargedAboveNaturalSize', () => {
    const plan = planQuestionLayout(input({ question: { width: 400, height: 200 }, answers: fourAnswers(80, 40) }));

    expect(plan.scale).toBe(1);
  });

  it('twoChoices_DoesNotAskForMoreColumnsThanChoices', () => {
    const plan = planQuestionLayout(
      input({ answers: [{ width: 100, height: 40 }, { width: 100, height: 40 }] }),
    );

    expect(plan.answerColumns).toBe(2);
  });

  it('choicesWithoutRecordedSize_StillReturnsAUsablePlan', () => {
    const plan = planQuestionLayout(input({ answers: fourAnswers(0, 0) }));

    expect(plan.scale).toBeGreaterThan(0);
    expect(plan.fits).toBe(true);
  });

  it('noChoices_FitsQuestionAlone', () => {
    const plan = planQuestionLayout(input({ answers: [], question: { width: 1000, height: 1600 }, height: 800 }));

    expect(plan.scale).toBeCloseTo(0.5, 1);
    expect(plan.fits).toBe(true);
  });
});

describe('planPassageLayout', () => {
  it('widePassage_StaysOnTopAndLeavesRoomForQuestion', () => {
    const plan = planPassageLayout({ passage: { width: 4000, height: 1000 }, width: 1000, height: 700 });

    expect(plan.arrangement).toBe('top');
    expect(plan.boxWidth).toBe(1000);
    expect(plan.questionHeight).toBe(700 - 250 - 16);
  });

  it('widePassageOnShortScreen_IsLimitedSoQuestionStaysVisible', () => {
    const plan = planPassageLayout({ passage: { width: 2000, height: 1000 }, width: 1000, height: 400 });

    expect(plan.boxWidth / 2).toBeCloseTo(400 * 0.45, 5);
    expect(plan.questionHeight).toBeCloseTo(400 - 180 - 16, 5);
  });

  it('widePassageOnPhone_UsesFullWidthOnTop', () => {
    const plan = planPassageLayout({ passage: { width: 4000, height: 1000 }, width: 358, height: null });

    expect(plan).toMatchObject({ arrangement: 'top', boxWidth: 358, questionHeight: null });
  });

  it('longPassageOnWideScreen_StaysBesideAndScrollsInsideHeight', () => {
    const plan = planPassageLayout({ passage: { width: 3800, height: 3400 }, width: 1112, height: 700 });

    expect(plan.arrangement).toBe('beside');
    expect(plan.boxWidth).toBeCloseTo(500.4, 1);
    expect(plan.boxHeight).toBe(700);
    expect(plan.questionWidth).toBeCloseTo(1112 - 500.4 - 16, 1);
  });

  it('longPassageOnNarrowScreen_UsesTabs', () => {
    const plan = planPassageLayout({ passage: { width: 3800, height: 3400 }, width: 764, height: 600 });

    expect(plan).toMatchObject({ arrangement: 'tabs', questionWidth: 764, questionHeight: 600 });
  });

  it('passageWithoutRecordedSize_IsTreatedAsLongText', () => {
    expect(planPassageLayout({ passage: null, width: 1112, height: 700 }).arrangement).toBe('beside');
    expect(planPassageLayout({ passage: null, width: 358, height: null }).arrangement).toBe('tabs');
  });
});
