/**
 * Issue #588 (dilim c): kareli zeminde çizimin (`gridDraw`) görselsiz formu — zemin boyutu ve görünümü (kareli / noktalı), şekil
 * (dikdörtgen / kare), kural (alan ve/veya çevre, birim kare cinsinden) ve "bütün dikdörtgenler" seçeneği. Kural çözüme gider;
 * yönergeye yazılır ("Çevre uzunluğu 14 cm olan dikdörtgenlerin tamamını çiziniz").
 *
 * Kurallar backend `GridDrawGrader.ValidateDefinition` ile aynı.
 */
import {
  GRID_DRAW,
  GRID_DRAW_LIMITS,
  GridDrawBackground,
  GridDrawRule,
  GridDrawShapeKind,
  GridSize,
  gridDrawTargets,
  parseGridDrawPlan,
  parseGridDrawSolution,
} from './grid-draw';

export interface GridDrawForm {
  kind: 'gridDraw';
  rows: number;
  cols: number;
  grid: GridDrawBackground;
  shape: GridDrawShapeKind;
  /** Alan metni (birim kare); boş = koşul yok. */
  area: string;
  /** Çevre metni (birim); boş = koşul yok. */
  perimeter: string;
  /** Kurala uyan bütün boyutlar çizilmeli. */
  all: boolean;
}

export interface GridDrawError {
  key: string;
  params?: Record<string, unknown>;
}

export interface GridDrawDefinition {
  interactionType: typeof GRID_DRAW;
  plan: string;
  solution: string;
}

export const GRID_DRAW_FORM_LIMITS = GRID_DRAW_LIMITS;

export function isGridDrawForm(form: { kind: string }): form is GridDrawForm {
  return form.kind === 'gridDraw';
}

/** Varsayılan: 10 × 16 kareli zemin, bütün dikdörtgenler, kural boş. */
export function emptyGridDrawForm(): GridDrawForm {
  return { kind: 'gridDraw', rows: 10, cols: 16, grid: 'square', shape: 'rectangle', area: '', perimeter: '', all: true };
}

/** Sayı metni → pozitif tam sayı; boşsa null, geçersizse undefined. */
function ruleNumber(value: string): number | null | undefined {
  const t = value.trim();
  if (!t) return null;
  return /^\d+$/.test(t) && Number(t) > 0 ? Number(t) : undefined;
}

const sizeOk = (n: number) => Number.isInteger(n) && n >= GRID_DRAW_LIMITS.minSize && n <= GRID_DRAW_LIMITS.maxSize;

/** Formun kuralı; zemin ya da sayılar geçersizse null. */
export function gridDrawFormRule(form: GridDrawForm): GridDrawRule | null {
  const area = ruleNumber(form.area);
  const perimeter = ruleNumber(form.perimeter);
  if (area === undefined || perimeter === undefined || (area === null && perimeter === null)) return null;
  if (area !== null && area > form.rows * form.cols) return null;
  if (perimeter !== null && (perimeter < 4 || perimeter > 2 * (form.rows + form.cols))) return null;
  return { area, perimeter, all: form.all };
}

/** Kurala uyan ve zemine sığan boyutlar (editör önizlemesi). */
export function gridDrawFormTargets(form: GridDrawForm): GridSize[] {
  const rule = gridDrawFormRule(form);
  return rule && sizeOk(form.rows) && sizeOk(form.cols) ? gridDrawTargets(form, rule) : [];
}

export function validateGridDrawForm(form: GridDrawForm): GridDrawError[] {
  const errors: GridDrawError[] = [];
  const { minSize, maxSize, maxTargets } = GRID_DRAW_LIMITS;
  if (!sizeOk(form.rows) || !sizeOk(form.cols)) {
    errors.push({ key: 'drawSize', params: { min: minSize, max: maxSize } });
    return errors;
  }
  const area = ruleNumber(form.area);
  const perimeter = ruleNumber(form.perimeter);
  if (area === null && perimeter === null) errors.push({ key: 'drawRuleMissing' });
  else if (area === undefined || perimeter === undefined || !gridDrawFormRule(form)) errors.push({ key: 'drawRuleInvalid' });
  else {
    const targets = gridDrawFormTargets(form);
    if (!targets.length) errors.push({ key: 'drawNoTarget' });
    else if (form.all && targets.length > maxTargets) errors.push({ key: 'drawTooManyTargets', params: { max: maxTargets } });
  }
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateGridDrawForm} geçmeli. */
export function buildGridDrawDefinition(form: GridDrawForm): GridDrawDefinition {
  const plan: Record<string, unknown> = { version: 1, type: GRID_DRAW, rows: form.rows, cols: form.cols };
  if (form.grid === 'dot') plan['grid'] = 'dot';
  if (form.shape === 'square') plan['shape'] = 'square';
  const rule = gridDrawFormRule(form);
  const solution: Record<string, unknown> = {};
  if (rule?.area != null) solution['area'] = rule.area;
  if (rule?.perimeter != null) solution['perimeter'] = rule.perimeter;
  solution['all'] = form.all;
  return { interactionType: GRID_DRAW, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
}

/** Kayıtlı plan + çözüm → form (düzenleme). */
export function parseGridDrawDefinition(planJson: string | null | undefined, solutionJson: string | null | undefined): GridDrawForm | null {
  const plan = parseGridDrawPlan(planJson);
  if (!plan) return null;
  const rule = parseGridDrawSolution(solutionJson);
  return {
    kind: 'gridDraw',
    rows: plan.rows,
    cols: plan.cols,
    grid: plan.grid,
    shape: plan.shape,
    area: rule?.area != null ? String(rule.area) : '',
    perimeter: rule?.perimeter != null ? String(rule.perimeter) : '',
    all: rule?.all ?? true,
  };
}
