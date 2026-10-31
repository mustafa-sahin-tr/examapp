/**
 * Issue #586: sözel mantık tablosu (`InteractionType = logicGrid`, bileşen G; kişi × özellik).
 *
 * Plan: konu öğeleri (satırlar, ör. kişiler), kategoriler (ör. renk, hayvan; her birinde konu öğesi kadar seçenek) ve ipuçları.
 * Çözüm (`{"assign":{"p1":{"c1":"o2"}}}`) öğrenciye gitmez (#570). Öğrenci hücrelere ✓ / ✗ koyar
 * (`{"cells":[{row,col,mark}]}`); kontrolde yalnız ✓ sayılır: (konu öğesi, kategori) çifti doğru ⇔ o kategoride tek ✓ ve doğru
 * seçenek (backend `LogicGridGrader`). Satır / sütun tekliği oynatıcıda anında denetlenir ({@link conflicts}). Puansız.
 *
 * Merdiven tablo (kitaptaki gibi, 2+ kategoride): konu satırlarının altında kategori × kategori blokları — satır grupları konu,
 * sonra son kategoriden ikinciye; `Ci` satır grubunun sütunları `C1..C(i-1)` ({@link staircase}). Bu bloklar öğrencinin karalama
 * alanıdır, puanlanmaz; payload'da `notes` olarak blok başına kompakt dizi gider: anahtar `"satırKategori:sütunKategori"`, değer
 * satır öncelikli `n × n` karakter (`.` boş, `y` ✓, `n` ✗).
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

/** Merdiven tablonun bir satır grubu: konu (`category` null) ya da bir kategori; `cols` bu grubun blok sütunları. */
export interface LogicRowGroup {
  category: LogicCategory | null;
  label: string;
  items: LogicItem[];
  cols: LogicCategory[];
}

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

/** Satır grupları: konu (bütün kategoriler), sonra `Cn`..`C2` (her biri kendinden önceki kategorilerle). */
export function staircase(plan: LogicGridPlanV1): LogicRowGroup[] {
  const groups: LogicRowGroup[] = [{ category: null, label: plan.subject.label, items: plan.subject.items, cols: plan.categories }];
  for (let i = plan.categories.length - 1; i >= 1; i--) {
    const category = plan.categories[i];
    groups.push({ category, label: category.label, items: category.items, cols: plan.categories.slice(0, i) });
  }
  return groups;
}

/** Hücrenin bloğu: satır öğesinin grubu ve sütun seçeneğinin kategorisi (grubun sütunlarında değilse null). */
function blockOf(plan: LogicGridPlanV1, row: string, col: string): { group: LogicRowGroup; category: LogicCategory } | null {
  const category = categoryOf(plan, col);
  if (!category) return null;
  const group = staircase(plan).find((g) => g.items.some((i) => i.id === row));
  return group && group.cols.includes(category) ? { group, category } : null;
}

export function noteKey(rowCategoryId: string, colCategoryId: string): string {
  return `${rowCategoryId}:${colCategoryId}`;
}

const NOTE_CHARS: Record<string, LogicMark | null> = { '.': null, y: 'yes', n: 'no' };
const NOTE_CODES: Record<LogicMark | 'none', string> = { none: '.', yes: 'y', no: 'n' };

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
    const notes = parsed['notes'];
    if (plan && notes && typeof notes === 'object' && !Array.isArray(notes)) {
      for (const group of staircase(plan).slice(1)) {
        for (const category of group.cols) {
          const text = (notes as Record<string, unknown>)[noteKey(group.category!.id, category.id)];
          if (typeof text !== 'string' || text.length !== group.items.length * category.items.length) continue;
          group.items.forEach((item, r) =>
            category.items.forEach((option, c) => {
              const mark = NOTE_CHARS[text[r * category.items.length + c]];
              if (mark) (out[item.id] ??= Object.create(null) as Record<string, LogicMark>)[option.id] = mark;
            })
          );
        }
      }
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
  const notes: Record<string, string> = {};
  for (const group of staircase(plan).slice(1)) {
    for (const category of group.cols) {
      const text = group.items
        .flatMap((item) => category.items.map((option) => NOTE_CODES[markOf(marks, item.id, option.id) ?? 'none']))
        .join('');
      if (/[yn]/.test(text)) notes[noteKey(group.category!.id, category.id)] = text;
    }
  }
  return JSON.stringify(Object.keys(notes).length ? { version: 1, type: LOGIC_GRID, cells, notes } : { version: 1, type: LOGIC_GRID, cells });
}

/**
 * ✓ konunca aynı blokta o satırın ve o sütunun boş hücrelerine ✗ yazılır (öğrencinin el işi azalır; mevcut ✓ / ✗ değişmez).
 * Döner: yeni işaretler.
 */
export function withAutoCrosses(plan: LogicGridPlanV1, marks: LogicMarks, row: string, col: string): LogicMarks {
  const block = blockOf(plan, row, col);
  if (!block) return marks;
  const next = cloneMarks(marks);
  const set = (r: string, c: string) => {
    if (markOf(next, r, c)) return;
    (next[r] ??= Object.create(null) as Record<string, LogicMark>)[c] = 'no';
  };
  for (const option of block.category.items) if (option.id !== col) set(row, option.id);
  for (const item of block.group.items) if (item.id !== row) set(item.id, col);
  return next;
}

/** Hücre merdiven tablonun bir bloğunda mı (konu satırı ya da izinli kategori × kategori)? */
export function isLogicCell(plan: LogicGridPlanV1, row: string, col: string): boolean {
  return blockOf(plan, row, col) !== null;
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
  for (const group of staircase(plan)) {
    for (const category of group.cols) {
      for (const item of group.items) flag(category.items.map((o) => [item.id, o.id] as const));
      for (const option of category.items) flag(group.items.map((i) => [i.id, option.id] as const));
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
    const notes = parsed['notes'];
    const noted = !!notes && typeof notes === 'object' && Object.values(notes).some((v) => typeof v === 'string' && /[yn]/.test(v));
    return (!Array.isArray(parsed['cells']) || parsed['cells'].length === 0) && !noted;
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

/**
 * Özet (cevap) tablosu: konu öğesi × kategori → konu bloğunda ✓ konan seçeneğin etiketi; ✓ yoksa boş, birden çoksa `?`.
 */
export function summaryRows(plan: LogicGridPlanV1, marks: LogicMarks): { subject: LogicItem; values: string[] }[] {
  return plan.subject.items.map((subject) => ({
    subject,
    values: plan.categories.map((category) => {
      const picks = ticks(plan, marks, subject.id, category.id);
      return picks.length === 1 ? (category.items.find((o) => o.id === picks[0])?.label ?? '') : picks.length > 1 ? '?' : '';
    }),
  }));
}

/** Çözümün işaretleri (konu bloğunda doğru seçenekler ✓): çözüm gösteriminde özet ve tablo bunu kullanır. */
export function solutionMarks(plan: LogicGridPlanV1, assign: LogicAssign): LogicMarks {
  const out = Object.create(null) as LogicMarks;
  for (const subject of plan.subject.items) {
    if (!Object.hasOwn(assign, subject.id)) continue;
    for (const option of Object.values(assign[subject.id])) (out[subject.id] ??= Object.create(null) as Record<string, LogicMark>)[option] = 'yes';
  }
  return out;
}
