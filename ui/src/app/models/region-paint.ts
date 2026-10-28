/**
 * Issue #587: bölge seçme ve boyama (`InteractionType = regionPaint`, bileşen E). Dilim a: ızgara modu — "eş karelere ayrılmış
 * şeklin 2/5'ini siyaha, 0,35'ini maviye, %25'ini yeşile boyayalım".
 *
 * Plan: ızgara boyutu ve palet (sabit adlı renkler, `--paint-<ad>` token'ı; etiket isteğe bağlı). Çözüm (`{"counts":{"p1":40}}`,
 * renk başına hücre sayısı, yer serbest) öğrenciye gitmez (#570). Öğrenci hücreleri boyar (`{"grid":["..1..",...]}`; `.` boş,
 * `1`..`8` palet sırası); kontrol renk başına sayıya bakar (backend `RegionPaintGrader`). Puansız.
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const REGION_PAINT = 'regionPaint' as const;

/** Backend `RegionPaintGrader` sınırlarıyla aynı. */
export const REGION_PAINT_LIMITS = {
  minSize: 2,
  maxSize: 20,
  maxPalette: 8,
  maxLabel: 40,
} as const;

export const PAINT_COLORS = ['red', 'orange', 'yellow', 'green', 'blue', 'purple', 'pink', 'brown', 'black', 'gray'] as const;

export type PaintColor = (typeof PAINT_COLORS)[number];

export const EMPTY_CELL = '.';

export interface PaintEntry {
  id: string;
  color: PaintColor;
  label: string;
}

export interface RegionPaintPlanV1 {
  version: 1;
  type: typeof REGION_PAINT;
  mode: 'grid';
  rows: number;
  cols: number;
  palette: PaintEntry[];
}

/** `cells[r][c]` = palet dizini (0 tabanlı) ya da null (boş). */
export type PaintCells = (number | null)[][];

export interface RegionPaintFeedback {
  rows: { id: string; correct: boolean }[];
}

export function isRegionPaintType(type: unknown): boolean {
  return isInteractionType(type, REGION_PAINT);
}

export function isPaintColor(value: unknown): value is PaintColor {
  return typeof value === 'string' && (PAINT_COLORS as readonly string[]).includes(value);
}

/** Palet renginin CSS değeri (styles.scss token'ı). */
export function paintCss(color: PaintColor): string {
  return `var(--paint-${color})`;
}

/** Plan JSON'unu güvenli ayrıştırır; tür / mod farklı, boyut sınır dışı ya da palet boşsa null. */
export function parseRegionPaintPlan(json: string | null | undefined): RegionPaintPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isRegionPaintType(parsed['type']) || parsed['mode'] !== 'grid') return null;
    const { minSize, maxSize, maxPalette } = REGION_PAINT_LIMITS;
    const size = (v: unknown) => (Number.isInteger(v) && (v as number) >= minSize && (v as number) <= maxSize ? (v as number) : null);
    const rows = size(parsed['rows']);
    const cols = size(parsed['cols']);
    if (rows === null || cols === null) return null;
    const ids = new Set<string>();
    const colors = new Set<string>();
    const palette: PaintEntry[] = [];
    for (const raw of Array.isArray(parsed['palette']) ? (parsed['palette'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const { id, color, label } = raw as Record<string, unknown>;
      if (!isValidInteractionId(id) || ids.has(id) || !isPaintColor(color) || colors.has(color)) continue;
      ids.add(id);
      colors.add(color);
      palette.push({ id, color, label: typeof label === 'string' ? label : '' });
    }
    if (palette.length === 0 || palette.length > maxPalette) return null;
    return { version: 1, type: REGION_PAINT, mode: 'grid', rows, cols, palette };
  } catch {
    return null;
  }
}

export function emptyCells(plan: Pick<RegionPaintPlanV1, 'rows' | 'cols'>): PaintCells {
  return Array.from({ length: plan.rows }, () => Array.from({ length: plan.cols }, () => null));
}

/** Öğrenci cevabı → hücreler; boyutu tutmayan satır / palet dışı karakter boş sayılır. Başka tür / bozuksa boş ızgara. */
export function parseRegionPaintAnswer(json: string | null | undefined, plan: RegionPaintPlanV1 | null): PaintCells {
  if (!plan) return [];
  const cells = emptyCells(plan);
  if (!json) return cells;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isRegionPaintType(parsed['type']) || !Array.isArray(parsed['grid'])) return cells;
    (parsed['grid'] as unknown[]).slice(0, plan.rows).forEach((row, r) => {
      if (typeof row !== 'string') return;
      for (let c = 0; c < Math.min(row.length, plan.cols); c++) {
        const index = row.charCodeAt(c) - 49; // '1' → 0
        if (index >= 0 && index < plan.palette.length) cells[r][c] = index;
      }
    });
    return cells;
  } catch {
    return cells;
  }
}

/** Payload: satır başına `cols` karakter. */
export function buildRegionPaintPayload(cells: PaintCells): string {
  const grid = cells.map((row) => row.map((v) => (v === null ? EMPTY_CELL : String(v + 1))).join(''));
  return JSON.stringify({ version: 1, type: REGION_PAINT, grid });
}

/** Palet dizini başına boyanan hücre sayısı. */
export function countPainted(cells: PaintCells, paletteSize: number): number[] {
  const counts = Array.from({ length: paletteSize }, () => 0);
  for (const row of cells) for (const v of row) if (v !== null && v < paletteSize) counts[v]++;
  return counts;
}

export function isEmptyPaint(cells: PaintCells): boolean {
  return cells.every((row) => row.every((v) => v === null));
}

/** Çözüm JSON'u: palet id'si → hücre sayısı; plana uymayan / negatif atlanır. */
export function parseRegionPaintSolution(json: string | null | undefined, plan?: RegionPaintPlanV1 | null): Record<string, number> {
  const out = Object.create(null) as Record<string, number>;
  if (!json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const counts = parsed && typeof parsed === 'object' ? parsed['counts'] : null;
    if (!counts || typeof counts !== 'object' || Array.isArray(counts)) return out;
    for (const [id, value] of Object.entries(counts as Record<string, unknown>)) {
      if (!isValidInteractionId(id) || !Number.isInteger(value) || (value as number) < 0) continue;
      if (plan && !plan.palette.some((p) => p.id === id)) continue;
      out[id] = value as number;
    }
    return out;
  } catch {
    return out;
  }
}

/**
 * Çözüm gösterimi: renkler palet sırasıyla, satır öncelikli doldurulur (yer serbest olduğundan tek örnek). Toplam hücreyi aşan kısım
 * kesilir.
 */
export function solutionCells(plan: RegionPaintPlanV1, counts: Record<string, number>): PaintCells {
  const cells = emptyCells(plan);
  let at = 0;
  plan.palette.forEach((entry, index) => {
    const n = Object.hasOwn(counts, entry.id) ? counts[entry.id] : 0;
    for (let k = 0; k < n && at < plan.rows * plan.cols; k++, at++) cells[Math.floor(at / plan.cols)][at % plan.cols] = index;
  });
  return cells;
}

/** Boyasız cevap: sunucu boş cevap sayar. Başka tür false. */
export function isBlankRegionPaintPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isRegionPaintType(parsed['type'])) return false;
    const grid = parsed['grid'];
    return !Array.isArray(grid) || grid.every((row) => typeof row !== 'string' || /^\.*$/.test(row));
  } catch {
    return false;
  }
}

export function parseRegionPaintFeedback(json: string | null | undefined): RegionPaintFeedback | null {
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
