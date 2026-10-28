/**
 * Issue #587 dilim b: görsel üzerinde bölge boyama / daire içine alma (`regionPaint`, `mode: "regions"`) yazarlığının saf kısmı.
 * Kurallar backend `RegionPaintGrader.ValidateDefinition` ile aynı: palet 1..8 tekil izinli renk (etiket ≤ 40), 1..100 çokgen bölge
 * (3..64 köşe, alan ≥ 1; köşeler soru bölgesine göreli piksel), çözüm `{"fills":{"r1":"p1"}}` (en az bir hedef bölge; yazılmayan
 * bölge boş kalmalı). Bölgeler tuvalde dikdörtgen / elips sürükleyerek ya da köşe köşe tıklayarak (çokgen) çizilir; tuvalde bölgeye
 * tıklamak fırçadaki rengi çözüm olarak verir / kaldırır.
 */
import { InteractionBox } from '../../models/interaction-ids';
import {
  PAINT_COLORS,
  PaintColor,
  PaintEntry,
  PaintFills,
  PaintPoint,
  PaintRegion,
  REGION_PAINT,
  REGION_PAINT_LIMITS,
  RegionPaintRegionsPlanV1,
  RegionPaintStyle,
  isValidPolygon,
  parseRegionPaintFillsSolution,
  pointInPolygon,
  polygonArea,
} from '../../models/region-paint';

/** Sürüklemeyle çizilen bölgenin biçimi. */
export type PaintDrawShape = 'rect' | 'ellipse';

/** Elips bölge bu kadar köşeli çokgene çevrilir. */
export const ELLIPSE_POINTS = 24;

/** Çokgen çiziminde ilk köşeye bu kadar (görsel pikseli) yakın tık çokgeni kapatır. */
export const CLOSE_DISTANCE = 10;

export interface PaintRegionsAuthoringState {
  style: RegionPaintStyle;
  palette: PaintEntry[];
  regions: PaintRegion[];
  /** Çözüm: hedef bölge → palet id'si. */
  fills: PaintFills;
  /** Tuvalde bölgeye tıklayınca verilecek renk (palet id'si); null = boş bırak (hedeften çıkar). */
  brush: string | null;
  shape: PaintDrawShape;
  /** Çizilmekte olan çokgenin köşeleri (soru bölgesine göreli). */
  pending: PaintPoint[];
  selectedRegionId: string | null;
}

export type PaintRegionsIssueCode = 'noRegions' | 'noTargets' | 'tooManyRegions' | 'labelTooLong' | 'pendingPolygon';

export interface PaintRegionsInspector {
  style: RegionPaintStyle;
  palette: Array<PaintEntry & { targets: number }>;
  regions: Array<{ id: string; no: number; label: string; fill: string | null }>;
  brush: string | null;
  shape: PaintDrawShape;
  pendingCount: number;
  selectedRegionId: string | null;
  freeColors: PaintColor[];
  limits: typeof REGION_PAINT_LIMITS;
  issues: PaintRegionsIssueCode[];
}

export function createPaintRegionsState(): PaintRegionsAuthoringState {
  return {
    style: 'fill',
    palette: [{ id: 'p1', color: 'blue', label: '' }],
    regions: [],
    fills: {},
    brush: 'p1',
    shape: 'rect',
    pending: [],
    selectedRegionId: null,
  };
}

/** Kayıtlı / taslak plan (+ çözüm) → yazarlık durumu. */
export function paintRegionsStateFromPlan(
  plan: RegionPaintRegionsPlanV1,
  solutionJson?: string | null
): PaintRegionsAuthoringState {
  return {
    style: plan.style,
    palette: plan.palette.map((p) => ({ ...p })),
    regions: plan.regions.map((r) => ({ ...r, points: r.points.map(([x, y]) => [x, y] as PaintPoint) })),
    fills: { ...parseRegionPaintFillsSolution(solutionJson, plan) },
    brush: plan.palette[0]?.id ?? null,
    shape: 'rect',
    pending: [],
    selectedRegionId: null,
  };
}

function nextId(prefix: 'p' | 'r', used: Iterable<string>): string {
  const taken = new Set(used);
  let n = 1;
  while (taken.has(`${prefix}${n}`)) n++;
  return `${prefix}${n}`;
}

const round = (v: number) => Math.round(v * 10) / 10;

/** Dikdörtgen kutu → 4 köşe. */
export function rectPoints(box: InteractionBox): PaintPoint[] {
  const x2 = box.x + box.width;
  const y2 = box.y + box.height;
  return [
    [round(box.x), round(box.y)],
    [round(x2), round(box.y)],
    [round(x2), round(y2)],
    [round(box.x), round(y2)],
  ];
}

/** Kutuya çizilmiş elips → {@link ELLIPSE_POINTS} köşeli çokgen (koordinatlar 0'ın altına inmez). */
export function ellipsePoints(box: InteractionBox): PaintPoint[] {
  const cx = box.x + box.width / 2;
  const cy = box.y + box.height / 2;
  return Array.from({ length: ELLIPSE_POINTS }, (_, i) => {
    const a = (2 * Math.PI * i) / ELLIPSE_POINTS;
    return [round(Math.max(0, cx + (box.width / 2) * Math.cos(a))), round(Math.max(0, cy + (box.height / 2) * Math.sin(a)))];
  });
}

/** Yeni bölge ekler (geçersiz çokgen ya da sınır doluysa durum değişmez); fırçada renk varsa bölge o renkle hedef olur. */
export function addPaintRegion(state: PaintRegionsAuthoringState, points: PaintPoint[]): PaintRegionsAuthoringState {
  if (!isValidPolygon(points) || state.regions.length >= REGION_PAINT_LIMITS.maxRegions) return state;
  const id = nextId('r', [...state.regions.map((r) => r.id), ...state.palette.map((p) => p.id)]);
  const fills = { ...state.fills };
  if (state.brush && state.palette.some((p) => p.id === state.brush)) fills[id] = state.brush;
  return { ...state, regions: [...state.regions, { id, points, label: '' }], fills, selectedRegionId: id };
}

export function removePaintRegion(state: PaintRegionsAuthoringState, id: string): PaintRegionsAuthoringState {
  const fills = { ...state.fills };
  delete fills[id];
  return {
    ...state,
    regions: state.regions.filter((r) => r.id !== id),
    fills,
    selectedRegionId: state.selectedRegionId === id ? null : state.selectedRegionId,
  };
}

export function clearPaintRegions(state: PaintRegionsAuthoringState): PaintRegionsAuthoringState {
  return { ...state, regions: [], fills: {}, pending: [], selectedRegionId: null };
}

export function setPaintRegionLabel(state: PaintRegionsAuthoringState, id: string, label: string): PaintRegionsAuthoringState {
  return { ...state, regions: state.regions.map((r) => (r.id === id ? { ...r, label } : r)) };
}

/** Bölgenin çözümü: palet id'si ya da null (boş kalmalı). */
export function setPaintRegionFill(state: PaintRegionsAuthoringState, id: string, paint: string | null): PaintRegionsAuthoringState {
  if (!state.regions.some((r) => r.id === id)) return state;
  const fills = { ...state.fills };
  if (paint && state.palette.some((p) => p.id === paint)) fills[id] = paint;
  else delete fills[id];
  return { ...state, fills, selectedRegionId: id };
}

/** Tuvalde bölgeye tık: fırçadaki renk verilir; zaten o renkse (ya da fırça boşsa) hedeften çıkar. */
export function togglePaintRegionFill(state: PaintRegionsAuthoringState, id: string): PaintRegionsAuthoringState {
  const current = Object.hasOwn(state.fills, id) ? state.fills[id] : null;
  return setPaintRegionFill(state, id, state.brush && current !== state.brush ? state.brush : null);
}

/** Soru bölgesine göreli noktayı içeren bölge (iç içe bölgelerde en küçüğü). */
export function paintRegionAt(state: PaintRegionsAuthoringState, x: number, y: number): PaintRegion | null {
  const hits = state.regions.filter((r) => pointInPolygon(x, y, r.points));
  if (hits.length === 0) return null;
  return hits.reduce((a, b) => (polygonArea(b.points) < polygonArea(a.points) ? b : a));
}

export function setPaintStyle(state: PaintRegionsAuthoringState, style: RegionPaintStyle): PaintRegionsAuthoringState {
  return { ...state, style };
}

export function setPaintBrush(state: PaintRegionsAuthoringState, brush: string | null): PaintRegionsAuthoringState {
  return { ...state, brush: brush && state.palette.some((p) => p.id === brush) ? brush : null };
}

export function setPaintShape(state: PaintRegionsAuthoringState, shape: PaintDrawShape): PaintRegionsAuthoringState {
  return { ...state, shape };
}

/** Kullanılmamış ilk renkle palete renk ekler (en çok 8); yeni renk fırçaya alınır. */
export function addPaintColor(state: PaintRegionsAuthoringState, color?: PaintColor): PaintRegionsAuthoringState {
  const used = new Set(state.palette.map((p) => p.color));
  const pick = color && !used.has(color) ? color : PAINT_COLORS.find((c) => !used.has(c));
  if (!pick || state.palette.length >= REGION_PAINT_LIMITS.maxPalette) return state;
  const id = nextId('p', [...state.palette.map((p) => p.id), ...state.regions.map((r) => r.id)]);
  return { ...state, palette: [...state.palette, { id, color: pick, label: '' }], brush: id };
}

/** Rengi paletten çıkarır (son renk çıkmaz); o renkli hedefler boş kalmalıya döner. */
export function removePaintColor(state: PaintRegionsAuthoringState, id: string): PaintRegionsAuthoringState {
  if (state.palette.length <= 1 || !state.palette.some((p) => p.id === id)) return state;
  const palette = state.palette.filter((p) => p.id !== id);
  const fills: PaintFills = {};
  for (const [region, paint] of Object.entries(state.fills)) if (paint !== id) fills[region] = paint;
  return { ...state, palette, fills, brush: state.brush === id ? palette[0].id : state.brush };
}

export function setPaintColor(state: PaintRegionsAuthoringState, id: string, color: PaintColor): PaintRegionsAuthoringState {
  if (state.palette.some((p) => p.id !== id && p.color === color)) return state;
  return { ...state, palette: state.palette.map((p) => (p.id === id ? { ...p, color } : p)) };
}

export function setPaintLabel(state: PaintRegionsAuthoringState, id: string, label: string): PaintRegionsAuthoringState {
  return { ...state, palette: state.palette.map((p) => (p.id === id ? { ...p, label } : p)) };
}

/**
 * Çokgen çizimi: köşe ekler. En az 3 köşe varken ilk köşeye {@link CLOSE_DISTANCE} içinde tık çokgeni kapatır (bölge olur).
 * En çok 64 köşe; dolunca kapatılır.
 */
export function addPendingPoint(state: PaintRegionsAuthoringState, point: PaintPoint): PaintRegionsAuthoringState {
  const pending = state.pending;
  if (pending.length >= REGION_PAINT_LIMITS.minPoints) {
    const [fx, fy] = pending[0];
    if (Math.hypot(point[0] - fx, point[1] - fy) <= CLOSE_DISTANCE) return finishPendingPolygon(state);
  }
  const next = [...pending, [round(Math.max(0, point[0])), round(Math.max(0, point[1]))] as PaintPoint];
  return next.length >= REGION_PAINT_LIMITS.maxPoints ? finishPendingPolygon({ ...state, pending: next }) : { ...state, pending: next };
}

/** Bekleyen çokgeni bölge yapar (geçersizse yalnız temizler). */
export function finishPendingPolygon(state: PaintRegionsAuthoringState): PaintRegionsAuthoringState {
  const points = state.pending;
  return addPaintRegion({ ...state, pending: [] }, points);
}

export function cancelPendingPolygon(state: PaintRegionsAuthoringState): PaintRegionsAuthoringState {
  return { ...state, pending: [] };
}

export function paintRegionsIssues(state: PaintRegionsAuthoringState): PaintRegionsIssueCode[] {
  const issues: PaintRegionsIssueCode[] = [];
  if (state.regions.length === 0) issues.push('noRegions');
  else if (!state.regions.some((r) => Object.hasOwn(state.fills, r.id))) issues.push('noTargets');
  if (state.regions.length > REGION_PAINT_LIMITS.maxRegions) issues.push('tooManyRegions');
  const max = REGION_PAINT_LIMITS.maxLabel;
  if ([...state.palette, ...state.regions].some((e) => e.label.trim().length > max)) issues.push('labelTooLong');
  if (state.pending.length > 0) issues.push('pendingPolygon');
  return issues;
}

/** Sunucunun allowlist'ine uygun plan (etiketler kırpılır, boşsa yazılmaz). */
export function buildPaintRegionsPlan(state: PaintRegionsAuthoringState): RegionPaintRegionsPlanV1 {
  const label = (text: string) => text.trim();
  return {
    version: 1,
    type: REGION_PAINT,
    mode: 'regions',
    style: state.style,
    palette: state.palette.map((p) => ({ id: p.id, color: p.color, ...(label(p.label) ? { label: label(p.label) } : {}) }) as PaintEntry),
    regions: state.regions.map(
      (r) => ({ id: r.id, points: r.points.map(([x, y]) => [x, y]), ...(label(r.label) ? { label: label(r.label) } : {}) }) as PaintRegion
    ),
  };
}

/** Çözüm: plan sırasıyla hedef bölgeler; hiç hedef yoksa null. */
export function buildPaintRegionsSolution(state: PaintRegionsAuthoringState): { fills: PaintFills } | null {
  const palette = new Set(state.palette.map((p) => p.id));
  const fills: PaintFills = {};
  for (const region of state.regions) {
    const paint = Object.hasOwn(state.fills, region.id) ? state.fills[region.id] : null;
    if (paint && palette.has(paint)) fills[region.id] = paint;
  }
  return Object.keys(fills).length > 0 ? { fills } : null;
}

export function buildPaintRegionsInspector(state: PaintRegionsAuthoringState): PaintRegionsInspector {
  const used = new Set(state.palette.map((p) => p.color));
  return {
    style: state.style,
    palette: state.palette.map((p) => ({ ...p, targets: Object.values(state.fills).filter((f) => f === p.id).length })),
    regions: state.regions.map((r, i) => ({
      id: r.id,
      no: i + 1,
      label: r.label,
      fill: Object.hasOwn(state.fills, r.id) ? state.fills[r.id] : null,
    })),
    brush: state.brush,
    shape: state.shape,
    pendingCount: state.pending.length,
    selectedRegionId: state.selectedRegionId,
    freeColors: PAINT_COLORS.filter((c) => !used.has(c)),
    limits: REGION_PAINT_LIMITS,
    issues: paintRegionsIssues(state),
  };
}
