/**
 * Issue #586: kelime avı etkileşimi (`InteractionType = wordSearch`, bileşen G).
 *
 * Plan: harf ızgarası (öğrenciye gider) + kelime listesi (`label` kelimenin kendisi ya da ipucu). Çözüm kelime başına düz çizgi
 * (`{"lines":{"w1":{"from":[r,c],"to":[r,c]}}}`), öğrenciye gitmez (#570). Öğrenci payload'u işaretlediği düz çizgiler
 * (`{"lines":[{from,to}]}`, kelimeye bağlı değil). Kontrol harflere bakar (ters yön ve kelimenin başka yerde geçmesi sayılır);
 * geri bildirim kelime başına bulundu mu ve her çizginin bir kelime olup olmadığı (backend `WordSearchGrader`). Puansız.
 */
import { cleanLabel, isInteractionType, isValidInteractionId } from './interaction-ids';

export const WORD_SEARCH = 'wordSearch' as const;

/** Backend `WordSearchGrader` sınırlarıyla aynı. */
export const WORD_SEARCH_LIMITS = {
  minSize: 4,
  maxSize: 20,
  maxWords: 40,
  minWordLength: 2,
  maxLabel: 100,
} as const;

/** Öğrencinin işaretleyebileceği en çok çizgi (kelime sayısının iki katı, en az 10). */
export function maxWordSearchLines(words: number): number {
  return Math.max(10, 2 * words);
}

export type Cell = [number, number];

export interface WordSearchLine {
  from: Cell;
  to: Cell;
}

export interface WordSearchWord {
  id: string;
  label: string | null;
}

export interface WordSearchPlanV1 {
  version: 1;
  type: typeof WORD_SEARCH;
  rows: number;
  cols: number;
  /** `letters[r]` = satırın harfleri (tam `cols` karakter). */
  letters: string[][];
  words: WordSearchWord[];
}

export interface WordSearchFeedback {
  rows: { id: string; correct: boolean }[];
  /** Öğrencinin her çizgisi (payload sırası) bir kelime mi. */
  lines: boolean[];
}

export function isWordSearchType(type: unknown): boolean {
  return isInteractionType(type, WORD_SEARCH);
}

function readInt(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) ? value : null;
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / ızgara eksik ise null. */
export function parseWordSearchPlan(json: string | null | undefined): WordSearchPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isWordSearchType(parsed['type'])) return null;
    const rows = readInt(parsed['rows']);
    const cols = readInt(parsed['cols']);
    const { minSize, maxSize } = WORD_SEARCH_LIMITS;
    if (rows === null || cols === null || rows < minSize || rows > maxSize || cols < minSize || cols > maxSize) return null;
    const rawLetters = parsed['letters'];
    if (!Array.isArray(rawLetters) || rawLetters.length !== rows) return null;
    const letters: string[][] = [];
    for (const line of rawLetters as unknown[]) {
      if (typeof line !== 'string') return null;
      const chars = [...line];
      if (chars.length !== cols) return null;
      letters.push(chars);
    }
    const seen = new Set<string>();
    const words: WordSearchWord[] = [];
    for (const raw of Array.isArray(parsed['words']) ? (parsed['words'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const item = raw as Record<string, unknown>;
      if (!isValidInteractionId(item['id']) || seen.has(item['id'])) continue;
      seen.add(item['id']);
      words.push({ id: item['id'], label: cleanLabel(item['label']) });
    }
    return { version: 1, type: WORD_SEARCH, rows, cols, letters, words };
  } catch {
    return null;
  }
}

/** İki hücre düz bir çizgi (yatay, dikey, 45° çapraz) mi? Aynı hücre değil. */
export function isStraight(from: Cell, to: Cell): boolean {
  const dr = Math.abs(to[0] - from[0]);
  const dc = Math.abs(to[1] - from[1]);
  return (dr > 0 || dc > 0) && (dr === 0 || dc === 0 || dr === dc);
}

export function lineCells(line: WordSearchLine): Cell[] {
  const sr = Math.sign(line.to[0] - line.from[0]);
  const sc = Math.sign(line.to[1] - line.from[1]);
  const length = Math.max(Math.abs(line.to[0] - line.from[0]), Math.abs(line.to[1] - line.from[1])) + 1;
  return Array.from({ length }, (_, i) => [line.from[0] + sr * i, line.from[1] + sc * i] as Cell);
}

export function inGrid(plan: Pick<WordSearchPlanV1, 'rows' | 'cols'>, [r, c]: Cell): boolean {
  return r >= 0 && c >= 0 && r < plan.rows && c < plan.cols;
}

/** Yönden bağımsız eşitlik (aynı hücreler). */
export function sameLine(a: WordSearchLine, b: WordSearchLine): boolean {
  const eq = (x: Cell, y: Cell) => x[0] === y[0] && x[1] === y[1];
  return (eq(a.from, b.from) && eq(a.to, b.to)) || (eq(a.from, b.to) && eq(a.to, b.from));
}

export function spell(plan: WordSearchPlanV1, line: WordSearchLine): string {
  return lineCells(line)
    .map(([r, c]) => plan.letters[r]?.[c] ?? '')
    .join('')
    .toLocaleUpperCase('tr-TR');
}

function readCell(value: unknown): Cell | null {
  if (!Array.isArray(value) || value.length !== 2) return null;
  const r = readInt(value[0]);
  const c = readInt(value[1]);
  return r === null || c === null ? null : [r, c];
}

function readLine(value: unknown): WordSearchLine | null {
  if (!value || typeof value !== 'object') return null;
  const item = value as Record<string, unknown>;
  const from = readCell(item['from']);
  const to = readCell(item['to']);
  return from && to && isStraight(from, to) ? { from, to } : null;
}

/** Öğrenci cevabı: düz, ızgara içi çizgiler (en çok sınır kadar); başka tür / bozuksa boş. */
export function parseWordSearchAnswer(json: string | null | undefined, plan: WordSearchPlanV1 | null): WordSearchLine[] {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isWordSearchType(parsed['type']) || !Array.isArray(parsed['lines'])) return [];
    const lines = (parsed['lines'] as unknown[])
      .map(readLine)
      .filter((l): l is WordSearchLine => !!l && (!plan || (inGrid(plan, l.from) && inGrid(plan, l.to))));
    return plan ? lines.slice(0, maxWordSearchLines(plan.words.length)) : lines;
  } catch {
    return [];
  }
}

/** Çözüm: kelime → çizgi; bozuk / boşsa boş. */
export function parseWordSearchSolution(
  json: string | null | undefined,
  plan?: WordSearchPlanV1 | null
): Record<string, WordSearchLine> {
  const out = Object.create(null) as Record<string, WordSearchLine>;
  if (!json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const lines = parsed && typeof parsed === 'object' ? parsed['lines'] : null;
    if (!lines || typeof lines !== 'object' || Array.isArray(lines)) return out;
    const ids = plan ? new Set(plan.words.map((w) => w.id)) : null;
    for (const [id, raw] of Object.entries(lines as Record<string, unknown>)) {
      const line = readLine(raw);
      if (!isValidInteractionId(id) || !line || (ids && !ids.has(id))) continue;
      if (plan && !(inGrid(plan, line.from) && inGrid(plan, line.to))) continue;
      out[id] = line;
    }
    return out;
  } catch {
    return out;
  }
}

export function buildWordSearchPayload(lines: readonly WordSearchLine[]): string {
  return JSON.stringify({
    version: 1,
    type: WORD_SEARCH,
    lines: lines.map((l) => ({ from: [...l.from], to: [...l.to] })),
  });
}

/** Çizgisiz cevap: sunucu boş cevap sayar. Başka tür false. */
export function isBlankWordSearchPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isWordSearchType(parsed['type'])) return false;
    return !Array.isArray(parsed['lines']) || parsed['lines'].length === 0;
  } catch {
    return false;
  }
}

/** Kontrol geri bildirimi; bozuksa null. */
export function parseWordSearchFeedback(json: string | null | undefined): WordSearchFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    const rows = (parsed['rows'] as unknown[]).flatMap((raw) => {
      if (!raw || typeof raw !== 'object') return [];
      const { id, correct } = raw as Record<string, unknown>;
      return typeof id === 'string' && typeof correct === 'boolean' ? [{ id, correct }] : [];
    });
    const lines = Array.isArray(parsed['lines']) ? (parsed['lines'] as unknown[]).map((x) => x === true) : [];
    return { rows, lines };
  } catch {
    return null;
  }
}
