/**
 * Issue #585: yol oyunlarının (`mazeTree`) şablon formları — görselden çıkarım yok, öğretmen şablonu doldurur:
 * - `mazeTree`: tam ikili D/Y ağacı (derinlik 1–4); ifadeler genişlik öncelikli sırayla (kök, 1. düzey soldan sağa, …), her
 *   ifadenin doğrusu (sol / sağ dal), çıkış adları;
 * - `mazeBoard`: ışıklı pano satırları başlangıçtan bitişe (satır adı, kutular, doğru kutu);
 * - `mazeRooms`: R×C oda ızgarası (2–4), komşu odalar arası kapılar (etiketsiz kapı = duvar; bir dörtlüde tek çapraz), başlangıç
 *   ve bitiş odası, her odanın doğru kapısı;
 * - `mazeStones`: taş cümleleri ve basılmaması gerekenler.
 *
 * Düzenlemede form plan + çözümden geri kurulur; şablonun ifade edemediği plan (ör. eksik dallı ağaç) null döner.
 */
import {
  MAZE_LIMITS,
  MAZE_TREE,
  MazeTreePlanV1,
  boardRows,
  edgeTarget,
  mazeRoute,
  nodeOf,
  parseMazeSolution,
  parseMazeTreePlan,
  roomsGrid,
} from './maze-tree';

export type MazeKind = 'mazeTree' | 'mazeBoard' | 'mazeRooms' | 'mazeStones';

export const MAZE_KINDS: readonly MazeKind[] = ['mazeTree', 'mazeBoard', 'mazeRooms', 'mazeStones'];

export const MAZE_FORM_LIMITS = {
  minDepth: 1,
  maxDepth: 4,
  minBoardRows: 1,
  maxBoardRows: MAZE_LIMITS.maxBoardRows,
  minBoxes: MAZE_LIMITS.minBoardBoxes,
  maxBoxes: MAZE_LIMITS.maxBoardBoxes,
  minGrid: 2,
  maxGrid: 4,
  minStones: MAZE_LIMITS.minStones,
  maxStones: MAZE_LIMITS.maxStones,
  maxLabel: MAZE_LIMITS.maxLabelLength,
} as const;

export interface MazeTreeNodeForm {
  text: string;
  /** Doğru dal: 0 = sol (ilk dal, ör. D), 1 = sağ. */
  correct: 0 | 1;
}

export interface MazeTreeForm {
  kind: 'mazeTree';
  depth: number;
  /** Dal etiketleri [sol, sağ] (ör. D / Y). */
  branches: [string, string];
  /** Genişlik öncelikli: 2^depth − 1 ifade. */
  nodes: MazeTreeNodeForm[];
  /** Soldan sağa 2^depth çıkış adı. */
  exits: string[];
}

export interface MazeBoardRowForm {
  label: string;
  boxes: string[];
  correct: number;
}

export interface MazeBoardForm {
  kind: 'mazeBoard';
  /** Başlangıçtan (alt) bitişe (üst). */
  rows: MazeBoardRowForm[];
  finish: string;
}

export interface MazeRoomForm {
  text: string;
  /** Doğru kapı (`doors` dizinine göre); null = seçilmedi. */
  answer: number | null;
}

/** Komşu iki oda arası kapı adayı; boş etiket = kapı yok. */
export interface MazeDoorForm {
  a: number;
  b: number;
  label: string;
}

export interface MazeRoomsForm {
  kind: 'mazeRooms';
  rows: number;
  cols: number;
  /** Satır satır R×C oda. */
  rooms: MazeRoomForm[];
  doors: MazeDoorForm[];
  start: number;
  finish: number;
}

export interface MazeStoneForm {
  text: string;
  marked: boolean;
}

export interface MazeStonesForm {
  kind: 'mazeStones';
  stones: MazeStoneForm[];
}

export type MazeForm = MazeTreeForm | MazeBoardForm | MazeRoomsForm | MazeStonesForm;

export interface MazeError {
  key: string;
  params?: Record<string, unknown>;
}

export interface MazeDefinition {
  interactionType: typeof MAZE_TREE;
  plan: string;
  solution: string;
}

export function isMazeKind(kind: string): kind is MazeKind {
  return (MAZE_KINDS as readonly string[]).includes(kind);
}

export function isMazeForm(form: { kind: string }): form is MazeForm {
  return isMazeKind(form.kind);
}

/** Çıkışın varsayılan adı ("1. çıkış"); şablon çevirisi ebeveynden gelir. */
export type ExitName = (n: number) => string;

export function emptyMazeForm(kind: MazeKind, branches: [string, string], exitName: ExitName): MazeForm {
  switch (kind) {
    case 'mazeTree':
      return resizeTree({ kind, depth: 0, branches: [...branches], nodes: [], exits: [] }, 1, exitName);
    case 'mazeBoard':
      return {
        kind,
        rows: [0, 1, 2].map(() => ({ label: '', boxes: ['', '', ''], correct: 0 })),
        finish: '',
      };
    case 'mazeRooms':
      return resizeRooms({ kind, rows: 0, cols: 0, rooms: [], doors: [], start: 0, finish: 0 }, 3, 3);
    case 'mazeStones':
      return { kind, stones: Array.from({ length: 4 }, () => ({ text: '', marked: false })) };
  }
}

/** Ağaç derinliğini değiştirir; genişlik öncelikli sıra sayesinde üst düzeyler korunur, çıkış adları yeniden numaralanır. */
/** Doğru dalları izleyen rotanın vardığı çıkışın dizini (soldan, 0'dan). */
export function treeCorrectExit(form: MazeTreeForm): number {
  let i = 0;
  for (let d = 0; d < form.depth; d++) i = 2 * i + 1 + (form.nodes[i]?.correct ?? 0);
  return i - (2 ** form.depth - 1);
}

export function resizeTree(form: MazeTreeForm, depth: number, exitName: ExitName): MazeTreeForm {
  const d = Math.min(MAZE_FORM_LIMITS.maxDepth, Math.max(MAZE_FORM_LIMITS.minDepth, depth));
  const internal = 2 ** d - 1;
  const nodes = Array.from({ length: internal }, (_, i) => form.nodes[i] ?? { text: '', correct: 0 as const });
  const leaves = 2 ** d;
  const keep = d === form.depth;
  const exits = Array.from({ length: leaves }, (_, i) => (keep && form.exits[i] !== undefined ? form.exits[i] : exitName(i + 1)));
  return { ...form, depth: d, nodes, exits };
}

/** Pozisyondan oda dizini. */
export function roomIndex(form: Pick<MazeRoomsForm, 'cols'>, row: number, col: number): number {
  return row * form.cols + col;
}

/** Kapı adayları: her odanın sağ, alt ve iki alt çapraz komşusu (satır satır). */
export function doorCandidates(rows: number, cols: number): { a: number; b: number }[] {
  const out: { a: number; b: number }[] = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const a = r * cols + c;
      if (c + 1 < cols) out.push({ a, b: a + 1 });
      if (r + 1 < rows) out.push({ a, b: a + cols });
      if (r + 1 < rows && c + 1 < cols) out.push({ a, b: a + cols + 1 });
      if (r + 1 < rows && c > 0) out.push({ a, b: a + cols - 1 });
    }
  }
  return out;
}

/** Izgara boyutunu değiştirir; aynı konumdaki odalar ve aynı iki oda arasındaki kapı etiketleri korunur. */
export function resizeRooms(form: MazeRoomsForm, rows: number, cols: number): MazeRoomsForm {
  const R = Math.min(MAZE_FORM_LIMITS.maxGrid, Math.max(MAZE_FORM_LIMITS.minGrid, rows));
  const C = Math.min(MAZE_FORM_LIMITS.maxGrid, Math.max(MAZE_FORM_LIMITS.minGrid, cols));
  const pos = (index: number, width: number) => ({ r: Math.floor(index / width), c: index % width });
  const oldIndex = (r: number, c: number) => (r < form.rows && c < form.cols ? r * form.cols + c : -1);
  const newIndex = (old: number) => {
    const { r, c } = pos(old, form.cols);
    return r < R && c < C ? r * C + c : -1;
  };

  const labels = new Map<string, string>();
  form.doors.forEach((d) => {
    const a = newIndex(d.a);
    const b = newIndex(d.b);
    if (a >= 0 && b >= 0) labels.set(`${Math.min(a, b)}-${Math.max(a, b)}`, d.label);
  });
  const doors = doorCandidates(R, C).map(({ a, b }) => ({ a, b, label: labels.get(`${a}-${b}`) ?? '' }));
  const doorIndex = new Map(doors.map((d, i) => [`${d.a}-${d.b}`, i]));

  const rooms = Array.from({ length: R * C }, (_, index) => {
    const { r, c } = pos(index, C);
    const old = form.rooms[oldIndex(r, c)];
    if (!old) return { text: '', answer: null };
    const oldDoor = old.answer !== null ? form.doors[old.answer] : null;
    let answer: number | null = null;
    if (oldDoor) {
      const a = newIndex(oldDoor.a);
      const b = newIndex(oldDoor.b);
      if (a >= 0 && b >= 0) answer = doorIndex.get(`${Math.min(a, b)}-${Math.max(a, b)}`) ?? null;
    }
    return { text: old.text, answer };
  });
  const start = newIndex(form.start) >= 0 && form.rooms.length ? newIndex(form.start) : 0;
  const finishCandidate = form.rooms.length ? newIndex(form.finish) : -1;
  const finish = finishCandidate >= 0 && finishCandidate !== start ? finishCandidate : start === R * C - 1 ? 0 : R * C - 1;
  return { ...form, rows: R, cols: C, rooms, doors, start, finish };
}

/** Odanın kapıları (etiketli; `doors` dizinleri). */
export function roomDoors(form: MazeRoomsForm, room: number): number[] {
  return form.doors.flatMap((d, i) => ((d.a === room || d.b === room) && d.label.trim() ? [i] : []));
}

/** Kapının öbür odası. */
export function otherRoom(door: MazeDoorForm, room: number): number {
  return door.a === room ? door.b : door.a;
}

export function validateMazeForm(form: MazeForm): MazeError[] {
  const errors: MazeError[] = [];
  const max = MAZE_FORM_LIMITS.maxLabel;
  const tooLong = (values: string[]) => {
    if (values.some((v) => v.trim().length > max)) errors.push({ key: 'labelTooLong', params: { max } });
  };
  const duplicate = (values: string[]) => {
    const norm = values.map((v) => v.trim().toLocaleLowerCase('tr'));
    return new Set(norm).size !== norm.length;
  };

  switch (form.kind) {
    case 'mazeTree': {
      if (form.branches.some((b) => !b.trim())) errors.push({ key: 'mazeBlankBranch' });
      else if (duplicate(form.branches)) errors.push({ key: 'mazeDuplicateBranch' });
      if (form.nodes.some((n) => !n.text.trim())) errors.push({ key: 'mazeBlankStatement' });
      tooLong([...form.branches, ...form.nodes.map((n) => n.text), ...form.exits]);
      break;
    }
    case 'mazeBoard': {
      const { minBoardRows, maxBoardRows, minBoxes, maxBoxes } = MAZE_FORM_LIMITS;
      if (form.rows.length < minBoardRows || form.rows.length > maxBoardRows)
        errors.push({ key: 'mazeRowCount', params: { min: minBoardRows, max: maxBoardRows } });
      if (form.rows.some((r) => r.boxes.length < minBoxes || r.boxes.length > maxBoxes))
        errors.push({ key: 'mazeBoxCount', params: { min: minBoxes, max: maxBoxes } });
      if (form.rows.some((r) => r.boxes.some((b) => !b.trim()))) errors.push({ key: 'mazeBlankBox' });
      if (form.rows.some((r) => duplicate(r.boxes))) errors.push({ key: 'mazeDuplicateBox' });
      tooLong([form.finish, ...form.rows.flatMap((r) => [r.label, ...r.boxes])]);
      break;
    }
    case 'mazeRooms': {
      if (form.start === form.finish) errors.push({ key: 'mazeStartIsFinish' });
      if (form.rooms.some((room, i) => i !== form.finish && !room.text.trim())) errors.push({ key: 'mazeBlankRoom' });
      // Bir dörtlüde iki çapraz kapı aynı köşede üst üste çizilirdi.
      const diag = (a: number, b: number) => form.doors.find((d) => d.a === a && d.b === b && d.label.trim());
      for (let r = 0; r + 1 < form.rows; r++) {
        for (let c = 0; c + 1 < form.cols; c++) {
          const tl = roomIndex(form, r, c);
          if (diag(tl, tl + form.cols + 1) && diag(tl + 1, tl + form.cols)) {
            errors.push({ key: 'mazeDoubleDiagonal' });
            r = form.rows;
            break;
          }
        }
      }
      if (form.rooms.some((_, i) => duplicate(roomDoors(form, i).map((d) => form.doors[d].label))))
        errors.push({ key: 'mazeDuplicateDoor' });
      tooLong([...form.rooms.map((r) => r.text), ...form.doors.map((d) => d.label)]);
      if (!errors.some((e) => e.key === 'mazeStartIsFinish')) {
        const plan = parseMazeTreePlan(buildMazeDefinition(form).plan);
        if (!plan || !allReachable(plan)) errors.push({ key: 'mazeUnreachable' });
        else if (!mazeRoute(plan, parseMazeSolution(buildMazeDefinition(form).solution, plan).choices))
          errors.push({ key: 'mazeNoRoute' });
      }
      break;
    }
    case 'mazeStones': {
      const { minStones, maxStones } = MAZE_FORM_LIMITS;
      if (form.stones.length < minStones || form.stones.length > maxStones)
        errors.push({ key: 'mazeStoneCount', params: { min: minStones, max: maxStones } });
      if (form.stones.some((s) => !s.text.trim())) errors.push({ key: 'mazeBlankStone' });
      // Hiç işaretsiz çözüm çözülemezdi (işaretsiz cevap boş cevap sayılır); backend de reddeder.
      if (!form.stones.some((s) => s.marked)) errors.push({ key: 'mazeNoMarkedStone' });
      tooLong(form.stones.map((s) => s.text));
      break;
    }
  }
  return errors;
}

/** Başlangıçtan (bitişten çıkmadan) her düğüme varılabiliyor mu? Backend kayıt kuralıyla aynı. */
function allReachable(plan: MazeTreePlanV1): boolean {
  if (!plan.start) return false;
  const seen = new Set([plan.start]);
  const queue = [plan.start];
  while (queue.length) {
    const id = queue.shift()!;
    if (nodeOf(plan, id)?.end) continue;
    for (const edge of plan.edges) {
      const next = edgeTarget(plan, edge, id);
      if (next && !seen.has(next)) {
        seen.add(next);
        queue.push(next);
      }
    }
  }
  return seen.size === plan.nodes.length;
}

/** Form → plan + çözüm (JSON). */
export function buildMazeDefinition(form: MazeForm): MazeDefinition {
  const json = (variant: string, plan: object, solution: object): MazeDefinition => ({
    interactionType: MAZE_TREE,
    plan: JSON.stringify({ version: 1, type: MAZE_TREE, variant, ...plan }),
    solution: JSON.stringify(solution),
  });

  switch (form.kind) {
    case 'mazeTree': {
      const internal = form.nodes.length;
      const nodes = [
        ...form.nodes.map((n, i) => ({ id: `n${i + 1}`, label: n.text.trim() })),
        ...form.exits.map((label, i) => ({ id: `x${i + 1}`, ...(label.trim() ? { label: label.trim() } : {}), end: true })),
      ];
      const childId = (bfs: number) => (bfs <= internal ? `n${bfs}` : `x${bfs - internal}`);
      const edges: { id: string; from: string; to: string; label: string }[] = [];
      const choices: Record<string, string> = {};
      form.nodes.forEach((node, i) => {
        const bfs = i + 1;
        [0, 1].forEach((side) => {
          const id = `e${edges.length + 1}`;
          edges.push({ id, from: `n${bfs}`, to: childId(2 * bfs + side), label: form.branches[side].trim() });
          if (node.correct === side) choices[`n${bfs}`] = id;
        });
      });
      return json('tree', { start: 'n1', nodes, edges }, { choices });
    }
    case 'mazeBoard': {
      const n = form.rows.length;
      const nodes = [
        ...form.rows.map((r, i) => ({ id: `r${i + 1}`, ...(r.label.trim() ? { label: r.label.trim() } : {}) })),
        { id: 'f', ...(form.finish.trim() ? { label: form.finish.trim() } : {}), end: true },
      ];
      const edges: { id: string; from: string; to: string; label: string }[] = [];
      const choices: Record<string, string> = {};
      form.rows.forEach((row, i) => {
        row.boxes.forEach((box, j) => {
          const id = `b${edges.length + 1}`;
          edges.push({ id, from: `r${i + 1}`, to: i + 1 < n ? `r${i + 2}` : 'f', label: box.trim() });
          if (row.correct === j) choices[`r${i + 1}`] = id;
        });
      });
      return json('board', { start: 'r1', nodes, edges }, { choices });
    }
    case 'mazeRooms': {
      const nodes = form.rooms.map((room, i) => ({
        id: `o${i + 1}`,
        ...(room.text.trim() ? { label: room.text.trim() } : {}),
        row: Math.floor(i / form.cols),
        col: i % form.cols,
        ...(i === form.finish ? { end: true } : {}),
      }));
      const edgeId = new Map<number, string>();
      const edges = form.doors.flatMap((door, i) => {
        if (!door.label.trim()) return [];
        const id = `d${edgeId.size + 1}`;
        edgeId.set(i, id);
        return [{ id, from: `o${door.a + 1}`, to: `o${door.b + 1}`, label: door.label.trim() }];
      });
      const choices: Record<string, string> = {};
      form.rooms.forEach((room, i) => {
        if (i === form.finish || room.answer === null) return;
        const door = form.doors[room.answer];
        const id = edgeId.get(room.answer);
        if (door && id && (door.a === i || door.b === i)) choices[`o${i + 1}`] = id;
      });
      return json('rooms', { start: `o${form.start + 1}`, nodes, edges }, { choices });
    }
    case 'mazeStones': {
      const nodes = form.stones.map((s, i) => ({ id: `s${i + 1}`, label: s.text.trim() }));
      const marked = form.stones.flatMap((s, i) => (s.marked ? [`s${i + 1}`] : []));
      return json('stones', { nodes }, { marked });
    }
  }
}

/** Kayıtlı plan + çözüm → form; şablonun ifade edemediği plan null. */
export function parseMazeDefinition(planJson: string | null | undefined, solutionJson: string | null | undefined): MazeForm | null {
  const plan = parseMazeTreePlan(planJson);
  if (!plan) return null;
  const solution = parseMazeSolution(solutionJson, plan);
  switch (plan.variant) {
    case 'tree':
      return parseTree(plan, solution.choices);
    case 'board':
      return parseBoard(plan, solution.choices);
    case 'rooms':
      return parseRooms(plan, solution.choices);
    case 'stones': {
      const marked = new Set(solution.marked);
      return { kind: 'mazeStones', stones: plan.nodes.map((n) => ({ text: n.label ?? '', marked: marked.has(n.id) })) };
    }
  }
}

function parseTree(plan: MazeTreePlanV1, choices: Record<string, string>): MazeTreeForm | null {
  // Genişlik öncelikli gezinti; her iç düğümde tam iki dal, bütün çıkışlar aynı düzeyde (tam ikili ağaç).
  const nodes: MazeTreeNodeForm[] = [];
  const exits: string[] = [];
  let branches: [string, string] | null = null;
  let level = [plan.start!];
  let depth = 0;
  while (level.length) {
    const next: string[] = [];
    const ends = level.map((id) => nodeOf(plan, id)?.end ?? false);
    if (ends.some((e) => e) && !ends.every((e) => e)) return null;
    if (ends.every((e) => e)) {
      level.forEach((id) => exits.push(nodeOf(plan, id)?.label ?? ''));
      break;
    }
    for (const id of level) {
      const out = plan.edges.filter((e) => e.from === id);
      if (out.length !== 2) return null;
      const labels: [string, string] = [out[0].label ?? '', out[1].label ?? ''];
      if (!branches) branches = labels;
      else if (labels[0] !== branches[0] || labels[1] !== branches[1]) return null;
      nodes.push({ text: nodeOf(plan, id)?.label ?? '', correct: choices[id] === out[1].id ? 1 : 0 });
      next.push(out[0].to, out[1].to);
    }
    level = next;
    depth++;
  }
  if (!branches || depth < MAZE_FORM_LIMITS.minDepth || depth > MAZE_FORM_LIMITS.maxDepth) return null;
  return { kind: 'mazeTree', depth, branches, nodes, exits };
}

function parseBoard(plan: MazeTreePlanV1, choices: Record<string, string>): MazeBoardForm | null {
  const { rows, finish } = boardRows(plan);
  if (!finish || rows.length === 0) return null;
  return {
    kind: 'mazeBoard',
    rows: rows.map((row) => ({
      label: row.node.label ?? '',
      boxes: row.boxes.map((b) => b.label ?? ''),
      correct: Math.max(0, row.boxes.findIndex((b) => b.id === choices[row.node.id])),
    })),
    finish: finish.label ?? '',
  };
}

function parseRooms(plan: MazeTreePlanV1, choices: Record<string, string>): MazeRoomsForm | null {
  const grid = roomsGrid(plan);
  if (grid.rows < MAZE_FORM_LIMITS.minGrid || grid.cols < MAZE_FORM_LIMITS.minGrid) return null;
  if (grid.rows > MAZE_FORM_LIMITS.maxGrid || grid.cols > MAZE_FORM_LIMITS.maxGrid) return null;
  // Şablon her hücrede bir oda ister; tek bitiş.
  if (plan.nodes.length !== grid.rows * grid.cols || plan.nodes.filter((n) => n.end).length !== 1) return null;
  const indexOf = new Map(plan.nodes.map((n) => [n.id, (n.row ?? 0) * grid.cols + (n.col ?? 0)]));
  const base: MazeRoomsForm = {
    kind: 'mazeRooms',
    rows: grid.rows,
    cols: grid.cols,
    rooms: [],
    doors: [],
    start: indexOf.get(plan.start!) ?? 0,
    finish: indexOf.get(plan.nodes.find((n) => n.end)!.id) ?? 0,
  };
  const doors = doorCandidates(grid.rows, grid.cols).map(({ a, b }) => ({ a, b, label: '' }));
  const doorAt = new Map(doors.map((d, i) => [`${d.a}-${d.b}`, i]));
  const edgeDoor = new Map<string, number>();
  for (const edge of plan.edges) {
    const a = indexOf.get(edge.from)!;
    const b = indexOf.get(edge.to)!;
    const i = doorAt.get(`${Math.min(a, b)}-${Math.max(a, b)}`);
    if (i === undefined) return null;
    doors[i].label = edge.label ?? '';
    edgeDoor.set(edge.id, i);
  }
  const rooms: MazeRoomForm[] = Array.from({ length: grid.rows * grid.cols }, () => ({ text: '', answer: null }));
  for (const node of plan.nodes) {
    const i = indexOf.get(node.id)!;
    rooms[i] = { text: node.label ?? '', answer: Object.hasOwn(choices, node.id) ? edgeDoor.get(choices[node.id]) ?? null : null };
  }
  return { ...base, rooms, doors };
}
