// Issue #587: boyama modeli — plan / cevap / çözüm ayrıştırma, payload, sayım, örnek çözüm dolgusu, boş cevap.
import {
  buildRegionPaintPayload,
  countPainted,
  isBlankRegionPaintPayload,
  parseRegionPaintAnswer,
  parseRegionPaintPlan,
  parseRegionPaintSolution,
  solutionCells,
} from './region-paint';

const PLAN = JSON.stringify({
  version: 1,
  type: 'regionPaint',
  mode: 'grid',
  rows: 2,
  cols: 3,
  palette: [
    { id: 'p1', color: 'blue', label: '1/2' },
    { id: 'p2', color: 'red' },
  ],
});

describe('region-paint (#587)', () => {
  it('parsePlan_ValidAndInvalid', () => {
    const plan = parseRegionPaintPlan(PLAN)!;
    expect(plan.palette.map((p) => p.color)).toEqual(['blue', 'red']);
    expect(plan.palette[1].label).toBe('');
    expect(parseRegionPaintPlan(PLAN.replace('"grid"', '"poly"'))).toBeNull();
    expect(parseRegionPaintPlan(PLAN.replace('"rows":2', '"rows":1'))).toBeNull();
    // İzinsiz renk atlanır; tek renk kalır.
    expect(parseRegionPaintPlan(PLAN.replace('"red"', '"#f00"'))!.palette.length).toBe(1);
    expect(parseRegionPaintPlan('{bozuk')).toBeNull();
  });

  it('answer_RoundTrip_IgnoresOutOfPalette', () => {
    const plan = parseRegionPaintPlan(PLAN)!;
    const cells = parseRegionPaintAnswer(JSON.stringify({ version: 1, type: 'regionPaint', grid: ['12.', '3.1'] }), plan);
    expect(cells).toEqual([
      [0, 1, null],
      [null, null, 0],
    ]);
    expect(buildRegionPaintPayload(cells)).toBe(JSON.stringify({ version: 1, type: 'regionPaint', grid: ['12.', '..1'] }));
    expect(countPainted(cells, 2)).toEqual([2, 1]);
  });

  it('solution_AndSampleFill', () => {
    const plan = parseRegionPaintPlan(PLAN)!;
    const counts = parseRegionPaintSolution(JSON.stringify({ counts: { p1: 3, p2: 1, p9: 2 } }), plan);
    expect(Object.keys(counts)).toEqual(['p1', 'p2']);
    expect(solutionCells(plan, counts)).toEqual([
      [0, 0, 0],
      [1, null, null],
    ]);
  });

  it('blankPayload', () => {
    expect(isBlankRegionPaintPayload(null)).toBe(true);
    expect(isBlankRegionPaintPayload(JSON.stringify({ version: 1, type: 'regionPaint', grid: ['...', '...'] }))).toBe(true);
    expect(isBlankRegionPaintPayload(JSON.stringify({ version: 1, type: 'regionPaint', grid: ['.1.', '...'] }))).toBe(false);
    expect(isBlankRegionPaintPayload(JSON.stringify({ version: 1, type: 'logicGrid', cells: [] }))).toBe(false);
  });
});
