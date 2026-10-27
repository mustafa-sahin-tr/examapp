/**
 * Issue #576: eşleştirme etkileşimi (`InteractionType = match`) — sol ↔ sağ, çeldiricili (fazla sağ öğe), örnek çift
 * önceden bağlı (`prelinked`). Öğeler metin ya da soru görselinden kırpmadır. Plan öğrenciye gider, çözüm gitmez (#570).
 * Puansız: kontrol önceden bağlı olmayan her sol öğe için doğru/yanlış döner (satır id = sol id).
 */
import {
  InteractionBox,
  cleanLabel,
  copyBox,
  isInteractionBox,
  isInteractionType,
  isValidInteractionId,
} from './interaction-ids';

export const MATCH = 'match' as const;

export const MATCH_LIMITS = {
  minLeft: 1,
  maxLeft: 30,
  minRight: 1,
  maxRight: 30,
  maxIdLength: 32,
  maxLabelLength: 200,
} as const;

export type MatchSide = 'left' | 'right';

export interface MatchItem {
  id: string;
  label?: string | null;
  box?: InteractionBox | null;
}

export interface MatchPair {
  leftId: string;
  rightId: string;
}

export interface MatchPlanV1 {
  version: 1;
  type: typeof MATCH;
  left: MatchItem[];
  right: MatchItem[];
  prelinked: MatchPair[];
}

/** Sol id → sağ id (çözüm ve öğrenci eşleşmesi aynı biçimde; prototipsiz). */
export type MatchMap = Record<string, string>;

export interface MatchAnswerPayload {
  version: 1;
  type: typeof MATCH;
  pairs: MatchPair[];
}

export function emptyMatchMap(): MatchMap {
  return Object.create(null) as MatchMap;
}

/** Solun eşi (yalnız kendi özelliği). */
export function rightOf(map: MatchMap, leftId: string): string | null {
  return Object.hasOwn(map, leftId) && typeof map[leftId] === 'string' ? map[leftId] : null;
}

/** Sağ öğeyi kullanan sol (yoksa null). */
export function leftOf(map: MatchMap, rightId: string): string | null {
  return Object.keys(map).find((key) => map[key] === rightId) ?? null;
}

/** Eşleşmesi değiştirilmiş yeni eşleme: sağ öğe başka sola bağlıysa oradan çözülür; null eşi siler. */
export function withPair(map: MatchMap, leftId: string, rightId: string | null): MatchMap {
  const next = emptyMatchMap();
  Object.keys(map).forEach((key) => {
    if (key !== leftId && map[key] !== rightId) next[key] = map[key];
  });
  if (rightId) next[leftId] = rightId;
  return next;
}

function parseItems(value: unknown, seen: Set<string>): MatchItem[] {
  if (!Array.isArray(value)) return [];
  const items: MatchItem[] = [];
  for (const raw of value as unknown[]) {
    if (!raw || typeof raw !== 'object') continue;
    const item = raw as Record<string, unknown>;
    const id = item['id'];
    const label = cleanLabel(item['label']);
    const box = isInteractionBox(item['box']) ? copyBox(item['box']) : null;
    if (!isValidInteractionId(id) || seen.has(id) || (!label && !box)) continue;
    seen.add(id);
    items.push({ id, label, box });
  }
  return items;
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / bozuk / bir taraf boşsa null. */
export function parseMatchPlan(json: string | null | undefined): MatchPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isMatchType(parsed['type'])) return null;
    // Sol ve sağ id'leri ayrı uzaylar; yine de aynı id iki tarafta olursa satır id'si karışmasın diye ortak küme.
    const seen = new Set<string>();
    const left = parseItems(parsed['left'], seen);
    const right = parseItems(parsed['right'], seen);
    if (left.length === 0 || right.length === 0) return null;
    const leftIds = new Set(left.map((i) => i.id));
    const rightIds = new Set(right.map((i) => i.id));
    const usedLeft = new Set<string>();
    const usedRight = new Set<string>();
    const prelinked: MatchPair[] = [];
    for (const raw of Array.isArray(parsed['prelinked']) ? (parsed['prelinked'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const { leftId, rightId } = raw as Record<string, unknown>;
      if (typeof leftId !== 'string' || typeof rightId !== 'string') continue;
      if (!leftIds.has(leftId) || !rightIds.has(rightId) || usedLeft.has(leftId) || usedRight.has(rightId)) continue;
      usedLeft.add(leftId);
      usedRight.add(rightId);
      prelinked.push({ leftId, rightId });
    }
    return { version: 1, type: MATCH, left, right, prelinked };
  } catch {
    return null;
  }
}

/**
 * Eşleşme listesini plana göre temizler: bilinmeyen id, önceden bağlı sol / sağ, ikinci kez kullanılan sol ya da sağ
 * atlanır (ilk gelen kalır).
 */
export function normalizeMatchPairs(value: unknown, plan?: MatchPlanV1 | null): MatchMap {
  const result = emptyMatchMap();
  if (!Array.isArray(value)) return result;
  const leftIds = plan ? new Set(plan.left.map((i) => i.id)) : null;
  const rightIds = plan ? new Set(plan.right.map((i) => i.id)) : null;
  const usedRight = new Set<string>(plan?.prelinked.map((p) => p.rightId) ?? []);
  const fixedLeft = new Set<string>(plan?.prelinked.map((p) => p.leftId) ?? []);
  for (const raw of value as unknown[]) {
    if (!raw || typeof raw !== 'object') continue;
    const { leftId, rightId } = raw as Record<string, unknown>;
    if (!isValidInteractionId(leftId) || !isValidInteractionId(rightId)) continue;
    if ((leftIds && !leftIds.has(leftId)) || (rightIds && !rightIds.has(rightId))) continue;
    if (fixedLeft.has(leftId) || Object.hasOwn(result, leftId) || usedRight.has(rightId)) continue;
    usedRight.add(rightId);
    result[leftId] = rightId;
  }
  return result;
}

export function toMatchPairs(map: MatchMap, plan?: MatchPlanV1 | null): MatchPair[] {
  const order = plan ? plan.left.map((i) => i.id) : Object.keys(map);
  return order.flatMap((leftId) => {
    const rightId = rightOf(map, leftId);
    return rightId ? [{ leftId, rightId }] : [];
  });
}

/** Çözüm JSON'u (`{"l2":"r3"}`) — bozuk / boşsa `{}`. */
export function parseMatchSolution(json: string | null | undefined, plan?: MatchPlanV1 | null): MatchMap {
  if (!json) return emptyMatchMap();
  try {
    const parsed = JSON.parse(json) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return emptyMatchMap();
    const list = Object.entries(parsed as Record<string, unknown>).map(([leftId, rightId]) => ({ leftId, rightId }));
    return normalizeMatchPairs(list, plan);
  } catch {
    return emptyMatchMap();
  }
}

/** Öğrenci cevabından eşleşmeler (`{"type":"match","pairs":[...]}`) — başka tür / bozuksa `{}`. */
export function parseMatchPairs(json: string | null | undefined, plan?: MatchPlanV1 | null): MatchMap {
  if (!json) return emptyMatchMap();
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isMatchType(parsed['type'])) return emptyMatchMap();
    return normalizeMatchPairs(parsed['pairs'], plan);
  } catch {
    return emptyMatchMap();
  }
}

/** Payload yalnız öğrencinin eşleşmelerini taşır (önceden bağlı örnek çift sabittir, gönderilmez). */
export function buildMatchPayload(map: MatchMap, plan?: MatchPlanV1 | null): string {
  const payload: MatchAnswerPayload = { version: 1, type: MATCH, pairs: toMatchPairs(map, plan) };
  return JSON.stringify(payload);
}

export function isMatchType(type: unknown): boolean {
  return isInteractionType(type, MATCH);
}

/** Eşleşmesiz cevap: sunucu boş cevap sayar, ekranlar da cevapsız saymalı. */
export function isBlankMatchPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isMatchType(parsed['type'])) return false;
    return Object.keys(normalizeMatchPairs(parsed['pairs'])).length === 0;
  } catch {
    return false;
  }
}

/** Öğrencinin eşlemesi gereken sol öğeler (önceden bağlı olanlar hariç). */
export function openLefts(plan: MatchPlanV1): MatchItem[] {
  const fixed = new Set(plan.prelinked.map((p) => p.leftId));
  return plan.left.filter((item) => !fixed.has(item.id));
}
