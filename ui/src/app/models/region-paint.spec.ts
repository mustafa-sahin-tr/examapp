// Issue #587: boyama modeli — plan / cevap / çözüm ayrıştırma, payload, sayım, örnek çözüm dolgusu, boş cevap.
import {
  buildRegionPaintFillsPayload,
  buildRegionPaintPayload,
  countPainted,
  isBlankRegionPaintPayload,
  parseRegionPaintAnswer,
  parseRegionPaintFeedback,
  parseRegionPaintFills,
  parseRegionPaintFillsSolution,
  parseRegionPaintPlan,
  parseRegionPaintRegionsPlan,
  parseRegionPaintSolution,
  pointInPolygon,
  polygonArea,
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

describe('region-paint regions mode (#587 dilim b)', () => {
  const REGIONS = JSON.stringify({
    version: 1,
    type: 'regionPaint',
    mode: 'regions',
    style: 'circle',
    palette: [
      { id: 'p1', color: 'red' },
      { id: 'p1', color: 'blue' },
    ],
    regions: [
      { id: 'r1', points: [[0, 0], [10, 0], [10, 10], [0, 10]], label: 'A' },
      // Alan 0 (doğrusal), id palet id'siyle çakışıyor, köşe eksik → atlanır.
      { id: 'r2', points: [[0, 0], [5, 5], [9, 9]] },
      { id: 'p1', points: [[0, 0], [10, 0], [10, 10]] },
      { id: 'r3', points: [[0, 0], [10, 0]] },
      { id: 'r4', points: [[20, 0], [30, 0], [25, 10]] },
    ],
  });

  it('parsesPlan_SkipsBadRegions_DuplicatePalette', () => {
    const plan = parseRegionPaintRegionsPlan(REGIONS)!;
    expect(plan.style).toBe('circle');
    expect(plan.palette.map((p) => p.id)).toEqual(['p1']);
    expect(plan.regions.map((r) => r.id)).toEqual(['r1', 'r4']);
    expect(parseRegionPaintPlan(REGIONS)).toBeNull();
    expect(parseRegionPaintRegionsPlan(PLAN)).toBeNull();
  });

  it('fills_RoundTrip_LimitedToPlan', () => {
    const plan = parseRegionPaintRegionsPlan(REGIONS)!;
    const fills = parseRegionPaintFills(
      JSON.stringify({ version: 1, type: 'regionPaint', fills: { r4: 'p1', r1: 'p1', r9: 'p1' } }),
      plan
    );
    expect(Object.keys(fills)).toEqual(['r4', 'r1']);
    expect(buildRegionPaintFillsPayload(fills, plan)).toBe(JSON.stringify({ version: 1, type: 'regionPaint', fills: { r1: 'p1', r4: 'p1' } }));
    expect(parseRegionPaintFillsSolution(JSON.stringify({ fills: { r1: 'p1', r4: 'p9' } }), plan)).toEqual({ r1: 'p1' });
  });

  it('blankFills_IsBlank', () => {
    expect(isBlankRegionPaintPayload(JSON.stringify({ version: 1, type: 'regionPaint', fills: {} }))).toBe(true);
    expect(isBlankRegionPaintPayload(JSON.stringify({ version: 1, type: 'regionPaint', fills: { r1: 'p1' } }))).toBe(false);
  });

  it('geometry', () => {
    expect(polygonArea([[0, 0], [10, 0], [10, 10], [0, 10]])).toBe(100);
    expect(pointInPolygon(5, 5, [[0, 0], [10, 0], [10, 10], [0, 10]])).toBe(true);
    expect(pointInPolygon(15, 5, [[0, 0], [10, 0], [10, 10], [0, 10]])).toBe(false);
  });

  it('feedback_KeepsReasonAndCounts', () => {
    const fb = parseRegionPaintFeedback(
      JSON.stringify({ rows: [{ id: 'r1', correct: false, reason: 'color' }, { id: 'r2', correct: false, reason: 'x' }], found: 1, missing: 2, extra: 0 })
    )!;
    expect(fb.rows).toEqual([{ id: 'r1', correct: false, reason: 'color' }, { id: 'r2', correct: false }]);
    expect([fb.found, fb.missing, fb.extra, fb.tooMany]).toEqual([1, 2, 0, undefined]);
  });
});
