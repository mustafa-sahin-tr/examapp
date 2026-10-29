// Issue #587: boyama modeli — plan / cevap / çözüm ayrıştırma, payload, sayım, örnek çözüm dolgusu, boş cevap.
import {
  buildRegionPaintFillsPayload,
  buildRegionPaintPayload,
  countPainted,
  fillKeys,
  groupName,
  isBlankRegionPaintPayload,
  parseRegionPaintAnswer,
  parseRegionPaintFeedback,
  parseRegionPaintFills,
  parseRegionPaintFillsSolution,
  parseRegionPaintPlan,
  parseRegionPaintRegionsPlan,
  parseRegionPaintSolution,
  pieSlicePath,
  pointInPolygon,
  polygonArea,
  serializeRegionPaintPlan,
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

describe('region-paint numbered painting (#587 dilim c)', () => {
  const GROUPS = JSON.stringify({
    version: 1,
    type: 'regionPaint',
    mode: 'regions',
    palette: [{ id: 'p1', color: 'blue' }, { id: 'p2', color: 'red' }],
    groups: [{ id: 'g1', label: '1' }, { id: 'g2' }, { id: 'g3' }],
    regions: [
      { id: 'r1', points: [[0, 0], [10, 0], [10, 10]], group: 'g1' },
      { id: 'r2', points: [[20, 0], [30, 0], [25, 10]], group: 'g2' },
      // Grupsuz / bilinmeyen gruplu bölge atlanır; bölgesiz grup (g3) düşer.
      { id: 'r3', points: [[40, 0], [50, 0], [45, 10]] },
      { id: 'r4', points: [[40, 0], [50, 0], [45, 10]], group: 'g9' },
      { id: 'r5', points: [[60, 0], [70, 0], [65, 10]], group: 'g1' },
    ],
  });

  it('parsesGroups_FillsKeyedByGroup', () => {
    const plan = parseRegionPaintRegionsPlan(GROUPS)!;
    expect(plan.groups.map((g) => g.id)).toEqual(['g1', 'g2']);
    expect(plan.regions.map((r) => [r.id, r.group])).toEqual([['r1', 'g1'], ['r2', 'g2'], ['r5', 'g1']]);
    expect(fillKeys(plan)).toEqual(['g1', 'g2']);
    expect(groupName(plan, 'g1')).toBe('1');
    expect(groupName(plan, 'g2')).toBe('2');
    const fills = parseRegionPaintFills(JSON.stringify({ version: 1, type: 'regionPaint', fills: { g2: 'p2', r1: 'p1' } }), plan);
    expect(fills).toEqual({ g2: 'p2' });
    expect(buildRegionPaintFillsPayload({ g2: 'p2', g1: 'p1' }, plan)).toBe(
      JSON.stringify({ version: 1, type: 'regionPaint', fills: { g1: 'p1', g2: 'p2' } })
    );
  });

  it('ungroupedPlan_HasNoGroups', () => {
    const plan = parseRegionPaintRegionsPlan(
      JSON.stringify({ version: 1, type: 'regionPaint', mode: 'regions', palette: [{ id: 'p1', color: 'red' }], regions: [{ id: 'r1', points: [[0, 0], [9, 0], [9, 9]] }] })
    )!;
    expect(plan.groups).toEqual([]);
    expect(plan.regions[0].group).toBeUndefined();
    expect(fillKeys(plan)).toEqual(['r1']);
  });
});

describe('regionPaint pie (#588)', () => {
  it('parsesPieAsSingleRowGrid', () => {
    const plan = parseRegionPaintPlan(JSON.stringify({ version: 1, type: 'regionPaint', mode: 'pie', slices: 12, palette: [{ id: 'p1', color: 'red' }] }));
    expect(plan).toMatchObject({ mode: 'pie', rows: 1, cols: 12 });
    expect(serializeRegionPaintPlan(plan!)).toEqual({
      version: 1,
      type: 'regionPaint',
      mode: 'pie',
      slices: 12,
      palette: [{ id: 'p1', color: 'red', label: '' }],
    });
    expect(parseRegionPaintPlan(JSON.stringify({ version: 1, type: 'regionPaint', mode: 'pie', slices: 37, palette: [{ id: 'p1', color: 'red' }] }))).toBeNull();
  });

  it('slicePath_StartsAtTopClockwise', () => {
    expect(pieSlicePath(0, 4, 100)).toBe('M 0 0 L 0 -100 A 100 100 0 0 1 100 0 Z');
  });
});

