/**
 * Issue #634: analog saat kurma (`InteractionType = clockSet`). "Saat 3.45'i gösteriniz", "Ders 40 dakika sonra bitiyor; bitiş
 * saatini saatte gösteriniz". Saati okuma ve süre hesabı `slotFill` ile kalır.
 *
 * Plan: 1–6 saat (etiket, isteğe bağlı sabit = önceden kurulmuş, oynatılamaz saat), yelkovan adımı (1 / 5 / 15 dk), rakam ve dakika
 * çizgisi görünürlüğü. Çözüm `{"times":{"c1":"03:45"}}` (12 saatlik kadran: 15:45 = 3:45). Öğrenci akrebi ve yelkovanı ayrı çevirir
 * (karar #634): payload `{"clocks":{"c1":{"h":225,"m":45}}}` — `h` akrebin 12 saatlik kadranda dakika cinsinden konumu (0..719),
 * `m` yelkovan (0..59). Kontrol backend `ClockSetGrader` (yelkovan tam, akrep ±7,5°). Puansız.
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const CLOCK_SET = 'clockSet' as const;

/** Backend `ClockSetGrader` sınırlarıyla aynı. */
export const CLOCK_SET_LIMITS = {
  maxClocks: 6,
  maxLabel: 40,
  dialUnits: 720,
  hourTolerance: 15,
  steps: [1, 5, 15] as readonly number[],
} as const;

/** Akrebin klavye / sürükleme adımı (kadran birimi): 5 = 2,5° — saat aralığının 1/12'si. */
export const HOUR_HAND_STEP = 5;

export interface ClockFace {
  id: string;
  label: string | null;
  /** Sabit saat: kadran dakikası (0..719); null = öğrenci kurar. */
  fixed: number | null;
}

export interface ClockSetPlanV1 {
  version: 1;
  type: typeof CLOCK_SET;
  step: number;
  numbers: boolean;
  minuteTicks: boolean;
  clocks: ClockFace[];
}

/** Akrep (0..719 kadran birimi) ve yelkovan (0..59). */
export interface ClockHands {
  h: number;
  m: number;
}

export type ClockAnswer = Record<string, ClockHands>;

export function isClockSetType(type: unknown): boolean {
  return isInteractionType(type, CLOCK_SET);
}

/** `"HH:MM"` (0..23 : 0..59) → 12 saatlik kadranda dakika; geçersizse null. Backend `TryParseTime` ile aynı. */
export function parseClockTime(text: unknown): number | null {
  if (typeof text !== 'string' || !/^\d{2}:\d{2}$/.test(text)) return null;
  const h = Number(text.slice(0, 2));
  const m = Number(text.slice(3, 5));
  if (h > 23 || m > 59) return null;
  return (h % 12) * 60 + m;
}

/** Kadran dakikası → `"HH:MM"` (12 → `"12:MM"`). */
export function formatClockTime(dialMinutes: number): string {
  const total = ((dialMinutes % 720) + 720) % 720;
  const h = Math.floor(total / 60);
  return `${String(h === 0 ? 12 : h).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

/** Saatin doğru elleri (akrep = kadran dakikası, yelkovan = dakika). */
export function handsFor(dialMinutes: number): ClockHands {
  const total = ((dialMinutes % 720) + 720) % 720;
  return { h: total, m: total % 60 };
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı ya da serbest saat yoksa null. Bozuk saat atlanır. */
export function parseClockSetPlan(json: string | null | undefined): ClockSetPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isClockSetType(parsed['type'])) return null;
    const step = CLOCK_SET_LIMITS.steps.includes(parsed['step'] as number) ? (parsed['step'] as number) : 5;
    const ids = new Set<string>();
    const clocks: ClockFace[] = [];
    for (const item of Array.isArray(parsed['clocks']) ? (parsed['clocks'] as unknown[]) : []) {
      if (!item || typeof item !== 'object') continue;
      const { id, label, fixed } = item as Record<string, unknown>;
      if (typeof id !== 'string' || !isValidInteractionId(id) || ids.has(id)) continue;
      const fixedMinutes = fixed == null ? null : parseClockTime(fixed);
      if (fixed != null && fixedMinutes == null) continue;
      ids.add(id);
      clocks.push({ id, label: typeof label === 'string' && label.trim() ? label.trim() : null, fixed: fixedMinutes });
    }
    if (!clocks.length || clocks.length > CLOCK_SET_LIMITS.maxClocks || clocks.every((c) => c.fixed != null)) return null;
    return {
      version: 1,
      type: CLOCK_SET,
      step,
      numbers: parsed['numbers'] !== false,
      minuteTicks: parsed['minuteTicks'] !== false,
      clocks,
    };
  } catch {
    return null;
  }
}

/** Çözüm JSON'u → serbest saat id → kadran dakikası; bozuksa boş. */
export function parseClockSetSolution(json: string | null | undefined): Record<string, number> {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const times = parsed && typeof parsed === 'object' ? parsed['times'] : null;
    if (!times || typeof times !== 'object') return {};
    const out: Record<string, number> = {};
    for (const [id, value] of Object.entries(times as Record<string, unknown>)) {
      const minutes = parseClockTime(value);
      if (isValidInteractionId(id) && minutes != null) out[id] = minutes;
    }
    return out;
  } catch {
    return {};
  }
}

const isHour = (v: unknown): v is number => Number.isInteger(v) && (v as number) >= 0 && (v as number) < 720;
const isMinute = (v: unknown): v is number => Number.isInteger(v) && (v as number) >= 0 && (v as number) <= 59;

/** Öğrenci cevabı → saat id → eller; plan dışı / sabit saat ya da bozuk değer atlanır. Başka tür / bozuksa boş. */
export function parseClockSetAnswer(json: string | null | undefined, plan: ClockSetPlanV1 | null): ClockAnswer {
  if (!plan || !json) return {};
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isClockSetType(parsed['type'])) return {};
    const clocks = parsed['clocks'];
    if (!clocks || typeof clocks !== 'object') return {};
    const free = new Set(plan.clocks.filter((c) => c.fixed == null).map((c) => c.id));
    const out: ClockAnswer = {};
    for (const [id, value] of Object.entries(clocks as Record<string, unknown>)) {
      if (!free.has(id) || !value || typeof value !== 'object') continue;
      const { h, m } = value as Record<string, unknown>;
      if (isHour(h) && isMinute(m) && m % plan.step === 0) out[id] = { h, m };
    }
    return out;
  } catch {
    return {};
  }
}

export function buildClockSetPayload(answer: ClockAnswer): string {
  const clocks: ClockAnswer = {};
  for (const [id, hands] of Object.entries(answer)) clocks[id] = { h: hands.h, m: hands.m };
  return JSON.stringify({ version: 1, type: CLOCK_SET, clocks });
}

/** Saatsiz payload: sunucu boş cevap sayar. Başka tür false. */
export function isBlankClockSetPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isClockSetType(parsed['type'])) return false;
    const clocks = parsed['clocks'];
    return !clocks || typeof clocks !== 'object' || Object.keys(clocks).length === 0;
  } catch {
    return false;
  }
}

/** Geri bildirim → saat id → doğru mu; bozuksa null. */
export function parseClockSetFeedback(json: string | null | undefined): Record<string, boolean> | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    const out: Record<string, boolean> = {};
    for (const row of parsed['rows'] as unknown[]) {
      if (!row || typeof row !== 'object') continue;
      const { id, correct } = row as Record<string, unknown>;
      if (typeof id === 'string') out[id] = correct === true;
    }
    return out;
  } catch {
    return null;
  }
}

/** Backend `Matches` ile aynı: yelkovan hedef dakikada, akrep hedef konumun ±15 birim içinde (12'nin etrafında döner). */
export function clockMatches(hands: ClockHands, target: number): boolean {
  if (hands.m !== target % 60) return false;
  const diff = Math.abs(hands.h - target) % 720;
  return Math.min(diff, 720 - diff) <= CLOCK_SET_LIMITS.hourTolerance;
}

/** Kadran birimini (akrep 0..719 / yelkovan 0..59 × 12) 12'den saat yönünde açıya (derece) çevirir. */
export function hourAngle(h: number): number {
  return (h / 720) * 360;
}

export function minuteAngle(m: number): number {
  return (m / 60) * 360;
}

/**
 * İşaretçi açısı (12'den saat yönünde, derece) → el değeri, adıma yuvarlanmış. Akrep 0..719 (adım {@link HOUR_HAND_STEP}),
 * yelkovan 0..59 (planın adımı).
 */
export function snapHour(angle: number): number {
  const units = Math.round(((((angle % 360) + 360) % 360) / 360) * 720 / HOUR_HAND_STEP) * HOUR_HAND_STEP;
  return units % 720;
}

export function snapMinute(angle: number, step: number): number {
  const units = Math.round(((((angle % 360) + 360) % 360) / 360) * 60 / step) * step;
  return units % 60;
}

/** Klavye: eli `delta` adım çevirir (döner). */
export function turnHour(h: number, delta: number): number {
  return (((h + delta * HOUR_HAND_STEP) % 720) + 720) % 720;
}

export function turnMinute(m: number, delta: number, step: number): number {
  return (((m + delta * step) % 60) + 60) % 60;
}
