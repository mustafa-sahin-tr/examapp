import { EXAM_SCORE_EXAMS, ExamScoreExam, ExamScoreType, estimateScores, isValidAnswers, netOf } from './exam-score';

const examById = (id: string): ExamScoreExam => EXAM_SCORE_EXAMS.find((exam) => exam.id === id)!;
const scoreOf = (exam: ExamScoreExam, id: string, answers: Parameters<typeof estimateScores>[1]) =>
  estimateScores(exam, answers).find((estimate) => estimate.id === id)!;

const allCorrect = (exam: ExamScoreExam) => Object.fromEntries(exam.tests.map((test) => [test.id, { correct: test.questions, wrong: 0 }]));

describe('exam score (#694)', () => {
  it('net_UsesEachExamsPenalty', () => {
    expect(netOf({ correct: 10, wrong: 3 }, 3)).toBe(9);
    expect(netOf({ correct: 10, wrong: 4 }, 4)).toBe(9);
  });

  it('answers_RejectTooManyNegativeOrFractional', () => {
    expect(isValidAnswers({ correct: 15, wrong: 5 }, 20)).toBe(true);
    expect(isValidAnswers({ correct: 15, wrong: 6 }, 20)).toBe(false);
    expect(isValidAnswers({ correct: -1, wrong: 0 }, 20)).toBe(false);
    expect(isValidAnswers({ correct: 1.5, wrong: 0 }, 20)).toBe(false);
  });

  it('data_NamesYearAndSourceForEveryExam', () => {
    for (const exam of EXAM_SCORE_EXAMS) {
      expect(exam.year, exam.id).toBeGreaterThanOrEqual(2026);
      expect(exam.sources.length, exam.id).toBeGreaterThan(0);
      for (const type of exam.scores) {
        for (const testId of Object.keys(type.weights)) {
          expect(exam.tests.some((test) => test.id === testId), testId).toBe(true);
        }
      }
    }
  });

  it('scaleBounds_MatchTheDocumentedRule', () => {
    const standard = (exam: ExamScoreExam, testId: string, raw: number) => {
      const test = exam.tests.find((t) => t.id === testId)!;
      return 50 + (10 * (raw - test.mean)) / test.standardDeviation;
    };
    const lowest = (exam: ExamScoreExam, type: ExamScoreType): number => {
      const ids = Object.keys(type.weights);
      const total = (rawOf: (id: string) => number) => ids.reduce((sum, id) => sum + type.weights[id] * standard(exam, id, rawOf(id)), 0);
      if (!type.qualifyingTests) {
        return total((id) => -exam.tests.find((t) => t.id === id)!.questions / exam.wrongAnswersPerCorrect);
      }
      const tyt = type.tytWeight ? type.tytWeight * lowest(exam, exam.scores.find((s) => s.id === 'tyt')!) : 0;
      return tyt + Math.min(...type.qualifyingTests.map((qualifying) => total((id) => (id === qualifying ? 0.5 : 0))));
    };
    const highest = (exam: ExamScoreExam, type: ExamScoreType): number => {
      const own = Object.entries(type.weights).reduce((sum, [id, weight]) => {
        const test = exam.tests.find((t) => t.id === id)!;
        return sum + weight * standard(exam, id, test.validQuestions ?? test.questions);
      }, 0);
      return own + (type.tytWeight ? type.tytWeight * highest(exam, exam.scores.find((s) => s.id === 'tyt')!) : 0);
    };
    for (const exam of EXAM_SCORE_EXAMS) {
      for (const type of exam.scores) {
        expect(type.scaleMin, `${type.id} min`).toBeCloseTo(lowest(exam, type), 1);
        expect(type.scaleMax, `${type.id} max`).toBeCloseTo(highest(exam, type), 1);
      }
    }
  });

  it('lgs_FullMarksGive500_EvenNetsFollowTheScale', () => {
    const exam = examById('lgs');
    expect(scoreOf(exam, 'lgs', allCorrect(exam)).value).toBe(500);
    expect(scoreOf(exam, 'lgs', {}).value).toBe(200);
    const half = Object.fromEntries(exam.tests.map((test) => [test.id, { correct: test.questions / 2, wrong: 0 }]));
    expect(scoreOf(exam, 'lgs', half).value).toBe(350);
  });

  it('range_StaysInsideTheOfficialScale', () => {
    const exam = examById('lgs');
    const full = scoreOf(exam, 'lgs', allCorrect(exam));
    expect(full.high).toBe(500);
    expect(full.low).toBe(475);
  });

  it('yks_FullMarksGive500ForEveryScoreType', () => {
    const exam = examById('yks');
    for (const estimate of estimateScores(exam, allCorrect(exam))) {
      expect(estimate.value, estimate.id).toBe(500);
    }
  });

  it('yks_NoScoreBelowTheRawScoreRequirement', () => {
    const exam = examById('yks');
    expect(estimateScores(exam, {}).every((estimate) => estimate.value === null)).toBe(true);
    const tytOnly = { tytTurkish: { correct: 20, wrong: 0 } };
    expect(scoreOf(exam, 'tyt', tytOnly).value).not.toBeNull();
    expect(scoreOf(exam, 'say', tytOnly).value).toBeNull();
  });

  it('yks_AytScoreNeedsATytScore', () => {
    const exam = examById('yks');
    expect(scoreOf(exam, 'say', { aytMathematics: { correct: 20, wrong: 0 } }).value).toBeNull();
  });

  it('yks_MoreNetsRaiseTheScore', () => {
    const exam = examById('yks');
    const low = scoreOf(exam, 'tyt', { tytTurkish: { correct: 10, wrong: 0 } }).value!;
    const high = scoreOf(exam, 'tyt', { tytTurkish: { correct: 30, wrong: 0 } }).value!;
    expect(high).toBeGreaterThan(low);
  });
});
