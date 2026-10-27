// Issue #576: match plan / çözüm / payload ayrıştırma.
import { buildMatchPayload, isBlankMatchPayload, openLefts, parseMatchPairs, parseMatchPlan, parseMatchSolution, withPair } from './match';

const PLAN = JSON.stringify({
  version: 1,
  type: 'match',
  left: [{ id: 'l1', label: 'A' }, { id: 'l2', box: { x: 0, y: 0, width: 5, height: 5 } }, { id: 'l3' }],
  right: [{ id: 'r1', label: '1' }, { id: 'r2', label: '2' }, { id: 'l1', label: 'dup id' }],
  prelinked: [{ leftId: 'l1', rightId: 'r1' }, { leftId: 'l2', rightId: 'r1' }],
});

describe('match model (#576)', () => {
  it('parsePlan_DropsItemsWithoutLabelOrBox_SharedIdSpace_UniquePrelinked', () => {
    const plan = parseMatchPlan(PLAN)!;
    expect(plan.left.map((i) => i.id)).toEqual(['l1', 'l2']);
    expect(plan.right.map((i) => i.id)).toEqual(['r1', 'r2']);
    expect(plan.prelinked).toEqual([{ leftId: 'l1', rightId: 'r1' }]);
    expect(openLefts(plan).map((i) => i.id)).toEqual(['l2']);
  });

  it('pairs_ExcludePrelinkedAndDuplicates_PayloadOmitsExample', () => {
    const plan = parseMatchPlan(PLAN)!;
    const map = parseMatchPairs(
      JSON.stringify({ type: 'Match', pairs: [{ leftId: 'l1', rightId: 'r2' }, { leftId: 'l2', rightId: 'r1' }, { leftId: 'l2', rightId: 'r2' }] }),
      plan
    );
    expect({ ...map }).toEqual({ l2: 'r2' });
    expect(JSON.parse(buildMatchPayload(map, plan)).pairs).toEqual([{ leftId: 'l2', rightId: 'r2' }]);
    expect({ ...parseMatchSolution('{"l2":"r2","l1":"r1"}', plan) }).toEqual({ l2: 'r2' });
    expect({ ...withPair(map, 'l3', 'r2') }).toEqual({ l3: 'r2' });
    expect(isBlankMatchPayload('{"type":"match","pairs":[]}')).toBe(true);
    expect(isBlankMatchPayload(buildMatchPayload(map, plan))).toBe(false);
  });
});
