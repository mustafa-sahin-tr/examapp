// Issue #588 (dilim c): kareli zeminde çizim modeli — plan / cevap / çözüm ayrıştırma, hedef boyutlar (backend `Targets` ile aynı),
// boş cevap, geri bildirim, örnek yerleşim.
import {
  buildGridDrawPayload,
  exampleLayout,
  gridDrawTargets,
  isBlankGridDrawPayload,
  overlaps,
  parseGridDrawAnswer,
  parseGridDrawFeedback,
  parseGridDrawPlan,
  parseGridDrawSolution,
} from './grid-draw';

const PLAN = JSON.stringify({ version: 1, type: 'gridDraw', rows: 10, cols: 16 });

describe('grid-draw model (#588)', () => {
  it('planı ayrıştırır; sınır dışı zemin ya da başka tür null', () => {
    expect(parseGridDrawPlan(PLAN)).toEqual({ version: 1, type: 'gridDraw', rows: 10, cols: 16, grid: 'square', shape: 'rectangle' });
    expect(parseGridDrawPlan(JSON.stringify({ type: 'gridDraw', rows: 10, cols: 10, grid: 'dot', shape: 'square' }))?.shape).toBe('square');
    expect(parseGridDrawPlan(JSON.stringify({ type: 'gridDraw', rows: 1, cols: 10 }))).toBeNull();
    expect(parseGridDrawPlan(JSON.stringify({ type: 'barChart', rows: 10, cols: 10 }))).toBeNull();
    expect(parseGridDrawPlan('{')).toBeNull();
  });

  it('kurala uyan ve zemine sığan boyutları bulur', () => {
    const plan = parseGridDrawPlan(PLAN)!;
    expect(gridDrawTargets(plan, { area: null, perimeter: 14, all: true })).toEqual([
      { short: 1, long: 6 },
      { short: 2, long: 5 },
      { short: 3, long: 4 },
    ]);
    // 1 × 20 sığmaz.
    expect(gridDrawTargets(plan, { area: 20, perimeter: null, all: true })).toEqual([
      { short: 2, long: 10 },
      { short: 4, long: 5 },
    ]);
    expect(gridDrawTargets({ ...plan, shape: 'square' }, { area: null, perimeter: 20, all: false })).toEqual([{ short: 5, long: 5 }]);
  });

  it('çözümü ayrıştırır; koşulsuz çözüm null', () => {
    expect(parseGridDrawSolution('{"perimeter":14,"all":true}')).toEqual({ area: null, perimeter: 14, all: true });
    expect(parseGridDrawSolution('{"all":true}')).toBeNull();
    expect(parseGridDrawSolution(null)).toBeNull();
  });

  it('cevap: zemine sığmayan atlanır, payload yankısı, boş cevap', () => {
    const plan = parseGridDrawPlan(PLAN)!;
    const payload = buildGridDrawPayload([
      { row: 0, col: 0, width: 3, height: 4 },
      { row: 8, col: 0, width: 3, height: 4 },
    ]);
    expect(parseGridDrawAnswer(payload, plan)).toEqual([{ row: 0, col: 0, width: 3, height: 4 }]);
    expect(isBlankGridDrawPayload(buildGridDrawPayload([]))).toBe(true);
    expect(isBlankGridDrawPayload(payload)).toBe(false);
    expect(isBlankGridDrawPayload('{"type":"barChart","values":{}}')).toBe(false);
  });

  it('geri bildirimi okur', () => {
    expect(parseGridDrawFeedback('{"shapes":["ok","duplicate","wrong"],"found":1,"missing":2,"extra":1}')).toEqual({
      shapes: ['ok', 'duplicate', 'wrong'],
      found: 1,
      missing: 2,
      extra: 1,
    });
    expect(parseGridDrawFeedback('{"rows":[]}')).toBeNull();
  });

  it('örnek yerleşim: boyutlar zemine üst üste binmeden yerleşir', () => {
    const plan = parseGridDrawPlan(PLAN)!;
    const sizes = gridDrawTargets(plan, { area: null, perimeter: 14, all: true });
    const placed = exampleLayout(plan, sizes);
    expect(placed.length).toBe(3);
    for (const [i, a] of placed.entries()) for (const b of placed.slice(i + 1)) expect(overlaps(a, b)).toBe(false);
    expect(placed.every((r) => r.row + r.height <= 10 && r.col + r.width <= 16)).toBe(true);
  });
});
