// Issue #586: mantık tablosu modeli — plan, işaretler, otomatik ✗, teklik denetimi, payload, çözüm.
import {
  buildLogicGridPayload,
  conflicts,
  isBlankLogicGridPayload,
  markOf,
  parseLogicGridAnswer,
  parseLogicGridPlan,
  parseLogicGridSolution,
  ticks,
  withAutoCrosses,
} from './logic-grid';

const LOGIC_PLAN = {
  version: 1,
  type: 'logicGrid',
  subject: {
    label: 'Çocuk',
    items: [
      { id: 'p1', label: 'Ali' },
      { id: 'p2', label: 'Ayşe' },
      { id: 'p3', label: 'Can' },
    ],
  },
  categories: [
    {
      id: 'c1',
      label: 'Renk',
      items: [
        { id: 'o1', label: 'Mavi' },
        { id: 'o2', label: 'Kırmızı' },
        { id: 'o3', label: 'Yeşil' },
      ],
    },
  ],
  clues: [{ id: 'q1', text: 'Ali maviyi sevmez.' }],
};

const cells = (...list: [string, string, 'yes' | 'no'][]) =>
  JSON.stringify({ version: 1, type: 'logicGrid', cells: list.map(([row, col, mark]) => ({ row, col, mark })) });

describe('logic-grid model (#586)', () => {
  const plan = parseLogicGridPlan(JSON.stringify(LOGIC_PLAN))!;

  it('parsePlan', () => {
    expect(plan.subject.items.length).toBe(3);
    expect(plan.clues[0].text).toBe('Ali maviyi sevmez.');
    expect(parseLogicGridPlan(JSON.stringify({ ...LOGIC_PLAN, categories: [] }))).toBeNull();
  });

  it('answer_DropsUnknownCells_PayloadInPlanOrder', () => {
    const marks = parseLogicGridAnswer(cells(['p2', 'o1', 'yes'], ['p9', 'o1', 'yes'], ['p1', 'o3', 'no']), plan);
    expect(markOf(marks, 'p2', 'o1')).toBe('yes');
    expect(markOf(marks, 'p9', 'o1')).toBeNull();
    expect(buildLogicGridPayload(plan, marks)).toBe(cells(['p1', 'o3', 'no'], ['p2', 'o1', 'yes']));
  });

  it('autoCrosses_FillRowAndColumnOfBlock_KeepExisting', () => {
    const marks = withAutoCrosses(plan, parseLogicGridAnswer(cells(['p1', 'o1', 'yes'], ['p3', 'o2', 'yes']), plan), 'p1', 'o1');
    expect(markOf(marks, 'p1', 'o2')).toBe('no');
    expect(markOf(marks, 'p1', 'o3')).toBe('no');
    expect(markOf(marks, 'p2', 'o1')).toBe('no');
    expect(markOf(marks, 'p3', 'o2')).toBe('yes');
    expect(ticks(plan, marks, 'p1', 'c1')).toEqual(['o1']);
  });

  it('conflicts_TwoTicksOrAllCrosses', () => {
    expect(conflicts(plan, parseLogicGridAnswer(cells(['p1', 'o1', 'yes'], ['p1', 'o2', 'yes']), plan))).toEqual(new Set(['p1:o1', 'p1:o2']));
    expect(conflicts(plan, parseLogicGridAnswer(cells(['p1', 'o1', 'yes'], ['p2', 'o1', 'yes']), plan)).has('p2:o1')).toBe(true);
    const allNo = conflicts(plan, parseLogicGridAnswer(cells(['p1', 'o1', 'no'], ['p1', 'o2', 'no'], ['p1', 'o3', 'no']), plan));
    expect(allNo.has('p1:o3')).toBe(true);
    expect(conflicts(plan, parseLogicGridAnswer(cells(['p1', 'o1', 'yes']), plan)).size).toBe(0);
  });

  it('solution_AndBlankPayload', () => {
    const assign = parseLogicGridSolution(JSON.stringify({ assign: { p1: { c1: 'o3', c9: 'o1' }, p2: { c1: 'o9' } } }), plan);
    expect(assign['p1']).toEqual({ c1: 'o3' });
    expect(Object.hasOwn(assign, 'p2')).toBe(false);
    expect(isBlankLogicGridPayload(null)).toBe(true);
    expect(isBlankLogicGridPayload(cells())).toBe(true);
    expect(isBlankLogicGridPayload(cells(['p1', 'o1', 'no']))).toBe(false);
  });
});
