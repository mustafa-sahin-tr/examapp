/**
 * Issue #587: boyamanın (`regionPaint`, ızgara modu) görselsiz formu — ızgara boyutu ve renkler; her renk için hedef kitaptaki gibi
 * yazılır: kesir (`2/5`), ondalık (`0,35`), yüzde (`%25`) ya da doğrudan kare sayısı (`40`). Oran ızgaranın kare sayısıyla çarpılıp
 * tam sayıya inmeli (10×10'da `1/3` olmaz). Kayda çözüm olarak kare sayıları gider; hedef metni gitmez. Düzenlemede kayıtlı sayılar
 * kare sayısı olarak açılır.
 *
 * Issue #588 dilim d: `shape: "pie"` daire grafiği — `slices` eş dilim, hedefler dilim sayısına göre (16 dilimde `1/4` = 4 dilim).
 *
 * Kurallar backend `RegionPaintGrader.ValidateDefinition` ile aynı.
 */
import { parseTrNumber } from '../shared/utils/slot-normalizer';
import {
  PAINT_COLORS,
  PaintColor,
  REGION_PAINT,
  REGION_PAINT_LIMITS,
  parseRegionPaintPlan,
  parseRegionPaintSolution,
  serializeRegionPaintPlan,
} from './region-paint';

export interface RegionPaintEntryForm {
  color: PaintColor;
  /** Palet düğmesinde görünen metin (isteğe bağlı; boşsa rengin adı). */
  label: string;
  /** Kesir / ondalık / yüzde / kare sayısı; boş = bu renk boyanmamalı (çeldirici, 0). */
  target: string;
}

export type RegionPaintShape = 'grid' | 'pie';

export interface RegionPaintForm {
  kind: 'regionPaint';
  /** `pie`: daire grafiği; `rows`/`cols` yerine `slices` geçerli. */
  shape: RegionPaintShape;
  rows: number;
  cols: number;
  slices: number;
  entries: RegionPaintEntryForm[];
}

export interface RegionPaintError {
  key: string;
  params?: Record<string, unknown>;
}

export interface RegionPaintDefinition {
  interactionType: typeof REGION_PAINT;
  plan: string;
  solution: string;
}

export const REGION_PAINT_FORM_LIMITS = REGION_PAINT_LIMITS;

export function isRegionPaintForm(form: { kind: string }): form is RegionPaintForm {
  return form.kind === 'regionPaint';
}

export function emptyRegionPaintForm(): RegionPaintForm {
  return { kind: 'regionPaint', shape: 'grid', rows: 10, cols: 10, slices: 8, entries: [{ color: 'blue', label: '', target: '' }] };
}

/** Boyanabilir parça sayısı: ızgarada kare, dairede dilim. */
export function regionPaintTotal(form: Pick<RegionPaintForm, 'shape' | 'rows' | 'cols' | 'slices'>): number {
  return form.shape === 'pie' ? form.slices : form.rows * form.cols;
}

/** Eklenecek renk: kullanılmayan ilk palet rengi; hepsi kullanılmışsa null. */
export function nextPaintColor(form: RegionPaintForm): PaintColor | null {
  return PAINT_COLORS.find((c) => !form.entries.some((e) => e.color === c)) ?? null;
}

/**
 * Hedef metni → kare sayısı: tam sayı doğrudan sayıdır; kesir, ondalık ve yüzde ızgaranın kare sayısıyla çarpılır. Boş = 0.
 * Tanınmayan, birimli, negatif, tam sayıya inmeyen ya da ızgarayı aşan hedef null.
 */
export function targetCells(target: string, total: number): number | null {
  if (!target.trim()) return 0;
  const parsed = parseTrNumber(target);
  if (!parsed || parsed.unit !== null || parsed.num < 0n) return null;
  const scaled = parsed.form === 'integer' ? parsed.num : parsed.num * BigInt(total);
  const den = parsed.form === 'integer' ? 1n : parsed.den;
  if (scaled % den !== 0n) return null;
  const count = Number(scaled / den);
  return count <= total ? count : null;
}

export function validateRegionPaintForm(form: RegionPaintForm): RegionPaintError[] {
  const errors: RegionPaintError[] = [];
  const { minSize, maxSize, minSlices, maxSlices, maxPalette, maxLabel } = REGION_PAINT_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  const sizeOk = (n: number) => Number.isInteger(n) && n >= minSize && n <= maxSize;
  if (form.shape === 'pie') {
    if (!Number.isInteger(form.slices) || form.slices < minSlices || form.slices > maxSlices)
      push('paintSliceCount', { min: minSlices, max: maxSlices });
  } else if (!sizeOk(form.rows) || !sizeOk(form.cols)) push('paintGridSize', { min: minSize, max: maxSize });
  if (form.entries.length < 1 || form.entries.length > maxPalette) push('paintColorCount', { max: maxPalette });
  if (new Set(form.entries.map((e) => e.color)).size !== form.entries.length) push('paintColorDuplicate');
  if (form.entries.some((e) => e.label.trim().length > maxLabel)) push('labelTooLong', { max: maxLabel });

  const total = regionPaintTotal(form);
  const counts = form.entries.map((e) => targetCells(e.target, total));
  const bad = form.entries.flatMap((e, i) => (counts[i] === null ? [e.target.trim()] : []));
  // Dairede mesajlar dilim der.
  const suffix = form.shape === 'pie' ? 'Pie' : '';
  if (bad.length) push('paintTargetInvalid' + suffix, { list: bad.join(', '), total });
  else {
    const sum = (counts as number[]).reduce((a, b) => a + b, 0);
    if (sum === 0) push('paintTargetMissing');
    else if (sum > total) push('paintTargetTooMany' + suffix, { sum, total });
  }
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateRegionPaintForm} geçmeli; çözülemeyen hedef 0 yazılır. */
export function buildRegionPaintDefinition(form: RegionPaintForm): RegionPaintDefinition {
  const total = regionPaintTotal(form);
  const palette = form.entries.map((e, i) => {
    const label = e.label.trim();
    return label ? { id: `p${i + 1}`, color: e.color, label } : { id: `p${i + 1}`, color: e.color };
  });
  const counts: Record<string, number> = {};
  form.entries.forEach((e, i) => (counts[`p${i + 1}`] = targetCells(e.target, total) ?? 0));
  return {
    interactionType: REGION_PAINT,
    plan: JSON.stringify(
      serializeRegionPaintPlan({
        mode: form.shape,
        rows: form.shape === 'pie' ? 1 : form.rows,
        cols: form.shape === 'pie' ? form.slices : form.cols,
        palette,
      })
    ),
    solution: JSON.stringify({ counts }),
  };
}

/** Kayıtlı plan + çözüm → form (düzenleme); hedefler kare sayısı olarak (0 = boş). */
export function parseRegionPaintDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): RegionPaintForm | null {
  const plan = parseRegionPaintPlan(planJson);
  if (!plan) return null;
  const counts = parseRegionPaintSolution(solutionJson, plan);
  return {
    kind: 'regionPaint',
    shape: plan.mode,
    // Daire planında ızgara alanları varsayılanda kalır (şekil değiştirilirse boş form gibi açılsın).
    rows: plan.mode === 'pie' ? 10 : plan.rows,
    cols: plan.mode === 'pie' ? 10 : plan.cols,
    slices: plan.mode === 'pie' ? plan.cols : 8,
    entries: plan.palette.map((p) => {
      const n = Object.hasOwn(counts, p.id) ? counts[p.id] : 0;
      return { color: p.color, label: p.label, target: n > 0 ? String(n) : '' };
    }),
  };
}
