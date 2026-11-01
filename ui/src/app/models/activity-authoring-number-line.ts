/**
 * Issue #588: sayı doğrusunun (`numberLine`) görselsiz formu — bölme sayısı, çentik etiketleri, kartlar ve her kartın hedef çentiği
 * (boş = çeldirici). Tolerans (çentik) tahmin soruları için; "Çentikleri gizle" yalnız etiketli çentikleri çizer. Kartlar formdaki
 * sırayla görünür.
 *
 * Kurallar backend `NumberLineGrader.ValidateDefinition` ile aynı.
 */
import { NUMBER_LINE, NUMBER_LINE_LIMITS, parseNumberLinePlan, parseNumberLineSolution } from './number-line';

export interface NumberLineLabelForm {
  at: number;
  text: string;
}

export interface NumberLineCardForm {
  label: string;
  /** Hedef çentik (0..bölme); boş = çeldirici (doğruya konmamalı). */
  target: string;
}

export interface NumberLineForm {
  kind: 'numberLine';
  divisions: number;
  tolerance: number;
  hideTicks: boolean;
  labels: NumberLineLabelForm[];
  cards: NumberLineCardForm[];
}

export interface NumberLineError {
  key: string;
  params?: Record<string, unknown>;
}

export interface NumberLineDefinition {
  interactionType: typeof NUMBER_LINE;
  plan: string;
  solution: string;
}

export const NUMBER_LINE_FORM_LIMITS = NUMBER_LINE_LIMITS;

export function isNumberLineForm(form: { kind: string }): form is NumberLineForm {
  return form.kind === 'numberLine';
}

/** Varsayılan: 0–1 arası 8 bölme, uçlar etiketli, kartsız (boş çeldirici "Boş kart bırakma" hatası veriyordu). */
export function emptyNumberLineForm(): NumberLineForm {
  return {
    kind: 'numberLine',
    divisions: 8,
    tolerance: 0,
    hideTicks: false,
    labels: [
      { at: 0, text: '0' },
      { at: 8, text: '1' },
    ],
    cards: [],
  };
}

/** Hedef metni → çentik; boş = null (çeldirici), geçersiz = undefined. */
export function targetTick(target: string, divisions: number): number | null | undefined {
  const text = target.trim();
  if (!text) return null;
  if (!/^\d+$/.test(text)) return undefined;
  const at = Number(text);
  return at <= divisions ? at : undefined;
}

export function validateNumberLineForm(form: NumberLineForm): NumberLineError[] {
  const errors: NumberLineError[] = [];
  const { maxDivisions, maxCards, maxTolerance, maxLabel, maxCard } = NUMBER_LINE_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  const div = form.divisions;
  const divOk = Number.isInteger(div) && div >= 1 && div <= maxDivisions;
  if (!divOk) push('lineDivisions', { max: maxDivisions });
  if (!Number.isInteger(form.tolerance) || form.tolerance < 0 || form.tolerance > maxTolerance || (divOk && form.tolerance >= div))
    push('lineTolerance', { max: maxTolerance });

  if (form.labels.some((l) => !l.text.trim())) push('lineLabelBlank');
  if (form.labels.some((l) => l.text.trim().length > maxLabel)) push('labelTooLong', { max: maxLabel });
  if (divOk && form.labels.some((l) => !Number.isInteger(l.at) || l.at < 0 || l.at > div)) push('lineLabelTick', { max: div });
  if (new Set(form.labels.map((l) => l.at)).size !== form.labels.length) push('lineLabelDuplicate');

  if (form.cards.length < 1 || form.cards.length > maxCards) push('lineCardCount', { max: maxCards });
  if (form.cards.some((c) => !c.label.trim())) push('blankCard');
  if (form.cards.some((c) => c.label.trim().length > maxCard)) push('labelTooLong', { max: maxCard });
  if (divOk) {
    const bad = form.cards.flatMap((c) => (targetTick(c.target, div) === undefined ? [c.target.trim()] : []));
    if (bad.length) push('lineTargetInvalid', { list: bad.join(', '), max: div });
    else if (form.cards.every((c) => targetTick(c.target, div) === null)) push('lineTargetMissing');
  }
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateNumberLineForm} geçmeli. */
export function buildNumberLineDefinition(form: NumberLineForm): NumberLineDefinition {
  const labels = [...form.labels]
    .map((l) => ({ at: l.at, text: l.text.trim() }))
    .sort((a, b) => a.at - b.at);
  const cards = form.cards.map((c, i) => ({ id: `k${i + 1}`, label: c.label.trim() }));
  const positions: Record<string, number | null> = {};
  form.cards.forEach((c, i) => (positions[`k${i + 1}`] = targetTick(c.target, form.divisions) ?? null));
  const plan: Record<string, unknown> = { version: 1, type: NUMBER_LINE, divisions: form.divisions, labels, cards };
  if (form.tolerance > 0) plan['tolerance'] = form.tolerance;
  if (form.hideTicks) plan['hideTicks'] = true;
  return { interactionType: NUMBER_LINE, plan: JSON.stringify(plan), solution: JSON.stringify({ positions }) };
}

/** Kayıtlı plan + çözüm → form (düzenleme). */
export function parseNumberLineDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): NumberLineForm | null {
  const plan = parseNumberLinePlan(planJson);
  if (!plan) return null;
  const solution = parseNumberLineSolution(solutionJson, plan);
  return {
    kind: 'numberLine',
    divisions: plan.divisions,
    tolerance: plan.tolerance,
    hideTicks: plan.hideTicks,
    labels: plan.labels.map((l) => ({ at: l.at, text: l.text })),
    cards: plan.cards.map((c) => {
      const at = Object.hasOwn(solution, c.id) ? solution[c.id] : null;
      return { label: c.label, target: at === null ? '' : String(at) };
    }),
  };
}
