/**
 * Issue #588: sayı doğrusu (`InteractionType = numberLine`, bileşen F ızgara tuvali ailesi). "Kesir kartlarını sayı doğrusuna
 * yerleştirelim", olasılık spektrumu ("imkânsız … kesin"), tahmin ("0,37'yi yaklaşık işaretle").
 *
 * Plan: doğru `divisions` eş parçaya bölünür (çentikler 0..divisions), çentik etiketleri (`labels`), kartlar, tolerans (çentik) ve
 * `hideTicks` (etiketsiz çentikler çizilmez). Çözüm (`{"positions":{"k1":3,"k2":null}}`; null = çeldirici) öğrenciye gitmez (#570).
 * Öğrenci kartları çentiklere koyar (`{"placed":{"k1":3}}`); kontrol çentiğe tolerans içinde bakar (backend `NumberLineGrader`). Puansız.
 */
import { isInteractionType, isValidInteractionId } from './interaction-ids';

export const NUMBER_LINE = 'numberLine' as const;

/** Backend `NumberLineGrader` sınırlarıyla aynı. */
export const NUMBER_LINE_LIMITS = {
  maxDivisions: 100,
  maxCards: 20,
  maxTolerance: 10,
  maxLabel: 30,
  maxCard: 40,
} as const;

export interface NumberLineCard {
  id: string;
  label: string;
}

export interface NumberLineLabel {
  at: number;
  text: string;
}

export interface NumberLinePlanV1 {
  version: 1;
  type: typeof NUMBER_LINE;
  divisions: number;
  labels: NumberLineLabel[];
  cards: NumberLineCard[];
  tolerance: number;
  hideTicks: boolean;
}

/** Kart id'si → çentik. */
export type NumberLinePlacement = Record<string, number>;

export interface NumberLineFeedback {
  rows: { id: string; correct: boolean }[];
}

export function isNumberLineType(type: unknown): boolean {
  return isInteractionType(type, NUMBER_LINE);
}

const isTick = (value: unknown, divisions: number): value is number =>
  Number.isInteger(value) && (value as number) >= 0 && (value as number) <= divisions;

/** Plan JSON'unu güvenli ayrıştırır; tür farklı, bölme sınır dışı ya da kart yoksa null. Bozuk etiket / kart atlanır. */
export function parseNumberLinePlan(json: string | null | undefined): NumberLinePlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isNumberLineType(parsed['type'])) return null;
    const { maxDivisions, maxCards, maxTolerance } = NUMBER_LINE_LIMITS;
    const divisions = parsed['divisions'];
    if (!Number.isInteger(divisions) || (divisions as number) < 1 || (divisions as number) > maxDivisions) return null;
    const div = divisions as number;
    const tol = parsed['tolerance'];
    const tolerance = Number.isInteger(tol) && (tol as number) >= 0 && (tol as number) <= maxTolerance ? (tol as number) : 0;

    const labels: NumberLineLabel[] = [];
    for (const raw of Array.isArray(parsed['labels']) ? (parsed['labels'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const { at, text } = raw as Record<string, unknown>;
      if (!isTick(at, div) || typeof text !== 'string' || !text || labels.some((l) => l.at === at)) continue;
      labels.push({ at, text });
    }
    labels.sort((a, b) => a.at - b.at);

    const ids = new Set<string>();
    const cards: NumberLineCard[] = [];
    for (const raw of Array.isArray(parsed['cards']) ? (parsed['cards'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const { id, label } = raw as Record<string, unknown>;
      if (!isValidInteractionId(id) || ids.has(id) || typeof label !== 'string' || !label.trim()) continue;
      ids.add(id);
      cards.push({ id, label });
    }
    if (cards.length === 0 || cards.length > maxCards) return null;
    return { version: 1, type: NUMBER_LINE, divisions: div, labels, cards, tolerance, hideTicks: parsed['hideTicks'] === true };
  } catch {
    return null;
  }
}

/** Öğrenci cevabı → yerleşim; plana uymayan kart / çentik atlanır. Başka tür / bozuksa boş. */
export function parseNumberLineAnswer(json: string | null | undefined, plan: NumberLinePlanV1 | null): NumberLinePlacement {
  const out = Object.create(null) as NumberLinePlacement;
  if (!plan || !json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const placed = parsed && typeof parsed === 'object' && isNumberLineType(parsed['type']) ? parsed['placed'] : null;
    if (!placed || typeof placed !== 'object' || Array.isArray(placed)) return out;
    for (const [id, at] of Object.entries(placed as Record<string, unknown>)) {
      if (plan.cards.some((c) => c.id === id) && isTick(at, plan.divisions)) out[id] = at;
    }
    return out;
  } catch {
    return out;
  }
}

/** Payload: kartlar plan sırasıyla (kararlı JSON). */
export function buildNumberLinePayload(plan: NumberLinePlanV1, placement: NumberLinePlacement): string {
  const placed: NumberLinePlacement = {};
  for (const card of plan.cards) if (Object.hasOwn(placement, card.id)) placed[card.id] = placement[card.id];
  return JSON.stringify({ version: 1, type: NUMBER_LINE, placed });
}

/** Çözüm JSON'u: kart id'si → hedef çentik ya da null (çeldirici); plana uymayan atlanır. */
export function parseNumberLineSolution(json: string | null | undefined, plan?: NumberLinePlanV1 | null): Record<string, number | null> {
  const out = Object.create(null) as Record<string, number | null>;
  if (!json) return out;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    const positions = parsed && typeof parsed === 'object' ? parsed['positions'] : null;
    if (!positions || typeof positions !== 'object' || Array.isArray(positions)) return out;
    for (const [id, at] of Object.entries(positions as Record<string, unknown>)) {
      if (!isValidInteractionId(id) || (plan && !plan.cards.some((c) => c.id === id))) continue;
      if (at === null) out[id] = null;
      else if (isTick(at, plan?.divisions ?? NUMBER_LINE_LIMITS.maxDivisions)) out[id] = at;
    }
    return out;
  } catch {
    return out;
  }
}

/** Çözüm gösterimi: hedefli kartlar hedef çentikte; çeldiriciler havuzda. */
export function solutionPlacement(solution: Record<string, number | null>): NumberLinePlacement {
  const out = Object.create(null) as NumberLinePlacement;
  for (const [id, at] of Object.entries(solution)) if (at !== null) out[id] = at;
  return out;
}

/** Çentiğin görünen adı: etiketi, yoksa `çentik/bölme` (ör. "3/8"). */
export function tickName(plan: NumberLinePlanV1, at: number): string {
  return plan.labels.find((l) => l.at === at)?.text ?? `${at}/${plan.divisions}`;
}

/** Kartsız cevap: sunucu boş cevap sayar. Başka tür false. */
export function isBlankNumberLinePayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isNumberLineType(parsed['type'])) return false;
    const placed = parsed['placed'];
    return !placed || typeof placed !== 'object' || Object.keys(placed).length === 0;
  } catch {
    return false;
  }
}

export function parseNumberLineFeedback(json: string | null | undefined): NumberLineFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    return {
      rows: (parsed['rows'] as unknown[]).flatMap((raw) => {
        if (!raw || typeof raw !== 'object') return [];
        const { id, correct } = raw as Record<string, unknown>;
        return typeof id === 'string' && typeof correct === 'boolean' ? [{ id, correct }] : [];
      }),
    };
  } catch {
    return null;
  }
}
