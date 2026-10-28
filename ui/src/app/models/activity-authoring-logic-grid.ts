/**
 * Issue #586: sözel mantık tablosunun (`logicGrid`) görselsiz formu — konu (ör. "Çocuk": Ali, Ayşe, Can), kategoriler (ör. "Renk":
 * mavi, kırmızı, yeşil; her kategoride konu öğesi kadar seçenek), ipuçları ve çözüm (konu öğesi başına kategori başına seçenek,
 * kategoride birebir). Konu öğesi sayısı değişince kategorilerin seçenek listesi aynı uzunluğa getirilir.
 *
 * Kurallar backend `LogicGridGrader.ValidateDefinition` ile aynı.
 */
import { LOGIC_GRID, LOGIC_GRID_LIMITS, parseLogicGridPlan, parseLogicGridSolution } from './logic-grid';

export interface LogicCategoryForm {
  label: string;
  options: string[];
}

export interface LogicGridForm {
  kind: 'logicGrid';
  subjectLabel: string;
  subjects: string[];
  categories: LogicCategoryForm[];
  clues: string[];
  /** `answer[s][c]` = konu öğesi `s`'nin `c` kategorisindeki seçeneğinin dizini; null = seçilmedi. */
  answer: (number | null)[][];
}

export interface LogicGridError {
  key: string;
  params?: Record<string, unknown>;
}

export interface LogicGridDefinition {
  interactionType: typeof LOGIC_GRID;
  plan: string;
  solution: string;
}

export const LOGIC_GRID_FORM_LIMITS = LOGIC_GRID_LIMITS;

export function isLogicGridForm(form: { kind: string }): form is LogicGridForm {
  return form.kind === 'logicGrid';
}

export function emptyLogicGridForm(): LogicGridForm {
  return {
    kind: 'logicGrid',
    subjectLabel: '',
    subjects: ['', '', ''],
    categories: [{ label: '', options: ['', '', ''] }],
    clues: [''],
    answer: [[null], [null], [null]],
  };
}

/** Konu öğesi sayısını değiştirir; kategorilerin seçenekleri ve çözüm tablosu aynı boyuta getirilir. */
export function resizeLogicSubjects(form: LogicGridForm, count: number): LogicGridForm {
  const n = Math.min(LOGIC_GRID_LIMITS.maxItems, Math.max(LOGIC_GRID_LIMITS.minItems, count));
  const fit = <T>(list: T[], fill: () => T) => (list.length >= n ? list.slice(0, n) : [...list, ...Array.from({ length: n - list.length }, fill)]);
  return {
    ...form,
    subjects: fit(form.subjects, () => ''),
    categories: form.categories.map((c) => ({ ...c, options: fit(c.options, () => '') })),
    answer: fit(form.answer, () => form.categories.map(() => null)).map((row) =>
      row.map((choice) => (choice !== null && choice < n ? choice : null))
    ),
  };
}

export function addLogicCategory(form: LogicGridForm): LogicGridForm {
  if (form.categories.length >= LOGIC_GRID_LIMITS.maxCategories) return form;
  return {
    ...form,
    categories: [...form.categories, { label: '', options: form.subjects.map(() => '') }],
    answer: form.answer.map((row) => [...row, null]),
  };
}

export function removeLogicCategory(form: LogicGridForm, index: number): LogicGridForm {
  if (form.categories.length <= 1) return form;
  return {
    ...form,
    categories: form.categories.filter((_, i) => i !== index),
    answer: form.answer.map((row) => row.filter((_, i) => i !== index)),
  };
}

export function validateLogicGridForm(form: LogicGridForm): LogicGridError[] {
  const errors: LogicGridError[] = [];
  const { minItems, maxItems, maxCategories, maxClues, maxLabel, maxClue } = LOGIC_GRID_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  const labels = (values: string[]) => {
    if (values.some((v) => !v.trim())) push('logicLabelRequired');
    if (values.some((v) => v.trim().length > maxLabel)) push('labelTooLong', { max: maxLabel });
    const norm = values.map((v) => v.trim().toLocaleLowerCase('tr')).filter(Boolean);
    if (new Set(norm).size !== norm.length) push('logicDuplicate');
  };

  if (form.subjects.length < minItems || form.subjects.length > maxItems) push('logicSubjectCount', { min: minItems, max: maxItems });
  if (form.categories.length < 1 || form.categories.length > maxCategories) push('logicCategoryCount', { max: maxCategories });
  labels([form.subjectLabel]);
  labels(form.subjects);
  labels(form.categories.map((c) => c.label));
  for (const category of form.categories) labels(category.options);

  const clues = form.clues.filter((c) => c.trim());
  if (clues.length > maxClues) push('logicClueCount', { max: maxClues });
  if (clues.some((c) => c.trim().length > maxClue)) push('labelTooLong', { max: maxClue });

  form.categories.forEach((category, ci) => {
    const chosen = form.subjects.map((_, si) => form.answer[si]?.[ci] ?? null);
    if (chosen.some((c) => c === null)) push('logicAnswerMissing');
    else if (new Set(chosen).size !== chosen.length) push('logicAnswerDuplicate', { category: category.label.trim() || ci + 1 });
  });
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateLogicGridForm} geçmeli; eksik seçimler çözüme yazılmaz. */
export function buildLogicGridDefinition(form: LogicGridForm): LogicGridDefinition {
  let optionId = 0;
  const categories = form.categories.map((category, ci) => ({
    id: `c${ci + 1}`,
    label: category.label.trim(),
    items: category.options.map((label) => ({ id: `o${++optionId}`, label: label.trim() })),
  }));
  const subjects = form.subjects.map((label, si) => ({ id: `p${si + 1}`, label: label.trim() }));
  const clues = form.clues
    .map((text) => text.trim())
    .filter(Boolean)
    .map((text, i) => ({ id: `q${i + 1}`, text }));
  const assign: Record<string, Record<string, string>> = {};
  subjects.forEach((subject, si) => {
    categories.forEach((category, ci) => {
      const choice = form.answer[si]?.[ci];
      if (choice === null || choice === undefined || !category.items[choice]) return;
      (assign[subject.id] ??= {})[category.id] = category.items[choice].id;
    });
  });
  return {
    interactionType: LOGIC_GRID,
    plan: JSON.stringify({
      version: 1,
      type: LOGIC_GRID,
      subject: { label: form.subjectLabel.trim(), items: subjects },
      categories,
      clues,
    }),
    solution: JSON.stringify({ assign }),
  };
}

/** Kayıtlı plan + çözüm → form (düzenleme). Seçenek sayısı konu sayısından farklı plan null. */
export function parseLogicGridDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): LogicGridForm | null {
  const plan = parseLogicGridPlan(planJson);
  if (!plan || plan.categories.some((c) => c.items.length !== plan.subject.items.length)) return null;
  const assign = parseLogicGridSolution(solutionJson, plan);
  return {
    kind: 'logicGrid',
    subjectLabel: plan.subject.label,
    subjects: plan.subject.items.map((s) => s.label),
    categories: plan.categories.map((c) => ({ label: c.label, options: c.items.map((i) => i.label) })),
    clues: plan.clues.length ? plan.clues.map((c) => c.text) : [''],
    answer: plan.subject.items.map((s) =>
      plan.categories.map((c) => {
        const option = Object.hasOwn(assign, s.id) ? assign[s.id][c.id] : undefined;
        const index = c.items.findIndex((i) => i.id === option);
        return index >= 0 ? index : null;
      })
    ),
  };
}
