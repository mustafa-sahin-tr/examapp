/**
 * Issue #634: saat kurmanın (`clockSet`) görselsiz formu — yelkovan adımı (1 / 5 / 15 dk), rakam / dakika çizgisi görünürlüğü ve
 * saatler: etiket ("Başlangıç"), saat (`HH:MM` ya da `H.MM`) ve "kurulu" (öğrenci okusun, oynatılamaz; ör. süre sorusunun başlangıç
 * saati). Kurulu olmayan saatin saati çözüme gider, kurulu saatinki plana.
 *
 * Kurallar backend `ClockSetGrader.ValidateDefinition` ile aynı.
 */
import { CLOCK_SET, CLOCK_SET_LIMITS, formatClockTime, parseClockSetPlan, parseClockSetSolution } from './clock-set';

export interface ClockForm {
  label: string;
  /** `HH:MM`, `H:MM` ya da `H.MM` (0..23 : 0..59). */
  time: string;
  /** Önceden kurulu, oynatılamaz saat. */
  fixed: boolean;
}

export interface ClockSetForm {
  kind: 'clockSet';
  step: number;
  numbers: boolean;
  minuteTicks: boolean;
  clocks: ClockForm[];
}

export interface ClockSetError {
  key: string;
  params?: Record<string, unknown>;
}

export interface ClockSetDefinition {
  interactionType: typeof CLOCK_SET;
  plan: string;
  solution: string;
}

export const CLOCK_SET_FORM_LIMITS = CLOCK_SET_LIMITS;

export function isClockSetForm(form: { kind: string }): form is ClockSetForm {
  return form.kind === 'clockSet';
}

/** Varsayılan: 5 dk adım, rakamlar ve dakika çizgileri açık, bir boş saat. */
export function emptyClockSetForm(): ClockSetForm {
  return { kind: 'clockSet', step: 5, numbers: true, minuteTicks: true, clocks: [{ label: '', time: '', fixed: false }] };
}

/** Formdaki saat metni → `"HH:MM"` (24 saatlik yazım korunur); geçersizse null. `3.45`, `3:45`, `15:45` kabul. */
export function normalizeClockText(text: string): string | null {
  const match = /^(\d{1,2})[:.](\d{2})$/.exec(text.trim());
  if (!match) return null;
  const h = Number(match[1]);
  const m = Number(match[2]);
  if (h > 23 || m > 59) return null;
  return `${String(h).padStart(2, '0')}:${match[2]}`;
}

export function validateClockSetForm(form: ClockSetForm): ClockSetError[] {
  const errors: ClockSetError[] = [];
  const { maxClocks, maxLabel, steps } = CLOCK_SET_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  if (!steps.includes(form.step)) push('clockStepInvalid');
  if (form.clocks.length < 1 || form.clocks.length > maxClocks) push('clockCount', { max: maxClocks });
  if (form.clocks.every((c) => c.fixed)) push('clockNoFree');
  for (const clock of form.clocks) {
    const time = normalizeClockText(clock.time);
    if (time === null) push('clockTimeInvalid');
    else if (!clock.fixed && Number(time.slice(3)) % form.step !== 0) push('clockTimeStep', { step: form.step });
    if (clock.label.trim().length > maxLabel) push('labelTooLong', { max: maxLabel });
  }
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateClockSetForm} geçmeli. */
export function buildClockSetDefinition(form: ClockSetForm): ClockSetDefinition {
  const times: Record<string, string> = {};
  const clocks = form.clocks.map((clock, i) => {
    const id = `c${i + 1}`;
    const time = normalizeClockText(clock.time) ?? '12:00';
    const out: Record<string, unknown> = { id };
    if (clock.label.trim()) out['label'] = clock.label.trim();
    if (clock.fixed) out['fixed'] = time;
    else times[id] = time;
    return out;
  });
  const plan: Record<string, unknown> = { version: 1, type: CLOCK_SET, step: form.step };
  if (!form.numbers) plan['numbers'] = false;
  if (!form.minuteTicks) plan['minuteTicks'] = false;
  plan['clocks'] = clocks;
  return { interactionType: CLOCK_SET, plan: JSON.stringify(plan), solution: JSON.stringify({ times }) };
}

/** Kayıtlı plan + çözüm → form (düzenleme). 24 saatlik yazım kayıtta korunmaz: saatler 12 saatlik (`03:45`) açılır. */
export function parseClockSetDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): ClockSetForm | null {
  const plan = parseClockSetPlan(planJson);
  if (!plan) return null;
  const targets = parseClockSetSolution(solutionJson);
  const rawTimes = rawSolutionTimes(solutionJson);
  return {
    kind: 'clockSet',
    step: plan.step,
    numbers: plan.numbers,
    minuteTicks: plan.minuteTicks,
    clocks: plan.clocks.map((clock) => ({
      label: clock.label ?? '',
      time: clock.fixed != null
        ? (rawFixed(planJson, clock.id) ?? formatClockTime(clock.fixed))
        : (rawTimes[clock.id] ?? (targets[clock.id] != null ? formatClockTime(targets[clock.id]) : '')),
      fixed: clock.fixed != null,
    })),
  };
}

/** Çözümdeki saatlerin yazıldığı hâli (15:45 15:45 olarak açılsın). */
function rawSolutionTimes(json: string | null | undefined): Record<string, string> {
  try {
    const times = json ? (JSON.parse(json) as Record<string, unknown>)['times'] : null;
    if (!times || typeof times !== 'object') return {};
    return Object.fromEntries(Object.entries(times as Record<string, unknown>).filter(([, v]) => typeof v === 'string')) as Record<string, string>;
  } catch {
    return {};
  }
}

function rawFixed(json: string | null | undefined, id: string): string | null {
  try {
    const clocks = json ? (JSON.parse(json) as Record<string, unknown>)['clocks'] : null;
    const found = Array.isArray(clocks) ? (clocks as Record<string, unknown>[]).find((c) => c?.['id'] === id) : null;
    return typeof found?.['fixed'] === 'string' ? (found['fixed'] as string) : null;
  } catch {
    return null;
  }
}
