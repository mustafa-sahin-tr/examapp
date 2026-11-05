import examScoreData from './exam-score-data.json';

/** #694: sınav verisi yıllık JSON'dan gelir; hesap yalnız tarayıcıda yapılır, girilen sayı hiçbir yere gönderilmez. */
export interface ExamScoreSource {
  label: string;
  url: string;
}

export interface ExamScoreTest {
  id: string;
  session: string;
  questions: number;
  /** Sınavdan sonra iptal edilen soru varsa geçerli soru sayısı; ölçeğin üst sınırı bununla hesaplanır. */
  validQuestions?: number;
  mean: number;
  standardDeviation: number;
}

export interface ExamScoreType {
  id: string;
  weights: Record<string, number>;
  /** AYT puan türlerinde ağırlıklı TYT puanının payı. */
  tytWeight?: number;
  marginPoints: number;
  scaleMin: number;
  scaleMax: number;
  /** Puanın hesaplanması için bu testlerin en az birinde 0,5 ham puan gerekir (ÖSYM kılavuzu). */
  qualifyingTests?: string[];
}

export interface ExamScoreExam {
  id: string;
  year: number;
  wrongAnswersPerCorrect: number;
  statisticsBasis: string;
  sources: ExamScoreSource[];
  tests: ExamScoreTest[];
  scores: ExamScoreType[];
}

export interface TestAnswers {
  correct: number;
  wrong: number;
}

export interface ScoreEstimate {
  id: string;
  /** Puan hesaplanamıyorsa (baraj sağlanmadı) `null`. */
  value: number | null;
  low: number | null;
  high: number | null;
}

export const EXAM_SCORE_EXAMS = examScoreData.exams as ExamScoreExam[];

const MIN_SCORE = 100;
const MAX_SCORE = 500;
const QUALIFYING_RAW_SCORE = 0.5;

export function netOf(answers: TestAnswers, wrongAnswersPerCorrect: number): number {
  return answers.correct - answers.wrong / wrongAnswersPerCorrect;
}

export function isValidAnswers(answers: TestAnswers, questions: number): boolean {
  const { correct, wrong } = answers;
  return Number.isInteger(correct) && Number.isInteger(wrong) && correct >= 0 && wrong >= 0 && correct + wrong <= questions;
}

function clampScore(value: number): number {
  return Math.min(MAX_SCORE, Math.max(MIN_SCORE, value));
}

/** Doğru veya yanlış eksikse o test boş sayılır. */
export function estimateScores(exam: ExamScoreExam, answers: Record<string, TestAnswers | undefined>): ScoreEstimate[] {
  const raw = (testId: string) => {
    const answer = answers[testId];
    return answer ? netOf(answer, exam.wrongAnswersPerCorrect) : 0;
  };
  const standard = (test: ExamScoreTest) => 50 + (10 * (raw(test.id) - test.mean)) / test.standardDeviation;
  const weighted = (type: ExamScoreType) =>
    Object.entries(type.weights).reduce((sum, [testId, weight]) => sum + weight * standard(exam.tests.find((t) => t.id === testId)!), 0);
  const qualifies = (type: ExamScoreType) => !type.qualifyingTests || type.qualifyingTests.some((id) => raw(id) >= QUALIFYING_RAW_SCORE);

  const tyt = exam.scores.find((type) => type.id === 'tyt');
  return exam.scores.map((type) => {
    if (!qualifies(type) || (type.tytWeight && tyt && !qualifies(tyt))) {
      return { id: type.id, value: null, low: null, high: null };
    }
    const total = weighted(type) + (type.tytWeight && tyt ? type.tytWeight * weighted(tyt) : 0);
    const value = clampScore(MIN_SCORE + ((MAX_SCORE - MIN_SCORE) * (total - type.scaleMin)) / (type.scaleMax - type.scaleMin));
    return {
      id: type.id,
      value: Math.round(value),
      low: Math.round(clampScore(value - type.marginPoints)),
      high: Math.round(clampScore(value + type.marginPoints)),
    };
  });
}
