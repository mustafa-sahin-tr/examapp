// Issue #588: sayı doğrusu modeli — plan / cevap / çözüm ayrıştırma, payload, boş cevap, çentik adı, geri bildirim.
import {
  buildNumberLinePayload,
  isBlankNumberLinePayload,
  parseNumberLineAnswer,
  parseNumberLineFeedback,
  parseNumberLinePlan,
  parseNumberLineSolution,
  solutionPlacement,
  tickName,
} from './number-line';

const PLAN = JSON.stringify({
  version: 1,
  type: 'numberLine',
  divisions: 8,
  labels: [
    { at: 8, text: '1' },
    { at: 0, text: '0' },
    { at: 9, text: 'x' },
    { at: 0, text: 'tekrar' },
  ],
  cards: [
    { id: 'k1', label: '3/8' },
    { id: 'k2', label: '1/2' },
    { id: 'k1', label: 'aynı id' },
    { id: 'k3', label: '  ' },
  ],
  tolerance: 1,
});

describe('number-line model (#588)', () => {
  it('parsesPlanSkippingBadEntries', () => {
    const plan = parseNumberLinePlan(PLAN)!;
    expect(plan.divisions).toBe(8);
    expect(plan.labels).toEqual([
      { at: 0, text: '0' },
      { at: 8, text: '1' },
    ]);
    expect(plan.cards.map((c) => c.id)).toEqual(['k1', 'k2']);
    expect(plan.tolerance).toBe(1);
    expect(plan.hideTicks).toBe(false);
  });

  it('rejectsBadPlan', () => {
    expect(parseNumberLinePlan(null)).toBeNull();
    expect(parseNumberLinePlan('{')).toBeNull();
    expect(parseNumberLinePlan(JSON.stringify({ type: 'regionPaint', divisions: 4, cards: [{ id: 'k1', label: 'a' }] }))).toBeNull();
    expect(parseNumberLinePlan(JSON.stringify({ type: 'numberLine', divisions: 101, cards: [{ id: 'k1', label: 'a' }] }))).toBeNull();
    expect(parseNumberLinePlan(JSON.stringify({ type: 'numberLine', divisions: 4, cards: [] }))).toBeNull();
  });

  it('answerRoundTripsAndDropsUnknown', () => {
    const plan = parseNumberLinePlan(PLAN)!;
    const payload = buildNumberLinePayload(plan, { k2: 4, k1: 3 });
    expect(JSON.parse(payload)).toEqual({ version: 1, type: 'numberLine', placed: { k1: 3, k2: 4 } });
    expect(parseNumberLineAnswer(payload, plan)).toEqual({ k1: 3, k2: 4 });
    const bad = JSON.stringify({ version: 1, type: 'numberLine', placed: { k1: 9, k2: 2, k9: 1 } });
    expect({ ...parseNumberLineAnswer(bad, plan) }).toEqual({ k2: 2 });
    expect({ ...parseNumberLineAnswer('{', plan) }).toEqual({});
  });

  it('solutionAndPlacement', () => {
    const plan = parseNumberLinePlan(PLAN)!;
    const solution = parseNumberLineSolution(JSON.stringify({ positions: { k1: 3, k2: null, k9: 1 } }), plan);
    expect({ ...solution }).toEqual({ k1: 3, k2: null });
    expect({ ...solutionPlacement(solution) }).toEqual({ k1: 3 });
  });

  it('tickNameUsesLabelOrFraction', () => {
    const plan = parseNumberLinePlan(PLAN)!;
    expect(tickName(plan, 8)).toBe('1');
    expect(tickName(plan, 3)).toBe('3/8');
  });

  it('blankPayload', () => {
    expect(isBlankNumberLinePayload(null)).toBe(true);
    expect(isBlankNumberLinePayload(JSON.stringify({ type: 'numberLine', placed: {} }))).toBe(true);
    expect(isBlankNumberLinePayload(JSON.stringify({ type: 'numberLine', placed: { k1: 1 } }))).toBe(false);
    expect(isBlankNumberLinePayload(JSON.stringify({ type: 'regionPaint', grid: [] }))).toBe(false);
  });

  it('parsesFeedback', () => {
    expect(parseNumberLineFeedback(JSON.stringify({ rows: [{ id: 'k1', correct: true }, { id: 2 }] }))).toEqual({
      rows: [{ id: 'k1', correct: true }],
    });
    expect(parseNumberLineFeedback('x')).toBeNull();
  });
});
