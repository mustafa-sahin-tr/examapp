/**
 * Issue #588 (dilim c): kareli / noktalı zeminde kural tabanlı çizim (`InteractionType = gridDraw`, bileşen F ızgara tuvali ailesi).
 * "Çevre uzunluğu 14 cm olan dikdörtgenleri kareli zemine çiziniz", "alanı 20 m² olan bütün dikdörtgenler".
 *
 * Plan: zemin `rows`×`cols` birim kare, `grid` `square` | `dot` (yalnız görünüm), `shape` `rectangle` | `square`. Kural çözümdedir
 * (`{"area"?,"perimeter"?,"all"}`) ve öğrenciye gitmez (#570); yönergede yazar. Öğrenci dikdörtgen çizer
 * (`{"shapes":[{row,col,width,height}]}`); yer ve yön serbest, kurala uyan her boyut doğru (backend `GridDrawGrader`). Puansız.
 */
import { isInteractionType } from './interaction-ids';

export const GRID_DRAW = 'gridDraw' as const;

/** Backend `GridDrawGrader` sınırlarıyla aynı. */
export const GRID_DRAW_LIMITS = {
  minSize: 2,
  maxSize: 30,
  maxShapes: 30,
  maxTargets: 12,
} as const;

export type GridDrawBackground = 'square' | 'dot';
export type GridDrawShapeKind = 'rectangle' | 'square';

export interface GridDrawPlanV1 {
  version: 1;
  type: typeof GRID_DRAW;
  rows: number;
  cols: number;
  grid: GridDrawBackground;
  shape: GridDrawShapeKind;
}

export interface GridDrawRule {
  area: number | null;
  perimeter: number | null;
  all: boolean;
}

export interface GridRect {
  row: number;
  col: number;
  width: number;
  height: number;
}

/** Döndürmeden bağımsız boyut: kısa ≤ uzun. */
export interface GridSize {
  short: number;
  long: number;
}

export type GridDrawShapeStatus = 'ok' | 'wrong' | 'duplicate';

export interface GridDrawFeedback {
  /** Çizim sırasıyla. */
  shapes: GridDrawShapeStatus[];
  found: number;
  missing: number;
  extra: number;
}

export function isGridDrawType(type: unknown): boolean {
  return isInteractionType(type, GRID_DRAW);
}

const isSize = (value: unknown): value is number =>
  Number.isInteger(value) && (value as number) >= GRID_DRAW_LIMITS.minSize && (value as number) <= GRID_DRAW_LIMITS.maxSize;

/** Plan JSON'unu güvenli ayrıştırır; tür farklı ya da zemin sınır dışıysa null. */
export function parseGridDrawPlan(json: string | null | undefined): GridDrawPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isGridDrawType(parsed['type'])) return null;
    const { rows, cols } = parsed;
    if (!isSize(rows) || !isSize(cols)) return null;
    return {
      version: 1,
      type: GRID_DRAW,
      rows,
      cols,
      grid: parsed['grid'] === 'dot' ? 'dot' : 'square',
      shape: parsed['shape'] === 'square' ? 'square' : 'rectangle',
    };
  } catch {
    return null;
  }
}

/** Çözüm JSON'u → kural; alan da çevre de yoksa null. */
export function parseGridDrawSolution(json: string | null | undefined): GridDrawRule | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object') return null;
    const positive = (v: unknown) => (Number.isInteger(v) && (v as number) > 0 ? (v as number) : null);
    const area = positive(parsed['area']);
    const perimeter = positive(parsed['perimeter']);
    if (area === null && perimeter === null) return null;
    return { area, perimeter, all: parsed['all'] === true };
  } catch {
    return null;
  }
}

export function sizeOf(rect: Pick<GridRect, 'width' | 'height'>): GridSize {
  return rect.width <= rect.height ? { short: rect.width, long: rect.height } : { short: rect.height, long: rect.width };
}

export const sizeKey = (size: GridSize): string => `${size.short}x${size.long}`;

/** Kurala uyan ve zemine (döndürülerek de) sığan bütün boyutlar, küçükten büyüğe (backend `Targets` ile aynı). */
export function gridDrawTargets(plan: Pick<GridDrawPlanV1, 'rows' | 'cols' | 'shape'>, rule: GridDrawRule): GridSize[] {
  const out: GridSize[] = [];
  const limit = Math.max(plan.rows, plan.cols);
  for (let a = 1; a <= limit; a++) {
    for (let b = a; b <= limit; b++) {
      if (plan.shape === 'square' && a !== b) continue;
      if (rule.area !== null && a * b !== rule.area) continue;
      if (rule.perimeter !== null && 2 * (a + b) !== rule.perimeter) continue;
      if ((a <= plan.cols && b <= plan.rows) || (b <= plan.cols && a <= plan.rows)) out.push({ short: a, long: b });
    }
  }
  return out;
}

export function fitsBoard(rect: GridRect, plan: Pick<GridDrawPlanV1, 'rows' | 'cols'>): boolean {
  return (
    [rect.row, rect.col, rect.width, rect.height].every(Number.isInteger) &&
    rect.row >= 0 &&
    rect.col >= 0 &&
    rect.width >= 1 &&
    rect.height >= 1 &&
    rect.row + rect.height <= plan.rows &&
    rect.col + rect.width <= plan.cols
  );
}

export function overlaps(a: GridRect, b: GridRect): boolean {
  return a.col < b.col + b.width && b.col < a.col + a.width && a.row < b.row + b.height && b.row < a.row + a.height;
}

/** Öğrenci cevabı → dikdörtgenler; zemine sığmayan atlanır, en çok 30. Başka tür / bozuksa boş. */
export function parseGridDrawAnswer(json: string | null | undefined, plan: GridDrawPlanV1 | null): GridRect[] {
  if (!plan || !json) return [];
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isGridDrawType(parsed['type']) || !Array.isArray(parsed['shapes'])) return [];
    return (parsed['shapes'] as unknown[])
      .flatMap((raw) => {
        if (!raw || typeof raw !== 'object') return [];
        const { row, col, width, height } = raw as Record<string, number>;
        const rect = { row, col, width, height };
        return fitsBoard(rect, plan) ? [rect] : [];
      })
      .slice(0, GRID_DRAW_LIMITS.maxShapes);
  } catch {
    return [];
  }
}

export function buildGridDrawPayload(shapes: GridRect[]): string {
  return JSON.stringify({
    version: 1,
    type: GRID_DRAW,
    shapes: shapes.map(({ row, col, width, height }) => ({ row, col, width, height })),
  });
}

/** Çizimsiz cevap: sunucu boş cevap sayar. Başka tür false. */
export function isBlankGridDrawPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isGridDrawType(parsed['type'])) return false;
    return !Array.isArray(parsed['shapes']) || parsed['shapes'].length === 0;
  } catch {
    return false;
  }
}

export function parseGridDrawFeedback(json: string | null | undefined): GridDrawFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['shapes'])) return null;
    const count = (v: unknown) => (Number.isInteger(v) && (v as number) >= 0 ? (v as number) : 0);
    return {
      shapes: (parsed['shapes'] as unknown[]).map((s) => (s === 'ok' || s === 'duplicate' ? s : 'wrong')),
      found: count(parsed['found']),
      missing: count(parsed['missing']),
      extra: count(parsed['extra']),
    };
  } catch {
    return null;
  }
}

/**
 * Çözüm gösterimi için örnek yerleşim: boyutları (aralarında bir kare boşlukla) satır öncelikli ilk boş yere koyar; önce yatay yönü
 * bütün zeminde dener, sığmazsa dikey. Sığmayan boyutlar yerleşmez (yalnız listede görünür).
 */
export function exampleLayout(plan: Pick<GridDrawPlanV1, 'rows' | 'cols'>, sizes: GridSize[]): GridRect[] {
  const placed: GridRect[] = [];
  const free = (rect: GridRect) => fitsBoard(rect, plan) && placed.every((p) => !overlaps(pad(p), rect));
  for (const size of [...sizes].sort((a, b) => b.short - a.short || b.long - a.long)) {
    const options = [
      { width: size.long, height: size.short },
      { width: size.short, height: size.long },
    ];
    let done = false;
    for (const o of options) {
      for (let row = 0; row < plan.rows && !done; row++) {
        for (let col = 0; col < plan.cols && !done; col++) {
          const rect = { row, col, ...o };
          if (free(rect)) {
            placed.push(rect);
            done = true;
          }
        }
      }
      if (done) break;
    }
  }
  return placed;
}

/** Bir kare boşluk payı (yan yana çizimler ayırt edilsin). */
function pad(rect: GridRect): GridRect {
  return { row: rect.row - 1, col: rect.col - 1, width: rect.width + 2, height: rect.height + 2 };
}
