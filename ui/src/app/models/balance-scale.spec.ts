// Issue #632: terazi modeli — plan / cevap / geri bildirim ayrıştırma, toplamlar, denge kuralı, örnek yerleşim.
import {
  buildBalanceScalePayload,
  findBalance,
  isBalanced,
  isBlankBalanceScalePayload,
  panTotals,
  parseBalanceScaleAnswer,
  parseBalanceScaleFeedback,
  parseBalanceScalePlan,
  parseBalanceScaleSolution,
  tiltOf,
  weightText,
} from './balance-scale';

const PLAN_JSON = JSON.stringify({
  version: 1,
  type: 'balanceScale',
  unit: 'kg',
  left: [{ id: 'l1', value: 7, label: '7 kg' }],
  cards: [
    { id: 'k1', value: 3 },
    { id: 'k2', value: 4 },
    { id: 'k3', value: 6, label: '(2×3) kg' },
  ],
});

describe('balance-scale model (#632)', () => {
  const plan = parseBalanceScalePlan(PLAN_JSON)!;

  it('parsesPlan_SkipsBadWeights_RejectsOtherTypes', () => {
    expect(plan.unit).toBe('kg');
    expect(plan.left.map((w) => w.id)).toEqual(['l1']);
    expect(plan.cards.map((w) => w.value)).toEqual([3, 4, 6]);
    expect(plan.hideTilt).toBe(false);
    const bad = parseBalanceScalePlan(
      JSON.stringify({ type: 'balanceScale', cards: [{ id: 'k1', value: 0 }, { id: 'k2', value: 5 }, { id: 'k2', value: 6 }] })
    );
    expect(bad?.cards.map((c) => c.id)).toEqual(['k2']);
    expect(parseBalanceScalePlan(JSON.stringify({ type: 'numberLine', cards: [] }))).toBeNull();
    expect(parseBalanceScalePlan(JSON.stringify({ type: 'balanceScale', cards: [] }))).toBeNull();
    expect(parseBalanceScalePlan('{')).toBeNull();
  });

  it('weightText_UsesLabelOrValueAndUnit', () => {
    expect(weightText(plan.cards[0], plan.unit)).toBe('3 kg');
    expect(weightText(plan.cards[2], plan.unit)).toBe('(2×3) kg');
    expect(weightText(plan.cards[0], null)).toBe('3');
  });

  it('answer_RoundTrips_DropsUnknownAndDuplicate', () => {
    const json = buildBalanceScalePayload({ left: ['k1'], right: ['k2', 'k3'] });
    expect(parseBalanceScaleAnswer(json, plan)).toEqual({ left: ['k1'], right: ['k2', 'k3'] });
    const dirty = JSON.stringify({ type: 'balanceScale', left: ['k1', 'l1', 'x'], right: ['k1', 'k2'] });
    expect(parseBalanceScaleAnswer(dirty, plan)).toEqual({ left: ['k1'], right: ['k2'] });
    expect(isBlankBalanceScalePayload(buildBalanceScalePayload({ left: [], right: [] }))).toBe(true);
    expect(isBlankBalanceScalePayload(json)).toBe(false);
    expect(isBlankBalanceScalePayload(JSON.stringify({ type: 'gridDraw', shapes: [] }))).toBe(false);
  });

  it('totalsAndBalance_IncludeFixedLoads', () => {
    const pans = { left: ['k1'], right: ['k2', 'k3'] };
    expect(panTotals(plan, pans)).toEqual({ left: 10, right: 10 });
    expect(tiltOf(panTotals(plan, pans))).toBe('level');
    expect(isBalanced(plan, pans, true)).toBe(true);
    expect(tiltOf(panTotals(plan, { left: [], right: ['k1'] }))).toBe('left');
    expect(isBalanced(plan, { left: [], right: ['k1', 'k2'] }, true)).toBe(false);
    expect(isBalanced(plan, { left: [], right: ['k1', 'k2'] }, false)).toBe(true);
    expect(isBalanced(plan, { left: [], right: [] }, false)).toBe(false);
  });

  it('findBalance_FindsArrangementOrNull', () => {
    const example = findBalance(plan, true)!;
    expect(isBalanced(plan, example, true)).toBe(true);
    const odd = parseBalanceScalePlan(
      JSON.stringify({ type: 'balanceScale', cards: [5, 9, 10, 15].map((value, i) => ({ id: `k${i + 1}`, value })) })
    )!;
    expect(findBalance(odd, true)).toBeNull();
    expect(isBalanced(odd, findBalance(odd, false)!, false)).toBe(true);
  });

  it('parsesSolutionAndFeedback', () => {
    expect(parseBalanceScaleSolution('{"useAll":false}')).toEqual({ useAll: false });
    expect(parseBalanceScaleSolution('{}')).toBeNull();
    expect(parseBalanceScaleFeedback('{"rows":[{"id":"balance","correct":true}],"tilt":"level"}')).toEqual({
      correct: true,
      tilt: 'level',
      unused: null,
    });
    expect(parseBalanceScaleFeedback('{"rows":[{"id":"balance","correct":false}],"tilt":"right","unused":2}')?.unused).toBe(2);
    expect(parseBalanceScaleFeedback('{"shapes":[]}')).toBeNull();
  });
});
