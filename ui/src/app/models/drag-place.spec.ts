// Issue #576: dragPlace plan / çözüm / payload ayrıştırma ve prototip güvenliği.
import {
  buildDragPlacePayload,
  isBlankDragPlacePayload,
  isDragPlaceType,
  parseDragPlacePlacements,
  parseDragPlacePlan,
  parseDragPlaceSolution,
} from './drag-place';

const PLAN = JSON.stringify({
  version: 1,
  type: 'dragPlace',
  mode: 'place',
  reuse: false,
  targets: [
    { id: 't1', label: 'A', capacity: 2 },
    { id: 't2', box: { x: 1, y: 2, width: 3, height: 4 }, capacity: 99 },
    { id: '__proto__', label: 'x', capacity: 1 },
    { id: 't1', label: 'dup', capacity: 1 },
  ],
  cards: [{ id: 'c1', label: 'Buz' }, { id: 'c2', box: { x: 0, y: 0, width: 10, height: 10 } }, { id: 'c3' }, { id: 'c4', label: ' ' }],
});

describe('drag-place model (#576)', () => {
  it('parsePlan_SkipsInvalidItems_ClampsCapacity_DefaultsModeAndReuse', () => {
    const plan = parseDragPlacePlan(PLAN)!;
    expect(plan.targets.map((t) => [t.id, t.capacity])).toEqual([['t1', 2], ['t2', 30]]);
    expect(plan.cards.map((c) => c.id)).toEqual(['c1', 'c2']);
    expect(parseDragPlacePlan(JSON.stringify({ type: 'DragPlace', targets: [{ id: 't1' }], cards: [{ id: 'c1', label: 'x' }] }))).toMatchObject({
      mode: 'place',
      reuse: false,
      targets: [{ id: 't1', capacity: 1 }],
    });
    expect(parseDragPlacePlan(JSON.stringify({ type: 'dragPlace', mode: 'order', targets: [{ id: 't1', capacity: 5 }], cards: [{ id: 'c1', label: 'x' }] }))!.targets[0].capacity).toBe(1);
    expect(parseDragPlacePlan('{bad')).toBeNull();
    expect(parseDragPlacePlan(JSON.stringify({ type: 'dragDropLabeling', targets: [], cards: [] }))).toBeNull();
  });

  it('placements_RespectCapacityReuseAndKnownIds', () => {
    const plan = parseDragPlacePlan(PLAN)!;
    const map = parseDragPlacePlacements(
      JSON.stringify({
        type: 'dragPlace',
        placements: [
          { targetId: 't1', cardId: 'c1' },
          { targetId: 't2', cardId: 'c1' },
          { targetId: 't1', cardId: 'c2' },
          { targetId: 't1', cardId: 'c3' },
          { targetId: 'constructor', cardId: 'c2' },
        ],
      }),
      plan
    );
    expect({ ...map }).toEqual({ t1: ['c1', 'c2'] });
    expect(Object.getPrototypeOf(map)).toBeNull();
    expect(JSON.parse(buildDragPlacePayload(map, plan))).toEqual({
      version: 1,
      type: 'dragPlace',
      placements: [{ targetId: 't1', cardId: 'c1' }, { targetId: 't1', cardId: 'c2' }],
    });
    expect({ ...parseDragPlaceSolution('{"t1":["c1"],"t2":[]}', plan) }).toEqual({ t1: ['c1'] });
  });

  it('blankAndType_CaseInsensitive', () => {
    expect(isDragPlaceType('DRAGPLACE')).toBe(true);
    expect(isBlankDragPlacePayload('{"type":"dragPlace","placements":[]}')).toBe(true);
    expect(isBlankDragPlacePayload('{"type":"dragPlace","placements":[{"targetId":"t1","cardId":"c1"}]}')).toBe(false);
    expect(isBlankDragPlacePayload('{"type":"match","pairs":[]}')).toBe(false);
  });
});
