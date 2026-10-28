// Issue #586: kelime avı modeli — plan ayrıştırma, düz çizgi, harf okuma, cevap / çözüm, boş cevap.
import {
  buildWordSearchPayload,
  isBlankWordSearchPayload,
  isStraight,
  lineCells,
  parseWordSearchAnswer,
  parseWordSearchPlan,
  parseWordSearchSolution,
  sameLine,
  spell,
} from './word-search';

const PLAN = {
  version: 1,
  type: 'wordSearch',
  rows: 4,
  cols: 5,
  letters: ['KALPX', 'EYZİM', 'DTARU', 'İNOKE'],
  words: [
    { id: 'w1', label: 'KALP' },
    { id: 'w2', label: 'KEDİ' },
  ],
};

describe('word-search model (#586)', () => {
  it('parsePlan_RequiresFullGrid', () => {
    expect(parseWordSearchPlan(JSON.stringify(PLAN))?.letters[1][3]).toBe('İ');
    expect(parseWordSearchPlan(JSON.stringify({ ...PLAN, letters: ['KALP', 'EYZİM', 'DTARU', 'İNOKE'] }))).toBeNull();
    expect(parseWordSearchPlan(JSON.stringify({ ...PLAN, rows: 3 }))).toBeNull();
    expect(parseWordSearchPlan(JSON.stringify({ ...PLAN, type: 'crossword' }))).toBeNull();
  });

  it('straightLines_CellsAndSpelling', () => {
    expect(isStraight([0, 0], [0, 3])).toBe(true);
    expect(isStraight([1, 1], [3, 3])).toBe(true);
    expect(isStraight([0, 0], [1, 2])).toBe(false);
    expect(isStraight([0, 0], [0, 0])).toBe(false);
    expect(lineCells({ from: [2, 3], to: [2, 1] })).toEqual([
      [2, 3],
      [2, 2],
      [2, 1],
    ]);
    const plan = parseWordSearchPlan(JSON.stringify(PLAN))!;
    expect(spell(plan, { from: [3, 0], to: [0, 0] })).toBe('İDEK');
    expect(sameLine({ from: [0, 0], to: [0, 3] }, { from: [0, 3], to: [0, 0] })).toBe(true);
  });

  it('answer_DropsCrookedAndOutOfGridLines_RoundTrips', () => {
    const plan = parseWordSearchPlan(JSON.stringify(PLAN))!;
    const payload = JSON.stringify({
      version: 1,
      type: 'wordSearch',
      lines: [
        { from: [0, 0], to: [0, 3] },
        { from: [0, 0], to: [1, 2] },
        { from: [0, 0], to: [0, 9] },
      ],
    });
    const lines = parseWordSearchAnswer(payload, plan);
    expect(lines).toEqual([{ from: [0, 0], to: [0, 3] }]);
    expect(buildWordSearchPayload(lines)).toBe(JSON.stringify({ version: 1, type: 'wordSearch', lines: [{ from: [0, 0], to: [0, 3] }] }));
  });

  it('solution_AndBlankPayload', () => {
    const plan = parseWordSearchPlan(JSON.stringify(PLAN))!;
    const solution = parseWordSearchSolution(
      JSON.stringify({ lines: { w1: { from: [0, 0], to: [0, 3] }, w9: { from: [0, 0], to: [1, 0] } } }),
      plan
    );
    expect(Object.keys(solution)).toEqual(['w1']);
    expect(isBlankWordSearchPayload(null)).toBe(true);
    expect(isBlankWordSearchPayload(JSON.stringify({ type: 'wordSearch', lines: [] }))).toBe(true);
    expect(isBlankWordSearchPayload(buildWordSearchPayload([{ from: [0, 0], to: [0, 1] }]))).toBe(false);
    expect(isBlankWordSearchPayload(JSON.stringify({ type: 'crossword', grid: [] }))).toBe(false);
  });
});
