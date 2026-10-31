/**
 * Issue #586: sözel mantık tablosunun (`logicGrid`) görselsiz formu. Öğretmen kitabın cevap tablosunu doldurur: başlıkta satır
 * grubunun (ör. "Çocuk") ve kategorilerin (ör. "Renk") adı, her satırda konu öğesi (ör. Ali) ve onun her kategorideki cevabı.
 * Kategoride her seçenek tam bir satıra gittiğinden seçenekler ve çözüm bu tablodan çıkar. Öğrencinin gördüğü seçenek sırası
 * ayrıca öğretmence belirlenir (`order`); satır sırasıyla aynı kalırsa çözüm köşegende görünür ({@link isRowOrder} uyarısı).
 *
 * Kurallar backend `LogicGridGrader.ValidateDefinition` ile aynı.
 */
import { LOGIC_GRID, LOGIC_GRID_LIMITS, parseLogicGridPlan, parseLogicGridSolution } from './logic-grid';

export interface LogicCategoryForm {
  label: string;
  /** `values[s]` = konu öğesi `s`'nin bu kategorideki cevabı (cevap tablosunun sütunu). */
  values: string[];
  /** Öğrencinin gördüğü seçenek sırası: konu öğesi dizinlerinin permütasyonu (`values[order[k]]` k. seçenek). */
  order: number[];
}

export interface LogicGridForm {
  kind: 'logicGrid';
  subjectLabel: string;
  subjects: string[];
  categories: LogicCategoryForm[];
  clues: string[];
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

const range = (n: number) => Array.from({ length: n }, (_, i) => i);

function emptyCategory(subjects: number): LogicCategoryForm {
  return { label: '', values: range(subjects).map(() => ''), order: range(subjects) };
}

export function emptyLogicGridForm(): LogicGridForm {
  return {
    kind: 'logicGrid',
    subjectLabel: '',
    subjects: ['', '', ''],
    categories: [emptyCategory(3)],
    clues: [''],
  };
}

/** Sona boş satır ekler; her kategoride yeni seçenek sıranın sonuna girer. */
export function addLogicSubject(form: LogicGridForm): LogicGridForm {
  const n = form.subjects.length;
  if (n >= LOGIC_GRID_LIMITS.maxItems) return form;
  return {
    ...form,
    subjects: [...form.subjects, ''],
    categories: form.categories.map((c) => ({ ...c, values: [...c.values, ''], order: [...c.order, n] })),
  };
}

/** Satırı (ve her kategorideki cevabını) siler; seçenek sırası kalan satırlara göre yeniden numaralanır. */
export function removeLogicSubject(form: LogicGridForm, index: number): LogicGridForm {
  if (form.subjects.length <= LOGIC_GRID_LIMITS.minItems || index < 0 || index >= form.subjects.length) return form;
  return {
    ...form,
    subjects: form.subjects.filter((_, i) => i !== index),
    categories: form.categories.map((c) => ({
      ...c,
      values: c.values.filter((_, i) => i !== index),
      order: c.order.filter((s) => s !== index).map((s) => (s > index ? s - 1 : s)),
    })),
  };
}

export function addLogicCategory(form: LogicGridForm): LogicGridForm {
  if (form.categories.length >= LOGIC_GRID_LIMITS.maxCategories) return form;
  return { ...form, categories: [...form.categories, emptyCategory(form.subjects.length)] };
}

export function removeLogicCategory(form: LogicGridForm, index: number): LogicGridForm {
  if (form.categories.length <= 1) return form;
  return { ...form, categories: form.categories.filter((_, i) => i !== index) };
}

/**
 * Satırı (cevaplarıyla) bir adım yukarı (-1) / aşağı (+1) taşır. Öğrencinin seçenek sırası değişmez: `order` satır dizinleriyle
 * tutulduğundan iki satırın dizinleri yer değiştirir.
 */
export function moveLogicSubject(form: LogicGridForm, index: number, delta: -1 | 1): LogicGridForm {
  const target = index + delta;
  if (index < 0 || target < 0 || index >= form.subjects.length || target >= form.subjects.length) return form;
  const swap = <T>(list: T[]) => list.map((v, i) => (i === index ? list[target] : i === target ? list[index] : v));
  const remap = (s: number) => (s === index ? target : s === target ? index : s);
  return {
    ...form,
    subjects: swap(form.subjects),
    categories: form.categories.map((c) => ({ ...c, values: swap(c.values), order: c.order.map(remap) })),
  };
}

/** Seçenek sırasında `position`'daki seçeneği bir adım sola (-1) / sağa (+1) taşır. */
export function moveLogicOption(form: LogicGridForm, category: number, position: number, delta: -1 | 1): LogicGridForm {
  const target = position + delta;
  return {
    ...form,
    categories: form.categories.map((c, i) => {
      if (i !== category || target < 0 || target >= c.order.length) return c;
      const order = [...c.order];
      [order[position], order[target]] = [order[target], order[position]];
      return { ...c, order };
    }),
  };
}

/** Seçenek sırasını karıştırır (satır sırasıyla aynı kalmaz; `random` testte sabitlenir). */
export function shuffleLogicOptions(form: LogicGridForm, category: number, random: () => number = Math.random): LogicGridForm {
  return {
    ...form,
    categories: form.categories.map((c, i) => {
      if (i !== category || c.order.length < 2) return c;
      let order = c.order;
      for (let attempt = 0; attempt < 10 && (order === c.order || isRowOrder(order)); attempt++) {
        order = [...c.order];
        for (let k = order.length - 1; k > 0; k--) {
          const j = Math.floor(random() * (k + 1));
          [order[k], order[j]] = [order[j], order[k]];
        }
      }
      // Şans eseri yine satır sırası (2 seçenekte yarı yarıya): bir adım döndür.
      if (isRowOrder(order)) order = [...order.slice(1), order[0]];
      return { ...c, order };
    }),
  };
}

/** Seçenekler satır sırasında mı (öğrencinin tablosunda çözüm köşegende görünür)? */
export function isRowOrder(order: number[]): boolean {
  return order.every((s, k) => s === k);
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
  for (const category of form.categories) labels(category.values);

  const clues = form.clues.filter((c) => c.trim());
  if (clues.length > maxClues) push('logicClueCount', { max: maxClues });
  if (clues.some((c) => c.trim().length > maxClue)) push('labelTooLong', { max: maxClue });

  return errors;
}

/** Form → plan + çözüm. Önce {@link validateLogicGridForm} geçmeli. Seçenekler öğretmenin sırasıyla, id'ler bu sırayla. */
export function buildLogicGridDefinition(form: LogicGridForm): LogicGridDefinition {
  let optionId = 0;
  const subjects = form.subjects.map((label, si) => ({ id: `p${si + 1}`, label: label.trim() }));
  const assign: Record<string, Record<string, string>> = {};
  const categories = form.categories.map((category, ci) => {
    const id = `c${ci + 1}`;
    const items = category.order.map((si) => {
      const option = { id: `o${++optionId}`, label: (category.values[si] ?? '').trim() };
      if (subjects[si]) (assign[subjects[si].id] ??= {})[id] = option.id;
      return option;
    });
    return { id, label: category.label.trim(), items };
  });
  const clues = form.clues
    .map((text) => text.trim())
    .filter(Boolean)
    .map((text, i) => ({ id: `q${i + 1}`, text }));
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
  const n = plan.subject.items.length;
  const categories = plan.categories.map((c) => {
    const values = range(n).map(() => '');
    const order: number[] = [];
    const loose: string[] = [];
    for (const item of c.items) {
      const owner = plan.subject.items.findIndex((s) => Object.hasOwn(assign, s.id) && assign[s.id][c.id] === item.id);
      if (owner >= 0 && !order.includes(owner)) {
        values[owner] = item.label;
        order.push(owner);
      } else loose.push(item.label);
    }
    // Çözümü eksik (taslak) kayıt: sahipsiz seçenekler boş kalan satırlara sırayla.
    for (const label of loose) {
      const free = range(n).find((s) => !order.includes(s))!;
      values[free] = label;
      order.push(free);
    }
    return { label: c.label, values, order };
  });
  return {
    kind: 'logicGrid',
    subjectLabel: plan.subject.label,
    subjects: plan.subject.items.map((s) => s.label),
    categories,
    clues: plan.clues.length ? plan.clues.map((c) => c.text) : [''],
  };
}
