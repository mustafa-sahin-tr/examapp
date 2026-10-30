/**
 * Issue #585: yol oyunları etkileşimi (`InteractionType = mazeTree`, bileşen G) — tek motor, dört varyant:
 * - `tree`: D/Y dallanan ağaç (ifade → D/Y dalı → … → çıkış),
 * - `rooms`: odalı labirent (oda = soru, kapı = cevap; kapılar iki yönlü),
 * - `board`: ışıklı pano (satır başına bir doğru kutu, alttan üste),
 * - `stones`: ırmak taşları (basılmaması gereken taşları işaretle; yolsuz).
 *
 * Plan öğrenciye gider, çözüm gitmez (#570). Kontrol ilk sapmada durur ve yalnız öğrencinin geçtiği düğümleri söyler
 * (`status` + sapma düğümü `at`); rota ve doğru dal geri bildirimde yoktur. Puansız.
 */
import { cleanLabel, isInteractionType, isValidInteractionId } from './interaction-ids';

export const MAZE_TREE = 'mazeTree' as const;

export type MazeVariant = 'tree' | 'rooms' | 'board' | 'stones';

export const MAZE_VARIANTS: readonly MazeVariant[] = ['tree', 'rooms', 'board', 'stones'];

/** Backend `MazeTreeGrader` sınırlarıyla aynı. */
export const MAZE_LIMITS = {
  maxNodes: 64,
  maxEdges: 160,
  maxLabelLength: 300,
  maxTreeDepth: 5,
  maxTreeBranches: 4,
  maxBoardRows: 12,
  minBoardBoxes: 2,
  maxBoardBoxes: 6,
  maxGridSize: 8,
  minStones: 2,
  maxStones: 40,
  maxPathLength: 128,
} as const;

export interface MazeNode {
  id: string;
  label: string | null;
  end: boolean;
  row: number | null;
  col: number | null;
}

export interface MazeEdge {
  id: string;
  from: string;
  to: string;
  label: string | null;
}

export interface MazeTreePlanV1 {
  version: 1;
  type: typeof MAZE_TREE;
  variant: MazeVariant;
  start: string | null;
  nodes: MazeNode[];
  edges: MazeEdge[];
}

/** Yol varyantlarında düğüm → doğru kenar; taşlarda basılmaması gereken taşlar (öğrenci diğerlerine basar). */
export interface MazeSolution {
  choices: Record<string, string>;
  marked: string[];
}

export type MazeStatus = 'reached' | 'deviated' | 'incomplete';

export interface MazeFeedback {
  rows: { id: string; correct: boolean }[];
  status: MazeStatus;
  /** Sapma düğümü / ilk yanlış taş (`deviated`), varılan bitiş (`reached`) ya da yolun kaldığı düğüm (`incomplete`). */
  at: string | null;
}

export function isMazeTreeType(type: unknown): boolean {
  return isInteractionType(type, MAZE_TREE);
}

function readInt(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) ? value : null;
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / bozuk / yol varyantında başlangıçsız ise null. */
export function parseMazeTreePlan(json: string | null | undefined): MazeTreePlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isMazeTreeType(parsed['type'])) return null;
    const variant = parsed['variant'];
    if (typeof variant !== 'string' || !(MAZE_VARIANTS as readonly string[]).includes(variant)) return null;

    const seen = new Set<string>();
    const nodes: MazeNode[] = [];
    for (const raw of Array.isArray(parsed['nodes']) ? (parsed['nodes'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const item = raw as Record<string, unknown>;
      const id = item['id'];
      if (!isValidInteractionId(id) || seen.has(id)) continue;
      seen.add(id);
      nodes.push({
        id,
        label: cleanLabel(item['label']),
        end: item['end'] === true,
        row: readInt(item['row']),
        col: readInt(item['col']),
      });
    }
    const nodeIds = new Set(nodes.map((n) => n.id));

    const edges: MazeEdge[] = [];
    if (variant !== 'stones') {
      for (const raw of Array.isArray(parsed['edges']) ? (parsed['edges'] as unknown[]) : []) {
        if (!raw || typeof raw !== 'object') continue;
        const item = raw as Record<string, unknown>;
        const { id, from, to } = item;
        if (!isValidInteractionId(id) || seen.has(id) || typeof from !== 'string' || typeof to !== 'string') continue;
        if (!nodeIds.has(from) || !nodeIds.has(to) || from === to) continue;
        seen.add(id);
        edges.push({ id, from, to, label: cleanLabel(item['label']) });
      }
    }

    const start = typeof parsed['start'] === 'string' ? parsed['start'] : null;
    if (variant === 'stones') {
      if (nodes.length === 0) return null;
      return { version: 1, type: MAZE_TREE, variant, start: null, nodes, edges: [] };
    }
    const startNode = nodes.find((n) => n.id === start);
    if (!startNode || startNode.end) return null;
    return { version: 1, type: MAZE_TREE, variant: variant as MazeVariant, start, nodes, edges };
  } catch {
    return null;
  }
}

export function nodeOf(plan: MazeTreePlanV1, id: string | null | undefined): MazeNode | null {
  return plan.nodes.find((n) => n.id === id) ?? null;
}

export function edgeOf(plan: MazeTreePlanV1, id: string | null | undefined): MazeEdge | null {
  return plan.edges.find((e) => e.id === id) ?? null;
}

/** Kenarla `nodeId`'den varılan düğüm; kenar o düğümden kullanılamıyorsa null (odalarda kapılar iki yönlü). */
export function edgeTarget(plan: MazeTreePlanV1, edge: MazeEdge, nodeId: string): string | null {
  if (edge.from === nodeId) return edge.to;
  if (plan.variant === 'rooms' && edge.to === nodeId) return edge.from;
  return null;
}

/** Düğümden kullanılabilir kenarlar (plan sırası). */
export function usableEdges(plan: MazeTreePlanV1, nodeId: string): MazeEdge[] {
  return plan.edges.filter((e) => edgeTarget(plan, e, nodeId) !== null);
}

export interface MazeWalk {
  /** Geçerli adımlar (kenar id). */
  edges: string[];
  /** Ziyaret sırası: başlangıç + her adımın vardığı düğüm. */
  nodes: string[];
  current: string;
  /** Bulunulan düğüm bir bitiş. */
  finished: boolean;
}

/** Yolu başlangıçtan yürür; kullanılamayan kenarda ya da bitişten sonra durur (geçersiz kuyruk atılır). */
export function walkMaze(plan: MazeTreePlanV1, path: readonly string[]): MazeWalk {
  const start = plan.start ?? '';
  const walk: MazeWalk = { edges: [], nodes: [start], current: start, finished: false };
  for (const edgeId of path.slice(0, MAZE_LIMITS.maxPathLength)) {
    if (nodeOf(plan, walk.current)?.end) break;
    const edge = edgeOf(plan, edgeId);
    const next = edge ? edgeTarget(plan, edge, walk.current) : null;
    if (!next) break;
    walk.edges.push(edgeId);
    walk.nodes.push(next);
    walk.current = next;
  }
  walk.finished = !!nodeOf(plan, walk.current)?.end;
  return walk;
}

/** Rota: başlangıçtan seçimleri izleyen kenarlar; bitişe döngüsüz varmazsa null. */
export function mazeRoute(plan: MazeTreePlanV1, choices: Record<string, string>): string[] | null {
  if (!plan.start) return null;
  const route: string[] = [];
  const visited = new Set<string>();
  let current = plan.start;
  while (!nodeOf(plan, current)?.end) {
    if (visited.has(current) || !Object.hasOwn(choices, current)) return null;
    visited.add(current);
    const edge = edgeOf(plan, choices[current]);
    const next = edge ? edgeTarget(plan, edge, current) : null;
    if (!next) return null;
    route.push(edge!.id);
    current = next;
  }
  return route.length > 0 ? route : null;
}

/** Çözüm JSON'u — bozuk / boşsa boş seçimler ve işaretler. */
export function parseMazeSolution(json: string | null | undefined, plan?: MazeTreePlanV1 | null): MazeSolution {
  const empty: MazeSolution = { choices: Object.create(null) as Record<string, string>, marked: [] };
  if (!json) return empty;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return empty;
    const nodeIds = plan ? new Set(plan.nodes.map((n) => n.id)) : null;
    const edgeIds = plan ? new Set(plan.edges.map((e) => e.id)) : null;
    const choicesRaw = parsed['choices'];
    if (choicesRaw && typeof choicesRaw === 'object' && !Array.isArray(choicesRaw)) {
      for (const [nodeId, edgeId] of Object.entries(choicesRaw as Record<string, unknown>)) {
        if (!isValidInteractionId(nodeId) || !isValidInteractionId(edgeId)) continue;
        if ((nodeIds && !nodeIds.has(nodeId)) || (edgeIds && !edgeIds.has(edgeId))) continue;
        empty.choices[nodeId] = edgeId;
      }
    }
    empty.marked = uniqueIds(parsed['marked'], nodeIds);
    return empty;
  } catch {
    return empty;
  }
}

function uniqueIds(value: unknown, allowed: Set<string> | null): string[] {
  if (!Array.isArray(value)) return [];
  const out: string[] = [];
  for (const id of value as unknown[]) {
    if (!isValidInteractionId(id) || out.includes(id) || (allowed && !allowed.has(id))) continue;
    out.push(id);
  }
  return out;
}

export interface MazeAnswer {
  path: string[];
  /** Taşlarda öğrencinin bastığı taşlar. */
  stepped: string[];
}

/** Öğrenci cevabı (`{"type":"mazeTree","path":[...]}` ya da `stepped`) — başka tür / bozuksa boş. Yol plana göre kırpılır. */
export function parseMazeAnswer(json: string | null | undefined, plan?: MazeTreePlanV1 | null): MazeAnswer {
  const empty: MazeAnswer = { path: [], stepped: [] };
  if (!json) return empty;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isMazeTreeType(parsed['type'])) return empty;
    const rawPath = Array.isArray(parsed['path'])
      ? (parsed['path'] as unknown[]).filter((id): id is string => isValidInteractionId(id))
      : [];
    const path = plan && plan.variant !== 'stones' ? walkMaze(plan, rawPath).edges : plan ? [] : rawPath;
    const stepped = plan && plan.variant !== 'stones' ? [] : uniqueIds(parsed['stepped'], plan ? new Set(plan.nodes.map((n) => n.id)) : null);
    return { path, stepped };
  } catch {
    return empty;
  }
}

export function buildMazePathPayload(path: readonly string[]): string {
  return JSON.stringify({ version: 1, type: MAZE_TREE, path: [...path] });
}

export function buildMazeSteppedPayload(stepped: readonly string[]): string {
  return JSON.stringify({ version: 1, type: MAZE_TREE, stepped: [...stepped] });
}

export function isEmptyMazeAnswer(answer: MazeAnswer): boolean {
  return answer.path.length === 0 && answer.stepped.length === 0;
}

/** Adımsız / işaretsiz cevap: sunucu boş cevap sayar, ekranlar da cevapsız saymalı. Başka tür false. */
export function isBlankMazePayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isMazeTreeType(parsed['type'])) return false;
    return isEmptyMazeAnswer(parseMazeAnswer(json));
  } catch {
    return false;
  }
}

/** Kontrol geri bildirimi (`{"rows","status","at"}`); bozuksa null. */
export function parseMazeFeedback(json: string | null | undefined): MazeFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object') return null;
    const status = parsed['status'];
    if (status !== 'reached' && status !== 'deviated' && status !== 'incomplete') return null;
    const rows = (Array.isArray(parsed['rows']) ? (parsed['rows'] as unknown[]) : []).flatMap((raw) => {
      if (!raw || typeof raw !== 'object') return [];
      const { id, correct } = raw as Record<string, unknown>;
      return typeof id === 'string' && typeof correct === 'boolean' ? [{ id, correct }] : [];
    });
    return { rows, status, at: typeof parsed['at'] === 'string' ? parsed['at'] : null };
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Yerleşim
// ---------------------------------------------------------------------------

export interface TreeLayoutNode {
  id: string;
  /** Merkez (yaprak birimi; ilk yaprak 0.5). */
  x: number;
  /** Düzey (başlangıç 0). */
  y: number;
  /** Alt ağacın kapladığı yaprak sayısı (kart genişliği). */
  span: number;
}

export interface TreeLayout {
  nodes: TreeLayoutNode[];
  width: number;
  depth: number;
}

/** Ağaç yerleşimi: yapraklar soldan sağa sırayla, iç düğüm çocuklarının ortasında. Döngüye karşı ziyaret kümesi. */
export function treeLayout(plan: MazeTreePlanV1): TreeLayout {
  const out: TreeLayoutNode[] = [];
  const visited = new Set<string>();
  let leaves = 0;
  let depth = 0;
  const place = (id: string, level: number): TreeLayoutNode => {
    visited.add(id);
    depth = Math.max(depth, level);
    const children = plan.edges.filter((e) => e.from === id && !visited.has(e.to)).map((e) => place(e.to, level + 1));
    const node =
      children.length === 0
        ? { id, x: leaves++ + 0.5, y: level, span: 1 }
        : {
            id,
            x: (children[0].x + children[children.length - 1].x) / 2,
            y: level,
            span: children.reduce((sum, c) => sum + c.span, 0),
          };
    out.push(node);
    return node;
  };
  if (plan.start) place(plan.start, 0);
  return { nodes: out, width: Math.max(1, leaves), depth };
}

export interface BoardRow {
  node: MazeNode;
  boxes: MazeEdge[];
}

/** Pano satırları başlangıçtan bitişe (zincir); sonuncu satırdan sonra bitiş düğümü. */
export function boardRows(plan: MazeTreePlanV1): { rows: BoardRow[]; finish: MazeNode | null } {
  const rows: BoardRow[] = [];
  const visited = new Set<string>();
  let current = nodeOf(plan, plan.start);
  while (current && !current.end && !visited.has(current.id)) {
    visited.add(current.id);
    const boxes = plan.edges.filter((e) => e.from === current!.id);
    rows.push({ node: current, boxes });
    current = boxes.length ? nodeOf(plan, boxes[0].to) : null;
  }
  return { rows, finish: current?.end ? current : null };
}

/** Oda ızgarasının boyutu (en az 1×1). */
export function roomsGrid(plan: MazeTreePlanV1): { rows: number; cols: number } {
  const rows = Math.max(1, ...plan.nodes.map((n) => (n.row ?? 0) + 1));
  const cols = Math.max(1, ...plan.nodes.map((n) => (n.col ?? 0) + 1));
  return { rows, cols };
}
