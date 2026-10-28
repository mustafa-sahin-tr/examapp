/**
 * Issue #587: bölge seçme ve boyama (`InteractionType = regionPaint`, bileşen E). Dilim a: ızgara modu — "eş karelere ayrılmış
 * şeklin 2/5'ini siyaha, 0,35'ini maviye, %25'ini yeşile boyayalım".
 *
 * Plan: ızgara boyutu ve palet (sabit adlı renkler, `--paint-<ad>` token'ı; etiket isteğe bağlı). Çözüm (`{"counts":{"p1":40}}`,
 * renk başına hücre sayısı, yer serbest) öğrenciye gitmez (#570). Öğrenci hücreleri boyar (`{"grid":["..1..",...]}`; `.` boş,
 * `1`..`8` palet sırası); kontrol renk başına sayıya bakar (backend `RegionPaintGrader`). Puansız.
 *
 * Dilim b — bölge modu (`mode: "regions"`): görselin üstünde çokgen bölgeler (soru görseline göreli piksel köşeler). Çözüm
 * `{"fills":{"r1":"p1"}}` (boyanacak bölge → renk; yazılmayan bölge boş kalmalı). Öğrenci bölgeye dokunarak seçili rengi verir
 * (`{"fills":{...}}`); `style: "circle"` boyamayı daire içine alma olarak çizer. Kontrol hedef / fazla sayar, geri bildirim yalnız
 * boyalı bölgeleri taşır (backend `RegionPaintGrader`, `TextSelectGrader` kalıbı).
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const REGION_PAINT = 'regionPaint' as const;

/** Backend `RegionPaintGrader` sınırlarıyla aynı. */
export const REGION_PAINT_LIMITS = {
  minSize: 2,
  maxSize: 20,
  maxPalette: 8,
  maxLabel: 40,
  maxRegions: 100,
  minPoints: 3,
  maxPoints: 64,
  maxCoordinate: 100_000,
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
  /** Bölge modunda `reason`: `extra` (boyanmaması gerekirdi) | `color` (yanlış renk). */
  rows: { id: string; correct: boolean; reason?: RegionPaintReason }[];
  /** Yalnız bölge modunda. */
  found?: number;
  missing?: number;
  extra?: number;
  tooMany?: boolean;
}

export type RegionPaintReason = 'extra' | 'color';

export const REGION_PAINT_STYLES = ['fill', 'circle'] as const;

export type RegionPaintStyle = (typeof REGION_PAINT_STYLES)[number];

/** Köşe `[x, y]` (soru görseline göreli piksel). */
export type PaintPoint = [number, number];

export interface PaintRegion {
  id: string;
  points: PaintPoint[];
  label: string;
}

export interface RegionPaintRegionsPlanV1 {
  version: 1;
  type: typeof REGION_PAINT;
  mode: 'regions';
  style: RegionPaintStyle;
  palette: PaintEntry[];
  regions: PaintRegion[];
}

/** Bölge id'si → palet id'si. */
export type PaintFills = Record<string, string>;

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
    const palette = parsePalette(parsed['palette'], new Set<string>());
    if (palette.length === 0 || palette.length > maxPalette) return null;
    return { version: 1, type: REGION_PAINT, mode: 'grid', rows, cols, palette };
  } catch {
    return null;
  }
}

/** Paleti toleranslı okur (bozuk / yinelenen girdi atlanır); `ids` plan içi tekil id kümesi (bölgelerle paylaşılır). */
function parsePalette(value: unknown, ids: Set<string>): PaintEntry[] {
  const colors = new Set<string>();
  const palette: PaintEntry[] = [];
  for (const raw of Array.isArray(value) ? (value as unknown[]) : []) {
    if (!raw || typeof raw !== 'object') continue;
    const { id, color, label } = raw as Record<string, unknown>;
    if (!isValidInteractionId(id) || ids.has(id) || !isPaintColor(color) || colors.has(color)) continue;
    ids.add(id);
    colors.add(color);
    palette.push({ id, color, label: typeof label === 'string' ? label : '' });
  }
  return palette;
}

/** Çokgen alanı (köşe sırasından bağımsız, mutlak). */
export function polygonArea(points: readonly PaintPoint[]): number {
  let twice = 0;
  for (let i = 0; i < points.length; i++) {
    const [x1, y1] = points[i];
    const [x2, y2] = points[(i + 1) % points.length];
    twice += x1 * y2 - x2 * y1;
  }
  return Math.abs(twice) / 2;
}

/** Backend kuralı: 3..64 köşe, sonlu ve 0..100000 koordinatlar, alan ≥ 1. */
export function isValidPolygon(points: unknown): points is PaintPoint[] {
  const { minPoints, maxPoints, maxCoordinate } = REGION_PAINT_LIMITS;
  if (!Array.isArray(points) || points.length < minPoints || points.length > maxPoints) return false;
  const ok = points.every(
    (p) =>
      Array.isArray(p) &&
      p.length === 2 &&
      p.every((v) => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= maxCoordinate)
  );
  return ok && polygonArea(points as PaintPoint[]) >= 1;
}

/** Nokta çokgenin içinde mi (çift-tek kuralı). */
export function pointInPolygon(x: number, y: number, points: readonly PaintPoint[]): boolean {
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const [xi, yi] = points[i];
    const [xj, yj] = points[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

/** Köşelerin sınır kutusu. */
export function polygonBounds(points: readonly PaintPoint[]): { x: number; y: number; width: number; height: number } {
  const xs = points.map((p) => p[0]);
  const ys = points.map((p) => p[1]);
  const x = Math.min(...xs);
  const y = Math.min(...ys);
  return { x, y, width: Math.max(...xs) - x, height: Math.max(...ys) - y };
}

/** Bölge planını güvenli ayrıştırır; tür / mod farklı, palet ya da bölge boşsa null. Bozuk bölge atlanır. */
export function parseRegionPaintRegionsPlan(json: string | null | undefined): RegionPaintRegionsPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isRegionPaintType(parsed['type']) || parsed['mode'] !== 'regions') return null;
    const ids = new Set<string>();
    const palette = parsePalette(parsed['palette'], ids);
    if (palette.length === 0 || palette.length > REGION_PAINT_LIMITS.maxPalette) return null;
    const regions: PaintRegion[] = [];
    for (const raw of Array.isArray(parsed['regions']) ? (parsed['regions'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const { id, points, label } = raw as Record<string, unknown>;
      if (!isValidInteractionId(id) || ids.has(id) || !isValidPolygon(points)) continue;
      ids.add(id);
      regions.push({ id, points: points.map(([x, y]) => [x, y] as PaintPoint), label: typeof label === 'string' ? label : '' });
    }
    if (regions.length === 0 || regions.length > REGION_PAINT_LIMITS.maxRegions) return null;
    const style: RegionPaintStyle = parsed['style'] === 'circle' ? 'circle' : 'fill';
    return { version: 1, type: REGION_PAINT, mode: 'regions', style, palette, regions };
  } catch {
    return null;
  }
}

/** `fills` nesnesini okur; plan verilirse plandaki bölge / renkle sınırlar. Prototipsiz. */
function readFills(value: unknown, plan: RegionPaintRegionsPlanV1 | null): PaintFills {
  const out = Object.create(null) as PaintFills;
  if (!value || typeof value !== 'object' || Array.isArray(value)) return out;
  const regions = plan ? new Set(plan.regions.map((r) => r.id)) : null;
  const palette = plan ? new Set(plan.palette.map((p) => p.id)) : null;
  for (const [region, paint] of Object.entries(value as Record<string, unknown>)) {
    if (!isValidInteractionId(region) || !isValidInteractionId(paint)) continue;
    if ((regions && !regions.has(region)) || (palette && !palette.has(paint))) continue;
    out[region] = paint;
  }
  return out;
}

/** Öğrenci cevabı (bölge modu) → boyalı bölgeler; bozuk / başka türse boş. */
export function parseRegionPaintFills(json: string | null | undefined, plan: RegionPaintRegionsPlanV1 | null): PaintFills {
  if (!json) return Object.create(null) as PaintFills;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isRegionPaintType(parsed['type'])) return Object.create(null) as PaintFills;
    return readFills(parsed['fills'], plan);
  } catch {
    return Object.create(null) as PaintFills;
  }
}

/** Çözüm (bölge modu): hedef bölge → renk. */
export function parseRegionPaintFillsSolution(json: string | null | undefined, plan: RegionPaintRegionsPlanV1 | null): PaintFills {
  if (!json) return Object.create(null) as PaintFills;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    return parsed && typeof parsed === 'object' ? readFills(parsed['fills'], plan) : (Object.create(null) as PaintFills);
  } catch {
    return Object.create(null) as PaintFills;
  }
}

/** Payload (bölge modu): plan sırasıyla. */
export function buildRegionPaintFillsPayload(fills: PaintFills, plan?: RegionPaintRegionsPlanV1 | null): string {
  const ordered: PaintFills = {};
  const keys = plan ? plan.regions.map((r) => r.id).filter((id) => Object.hasOwn(fills, id)) : Object.keys(fills);
  for (const id of keys) ordered[id] = fills[id];
  return JSON.stringify({ version: 1, type: REGION_PAINT, fills: ordered });
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
    const fills = parsed['fills'];
    if (fills && typeof fills === 'object' && !Array.isArray(fills)) return Object.keys(fills).length === 0;
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
    const count = (v: unknown) => (Number.isInteger(v) && (v as number) >= 0 ? (v as number) : undefined);
    const feedback: RegionPaintFeedback = {
      rows: (parsed['rows'] as unknown[]).flatMap((raw) => {
        if (!raw || typeof raw !== 'object') return [];
        const { id, correct, reason } = raw as Record<string, unknown>;
        if (typeof id !== 'string' || typeof correct !== 'boolean') return [];
        return [reason === 'extra' || reason === 'color' ? { id, correct, reason } : { id, correct }];
      }),
    };
    const found = count(parsed['found']);
    if (found !== undefined) {
      feedback.found = found;
      feedback.missing = count(parsed['missing']) ?? 0;
      feedback.extra = count(parsed['extra']) ?? 0;
      if (parsed['tooMany'] === true) feedback.tooMany = true;
    }
    return feedback;
  } catch {
    return null;
  }
}
