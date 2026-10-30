import {
  MazeBoardForm,
  MazeRoomsForm,
  MazeStonesForm,
  MazeTreeForm,
  buildMazeDefinition,
  doorCandidates,
  emptyMazeForm,
  parseMazeDefinition,
  resizeRooms,
  resizeTree,
  validateMazeForm,
} from './activity-authoring-maze';
import { mazeRoute, parseMazeSolution, parseMazeTreePlan } from './maze-tree';

const exitName = (n: number) => `${n}. çıkış`;

function filledTree(): MazeTreeForm {
  const form = resizeTree(emptyMazeForm('mazeTree', ['D', 'Y'], exitName) as MazeTreeForm, 3, exitName);
  return { ...form, nodes: form.nodes.map((n, i) => ({ text: `İfade ${i + 1}`, correct: (i % 2) as 0 | 1 })) };
}

function filledRooms(): MazeRoomsForm {
  // 2×2: A1 → A2 → B2 (bitiş); A1-B1 kapısı da var (yan yol).
  const form = resizeRooms(emptyMazeForm('mazeRooms', ['D', 'Y'], exitName) as MazeRoomsForm, 2, 2);
  const door = (a: number, b: number) => form.doors.findIndex((d) => d.a === a && d.b === b);
  const doors = form.doors.map((d, i) =>
    i === door(0, 1) ? { ...d, label: '24' } : i === door(1, 3) ? { ...d, label: '12' } : i === door(0, 2) ? { ...d, label: '30' } : d
  );
  return {
    ...form,
    doors,
    start: 0,
    finish: 3,
    rooms: [
      { text: '6×4?', answer: door(0, 1) },
      { text: '3×4?', answer: door(1, 3) },
      { text: '5×6?', answer: door(0, 2) },
      { text: '', answer: null },
    ],
  };
}

describe('activity-authoring-maze', () => {
  it('builds a complete binary D/Y tree whose route follows each statement’s answer', () => {
    const def = buildMazeDefinition(filledTree());
    const plan = parseMazeTreePlan(def.plan)!;
    expect(plan.nodes.filter((n) => !n.end)).toHaveLength(7);
    expect(plan.nodes.filter((n) => n.end).map((n) => n.label)).toEqual(Array.from({ length: 8 }, (_, i) => `${i + 1}. çıkış`));
    expect(plan.edges).toHaveLength(14);
    // n1 doğrusu sol (D) → n2; n2 doğrusu sağ (Y) → n5; n5 doğrusu sol (D) → x3.
    const route = mazeRoute(plan, parseMazeSolution(def.solution, plan).choices)!;
    expect(route.map((id) => plan.edges.find((e) => e.id === id)!.to)).toEqual(['n2', 'n5', 'x3']);
  });

  it('round-trips every template through plan + solution', () => {
    const board: MazeBoardForm = {
      kind: 'mazeBoard',
      finish: 'Bitiş',
      rows: [
        { label: 'Gaz', boxes: ['Sıkıştırılamaz.', 'Akışkan değildir.', 'Kabın şeklini alır.'], correct: 2 },
        { label: '', boxes: ['a', 'b'], correct: 1 },
      ],
    };
    const stones: MazeStonesForm = { kind: 'mazeStones', stones: [{ text: '5\'er', marked: false }, { text: 'onbeşbin', marked: true }] };
    for (const form of [filledTree(), board, filledRooms(), stones]) {
      const def = buildMazeDefinition(form);
      expect(parseMazeDefinition(def.plan, def.solution)).toEqual(form);
    }
  });

  it('keeps upper levels when the tree depth changes', () => {
    const tree = filledTree();
    const deeper = resizeTree(tree, 4, exitName);
    expect(deeper.nodes).toHaveLength(15);
    expect(deeper.nodes.slice(0, 7)).toEqual(tree.nodes);
    expect(deeper.exits).toHaveLength(16);
    expect(resizeTree(tree, 9, exitName).depth).toBe(4);
  });

  it('carries custom exit texts into the new level when one level is added', () => {
    const root = emptyMazeForm('mazeTree', ['D', 'Y'], exitName) as MazeTreeForm;
    const typed = { ...root, nodes: [{ text: 'Kök', correct: 0 as const }], exits: ['Ay ışık üretir', '2. çıkış'] };
    const deeper = resizeTree(typed, 2, exitName);
    expect(deeper.nodes.map((n) => n.text)).toEqual(['Kök', 'Ay ışık üretir', '']);
    expect(deeper.exits).toEqual(['1. çıkış', '2. çıkış', '3. çıkış', '4. çıkış']);
  });

  it('keeps rooms and door labels at the same position when the grid grows', () => {
    const rooms = filledRooms();
    const grown = resizeRooms(rooms, 3, 3);
    expect(grown.rooms).toHaveLength(9);
    expect(grown.rooms[0].text).toBe('6×4?');
    expect(grown.rooms[4].text).toBe(''); // eski B2 (bitiş) → yeni B2 = dizin 4
    expect(grown.doors[grown.rooms[0].answer!]).toMatchObject({ a: 0, b: 1, label: '24' });
    expect(grown.finish).toBe(4);
    expect(doorCandidates(2, 2)).toHaveLength(6);
    // Küçülünce bitiş dışarıda kalır ve başlangıç son odaya düşerse bitiş ilk odaya geçer (çakışmaz).
    const shrunk = resizeRooms({ ...resizeRooms(rooms, 3, 3), start: 4, finish: 8 }, 2, 2);
    expect([shrunk.start, shrunk.finish]).toEqual([3, 0]);
  });

  it('validates the templates', () => {
    expect(validateMazeForm(filledTree())).toEqual([]);
    expect(validateMazeForm({ ...filledTree(), branches: ['D', 'd'] }).map((e) => e.key)).toContain('mazeDuplicateBranch');
    expect(validateMazeForm(emptyMazeForm('mazeTree', ['D', 'Y'], exitName)).map((e) => e.key)).toContain('mazeBlankStatement');

    expect(validateMazeForm(filledRooms())).toEqual([]);
    const loop = filledRooms();
    loop.rooms[1] = { ...loop.rooms[1], answer: loop.rooms[0].answer }; // A2 → A1: döngü
    expect(validateMazeForm(loop).map((e) => e.key)).toContain('mazeNoRoute');
    const walled = filledRooms();
    walled.doors = walled.doors.map((d) => (d.label === '30' ? { ...d, label: '' } : d)); // B1 kapısız
    expect(validateMazeForm(walled).map((e) => e.key)).toContain('mazeUnreachable');
    const crossed = filledRooms();
    crossed.doors = crossed.doors.map((d) => (d.a === 0 && d.b === 3) || (d.a === 1 && d.b === 2) ? { ...d, label: 'x' + d.a } : d);
    expect(validateMazeForm(crossed).map((e) => e.key)).toContain('mazeDoubleDiagonal');

    const board = emptyMazeForm('mazeBoard', ['D', 'Y'], exitName) as MazeBoardForm;
    expect(validateMazeForm(board).map((e) => e.key)).toContain('mazeBlankBox');
    expect(validateMazeForm({ kind: 'mazeStones', stones: [{ text: 'a', marked: true }] }).map((e) => e.key)).toContain('mazeStoneCount');
    expect(
      validateMazeForm({ kind: 'mazeStones', stones: [{ text: 'a', marked: false }, { text: 'b', marked: false }] }).map((e) => e.key)
    ).toContain('mazeNoMarkedStone');
  });
});
