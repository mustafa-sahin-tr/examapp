// Issue #586: çengel bulmaca modeli — plan ayrıştırma, numaralama, cevap ızgarası, çözüm, boş cevap.
import {
  buildCrosswordPayload,
  cellNumbers,
  clueNumbers,
  clueWord,
  isBlankCrosswordPayload,
  parseCrosswordAnswer,
  parseCrosswordPlan,
  parseCrosswordSolution,
  solutionGrid,
  toCellChar,
  whiteCells,
} from './crossword';

// K A L P
// İ . . .
// R A F .
const PLAN = {
  version: 1,
  type: 'crossword',
  rows: 3,
  cols: 4,
  clues: [
    { id: 'k1', dir: 'across', row: 0, col: 0, length: 4, text: 'Organ' },
    { id: 'k2', dir: 'down', row: 0, col: 0, length: 3, text: 'Kirli' },
    { id: 'k3', dir: 'across', row: 2, col: 0, length: 3, text: 'Raf' },
  ],
};

describe('crossword model (#586)', () => {
  it('parsePlan_SkipsCluesOutsideGrid_AndRejectsBadSize', () => {
    const plan = parseCrosswordPlan(
      JSON.stringify({ ...PLAN, clues: [...PLAN.clues, { id: 'k4', dir: 'down', row: 1, col: 3, length: 3, text: 'x' }] })
    );
    expect(plan?.clues.map((c) => c.id)).toEqual(['k1', 'k2', 'k3']);
    expect(parseCrosswordPlan(JSON.stringify({ ...PLAN, rows: 1 }))).toBeNull();
    expect(parseCrosswordPlan(JSON.stringify({ ...PLAN, type: 'mazeTree' }))).toBeNull();
    expect(parseCrosswordPlan('{bozuk')).toBeNull();
  });

  it('numbers_AutoBySharedStartCells_KeepsExplicitNumbers', () => {
    const plan = parseCrosswordPlan(JSON.stringify(PLAN))!;
    expect([...clueNumbers(plan).entries()]).toEqual([
      ['k1', 1],
      ['k2', 1],
      ['k3', 2],
    ]);
    expect(cellNumbers(plan).get('2,0')).toBe(2);

    const explicit = parseCrosswordPlan(
      JSON.stringify({ ...PLAN, clues: PLAN.clues.map((c) => (c.id === 'k3' ? { ...c, number: 1 } : c)) })
    )!;
    // k3 1 numarasını aldı; k1/k2'nin hücresi kullanılmayan ilk numarayı (2) alır.
    expect(clueNumbers(explicit).get('k3')).toBe(1);
    expect(clueNumbers(explicit).get('k1')).toBe(2);
    expect(whiteCells(explicit).size).toBe(8);
  });

  it('answer_ParsesGrid_DropsBlackCellsAndBadChars_Uppercases', () => {
    const plan = parseCrosswordPlan(JSON.stringify(PLAN))!;
    const grid = parseCrosswordAnswer(
      JSON.stringify({ version: 1, type: 'crossword', grid: ['kalp', 'iX  ', 'ra- '] }),
      plan
    );
    expect(grid.map((r) => r.join(''))).toEqual(['KALP', 'İ   ', 'RA  ']);
    expect(clueWord(grid, plan.clues[2])).toBe('RA ');
    expect(buildCrosswordPayload(grid)).toBe(JSON.stringify({ version: 1, type: 'crossword', grid: ['KALP', 'İ   ', 'RA  '] }));
  });

  it('solution_FillsGrid_SkipsWrongLength', () => {
    const plan = parseCrosswordPlan(JSON.stringify(PLAN))!;
    const answers = parseCrosswordSolution(JSON.stringify({ answers: { k1: 'kalp', k2: 'KİR', k3: 'RAFF' } }), plan);
    expect(answers['k1']).toBe('KALP');
    expect(Object.hasOwn(answers, 'k3')).toBe(false);
    expect(solutionGrid(plan, answers)[1][0]).toBe('İ');
  });

  it('blankPayload_AndCellChar', () => {
    expect(isBlankCrosswordPayload(null)).toBe(true);
    expect(isBlankCrosswordPayload(JSON.stringify({ type: 'crossword', grid: ['   ', '  '] }))).toBe(true);
    expect(isBlankCrosswordPayload(JSON.stringify({ type: 'crossword', grid: [' A '] }))).toBe(false);
    expect(isBlankCrosswordPayload(JSON.stringify({ type: 'mazeTree', path: [] }))).toBe(false);
    expect(toCellChar('ai')).toBe('İ');
    expect(toCellChar('7')).toBe('7');
    expect(toCellChar('-')).toBe('');
  });
});
