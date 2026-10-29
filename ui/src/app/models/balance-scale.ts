/**
 * Issue #632: terazi dengeleme (`InteractionType = balanceScale`). "Aşağıdaki terazileri ağırlıkları kullanarak dengeye getiriniz"
 * (Süper Combo Mat s.203). Karar #589'da.
 *
 * Plan: kefelerdeki hazır yükler (`left` / `right`), öğrencinin kefelere koyacağı ağırlıklar (`cards`, değer görünür), birim ve
 * `hideTilt` (terazi kontrol edilene dek düz durur). Çözüm kuraldır (`{"useAll":bool}`): kefeler eşitse denge, birden çok geçerli
 * yerleşim. Öğrenci kartları kefelere koyar (`{"left":[ids],"right":[ids]}`); kontrol backend `BalanceScaleGrader`. Puansız.
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const BALANCE_SCALE = 'balanceScale' as const;

/** Backend `BalanceScaleGrader` sınırlarıyla aynı. */
export const BALANCE_SCALE_LIMITS = {
  maxValue: 10000,
  maxFixedPerPan: 6,
  maxCards: 12,
  maxLabel: 30,
  maxUnit: 10,
} as const;

export interface BalanceWeight {
  id: string;
  value: number;
  /** Görünen metin; yoksa değer + birim. */
  label: string | null;
}

export interface BalanceScalePlanV1 {
  version: 1;
  type: typeof BALANCE_SCALE;
  unit: string | null;
  left: BalanceWeight[];
  right: BalanceWeight[];
  cards: BalanceWeight[];
  hideTilt: boolean;
}

export type BalancePan = 'left' | 'right';

/** Kefelerdeki kart id'leri (sırayla). */
export interface BalancePans {
  left: string[];
  right: string[];
}

/** Ağır basan kefe; `level` = denge. */
export type BalanceTilt = 'level' | 'left' | 'right';

export interface BalanceScaleFeedback {
  correct: boolean;
  tilt: BalanceTilt;
  /** Yalnız "hepsini kullan" kuralında: kefeye konmamış kart sayısı. */
  unused: number | null;
}

export function isBalanceScaleType(type: unknown): boolean {
  return isInteractionType(type, BALANCE_SCALE);
}

const isValue = (value: unknown): value is number =>
  Number.isInteger(value) && (value as number) >= 1 && (value as number) <= BALANCE_SCALE_LIMITS.maxValue;

function parseWeights(raw: unknown, ids: Set<string>, max: number): BalanceWeight[] {
  const out: BalanceWeight[] = [];
  for (const item of Array.isArray(raw) ? (raw as unknown[]) : []) {
    if (!item || typeof item !== 'object') continue;
    const { id, value, label } = item as Record<string, unknown>;
    if (typeof id !== 'string' || !isValidInteractionId(id) || ids.has(id) || !isValue(value)) continue;
    ids.add(id);
    out.push({ id, value, label: typeof label === 'string' && label.trim() ? label.trim() : null });
  }
  return out.slice(0, max);
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı ya da kart yoksa null. Bozuk ağırlık atlanır. */
export function parseBalanceScalePlan(json: string | null | undefined): BalanceScalePlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isBalanceScaleType(parsed['type'])) return null;
    const { maxFixedPerPan, maxCards } = BALANCE_SCALE_LIMITS;
    const ids = new Set<string>();
    const left = parseWeights(parsed['left'], ids, maxFixedPerPan);
    const right = parseWeights(parsed['right'], ids, maxFixedPerPan);
    const cards = parseWeights(parsed['cards'], ids, maxCards);
    if (!cards.length) return null;
    const unit = typeof parsed['unit'] === 'string' && parsed['unit'].trim() ? parsed['unit'].trim() : null;
    return { version: 1, type: BALANCE_SCALE, unit, left, right, cards, hideTilt: parsed['hideTilt'] === true };
  } catch {
    return null;
  }
}

/** Çözüm JSON'u → "hepsini kullan"; bozuksa null. */
export function parseBalanceScaleSolution(json: string | null | undefined): { useAll: boolean } | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || typeof parsed['useAll'] !== 'boolean') return null;
    return { useAll: parsed['useAll'] };
  } catch {
    return null;
  }
}

export function weightText(weight: BalanceWeight, unit: string | null): string {
  return weight.label ?? (unit ? `${weight.value} ${unit}` : String(weight.value));
}

/** Öğrenci cevabı → kefeler; plan dışı ya da yinelenen kart atlanır. Başka tür / bozuksa boş. */
export function parseBalanceScaleAnswer(json: string | null | undefined, plan: BalanceScalePlanV1 | null): BalancePans {
  const empty: BalancePans = { left: [], right: [] };
  if (!plan || !json) return empty;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isBalanceScaleType(parsed['type'])) return empty;
    const cardIds = new Set(plan.cards.map((c) => c.id));
    const seen = new Set<string>();
    const side = (raw: unknown) =>
      (Array.isArray(raw) ? (raw as unknown[]) : []).flatMap((id) => {
        if (typeof id !== 'string' || !cardIds.has(id) || seen.has(id)) return [];
        seen.add(id);
        return [id];
      });
    return { left: side(parsed['left']), right: side(parsed['right']) };
  } catch {
    return empty;
  }
}

export function buildBalanceScalePayload(pans: BalancePans): string {
  return JSON.stringify({ version: 1, type: BALANCE_SCALE, left: [...pans.left], right: [...pans.right] });
}

/** İki kefe de boş: sunucu boş cevap sayar. Başka tür false. */
export function isBlankBalanceScalePayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isBalanceScaleType(parsed['type'])) return false;
    const count = (raw: unknown) => (Array.isArray(raw) ? raw.length : 0);
    return count(parsed['left']) + count(parsed['right']) === 0;
  } catch {
    return false;
  }
}

export function parseBalanceScaleFeedback(json: string | null | undefined): BalanceScaleFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    const row = (parsed['rows'] as unknown[])[0] as Record<string, unknown> | undefined;
    const tilt = parsed['tilt'];
    const unused = parsed['unused'];
    return {
      correct: row?.['correct'] === true,
      tilt: tilt === 'left' || tilt === 'right' ? tilt : 'level',
      unused: Number.isInteger(unused) && (unused as number) >= 0 ? (unused as number) : null,
    };
  } catch {
    return null;
  }
}

/** Kefe toplamları (hazır yükler + kartlar). */
export function panTotals(plan: BalanceScalePlanV1, pans: BalancePans): { left: number; right: number } {
  const values = new Map(plan.cards.map((c) => [c.id, c.value]));
  const sum = (fixed: BalanceWeight[], ids: string[]) =>
    fixed.reduce((s, w) => s + w.value, 0) + ids.reduce((s, id) => s + (values.get(id) ?? 0), 0);
  return { left: sum(plan.left, pans.left), right: sum(plan.right, pans.right) };
}

export function tiltOf(totals: { left: number; right: number }): BalanceTilt {
  return totals.left === totals.right ? 'level' : totals.left > totals.right ? 'left' : 'right';
}

/** Backend `IsBalanced` ile aynı: en az bir kart, toplamlar eşit, "hepsini kullan"da kart kalmamış. */
export function isBalanced(plan: BalanceScalePlanV1, pans: BalancePans, useAll: boolean): boolean {
  const placed = pans.left.length + pans.right.length;
  if (placed === 0 || (useAll && placed < plan.cards.length)) return false;
  const totals = panTotals(plan, pans);
  return totals.left === totals.right;
}

/**
 * Kurala uyan bir yerleşim (çözüm gösterimi ve editör denetimi). Kart başına sol / sağ / dışarı (useAll'da dışarı yok) denenir; en az
 * kart kullanan değil, plan sırasıyla ilk bulunan döner. 12 kartta en çok 3^12 ≈ 531 bin durum; bulunamazsa null.
 */
export function findBalance(plan: Pick<BalanceScalePlanV1, 'left' | 'right' | 'cards'>, useAll: boolean): BalancePans | null {
  const start = plan.left.reduce((s, w) => s + w.value, 0) - plan.right.reduce((s, w) => s + w.value, 0);
  const cards = plan.cards;
  const choice: (BalancePan | null)[] = new Array(cards.length).fill(null);
  const options: (BalancePan | null)[] = useAll ? ['left', 'right'] : ['left', 'right', null];
  const walk = (i: number, diff: number, placed: number): boolean => {
    if (i === cards.length) return diff === 0 && placed > 0;
    for (const o of options) {
      choice[i] = o;
      const next = o === 'left' ? diff + cards[i].value : o === 'right' ? diff - cards[i].value : diff;
      if (walk(i + 1, next, placed + (o ? 1 : 0))) return true;
    }
    return false;
  };
  if (!walk(0, start, 0)) return null;
  return {
    left: cards.filter((_, i) => choice[i] === 'left').map((c) => c.id),
    right: cards.filter((_, i) => choice[i] === 'right').map((c) => c.id),
  };
}
