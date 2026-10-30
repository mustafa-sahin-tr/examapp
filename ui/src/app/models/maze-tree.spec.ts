import {
  boardRows,
  buildMazeSteppedPayload,
  buildMazePathPayload,
  isBlankMazePayload,
  mazeRoute,
  parseMazeAnswer,
  parseMazeFeedback,
  parseMazeSolution,
  parseMazeTreePlan,
  roomsGrid,
  treeLayout,
  usableEdges,
  walkMaze,
} from './maze-tree';

// İki düzeyli D/Y ağacı (backend testleriyle aynı): doğru n1 D → n2 Y → x2.
const TREE = JSON.stringify({
  version: 1,
  type: 'mazeTree',
  variant: 'tree',
  start: 'n1',
  nodes: [
    { id: 'n1', label: 'Kök' },
    { id: 'n2', label: 'Sol' },
    { id: 'n3', label: 'Sağ' },
    { id: 'x1', label: '1', end: true },
    { id: 'x2', label: '2', end: true },
    { id: 'x3', label: '3', end: true },
    { id: 'x4', label: '4', end: true },
  ],
  edges: [
    { id: 'e1', from: 'n1', to: 'n2', label: 'D' },
    { id: 'e2', from: 'n1', to: 'n3', label: 'Y' },
    { id: 'e3', from: 'n2', to: 'x1', label: 'D' },
    { id: 'e4', from: 'n2', to: 'x2', label: 'Y' },
    { id: 'e5', from: 'n3', to: 'x3', label: 'D' },
    { id: 'e6', from: 'n3', to: 'x4', label: 'Y' },
  ],
});

const ROOMS = JSON.stringify({
  version: 1,
  type: 'mazeTree',
  variant: 'rooms',
  start: 'a',
  nodes: [
    { id: 'a', label: 'A', row: 0, col: 0 },
    { id: 'b', label: 'B', row: 0, col: 1 },
    { id: 'd', label: 'Bitiş', row: 1, col: 1, end: true },
  ],
  edges: [
    { id: 'd1', from: 'a', to: 'b', label: '24' },
    { id: 'd3', from: 'b', to: 'd', label: '12' },
  ],
});

describe('maze-tree model', () => {
  it('parses a tree plan and rejects other types or a missing start', () => {
    const plan = parseMazeTreePlan(TREE)!;
    expect(plan.variant).toBe('tree');
    expect(plan.nodes).toHaveLength(7);
    expect(plan.edges).toHaveLength(6);
    expect(parseMazeTreePlan(TREE.replace('"start":"n1"', '"start":"zz"'))).toBeNull();
    expect(parseMazeTreePlan(TREE.replace('"type":"mazeTree"', '"type":"match"'))).toBeNull();
    expect(parseMazeTreePlan('not json')).toBeNull();
  });

  it('walks a path and stops at an unusable edge or after an end node', () => {
    const plan = parseMazeTreePlan(TREE)!;
    expect(walkMaze(plan, ['e1', 'e4'])).toEqual({ edges: ['e1', 'e4'], nodes: ['n1', 'n2', 'x2'], current: 'x2', finished: true });
    expect(walkMaze(plan, ['e1', 'e5']).edges).toEqual(['e1']);
    expect(walkMaze(plan, ['e1', 'e4', 'e3']).edges).toEqual(['e1', 'e4']);
  });

  it('treats room doors as two-way', () => {
    const plan = parseMazeTreePlan(ROOMS)!;
    expect(usableEdges(plan, 'b').map((e) => e.id)).toEqual(['d1', 'd3']);
    expect(walkMaze(plan, ['d1', 'd1', 'd1', 'd3']).nodes).toEqual(['a', 'b', 'a', 'b', 'd']);
    expect(roomsGrid(plan)).toEqual({ rows: 2, cols: 2 });
  });

  it('derives the route from the solution choices', () => {
    const plan = parseMazeTreePlan(TREE)!;
    const solution = parseMazeSolution('{"choices":{"n1":"e1","n2":"e4","n3":"e5","zz":"e1"}}', plan);
    expect(Object.keys(solution.choices)).toEqual(['n1', 'n2', 'n3']);
    expect(mazeRoute(plan, solution.choices)).toEqual(['e1', 'e4']);
    expect(mazeRoute(plan, { n1: 'e1' })).toBeNull();
  });

  it('lays out a tree with leaves left to right and parents centred', () => {
    const layout = treeLayout(parseMazeTreePlan(TREE)!);
    const at = (id: string) => layout.nodes.find((n) => n.id === id)!;
    expect(layout.width).toBe(4);
    expect(layout.depth).toBe(2);
    expect(at('x1').x).toBe(0.5);
    expect(at('n2').x).toBe(1);
    expect(at('n1')).toEqual({ id: 'n1', x: 2, y: 0, span: 4 });
  });

  it('reads board rows as a chain to the finish', () => {
    const plan = parseMazeTreePlan(
      JSON.stringify({
        version: 1,
        type: 'mazeTree',
        variant: 'board',
        start: 'r1',
        nodes: [{ id: 'r1', label: 'Gaz' }, { id: 'r2' }, { id: 'f', end: true }],
        edges: [
          { id: 'b1', from: 'r1', to: 'r2', label: 'a' },
          { id: 'b2', from: 'r1', to: 'r2', label: 'b' },
          { id: 'b3', from: 'r2', to: 'f', label: 'c' },
          { id: 'b4', from: 'r2', to: 'f', label: 'd' },
        ],
      })
    )!;
    const { rows, finish } = boardRows(plan);
    expect(rows.map((r) => [r.node.id, r.boxes.map((b) => b.id)])).toEqual([
      ['r1', ['b1', 'b2']],
      ['r2', ['b3', 'b4']],
    ]);
    expect(finish?.id).toBe('f');
  });

  it('round-trips answers, clipping an invalid path tail', () => {
    const plan = parseMazeTreePlan(TREE)!;
    expect(parseMazeAnswer(buildMazePathPayload(['e1', 'e5']), plan).path).toEqual(['e1']);
    const stones = parseMazeTreePlan(
      JSON.stringify({ version: 1, type: 'mazeTree', variant: 'stones', nodes: [{ id: 's1', label: 'a' }, { id: 's2', label: 'b' }] })
    )!;
    expect(parseMazeAnswer(buildMazeSteppedPayload(['s2', 's2', 'zz']), stones).stepped).toEqual(['s2']);
  });

  it('detects blank payloads', () => {
    expect(isBlankMazePayload(null)).toBe(true);
    expect(isBlankMazePayload(buildMazePathPayload([]))).toBe(true);
    expect(isBlankMazePayload(buildMazeSteppedPayload([]))).toBe(true);
    expect(isBlankMazePayload(buildMazePathPayload(['e1']))).toBe(false);
    expect(isBlankMazePayload('{"type":"match","pairs":[]}')).toBe(false);
  });

  it('parses feedback', () => {
    expect(parseMazeFeedback('{"rows":[{"id":"n1","correct":false}],"status":"deviated","at":"n1"}')).toEqual({
      rows: [{ id: 'n1', correct: false }],
      status: 'deviated',
      at: 'n1',
    });
    expect(parseMazeFeedback('{"rows":[]}')).toBeNull();
  });
});
