/**
 * Issue #588 (dilim b): grafik kurmanın (`barChart`) görselsiz formu — görünüm (sütun / çizgi), başlık ve eksen adları, dikey eksen
 * (en büyük değer, etiket aralığı, adım), kategoriler ve her kategorinin değeri. "Hazır" işaretli kategori öğrenciye çizili gelir
 * (değiştirilemez, kontrol edilmez); diğerlerinin değeri çözümdür. Veriler (tablo) yönergeye ya da görsele yazılır.
 *
 * Kurallar backend `BarChartGrader.ValidateDefinition` ile aynı.
 */
import { BAR_CHART, BAR_CHART_LIMITS, BarChartStyle, isBarValue, parseBarChartPlan, parseBarChartSolution } from './bar-chart';

export interface BarChartCategoryForm {
  label: string;
  /** Değer metni (unit katı, 0..max). */
  value: string;
  /** Öğrenciye çizili gelir, kontrol edilmez. */
  fixed: boolean;
}

export interface BarChartForm {
  kind: 'barChart';
  style: BarChartStyle;
  title: string;
  xLabel: string;
  yLabel: string;
  max: number;
  step: number;
  unit: number;
  categories: BarChartCategoryForm[];
}

export interface BarChartError {
  key: string;
  params?: Record<string, unknown>;
}

export interface BarChartDefinition {
  interactionType: typeof BAR_CHART;
  plan: string;
  solution: string;
}

export const BAR_CHART_FORM_LIMITS = BAR_CHART_LIMITS;

export function isBarChartForm(form: { kind: string }): form is BarChartForm {
  return form.kind === 'barChart';
}

/** Varsayılan: 0–10 eksen, 2'şer etiket, 1'er adım, üç boş kategori. */
export function emptyBarChartForm(): BarChartForm {
  return {
    kind: 'barChart',
    style: 'bar',
    title: '',
    xLabel: '',
    yLabel: '',
    max: 10,
    step: 2,
    unit: 1,
    categories: [
      { label: '', value: '', fixed: false },
      { label: '', value: '', fixed: false },
      { label: '', value: '', fixed: false },
    ],
  };
}

/** Değer metni → sayı; geçersizse (boş dahil) undefined. */
export function categoryValue(value: string, form: Pick<BarChartForm, 'max' | 'unit'>): number | undefined {
  const t = value.trim();
  if (!/^\d+$/.test(t)) return undefined;
  const n = Number(t);
  return isBarValue(n, form) ? n : undefined;
}

/**
 * Eksen tutarlı mı (max 1..1000, step max'ı böler ≤ 20 aralık, unit step'i böler ≤ 100 adım; nokta grafiğinde ≤ 20 nokta).
 */
export function axisErrors(form: Pick<BarChartForm, 'max' | 'step' | 'unit'> & { style?: BarChartStyle }): string[] {
  const { maxValue, maxSteps, maxUnits, maxDots } = BAR_CHART_LIMITS;
  const { max, step, unit } = form;
  if (!Number.isInteger(max) || max < 1 || max > maxValue) return ['chartMax'];
  if (!Number.isInteger(step) || step < 1 || max % step !== 0 || max / step > maxSteps) return ['chartStep'];
  if (!Number.isInteger(unit) || unit < 1 || step % unit !== 0 || max / unit > maxUnits) return ['chartUnit'];
  if (form.style === 'dot' && max / unit > maxDots) return ['chartDots'];
  return [];
}

export function validateBarChartForm(form: BarChartForm): BarChartError[] {
  const errors: BarChartError[] = [];
  const { maxValue, maxSteps, maxUnits, maxDots, minCategories, maxCategories, maxCategory, maxTitle, maxAxis } = BAR_CHART_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  const axis = axisErrors(form);
  for (const key of axis) push(key, { max: maxValue, steps: maxSteps, units: maxUnits, dots: maxDots });

  if (form.title.trim().length > maxTitle) push('labelTooLong', { max: maxTitle });
  if (form.xLabel.trim().length > maxAxis || form.yLabel.trim().length > maxAxis) push('labelTooLong', { max: maxAxis });

  if (form.categories.length < minCategories || form.categories.length > maxCategories)
    push('chartCategoryCount', { min: minCategories, max: maxCategories });
  if (form.categories.some((c) => !c.label.trim())) push('chartCategoryBlank');
  if (form.categories.some((c) => c.label.trim().length > maxCategory)) push('labelTooLong', { max: maxCategory });
  if (!axis.length) {
    const bad = form.categories.filter((c) => categoryValue(c.value, form) === undefined).map((c) => c.label.trim() || '?');
    if (bad.length) push('chartValueInvalid', { list: bad.join(', '), max: form.max, unit: form.unit });
  }
  if (form.categories.length && form.categories.every((c) => c.fixed)) push('chartAllFixed');
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateBarChartForm} geçmeli. */
export function buildBarChartDefinition(form: BarChartForm): BarChartDefinition {
  const categories = form.categories.map((c, i) => ({ id: `c${i + 1}`, label: c.label.trim() }));
  const fixed: Record<string, number> = {};
  const values: Record<string, number> = {};
  form.categories.forEach((c, i) => ((c.fixed ? fixed : values)[`c${i + 1}`] = categoryValue(c.value, form) ?? 0));
  const plan: Record<string, unknown> = { version: 1, type: BAR_CHART };
  if (form.style !== 'bar') plan['style'] = form.style;
  for (const key of ['title', 'xLabel', 'yLabel'] as const) if (form[key].trim()) plan[key] = form[key].trim();
  plan['max'] = form.max;
  plan['step'] = form.step;
  if (form.unit !== form.step) plan['unit'] = form.unit;
  plan['categories'] = categories;
  if (Object.keys(fixed).length) plan['fixed'] = fixed;
  return { interactionType: BAR_CHART, plan: JSON.stringify(plan), solution: JSON.stringify({ values }) };
}

/** Kayıtlı plan + çözüm → form (düzenleme). */
export function parseBarChartDefinition(planJson: string | null | undefined, solutionJson: string | null | undefined): BarChartForm | null {
  const plan = parseBarChartPlan(planJson);
  if (!plan) return null;
  const solution = parseBarChartSolution(solutionJson, plan);
  return {
    kind: 'barChart',
    style: plan.style,
    title: plan.title,
    xLabel: plan.xLabel,
    yLabel: plan.yLabel,
    max: plan.max,
    step: plan.step,
    unit: plan.unit,
    categories: plan.categories.map((c) => {
      const fixed = Object.hasOwn(plan.fixed, c.id);
      const value = fixed ? plan.fixed[c.id] : solution[c.id];
      return { label: c.label, value: value === undefined ? '' : String(value), fixed };
    }),
  };
}
