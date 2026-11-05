import { TargetMockResult, TargetProgram } from '../../../models/target-goal.model';
import { EXAM_SCORE_EXAMS, ExamScoreExam, ScoreEstimate, TestAnswers, estimateScores, netOf, pointsPerNet } from './exam-score';

/** Şablondaki ders satırının hesaplayıcı testi. Grup anahtarı TYT'de birkaç dersi tek teste toplar (`ExamTemplateSeed`). */
const SECTION_TESTS: Record<string, { byGroup?: Record<string, string>; byName: Record<string, string> }> = {
  lgs: {
    byName: {
      'Türkçe': 'turkish',
      'T.C. İnkılap Tarihi ve Atatürkçülük': 'revolutionHistory',
      'Din Kültürü ve Ahlak Bilgisi': 'religion',
      'Yabancı Dil': 'foreignLanguage',
      'Matematik': 'mathematics',
      'Fen Bilimleri': 'science',
    },
  },
  tyt: {
    byGroup: { 'Sosyal Bilimler': 'tytSocial', 'Fen Bilimleri': 'tytScience' },
    byName: { 'Türkçe': 'tytTurkish', 'Temel Matematik': 'tytMathematics' },
  },
  ayt: {
    byName: {
      'Türk Dili ve Edebiyatı': 'aytLiterature',
      'Tarih-1': 'aytHistory1',
      'Coğrafya-1': 'aytGeography1',
      'Tarih-2': 'aytHistory2',
      'Coğrafya-2': 'aytGeography2',
      'Felsefe Grubu': 'aytPhilosophy',
      'Din Kültürü ve Ahlak Bilgisi': 'aytReligion',
      'Matematik': 'aytMathematics',
      'Fizik': 'aytPhysics',
      'Kimya': 'aytChemistry',
      'Biyoloji': 'aytBiology',
    },
  },
};

export const requiredTemplates = (scoreType: string): string[] =>
  scoreType === 'lgs' ? ['lgs'] : scoreType === 'tyt' ? ['tyt'] : ['tyt', 'ayt'];

export interface SubjectGap {
  testId: string;
  label: string;
  currentNet: number;
  addNets: number;
}

export type TargetGapResult =
  /** Puan türünün gerektirdiği deneme tamamlanmadı: fark görünmez. */
  | { status: 'needsExam'; missing: string[] }
  /** Şablon hesaplayıcıyla eşleşmiyor ya da puan türü modelde yok: yanlış sayı göstermektense fark gösterilmez. */
  | { status: 'unavailable' }
  /** Baraj sağlanmadı (#694): puan hesaplanmaz. */
  | { status: 'notQualified' }
  | { status: 'reached'; estimate: ScoreEstimate; modelYear: number }
  /** Her ders tam net olsa da model tabana ulaşmıyor: ders önerisi anlamsız. */
  | { status: 'outOfReach'; estimate: ScoreEstimate; pointsShort: number; modelYear: number }
  | { status: 'gap'; estimate: ScoreEstimate; pointsShort: number; totalNets: number; subjects: SubjectGap[]; modelYear: number };

const examOf = (scoreType: string): ExamScoreExam | undefined =>
  EXAM_SCORE_EXAMS.find((exam) => exam.scores.some((score) => score.id === scoreType));

/** Yukarı yuvarlanır: öneri eksik kalıp hedefin altında bırakmasın. */
const ceilTenth = (value: number): number => Math.ceil(value * 10 - 1e-9) / 10;

function collectAnswers(results: TargetMockResult[]): { answers: Record<string, TestAnswers>; labels: Record<string, string> } | null {
  const answers: Record<string, TestAnswers> = {};
  const labels: Record<string, string> = {};
  for (const result of results) {
    const map = SECTION_TESTS[result.templateCode];
    if (!map) {
      return null;
    }
    for (const section of result.sections) {
      const group = section.groupName ? map.byGroup?.[section.groupName] : undefined;
      const testId = group ?? map.byName[section.name];
      if (!testId) {
        return null;
      }
      const sum = answers[testId] ?? { correct: 0, wrong: 0 };
      answers[testId] = { correct: sum.correct + section.correct, wrong: sum.wrong + section.wrong };
      labels[testId] = group ? section.groupName! : section.name;
    }
  }
  return { answers, labels };
}

/**
 * Issue #695: son denemeden tahmini puan ve tabana kalan fark. Eksik puan, derslere kalan netleri oranında dağıtılır:
 * her ders kalan netinin aynı payını kazanır. Bu bir öneridir, tek doğru dağılım değildir; puan modeli #694 ile aynıdır.
 */
export function computeTargetGap(target: TargetProgram, results: TargetMockResult[]): TargetGapResult {
  const required = requiredTemplates(target.scoreType);
  const usable = results.filter((result) => result.complete && required.includes(result.templateCode));
  const missing = required.filter((code) => !usable.some((result) => result.templateCode === code));
  if (missing.length > 0) {
    return { status: 'needsExam', missing };
  }

  const exam = examOf(target.scoreType);
  const collected = collectAnswers(usable);
  const type = exam?.scores.find((score) => score.id === target.scoreType);
  if (!exam || !type || !collected) {
    return { status: 'unavailable' };
  }

  const estimate = estimateScores(exam, collected.answers).find((score) => score.id === target.scoreType)!;
  if (estimate.value === null) {
    return { status: 'notQualified' };
  }

  const pointsShort = Math.ceil(target.minimumScore - estimate.value);
  if (pointsShort <= 0) {
    return { status: 'reached', estimate, modelYear: exam.year };
  }

  const slopes = pointsPerNet(exam, type);
  const rows = Object.entries(slopes).map(([testId, slope]) => {
    const test = exam.tests.find((t) => t.id === testId)!;
    const answers = collected.answers[testId] ?? { correct: 0, wrong: 0 };
    const currentNet = netOf(answers, exam.wrongAnswersPerCorrect);
    const max = test.validQuestions ?? test.questions;
    return { testId, slope, currentNet, room: Math.max(0, max - currentNet), label: collected.labels[testId] ?? testId };
  });
  const reachablePoints = rows.reduce((sum, row) => sum + row.slope * row.room, 0);
  if (pointsShort > reachablePoints) {
    return { status: 'outOfReach', estimate, pointsShort, modelYear: exam.year };
  }

  const share = pointsShort / reachablePoints;
  const subjects = rows
    .map((row) => ({ testId: row.testId, label: row.label, currentNet: row.currentNet, addNets: Math.min(row.room, ceilTenth(row.room * share)) }))
    .filter((row) => row.addNets > 0);
  const totalNets = Math.round(subjects.reduce((sum, row) => sum + row.addNets, 0) * 10) / 10;
  return { status: 'gap', estimate, pointsShort, totalNets, subjects, modelYear: exam.year };
}
