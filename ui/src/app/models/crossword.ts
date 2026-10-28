/**
 * Issue #586: çengel bulmaca etkileşimi (`InteractionType = crossword`, bileşen G).
 *
 * Plan: ızgara boyutu + ipuçları (yön, başlangıç hücresi, uzunluk, numara, metin); beyaz hücreler ipuçlarının birleşimidir.
 * Harfler planda yoktur, çözümde (`{"answers":{"k1":"KALP"}}`) durur ve öğrenciye gitmez (#570). Öğrenci payload'u satır
 * başına `cols` karakterlik ızgara (`{"grid":["KA  ",...]}`, boş hücre boşluk). Kontrol ipucu başına doğru/yanlış söyler
 * (backend `CrosswordGrader`); harfler ve yanlış hücre geri bildirimde yoktur. Puansız.
 */
import { cleanLabel, isInteractionType, isValidInteractionId } from './interaction-ids';

export const CROSSWORD = 'crossword' as const;

export type CrosswordDir = 'across' | 'down';

/** Backend `CrosswordGrader` sınırlarıyla aynı. */
export const CROSSWORD_LIMITS = {
  minSize: 2,
  maxSize: 20,
  maxClues: 80,
  minLength: 2,
  maxClueText: 300,
  maxNumber: 999,
} as const;

export interface CrosswordClue {
  id: string;
  dir: CrosswordDir;
  row: number;
  col: number;
  length: number;
  number: number | null;
  text: string | null;
}

export interface CrosswordPlanV1 {
  version: 1;
  type: typeof CROSSWORD;
  rows: number;
  cols: number;
  clues: CrosswordClue[];
}

export interface CrosswordFeedback {
  rows: { id: string; correct: boolean }[];
}

/** Izgara: `grid[r][c]` tek karakter, boş hücre `' '`. */
export type CrosswordGrid = string[][];

export function isCrosswordType(type: unknown): boolean {
  return isInteractionType(type, CROSSWORD);
}

function readInt(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) ? value : null;
}

export function cellKey(row: number, col: number): string {
  return `${row},${col}`;
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / boyut geçersizse null. Izgaraya sığmayan ipucu atlanır. */
export function parseCrosswordPlan(json: string | null | undefined): CrosswordPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isCrosswordType(parsed['type'])) return null;
    const rows = readInt(parsed['rows']);
    const cols = readInt(parsed['cols']);
    const { minSize, maxSize } = CROSSWORD_LIMITS;
    if (rows === null || cols === null || rows < minSize || rows > maxSize || cols < minSize || cols > maxSize) return null;

    const seen = new Set<string>();
    const clues: CrosswordClue[] = [];
    for (const raw of Array.isArray(parsed['clues']) ? (parsed['clues'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const item = raw as Record<string, unknown>;
      const id = item['id'];
      const dir = item['dir'];
      const row = readInt(item['row']);
      const col = readInt(item['col']);
      const length = readInt(item['length']);
      if (!isValidInteractionId(id) || seen.has(id) || (dir !== 'across' && dir !== 'down')) continue;
      if (row === null || col === null || length === null || row < 0 || col < 0 || length < CROSSWORD_LIMITS.minLength) continue;
      if (row >= rows || col >= cols || (dir === 'across' ? col + length > cols : row + length > rows)) continue;
      const number = readInt(item['number']);
      seen.add(id);
      clues.push({
        id,
        dir,
        row,
        col,
        length,
        number: number !== null && number >= 1 && number <= CROSSWORD_LIMITS.maxNumber ? number : null,
        text: cleanLabel(item['text']),
      });
    }
    return { version: 1, type: CROSSWORD, rows, cols, clues };
  } catch {
    return null;
  }
}

export function clueCells(clue: Pick<CrosswordClue, 'dir' | 'row' | 'col' | 'length'>): [number, number][] {
  return Array.from({ length: clue.length }, (_, i) =>
    clue.dir === 'across' ? [clue.row, clue.col + i] : [clue.row + i, clue.col]
  );
}

/** Beyaz hücreler (`"r,c"`). */
export function whiteCells(plan: CrosswordPlanV1): Set<string> {
  const out = new Set<string>();
  for (const clue of plan.clues) for (const [r, c] of clueCells(clue)) out.add(cellKey(r, c));
  return out;
}

/**
 * İpucunun görünen numarası: planda varsa o; yoksa başlangıç hücreleri satır satır sırayla 1'den numaralanır (aynı hücreden
 * başlayan ipuçları aynı numarayı alır, planda numaralı hücrelerin numarası atlanmaz ama tekrar da kullanılmaz).
 */
export function clueNumbers(plan: CrosswordPlanV1): Map<string, number> {
  const out = new Map<string, number>();
  const byCell = new Map<string, number>();
  for (const clue of plan.clues) if (clue.number !== null) byCell.set(cellKey(clue.row, clue.col), clue.number);
  const used = new Set(byCell.values());
  let next = 1;
  const starts = [...plan.clues].sort((a, b) => a.row - b.row || a.col - b.col);
  for (const clue of starts) {
    const key = cellKey(clue.row, clue.col);
    if (!byCell.has(key)) {
      while (used.has(next)) next++;
      byCell.set(key, next);
      used.add(next);
    }
  }
  for (const clue of plan.clues) out.set(clue.id, clue.number ?? byCell.get(cellKey(clue.row, clue.col))!);
  return out;
}

/** Hücre → numara (ipucu başlangıçları). */
export function cellNumbers(plan: CrosswordPlanV1): Map<string, number> {
  const numbers = clueNumbers(plan);
  const out = new Map<string, number>();
  for (const clue of plan.clues) {
    const key = cellKey(clue.row, clue.col);
    if (!out.has(key)) out.set(key, numbers.get(clue.id)!);
  }
  return out;
}

/** Boş ızgara. */
export function emptyGrid(plan: Pick<CrosswordPlanV1, 'rows' | 'cols'>): CrosswordGrid {
  return Array.from({ length: plan.rows }, () => Array.from({ length: plan.cols }, () => ' '));
}

/** Hücreye yazılabilir karakter (harf / rakam). */
export function isCellChar(ch: string): boolean {
  return /^[\p{L}\p{N}]$/u.test(ch);
}

/** Klavye girdisinden hücre karakteri: son harf/rakam, tr-TR büyük harf; yoksa boş. */
export function toCellChar(input: string | null | undefined): string {
  const chars = [...(input ?? '')].filter(isCellChar);
  const last = chars[chars.length - 1];
  return last ? last.toLocaleUpperCase('tr-TR') : '';
}

/** Çözüm JSON'u: ipucu → cevap (büyük harf). Bozuk / boşsa boş; plana göre uzunluğu tutmayan atlanır. */
export function parseCrosswordSolution(
  json: string | null | undefined,
  plan?: CrosswordPlanV1 | null
): Record<string, string> {
  const out = Object.create(null) as Record<string, string>;
  if (!json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const answers = parsed && typeof parsed === 'object' ? parsed['answers'] : null;
    if (!answers || typeof answers !== 'object' || Array.isArray(answers)) return out;
    const lengths = plan ? new Map(plan.clues.map((c) => [c.id, c.length])) : null;
    for (const [id, word] of Object.entries(answers as Record<string, unknown>)) {
      if (!isValidInteractionId(id) || typeof word !== 'string') continue;
      if (lengths && lengths.get(id) !== [...word].length) continue;
      out[id] = word.toLocaleUpperCase('tr-TR');
    }
    return out;
  } catch {
    return out;
  }
}

/** Çözümden dolu ızgara (çözüm gösteriminde). */
export function solutionGrid(plan: CrosswordPlanV1, answers: Record<string, string>): CrosswordGrid {
  const grid = emptyGrid(plan);
  for (const clue of plan.clues) {
    if (!Object.hasOwn(answers, clue.id)) continue;
    const letters = [...answers[clue.id]];
    clueCells(clue).forEach(([r, c], i) => {
      if (letters[i]) grid[r][c] = letters[i];
    });
  }
  return grid;
}

/** Öğrenci cevabı (`{"type":"crossword","grid":[...]}`) → plan boyutunda ızgara; siyah hücre / geçersiz karakter boş. */
export function parseCrosswordAnswer(json: string | null | undefined, plan: CrosswordPlanV1 | null): CrosswordGrid {
  if (!plan) return [];
  const grid = emptyGrid(plan);
  if (!json) return grid;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isCrosswordType(parsed['type']) || !Array.isArray(parsed['grid'])) return grid;
    const white = whiteCells(plan);
    (parsed['grid'] as unknown[]).slice(0, plan.rows).forEach((line, r) => {
      if (typeof line !== 'string') return;
      [...line].slice(0, plan.cols).forEach((ch, c) => {
        if (white.has(cellKey(r, c)) && isCellChar(ch)) grid[r][c] = ch.toLocaleUpperCase('tr-TR');
      });
    });
    return grid;
  } catch {
    return grid;
  }
}

export function buildCrosswordPayload(grid: CrosswordGrid): string {
  return JSON.stringify({ version: 1, type: CROSSWORD, grid: grid.map((row) => row.join('')) });
}

export function isEmptyCrosswordGrid(grid: CrosswordGrid): boolean {
  return grid.every((row) => row.every((ch) => ch === ' '));
}

/** İpucunun öğrencinin ızgarasındaki kelimesi (boş hücre `' '`). */
export function clueWord(grid: CrosswordGrid, clue: CrosswordClue): string {
  return clueCells(clue)
    .map(([r, c]) => grid[r]?.[c] ?? ' ')
    .join('');
}

/** Harfsiz ızgara: sunucu boş cevap sayar, ekranlar da cevapsız saymalı. Başka tür false. */
export function isBlankCrosswordPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isCrosswordType(parsed['type'])) return false;
    const grid = parsed['grid'];
    return !Array.isArray(grid) || grid.every((line) => typeof line !== 'string' || line.trim() === '');
  } catch {
    return false;
  }
}

/** Kontrol geri bildirimi (`{"rows":[{id,correct}]}`); bozuksa null. */
export function parseCrosswordFeedback(json: string | null | undefined): CrosswordFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    const rows = (parsed['rows'] as unknown[]).flatMap((raw) => {
      if (!raw || typeof raw !== 'object') return [];
      const { id, correct } = raw as Record<string, unknown>;
      return typeof id === 'string' && typeof correct === 'boolean' ? [{ id, correct }] : [];
    });
    return { rows };
  } catch {
    return null;
  }
}
