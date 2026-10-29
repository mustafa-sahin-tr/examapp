/**
 * Issue #588 (dilim b): grafik kurma (`InteractionType = barChart`, bileşen F ızgara tuvali ailesi). "Tablodaki verilere göre sütun
 * grafiğini çizelim", "çizgi grafiğini tamamlayalım".
 *
 * Plan: dikey eksen 0..`max`, `step` etiketli ızgara çizgisi aralığı, `unit` öğrencinin ayarlayabildiği en küçük adım (varsayılan
 * `step`), kategoriler, `fixed` (önceden çizilmiş, değiştirilemeyen sütunlar), `style` `bar` | `line` (yalnız görünüm). Veriler görselde
 * ya da yönergede durur. Çözüm (`{"values":{"c2":4}}`) öğrenciye gitmez (#570). Öğrenci serbest sütunların değerini ayarlar
 * (`{"values":{"c2":4}}`); çizilmeyen sütun 0 sayılır (backend `BarChartGrader`). Puansız.
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const BAR_CHART = 'barChart' as const;

/** Backend `BarChartGrader` sınırlarıyla aynı. */
export const BAR_CHART_LIMITS = {
  maxValue: 1000,
  maxSteps: 20,
  maxUnits: 100,
  minCategories: 2,
  maxCategories: 12,
  maxCategory: 30,
  maxTitle: 80,
  maxAxis: 40,
} as const;

export type BarChartStyle = 'bar' | 'line';

export interface BarChartCategory {
  id: string;
  label: string;
}

export interface BarChartPlanV1 {
  version: 1;
  type: typeof BAR_CHART;
  style: BarChartStyle;
  title: string;
  xLabel: string;
  yLabel: string;
  max: number;
  step: number;
  unit: number;
  categories: BarChartCategory[];
  /** Önceden çizilmiş sütunlar: kategori id'si → değer. */
  fixed: Record<string, number>;
}

/** Kategori id'si → değer. */
export type BarChartValues = Record<string, number>;

export interface BarChartFeedback {
  rows: { id: string; correct: boolean }[];
}

export function isBarChartType(type: unknown): boolean {
  return isInteractionType(type, BAR_CHART);
}

/** Eksen değeri: `unit` katı, 0..max. */
export function isBarValue(value: unknown, plan: Pick<BarChartPlanV1, 'max' | 'unit'>): value is number {
  return Number.isInteger(value) && (value as number) >= 0 && (value as number) <= plan.max && (value as number) % plan.unit === 0;
}

const text = (value: unknown): string => (typeof value === 'string' ? value : '');

/** Plan JSON'unu güvenli ayrıştırır; tür farklı, eksen geçersiz ya da kategori sayısı sınır dışıysa null. Bozuk kategori atlanır. */
export function parseBarChartPlan(json: string | null | undefined): BarChartPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isBarChartType(parsed['type'])) return null;
    const { maxValue, maxSteps, maxUnits, minCategories, maxCategories } = BAR_CHART_LIMITS;
    const max = parsed['max'];
    const step = parsed['step'];
    if (!Number.isInteger(max) || (max as number) < 1 || (max as number) > maxValue) return null;
    if (!Number.isInteger(step) || (step as number) < 1 || (max as number) % (step as number) !== 0 || (max as number) / (step as number) > maxSteps)
      return null;
    const rawUnit = parsed['unit'] ?? step;
    if (!Number.isInteger(rawUnit) || (rawUnit as number) < 1 || (step as number) % (rawUnit as number) !== 0 || (max as number) / (rawUnit as number) > maxUnits)
      return null;
    const axis = { max: max as number, unit: rawUnit as number };

    const ids = new Set<string>();
    const categories: BarChartCategory[] = [];
    for (const raw of Array.isArray(parsed['categories']) ? (parsed['categories'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const { id, label } = raw as Record<string, unknown>;
      if (!isValidInteractionId(id) || ids.has(id) || typeof label !== 'string' || !label.trim()) continue;
      ids.add(id);
      categories.push({ id, label });
    }
    if (categories.length < minCategories || categories.length > maxCategories) return null;

    const fixed = Object.create(null) as Record<string, number>;
    const rawFixed = parsed['fixed'];
    if (rawFixed && typeof rawFixed === 'object' && !Array.isArray(rawFixed)) {
      for (const [id, value] of Object.entries(rawFixed as Record<string, unknown>)) {
        if (ids.has(id) && isBarValue(value, axis)) fixed[id] = value;
      }
    }
    if (Object.keys(fixed).length >= categories.length) return null;

    return {
      version: 1,
      type: BAR_CHART,
      style: parsed['style'] === 'line' ? 'line' : 'bar',
      title: text(parsed['title']),
      xLabel: text(parsed['xLabel']),
      yLabel: text(parsed['yLabel']),
      max: axis.max,
      step: step as number,
      unit: axis.unit,
      categories,
      fixed,
    };
  } catch {
    return null;
  }
}

/** Değerler için ortak ayrıştırma: yalnız serbest kategoriler, `unit` katı 0..max. */
function readValues(raw: unknown, plan: BarChartPlanV1): BarChartValues {
  const out = Object.create(null) as BarChartValues;
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return out;
  for (const [id, value] of Object.entries(raw as Record<string, unknown>)) {
    if (isFreeCategory(plan, id) && isBarValue(value, plan)) out[id] = value;
  }
  return out;
}

export function isFreeCategory(plan: BarChartPlanV1, id: string): boolean {
  return plan.categories.some((c) => c.id === id) && !Object.hasOwn(plan.fixed, id);
}

/** Öğrenci cevabı → değerler; plana uymayan atlanır. Başka tür / bozuksa boş. */
export function parseBarChartAnswer(json: string | null | undefined, plan: BarChartPlanV1 | null): BarChartValues {
  if (!plan || !json) return Object.create(null) as BarChartValues;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    return readValues(parsed && typeof parsed === 'object' && isBarChartType(parsed['type']) ? parsed['values'] : null, plan);
  } catch {
    return Object.create(null) as BarChartValues;
  }
}

/** Payload: serbest kategoriler plan sırasıyla (kararlı JSON). */
export function buildBarChartPayload(plan: BarChartPlanV1, values: BarChartValues): string {
  const out: BarChartValues = {};
  for (const c of plan.categories) if (isFreeCategory(plan, c.id) && Object.hasOwn(values, c.id)) out[c.id] = values[c.id];
  return JSON.stringify({ version: 1, type: BAR_CHART, values: out });
}

/** Çözüm JSON'u → serbest kategorilerin hedef değerleri. */
export function parseBarChartSolution(json: string | null | undefined, plan: BarChartPlanV1 | null): BarChartValues {
  if (!plan || !json) return Object.create(null) as BarChartValues;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    return readValues(parsed && typeof parsed === 'object' ? parsed['values'] : null, plan);
  } catch {
    return Object.create(null) as BarChartValues;
  }
}

/** Değersiz cevap: sunucu boş cevap sayar. Başka tür false. */
export function isBlankBarChartPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isBarChartType(parsed['type'])) return false;
    const values = parsed['values'];
    return !values || typeof values !== 'object' || Object.keys(values).length === 0;
  } catch {
    return false;
  }
}

export function parseBarChartFeedback(json: string | null | undefined): BarChartFeedback | null {
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
