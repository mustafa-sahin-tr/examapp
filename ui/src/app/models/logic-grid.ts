/**
 * Issue #586: sözel mantık tablosu (`InteractionType = logicGrid`, bileşen G; kişi × özellik).
 *
 * Plan: konu öğeleri (satırlar, ör. kişiler), kategoriler (ör. renk, hayvan; her birinde konu öğesi kadar seçenek) ve ipuçları.
 * Çözüm (`{"assign":{"p1":{"c1":"o2"}}}`) öğrenciye gitmez (#570). Öğrenci hücrelere ✓ / ✗ koyar
 * (`{"cells":[{row,col,mark}]}`); kontrolde yalnız ✓ sayılır: (konu öğesi, kategori) çifti doğru ⇔ o kategoride tek ✓ ve doğru
 * seçenek (backend `LogicGridGrader`). Satır / sütun tekliği oynatıcıda anında denetlenir ({@link conflicts}). Puansız.
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const LOGIC_GRID = 'logicGrid' as const;

/** Backend `LogicGridGrader` sınırlarıyla aynı. */
export const LOGIC_GRID_LIMITS = {
  minItems: 2,
  maxItems: 8,
  maxCategories: 4,
  maxClues: 20,
  maxLabel: 60,
  maxClue: 300,
} as const;

export type LogicMark = 'yes' | 'no';

export interface LogicItem {
  id: string;
  label: string;
}

export interface LogicCategory {
  id: string;
  label: string;
  items: LogicItem[];
}

export interface LogicGridPlanV1 {
  version: 1;
  type: typeof LOGIC_GRID;
  subject: { label: string; items: LogicItem[] };
  categories: LogicCategory[];
  clues: { id: string; text: string }[];
}

/** `marks[row][col]` = ✓ / ✗ (yoksa boş). */
export type LogicMarks = Record<string, Record<string, LogicMark>>;

/** Çözüm: konu öğesi → kategori → seçenek. */
export type LogicAssign = Record<string, Record<string, string>>;

export interface LogicGridFeedback {
  rows: { id: string; correct: boolean }[];
}

export function isLogicGridType(type: unknown): boolean {
  return isInteractionType(type, LOGIC_GRID);
}

export function pairKey(subjectId: string, categoryId: string): string {
  return `${subjectId}:${categoryId}`;
}

function readItems(value: unknown, ids: Set<string>): LogicItem[] {
  if (!Array.isArray(value)) return [];
  const out: LogicItem[] = [];
  for (const raw of value as unknown[]) {
    if (!raw || typeof raw !== 'object') continue;
    const { id, label } = raw as Record<string, unknown>;
    if (!isValidInteractionId(id) || ids.has(id)) continue;
    ids.add(id);
    out.push({ id, label: typeof label === 'string' ? label : '' });
  }
  return out;
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / konu ya da kategori yoksa null. */
export function parseLogicGridPlan(json: string | null | undefined): LogicGridPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isLogicGridType(parsed['type'])) return null;
    const ids = new Set<string>();
    const subjectRaw = parsed['subject'] as Record<string, unknown> | null;
    if (!subjectRaw || typeof subjectRaw !== 'object') return null;
    const subject = {
      label: typeof subjectRaw['label'] === 'string' ? subjectRaw['label'] : '',
      items: readItems(subjectRaw['items'], ids),
    };
    const categories: LogicCategory[] = [];
    for (const raw of Array.isArray(parsed['categories']) ? (parsed['categories'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const item = raw as Record<string, unknown>;
      if (!isValidInteractionId(item['id']) || ids.has(item['id'])) continue;
      ids.add(item['id']);
      categories.push({
        id: item['id'],
        label: typeof item['label'] === 'string' ? item['label'] : '',
        items: readItems(item['items'], ids),
      });
    }
    const clues = (Array.isArray(parsed['clues']) ? (parsed['clues'] as unknown[]) : []).flatMap((raw) => {
      if (!raw || typeof raw !== 'object') return [];
      const { id, text } = raw as Record<string, unknown>;
      return isValidInteractionId(id) && typeof text === 'string' && text.trim() ? [{ id, text }] : [];
    });
    if (subject.items.length === 0 || categories.length === 0) return null;
    return { version: 1, type: LOGIC_GRID, subject, categories, clues };
  } catch {
    return null;
  }
}

function categoryOf(plan: LogicGridPlanV1, optionId: string): LogicCategory | null {
  return plan.categories.find((c) => c.items.some((i) => i.id === optionId)) ?? null;
}

/** Öğrenci cevabı → işaretler; plana uymayan hücre atlanır. Başka tür / bozuksa boş. */
export function parseLogicGridAnswer(json: string | null | undefined, plan: LogicGridPlanV1 | null): LogicMarks {
  const out = Object.create(null) as LogicMarks;
  if (!json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isLogicGridType(parsed['type']) || !Array.isArray(parsed['cells'])) return out;
    const rows = plan ? new Set(plan.subject.items.map((s) => s.id)) : null;
    for (const raw of parsed['cells'] as unknown[]) {
      if (!raw || typeof raw !== 'object') continue;
      const { row, col, mark } = raw as Record<string, unknown>;
      if (!isValidInteractionId(row) || !isValidInteractionId(col) || (mark !== 'yes' && mark !== 'no')) continue;
      if (plan && (!rows!.has(row) || !categoryOf(plan, col))) continue;
      (out[row] ??= Object.create(null) as Record<string, LogicMark>)[col] = mark;
    }
    return out;
  } catch {
    return out;
  }
}

export function markOf(marks: LogicMarks, row: string, col: string): LogicMark | null {
  return Object.hasOwn(marks, row) && Object.hasOwn(marks[row], col) ? marks[row][col] : null;
}

export function countMarks(marks: LogicMarks): number {
  return Object.values(marks).reduce((sum, row) => sum + Object.keys(row).length, 0);
}

/** Payload: plan sırasıyla (satır, sütun) — aynı işaret kümesi aynı metni üretir. */
export function buildLogicGridPayload(plan: LogicGridPlanV1, marks: LogicMarks): string {
  const cells: { row: string; col: string; mark: LogicMark }[] = [];
  for (const subject of plan.subject.items) {
    for (const category of plan.categories) {
      for (const option of category.items) {
        const mark = markOf(marks, subject.id, option.id);
        if (mark) cells.push({ row: subject.id, col: option.id, mark });
      }
    }
  }
  return JSON.stringify({ version: 1, type: LOGIC_GRID, cells });
}

/**
 * ✓ konunca aynı kategori bloğunda o satırın ve o sütunun boş hücrelerine ✗ yazılır (öğrencinin el işi azalır; mevcut ✓ / ✗
 * değişmez). Döner: yeni işaretler.
 */
export function withAutoCrosses(plan: LogicGridPlanV1, marks: LogicMarks, row: string, col: string): LogicMarks {
  const category = categoryOf(plan, col);
  if (!category) return marks;
  const next = cloneMarks(marks);
  const set = (r: string, c: string) => {
    if (markOf(next, r, c)) return;
    (next[r] ??= Object.create(null) as Record<string, LogicMark>)[c] = 'no';
  };
  for (const option of category.items) if (option.id !== col) set(row, option.id);
  for (const subject of plan.subject.items) if (subject.id !== row) set(subject.id, col);
  return next;
}

export function cloneMarks(marks: LogicMarks): LogicMarks {
  const out = Object.create(null) as LogicMarks;
  for (const [row, cols] of Object.entries(marks)) out[row] = Object.assign(Object.create(null), cols) as Record<string, LogicMark>;
  return out;
}

/**
 * Tekliği bozan hücreler (`"satır:sütun"`): aynı kategori bloğunda bir satırda ya da bir sütunda birden çok ✓, ya da bir satırın
 * (sütunun) bütün hücreleri ✗ (yerleşecek yer kalmadı).
 */
export function conflicts(plan: LogicGridPlanV1, marks: LogicMarks): Set<string> {
  const out = new Set<string>();
  for (const category of plan.categories) {
    for (const subject of plan.subject.items) {
      const cells = category.items.map((o) => [subject.id, o.id] as const);
      flag(cells);
    }
    for (const option of category.items) {
      const cells = plan.subject.items.map((s) => [s.id, option.id] as const);
      flag(cells);
    }
  }
  return out;

  function flag(cells: (readonly [string, string])[]) {
    const yes = cells.filter(([r, c]) => markOf(marks, r, c) === 'yes');
    if (yes.length > 1) yes.forEach(([r, c]) => out.add(`${r}:${c}`));
    if (cells.length > 0 && cells.every(([r, c]) => markOf(marks, r, c) === 'no')) cells.forEach(([r, c]) => out.add(`${r}:${c}`));
  }
}

/** Çözüm JSON'u; plana uymayan atlanır. */
export function parseLogicGridSolution(json: string | null | undefined, plan?: LogicGridPlanV1 | null): LogicAssign {
  const out = Object.create(null) as LogicAssign;
  if (!json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const assign = parsed && typeof parsed === 'object' ? parsed['assign'] : null;
    if (!assign || typeof assign !== 'object' || Array.isArray(assign)) return out;
    for (const [row, cats] of Object.entries(assign as Record<string, unknown>)) {
      if (!isValidInteractionId(row) || !cats || typeof cats !== 'object' || Array.isArray(cats)) continue;
      if (plan && !plan.subject.items.some((s) => s.id === row)) continue;
      for (const [cat, option] of Object.entries(cats as Record<string, unknown>)) {
        if (!isValidInteractionId(cat) || !isValidInteractionId(option)) continue;
        if (plan && categoryOf(plan, option)?.id !== cat) continue;
        (out[row] ??= Object.create(null) as Record<string, string>)[cat] = option;
      }
    }
    return out;
  } catch {
    return out;
  }
}

/** İşaretsiz cevap: sunucu boş cevap sayar. Başka tür false. */
export function isBlankLogicGridPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isLogicGridType(parsed['type'])) return false;
    return !Array.isArray(parsed['cells']) || parsed['cells'].length === 0;
  } catch {
    return false;
  }
}

export function parseLogicGridFeedback(json: string | null | undefined): LogicGridFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    return {
      rows: (parsed['rows'] as unknown[]).flatMap((raw) => {
        if (!raw || typeof raw !== 'object') return [];
        const { id, correct } = raw as Record<string, unknown>;
        return typeof id === 'string' && typeof correct === 'boolean' ? [{ id, correct }] : [];
      }),
    };
  } catch {
    return null;
  }
}

/** (konu öğesi, kategori) için ✓ konan seçenekler. */
export function ticks(plan: LogicGridPlanV1, marks: LogicMarks, row: string, categoryId: string): string[] {
  const category = plan.categories.find((c) => c.id === categoryId);
  return category ? category.items.filter((o) => markOf(marks, row, o.id) === 'yes').map((o) => o.id) : [];
}
