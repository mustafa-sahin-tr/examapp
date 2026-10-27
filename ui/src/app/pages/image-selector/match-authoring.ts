/**
 * Issue #576: eşleştirme (match) yazarlığının saf kısmı. Kurallar backend `MatchGrader` (`ValidateDefinition`) ile aynı:
 * her tarafta 1..30 öğe, öğede etiket ya da kutu, sol ve sağ id'leri çakışmaz (`l*` / `r*`), sağ öğe en çok bir sola
 * bağlı, önceden bağlı (örnek) çift dışında en az bir sol ve her açık solun çözümü; fazla sağ öğe çeldiricidir.
 */
import { InteractionBox, copyBox } from '../../models/interaction-ids';
import { MATCH, MATCH_LIMITS, MatchPlanV1, MatchSide } from '../../models/match';
import { nextInteractionId } from './drag-place-authoring';

export interface MatchItemAuthoring {
  id: string;
  label: string;
  box: InteractionBox | null;
}

export interface MatchAuthoringState {
  left: MatchItemAuthoring[];
  right: MatchItemAuthoring[];
  /** Sol id → sağ id (prototipsiz). Örnek çiftler de burada tutulur, `prelinked` ile işaretlenir. */
  pairs: Record<string, string>;
  /** Örnek (önceden bağlı) gösterilen sol öğeler. */
  prelinked: string[];
  /** Tuvalde sağ kutuya tık = bu sola bağla. */
  selectedLeftId: string | null;
}

export type MatchIssueCode =
  | 'noLeft'
  | 'tooManyLeft'
  | 'noRight'
  | 'tooManyRight'
  | 'leftEmpty'
  | 'rightEmpty'
  | 'tooLong'
  | 'missingPair'
  | 'allPrelinked';

export interface MatchIssue {
  code: MatchIssueCode;
  no?: number;
  count?: number;
}

export interface MatchInspector {
  left: Array<MatchItemAuthoring & { no: number; rightId: string; prelinked: boolean }>;
  right: Array<MatchItemAuthoring & { no: number; leftNo: number | null }>;
  selectedLeftId: string | null;
  distractorCount: number;
  issues: MatchIssue[];
  limits: typeof MATCH_LIMITS;
}

function emptyPairs(): Record<string, string> {
  return Object.create(null) as Record<string, string>;
}

function pairOf(state: MatchAuthoringState, leftId: string): string | null {
  return Object.hasOwn(state.pairs, leftId) ? state.pairs[leftId] : null;
}

export function createMatchState(): MatchAuthoringState {
  return { left: [], right: [], pairs: emptyPairs(), prelinked: [], selectedLeftId: null };
}

function allIds(state: MatchAuthoringState): string[] {
  return [...state.left.map((i) => i.id), ...state.right.map((i) => i.id)];
}

function maxOf(side: MatchSide): number {
  return side === 'left' ? MATCH_LIMITS.maxLeft : MATCH_LIMITS.maxRight;
}

/** Yeni öğe (kırpma kutusu ve/veya etiket). Sol öğe eklenince seçili sol olur. */
export function addMatchItem(state: MatchAuthoringState, side: MatchSide, box: InteractionBox | null, label = ''): MatchAuthoringState {
  const list = state[side];
  if (list.length >= maxOf(side)) return state;
  const id = nextInteractionId(side === 'left' ? 'l' : 'r', allIds(state));
  const item: MatchItemAuthoring = { id, label: label.slice(0, MATCH_LIMITS.maxLabelLength), box: box ? copyBox(box) : null };
  return { ...state, [side]: [...list, item], selectedLeftId: side === 'left' ? id : state.selectedLeftId };
}

/** Metinden öğeler: her satır bir öğe. */
export function addMatchItemsFromText(state: MatchAuthoringState, side: MatchSide, text: string): MatchAuthoringState {
  let next = state;
  (text ?? '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => !!line)
    .forEach((line) => (next = addMatchItem(next, side, null, line)));
  return next;
}

export function removeMatchItem(state: MatchAuthoringState, side: MatchSide, id: string): MatchAuthoringState {
  const pairs = emptyPairs();
  Object.keys(state.pairs).forEach((key) => {
    if (side === 'left' ? key !== id : state.pairs[key] !== id) pairs[key] = state.pairs[key];
  });
  const left = side === 'left' ? state.left.filter((i) => i.id !== id) : state.left;
  return {
    ...state,
    [side]: state[side].filter((i) => i.id !== id),
    pairs,
    prelinked: state.prelinked.filter((leftId) => leftId !== id && Object.hasOwn(pairs, leftId)),
    selectedLeftId: state.selectedLeftId === id ? left.at(-1)?.id ?? null : state.selectedLeftId,
  };
}

export function setMatchItemLabel(state: MatchAuthoringState, side: MatchSide, id: string, label: string): MatchAuthoringState {
  const value = (label ?? '').slice(0, MATCH_LIMITS.maxLabelLength);
  return { ...state, [side]: state[side].map((i) => (i.id === id ? { ...i, label: value } : i)) };
}

/** Solu sağa bağlar (sağ başka sola bağlıysa oradan çözülür); null bağı kaldırır (örnek işareti de kalkar). */
export function setMatchPair(state: MatchAuthoringState, leftId: string, rightId: string | null): MatchAuthoringState {
  if (!state.left.some((i) => i.id === leftId)) return state;
  if (rightId !== null && !state.right.some((i) => i.id === rightId)) return state;
  const pairs = emptyPairs();
  Object.keys(state.pairs).forEach((key) => {
    if (key !== leftId && state.pairs[key] !== rightId) pairs[key] = state.pairs[key];
  });
  if (rightId) pairs[leftId] = rightId;
  return { ...state, pairs, prelinked: state.prelinked.filter((id) => Object.hasOwn(pairs, id)) };
}

/** Örnek çift aç / kapa (yalnız bağlı sol öğe). */
export function toggleMatchPrelinked(state: MatchAuthoringState, leftId: string): MatchAuthoringState {
  if (state.prelinked.includes(leftId)) return { ...state, prelinked: state.prelinked.filter((id) => id !== leftId) };
  if (!pairOf(state, leftId)) return state;
  return { ...state, prelinked: state.left.map((i) => i.id).filter((id) => id === leftId || state.prelinked.includes(id)) };
}

export function selectMatchLeft(state: MatchAuthoringState, leftId: string | null): MatchAuthoringState {
  if (leftId !== null && !state.left.some((i) => i.id === leftId)) return state;
  return { ...state, selectedLeftId: leftId };
}

function itemPlan(item: MatchItemAuthoring) {
  return { id: item.id, ...(item.label.trim() ? { label: item.label.trim() } : {}), ...(item.box ? { box: copyBox(item.box) } : {}) };
}

export function buildMatchPlan(state: MatchAuthoringState): MatchPlanV1 {
  return {
    version: 1,
    type: MATCH,
    left: state.left.map(itemPlan),
    right: state.right.map(itemPlan),
    prelinked: state.left
      .filter((i) => state.prelinked.includes(i.id) && !!pairOf(state, i.id))
      .map((i) => ({ leftId: i.id, rightId: pairOf(state, i.id)! })),
  };
}

/** Çözüm `{sol: sağ}` (örnek çiftler hariç); hiç eşleşme yoksa null (taslak). */
export function buildMatchSolution(state: MatchAuthoringState): Record<string, string> | null {
  const result: Record<string, string> = {};
  state.left.forEach((item) => {
    const rightId = pairOf(state, item.id);
    if (rightId && !state.prelinked.includes(item.id)) result[item.id] = rightId;
  });
  return Object.keys(result).length > 0 ? result : null;
}

export function matchIssues(state: MatchAuthoringState): MatchIssue[] {
  const limits = MATCH_LIMITS;
  const issues: MatchIssue[] = [];
  if (state.left.length < limits.minLeft) issues.push({ code: 'noLeft' });
  if (state.left.length > limits.maxLeft) issues.push({ code: 'tooManyLeft' });
  if (state.right.length < limits.minRight) issues.push({ code: 'noRight' });
  if (state.right.length > limits.maxRight) issues.push({ code: 'tooManyRight' });
  state.left.forEach((i, n) => {
    if (!i.box && !i.label.trim()) issues.push({ code: 'leftEmpty', no: n + 1 });
  });
  state.right.forEach((i, n) => {
    if (!i.box && !i.label.trim()) issues.push({ code: 'rightEmpty', no: n + 1 });
  });
  if ([...state.left, ...state.right].some((i) => i.label.trim().length > limits.maxLabelLength)) issues.push({ code: 'tooLong' });
  const open = state.left.filter((i) => !state.prelinked.includes(i.id));
  if (state.left.length > 0 && open.length === 0) issues.push({ code: 'allPrelinked' });
  const missing = open.filter((i) => !pairOf(state, i.id)).length;
  if (missing > 0) issues.push({ code: 'missingPair', count: missing });
  return issues;
}

export function buildMatchInspector(state: MatchAuthoringState): MatchInspector {
  const left = state.left.map((i, n) => ({ ...i, no: n + 1, rightId: pairOf(state, i.id) ?? '', prelinked: state.prelinked.includes(i.id) }));
  const leftNoOf = new Map(left.filter((l) => !!l.rightId).map((l) => [l.rightId, l.no]));
  const right = state.right.map((i, n) => ({ ...i, no: n + 1, leftNo: leftNoOf.get(i.id) ?? null }));
  return {
    left,
    right,
    selectedLeftId: state.selectedLeftId,
    distractorCount: right.filter((r) => r.leftNo === null).length,
    issues: matchIssues(state),
    limits: MATCH_LIMITS,
  };
}
