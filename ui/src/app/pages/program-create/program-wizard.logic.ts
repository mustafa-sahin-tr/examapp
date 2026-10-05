import { Option, ProgramStep } from '../../models/programstep';

/**
 * Program oluşturma sihirbazının saf (komponentten bağımsız) kuralları — issue #135.
 * Dallanma kuralı eski `next()` ile aynıdır: seçenek sırasına göre ilk seçili seçeneğin `nextStep`'i;
 * `null` → form; `steps` içinde karşılığı olmayan `nextStep` → form (savunma, issue #136).
 */

/** Adım id → seçili değerler. Her değişiklikte yeni Map üretilir (signal eşitliği referansa bakar). */
export type WizardAnswers = ReadonlyMap<number, readonly string[]>;

export type NextTarget =
  | { kind: 'step'; stepId: number }
  | { kind: 'form' }
  | { kind: 'invalid'; nextStep: number };

export interface RemainingRange {
  /** Şu anki adımdan sonra en az kaç soru kaldığı. */
  min: number;
  /** Şu anki adımdan sonra en fazla kaç soru kaldığı. */
  max: number;
}

/** Seçim sırasından bağımsız, adımdaki seçenek sırasıyla seçili seçenekler. */
export function selectedOptions(step: ProgramStep, values: readonly string[]): Option[] {
  return step.options.filter((o) => values.includes(o.value));
}

/** Seçili seçeneklere göre bir sonraki hedef. Seçim yoksa form (çağıran bunu önceden engeller). */
export function resolveNext(
  stepsById: ReadonlyMap<number, ProgramStep>,
  step: ProgramStep,
  values: readonly string[],
): NextTarget {
  const first = selectedOptions(step, values)[0];
  const nextStep = first?.nextStep;
  if (!nextStep) return { kind: 'form' };
  return stepsById.has(nextStep) ? { kind: 'step', stepId: nextStep } : { kind: 'invalid', nextStep };
}

/** `startId`'den `nextStep` grafiğinde (her seçenek dikkate alınarak) ulaşılabilen adımlar; döngü korumalı. */
export function reachableFrom(stepsById: ReadonlyMap<number, ProgramStep>, startId: number): Set<number> {
  const seen = new Set<number>();
  const stack = [startId];
  while (stack.length > 0) {
    const id = stack.pop()!;
    const step = stepsById.get(id);
    if (!step || seen.has(id)) continue;
    seen.add(id);
    for (const option of step.options) {
      if (option.nextStep) stack.push(option.nextStep);
    }
  }
  return seen;
}

/**
 * `fromId` adımından sonra kalan soru sayısı aralığı. Cevaplanmış adımda yalnız gerçek dal
 * (ilk seçili seçeneğin `nextStep`'i), cevapsız adımda tüm dallar dikkate alınır. Geçersiz/eksik
 * `nextStep` ve döngü formda biter sayılır.
 */
export function remainingRange(
  stepsById: ReadonlyMap<number, ProgramStep>,
  fromId: number,
  answers: WizardAnswers,
): RemainingRange {
  const visit = (id: number, onPath: ReadonlySet<number>): RemainingRange => {
    const step = stepsById.get(id);
    if (!step) return { min: 0, max: 0 };
    const values = answers.get(id) ?? [];
    const candidates = values.length > 0 ? selectedOptions(step, values).slice(0, 1) : step.options;
    const nextIds = new Set<number | null>(candidates.map((o) => o.nextStep ?? null));
    if (nextIds.size === 0) return { min: 0, max: 0 };

    const nextOnPath = new Set(onPath).add(id);
    let min = Number.POSITIVE_INFINITY;
    let max = 0;
    for (const nextId of nextIds) {
      if (nextId === null || !stepsById.has(nextId) || nextOnPath.has(nextId)) {
        min = Math.min(min, 0);
        continue;
      }
      const sub = visit(nextId, nextOnPath);
      min = Math.min(min, sub.min + 1);
      max = Math.max(max, sub.max + 1);
    }
    return { min, max };
  };
  return visit(fromId, new Set());
}

/** Adımdaki seçeneklerin farklı adımlara dallanıp dallanmadığı. */
export function hasDistinctBranches(step: ProgramStep): boolean {
  return new Set(step.options.map((o) => o.nextStep ?? null)).size > 1;
}

/**
 * Çoklu seçimde diğerlerini dışlayan "Yok" seçeneklerinin değerleri (Karar 1) — backend sözleşmesiyle
 * aynı kaynak: `api/ExamApp.Api/Services/ProgramService.cs` `CreateProgram` switch'i, `case 6` "8"
 * ("8" means "Yok") ve `case 7` "5" ("5" means "Yok") değerlerini dinlenme günü / zor ders listesinden
 * çıkarır. Bu eşleme orada değişirse burada da değişmeli.
 */
export const EXCLUSIVE_OPTION_VALUES: ReadonlyMap<number, string> = new Map([
  [6, '8'],
  [7, '5'],
]);

/**
 * Seçenek diğerlerini dışlıyor mu? Eşlemede bulunan adımlarda yalnız stepId + value belirleyicidir;
 * eşlemede olmayan adımlarda (yeni seed) etiket `yok` / `none` yedek olarak kullanılır.
 */
export function isExclusiveOption(step: ProgramStep, option: Option): boolean {
  const mapped = EXCLUSIVE_OPTION_VALUES.get(step.id);
  if (mapped !== undefined) return option.value === mapped;
  const label = option.label.trim().toLocaleLowerCase('tr');
  return label === 'yok' || label === 'none';
}

/**
 * Bir seçeneğe tıklanınca yeni seçili değerler. Tek seçimde değiştirir; çoklu seçimde aç/kapat,
 * "Yok" seçilince diğerleri kalkar, diğerinden biri seçilince "Yok" kalkar.
 */
export function toggleValue(step: ProgramStep, values: readonly string[], option: Option): string[] {
  if (!step.multiple) return [option.value];
  if (values.includes(option.value)) return values.filter((v) => v !== option.value);
  if (isExclusiveOption(step, option)) return [option.value];

  const exclusive = new Set(step.options.filter((o) => isExclusiveOption(step, o)).map((o) => o.value));
  const kept = values.filter((v) => !exclusive.has(v));
  // Seçenek sırasını koru: payload ve özet adım sırasına göre okunur.
  return step.options.map((o) => o.value).filter((v) => v === option.value || kept.includes(v));
}
