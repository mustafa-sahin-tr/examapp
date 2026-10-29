/**
 * Issue #632: terazi dengelemenin (`balanceScale`) görselsiz formu — birim, öğrencinin koyacağı ağırlıklar (değer + isteğe bağlı
 * etiket), kefelerdeki hazır yükler, "hepsini kullan" kuralı ve "terazi kontrol edilene dek düz dursun". Ağırlıklar formdaki sırayla
 * görünür. Kural çözüme gider; kurala uyan bir yerleşim yoksa kaydedilmez.
 *
 * Kurallar backend `BalanceScaleGrader.ValidateDefinition` ile aynı.
 */
import {
  BALANCE_SCALE,
  BALANCE_SCALE_LIMITS,
  BalanceWeight,
  findBalance,
  parseBalanceScalePlan,
  parseBalanceScaleSolution,
} from './balance-scale';

export interface BalanceWeightForm {
  /** Tam sayı metni (1..10000). */
  value: string;
  /** Görünen metin; boş = değer + birim. */
  label: string;
}

export interface BalanceScaleForm {
  kind: 'balanceScale';
  unit: string;
  cards: BalanceWeightForm[];
  left: BalanceWeightForm[];
  right: BalanceWeightForm[];
  useAll: boolean;
  hideTilt: boolean;
}

export interface BalanceScaleError {
  key: string;
  params?: Record<string, unknown>;
}

export interface BalanceScaleDefinition {
  interactionType: typeof BALANCE_SCALE;
  plan: string;
  solution: string;
}

export const BALANCE_SCALE_FORM_LIMITS = BALANCE_SCALE_LIMITS;

export function isBalanceScaleForm(form: { kind: string }): form is BalanceScaleForm {
  return form.kind === 'balanceScale';
}

/** Varsayılan: kg, dört boş ağırlık, hazır yük yok, hepsini kullan. */
export function emptyBalanceScaleForm(): BalanceScaleForm {
  return {
    kind: 'balanceScale',
    unit: 'kg',
    cards: Array.from({ length: 4 }, () => ({ value: '', label: '' })),
    left: [],
    right: [],
    useAll: true,
    hideTilt: false,
  };
}

/** Değer metni → tam sayı; geçersizse null. */
export function weightValue(text: string): number | null {
  const t = text.trim();
  if (!/^\d+$/.test(t)) return null;
  const n = Number(t);
  return n >= 1 && n <= BALANCE_SCALE_LIMITS.maxValue ? n : null;
}

function toWeights(list: BalanceWeightForm[], prefix: string): BalanceWeight[] {
  return list.map((w, i) => ({ id: `${prefix}${i + 1}`, value: weightValue(w.value) ?? 0, label: w.label.trim() || null }));
}

/** Formdaki ağırlıklar plan id'leriyle (`k1`…, `l1`…, `r1`…); geçersiz değer 0. */
export function balanceFormWeights(form: BalanceScaleForm): { cards: BalanceWeight[]; left: BalanceWeight[]; right: BalanceWeight[] } {
  return { cards: toWeights(form.cards, 'k'), left: toWeights(form.left, 'l'), right: toWeights(form.right, 'r') };
}

export function validateBalanceScaleForm(form: BalanceScaleForm): BalanceScaleError[] {
  const errors: BalanceScaleError[] = [];
  const { maxCards, maxFixedPerPan, maxLabel, maxUnit, maxValue } = BALANCE_SCALE_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  if (form.unit.trim().length > maxUnit) push('balanceUnitTooLong', { max: maxUnit });
  if (form.cards.length < 1 || form.cards.length > maxCards) push('balanceCardCount', { max: maxCards });
  if (form.left.length > maxFixedPerPan || form.right.length > maxFixedPerPan) push('balanceFixedCount', { max: maxFixedPerPan });
  const all = [...form.cards, ...form.left, ...form.right];
  if (all.some((w) => weightValue(w.value) === null)) push('balanceValueInvalid', { max: maxValue });
  if (all.some((w) => w.label.trim().length > maxLabel)) push('labelTooLong', { max: maxLabel });
  if (!errors.length && !findBalance(balanceFormWeights(form), form.useAll))
    push(form.useAll ? 'balanceUnsolvableAll' : 'balanceUnsolvable');
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateBalanceScaleForm} geçmeli. */
export function buildBalanceScaleDefinition(form: BalanceScaleForm): BalanceScaleDefinition {
  const out = (w: BalanceWeight) => (w.label ? { id: w.id, value: w.value, label: w.label } : { id: w.id, value: w.value });
  const weights = balanceFormWeights(form);
  const plan: Record<string, unknown> = { version: 1, type: BALANCE_SCALE };
  if (form.unit.trim()) plan['unit'] = form.unit.trim();
  if (weights.left.length) plan['left'] = weights.left.map(out);
  if (weights.right.length) plan['right'] = weights.right.map(out);
  plan['cards'] = weights.cards.map(out);
  if (form.hideTilt) plan['hideTilt'] = true;
  return { interactionType: BALANCE_SCALE, plan: JSON.stringify(plan), solution: JSON.stringify({ useAll: form.useAll }) };
}

/** Kayıtlı plan + çözüm → form (düzenleme). */
export function parseBalanceScaleDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): BalanceScaleForm | null {
  const plan = parseBalanceScalePlan(planJson);
  if (!plan) return null;
  const toForm = (w: BalanceWeight): BalanceWeightForm => ({ value: String(w.value), label: w.label ?? '' });
  return {
    kind: 'balanceScale',
    unit: plan.unit ?? '',
    cards: plan.cards.map(toForm),
    left: plan.left.map(toForm),
    right: plan.right.map(toForm),
    useAll: parseBalanceScaleSolution(solutionJson)?.useAll ?? true,
    hideTilt: plan.hideTilt,
  };
}
