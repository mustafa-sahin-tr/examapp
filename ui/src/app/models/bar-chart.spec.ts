// Issue #588 (dilim b): grafik kurma modeli — plan / cevap / çözüm ayrıştırma, payload, boş cevap, geri bildirim.
import {
  buildBarChartPayload,
  isBlankBarChartPayload,
  parseBarChartAnswer,
  parseBarChartFeedback,
  parseBarChartPlan,
  parseBarChartSolution,
} from './bar-chart';

const PLAN = JSON.stringify({
  version: 1,
  type: 'barChart',
  title: 'Sevilen meyveler',
  max: 10,
  step: 2,
  unit: 1,
  categories: [
    { id: 'c1', label: 'Elma' },
    { id: 'c2', label: 'Armut' },
    { id: 'c3', label: 'Muz' },
  ],
  fixed: { c1: 6 },
});

describe('bar-chart (#588)', () => {
  it('parsesPlan', () => {
    const plan = parseBarChartPlan(PLAN)!;
    expect(plan.style).toBe('bar');
    expect(plan.title).toBe('Sevilen meyveler');
    expect(plan.unit).toBe(1);
    expect(plan.categories.map((c) => c.id)).toEqual(['c1', 'c2', 'c3']);
    expect(plan.fixed).toEqual({ c1: 6 });
  });

  it('unitDefaultsToStep', () => {
    const plan = parseBarChartPlan(JSON.stringify({ type: 'barChart', max: 50, step: 10, style: 'line', categories: [{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }] }))!;
    expect(plan.unit).toBe(10);
    expect(plan.style).toBe('line');
  });

  it('rejectsBadPlans', () => {
    const base = { type: 'barChart', max: 10, step: 2, categories: [{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }] };
    expect(parseBarChartPlan(JSON.stringify({ ...base, step: 3 }))).toBeNull();
    expect(parseBarChartPlan(JSON.stringify({ ...base, unit: 3 }))).toBeNull();
    expect(parseBarChartPlan(JSON.stringify({ ...base, categories: [{ id: 'a', label: 'A' }] }))).toBeNull();
    expect(parseBarChartPlan(JSON.stringify({ ...base, fixed: { a: 2, b: 4 } }))).toBeNull();
    expect(parseBarChartPlan(JSON.stringify({ ...base, type: 'numberLine' }))).toBeNull();
    expect(parseBarChartPlan('{')).toBeNull();
  });

  it('answerAndPayloadSkipFixedAndInvalid', () => {
    const plan = parseBarChartPlan(PLAN)!;
    const answer = parseBarChartAnswer(JSON.stringify({ type: 'barChart', values: { c1: 2, c2: 4, c3: 11, x: 1 } }), plan);
    expect({ ...answer }).toEqual({ c2: 4 });
    expect(JSON.parse(buildBarChartPayload(plan, { c3: 0, c2: 4, c1: 2 }))).toEqual({ version: 1, type: 'barChart', values: { c2: 4, c3: 0 } });
    expect({ ...parseBarChartSolution('{"values":{"c2":4,"c3":7,"c1":6}}', plan) }).toEqual({ c2: 4, c3: 7 });
  });

  it('blankPayload', () => {
    expect(isBlankBarChartPayload(null)).toBe(true);
    expect(isBlankBarChartPayload('{"type":"barChart","values":{}}')).toBe(true);
    expect(isBlankBarChartPayload('{"type":"barChart","values":{"c2":0}}')).toBe(false);
    expect(isBlankBarChartPayload('{"type":"numberLine","placed":{}}')).toBe(false);
  });

  it('feedback', () => {
    expect(parseBarChartFeedback('{"rows":[{"id":"c2","correct":true},{"id":3}]}')).toEqual({ rows: [{ id: 'c2', correct: true }] });
    expect(parseBarChartFeedback('x')).toBeNull();
  });
});
