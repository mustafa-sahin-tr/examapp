import { TargetMockResult, TargetMockSection, TargetProgram } from '../../../models/target-goal.model';
import { EXAM_SCORE_EXAMS, TestAnswers, estimateScores } from './exam-score';
import { computeTargetGap, estimateScore, targetProgress } from './target-gap';

const target = (scoreType: string, minimumScore: number, year = 2026): TargetProgram => ({
  id: 1, scoreType, name: 'Örnek', institution: '', city: 'Ankara', district: '', year, minimumScore, percentile: null, sourceReference: null,
});

const section = (name: string, correct: number, wrong = 0, groupName: string | null = null): TargetMockSection => ({
  groupName, name, questionCount: 40, correct, wrong, blank: 0, net: correct, pending: false,
});

const result = (templateCode: string, sections: TargetMockSection[], complete = true): TargetMockResult => ({
  id: 1, name: 'Deneme', templateName: templateCode, templateCode, wrongAnswersPerCorrect: 4, complete, sections, net: 0,
});

const lgs = (correct: number) => result('lgs', [
  section('Türkçe', correct), section('T.C. İnkılap Tarihi ve Atatürkçülük', correct / 2), section('Din Kültürü ve Ahlak Bilgisi', correct / 2),
  section('Yabancı Dil', correct / 2), section('Matematik', correct), section('Fen Bilimleri', correct),
]);

const tyt = (correct: number) => result('tyt', [
  section('Türkçe', correct), section('Tarih', 2, 0, 'Sosyal Bilimler'), section('Coğrafya', 3, 1, 'Sosyal Bilimler'),
  section('Temel Matematik', correct), section('Fizik', 4, 0, 'Fen Bilimleri'),
]);

describe('computeTargetGap (#695)', () => {
  it('shows nothing before a required mock exam is complete', () => {
    expect(computeTargetGap(target('lgs', 450), [])).toEqual({ status: 'needsExam', missing: ['lgs'] });
    expect(computeTargetGap(target('lgs', 450), [{ ...lgs(10), complete: false }])).toEqual({ status: 'needsExam', missing: ['lgs'] });
  });

  it('a YKS field score needs both TYT and AYT', () => {
    expect(computeTargetGap(target('say', 450), [tyt(20)])).toEqual({ status: 'needsExam', missing: ['ayt'] });
  });

  it('suggested nets per subject bring the estimate up to the minimum score', () => {
    const gap = computeTargetGap(target('lgs', 450), [lgs(10)]);
    if (gap.status !== 'gap') {
      throw new Error(gap.status);
    }

    expect(gap.pointsShort).toBeGreaterThan(0);
    expect(gap.subjects.map((s) => s.testId).sort()).toEqual(['foreignLanguage', 'mathematics', 'religion', 'revolutionHistory', 'science', 'turkish']);
    expect(gap.totalNets).toBeCloseTo(gap.subjects.reduce((sum, s) => sum + s.addNets, 0), 5);

    const exam = EXAM_SCORE_EXAMS.find((e) => e.id === 'lgs')!;
    const improved: Record<string, TestAnswers> = {};
    for (const subject of gap.subjects) {
      improved[subject.testId] = { correct: subject.currentNet + subject.addNets, wrong: 0 };
    }
    const after = estimateScores(exam, improved).find((s) => s.id === 'lgs')!;
    expect(after.value!).toBeGreaterThanOrEqual(450);
    expect(after.value!).toBeLessThanOrEqual(452);
  });

  it('adds up TYT group sections as one test and counts wrong answers', () => {
    const gap = computeTargetGap(target('tyt', 450), [tyt(20)]);
    if (gap.status !== 'gap') {
      throw new Error(gap.status);
    }

    const social = gap.subjects.find((s) => s.testId === 'tytSocial')!;
    expect(social.label).toBe('Sosyal Bilimler');
    expect(social.currentNet).toBeCloseTo(4.75, 5);
  });

  it('says reached without a split when the estimate is at the minimum score', () => {
    expect(computeTargetGap(target('lgs', 100), [lgs(10)]).status).toBe('reached');
  });

  it('gives no split when even full nets cannot reach the minimum score', () => {
    const beyond = computeTargetGap({ ...target('tyt', 400), minimumScore: 5000 }, [tyt(20)]);
    expect(beyond.status).toBe('outOfReach');
  });

  it('does not invent a number for a template it cannot map', () => {
    expect(computeTargetGap(target('lgs', 450), [result('lgs', [section('Bilinmeyen Ders', 5), section('Türkçe', 5)])]).status).toBe('unavailable');
  });

  it('reports a failed threshold instead of a score', () => {
    expect(computeTargetGap(target('tyt', 300), [result('tyt', [section('Türkçe', 0), section('Temel Matematik', 0)])]).status).toBe('notQualified');
  });

  it('keeps the model year so the screen can flag a different target year', () => {
    const gap = computeTargetGap(target('lgs', 450, 2025), [lgs(10)]);
    expect(gap.status === 'gap' && gap.modelYear).toBe(2026);
  });
});

describe('targetProgress', () => {
  const lgsSections = (correct: number): TargetMockSection[] =>
    [
      ['Türkçe', 20],
      ['Matematik', 20],
      ['Fen Bilimleri', 20],
      ['T.C. İnkılap Tarihi ve Atatürkçülük', 10],
      ['Din Kültürü ve Ahlak Bilgisi', 10],
      ['Yabancı Dil', 10],
    ].map(([name, count]) => ({
      groupName: null,
      name: name as string,
      questionCount: count as number,
      correct: Math.min(correct, count as number),
      wrong: 0,
      blank: (count as number) - Math.min(correct, count as number),
      net: Math.min(correct, count as number),
      pending: false,
    }));
  const result = (id: number, finishedAt: string, correct: number, complete = true): TargetMockResult => ({
    id,
    name: `Deneme ${id}`,
    templateName: 'LGS',
    templateCode: 'lgs',
    wrongAnswersPerCorrect: 3,
    complete,
    net: correct,
    finishedAt,
    sections: lgsSections(correct),
  });

  it('keepsHistoryOrderAndHigherNetGivesHigherScore', () => {
    const points = targetProgress('lgs', [result(1, '2026-10-01T10:00:00Z', 8), result(2, '2026-10-02T10:00:00Z', 18)]);

    expect(points.map((p) => p.id)).toEqual([1, 2]);
    expect(points[1].score).toBeGreaterThan(points[0].score);
  });

  it('skipsUnfinishedExamsAndOtherScoreTypes', () => {
    expect(targetProgress('lgs', [result(1, '2026-10-01T10:00:00Z', 8, false)])).toEqual([]);
    expect(estimateScore('tyt', [result(1, '2026-10-01T10:00:00Z', 8)])).toBeNull();
  });
});
