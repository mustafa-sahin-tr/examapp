/**
 * Issue #576: sürükle-yerleştir etkileşimi (`InteractionType = dragPlace`) — `dragDropLabeling`'in genelleştirilmesi:
 * kart metin ya da soru görselinden kırpma, hedef kapasitesi (kova = çok kart), havuzdan tekrar kullanım, çeldirici kart
 * (çözümde hiçbir hedefe atanmamış kart) ve sıralama modu. Plan öğrenciye gider, çözüm gitmez (#570): çözüm ayrı alanda
 * (`interactionSolution`) yalnız yetkili/izinli durumda gelir. Kutu koordinatları soru bölgesine göredir.
 *
 * `place` modunda kutulu hedef görsel üstünde bırakma alanıdır, kutusuz hedef görselin altında etiketli kovadır.
 * `order` modunda hedefler sıralı konumlardır (kapasite 1). Puansız: kontrol yerleşim başına doğru/yanlış döner
 * (satır id = `"<targetId>:<cardId>"`).
 */
import {
  InteractionBox,
  cleanLabel,
  copyBox,
  isInteractionBox,
  isInteractionType,
  isValidInteractionId,
} from './interaction-ids';

export const DRAG_PLACE = 'dragPlace' as const;

/** Backend sözleşmesinin sınırları (plan / çözüm / payload doğrulaması). */
export const DRAG_PLACE_LIMITS = {
  minTargets: 1,
  maxTargets: 30,
  minCards: 1,
  maxCards: 60,
  minCapacity: 1,
  maxCapacity: 30,
  maxIdLength: 32,
  maxLabelLength: 200,
} as const;

export type DragPlaceMode = 'place' | 'order';

export interface DragPlaceTarget {
  id: string;
  label?: string | null;
  box?: InteractionBox | null;
  capacity: number;
}

export interface DragPlaceCard {
  id: string;
  label?: string | null;
  /** Kırpma kartı: soru görselinin bu bölgesi kart görseli olarak gösterilir. */
  box?: InteractionBox | null;
}

export interface DragPlacePlanV1 {
  version: 1;
  type: typeof DRAG_PLACE;
  mode: DragPlaceMode;
  reuse: boolean;
  targets: DragPlaceTarget[];
  cards: DragPlaceCard[];
}

/** Hedef id → hedefteki kart id'leri (çözüm ve öğrenci yerleşimi aynı biçimde; prototipsiz). */
export type DragPlaceMap = Record<string, string[]>;

export interface DragPlacement {
  targetId: string;
  cardId: string;
}

/** Öğrenci cevabı (`answerPayload`). Boş `placements` boş cevap sayılır. */
export interface DragPlaceAnswerPayload {
  version: 1;
  type: typeof DRAG_PLACE;
  placements: DragPlacement[];
}

export function emptyDragPlaceMap(): DragPlaceMap {
  return Object.create(null) as DragPlaceMap;
}

/** Hedefteki kartlar (yalnız kendi özelliği; yoksa boş dizi). */
export function cardsIn(map: DragPlaceMap, targetId: string): string[] {
  return Object.hasOwn(map, targetId) && Array.isArray(map[targetId]) ? map[targetId] : [];
}

/** Hedefin kartları değiştirilmiş yeni (prototipsiz) eşleme; boş dizi hedefi siler. */
export function withCards(map: DragPlaceMap, targetId: string, cards: string[]): DragPlaceMap {
  const next = emptyDragPlaceMap();
  Object.keys(map).forEach((key) => {
    if (key !== targetId) next[key] = map[key];
  });
  if (cards.length > 0) next[targetId] = cards;
  return next;
}

/** Yerleşim sayısı (hedeflerdeki toplam kart). */
export function placementCount(map: DragPlaceMap): number {
  return Object.keys(map).reduce((sum, key) => sum + cardsIn(map, key).length, 0);
}

function clampCapacity(value: unknown): number {
  const n = typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : DRAG_PLACE_LIMITS.minCapacity;
  return Math.min(DRAG_PLACE_LIMITS.maxCapacity, Math.max(DRAG_PLACE_LIMITS.minCapacity, n));
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / bozuk / hedef ya da kart yoksa null. Bozuk / yinelenen öğe atlanır. */
export function parseDragPlacePlan(json: string | null | undefined): DragPlacePlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isDragPlaceType(parsed['type'])) return null;
    if (!Array.isArray(parsed['targets']) || !Array.isArray(parsed['cards'])) return null;
    const mode: DragPlaceMode = parsed['mode'] === 'order' ? 'order' : 'place';
    const targetIds = new Set<string>();
    const targets: DragPlaceTarget[] = [];
    for (const raw of parsed['targets'] as unknown[]) {
      if (!raw || typeof raw !== 'object') continue;
      const t = raw as Record<string, unknown>;
      const id = t['id'];
      if (!isValidInteractionId(id) || targetIds.has(id)) continue;
      targetIds.add(id);
      targets.push({
        id,
        label: cleanLabel(t['label']),
        box: isInteractionBox(t['box']) ? copyBox(t['box']) : null,
        capacity: mode === 'order' ? 1 : clampCapacity(t['capacity']),
      });
    }
    const cardIds = new Set<string>();
    const cards: DragPlaceCard[] = [];
    for (const raw of parsed['cards'] as unknown[]) {
      if (!raw || typeof raw !== 'object') continue;
      const c = raw as Record<string, unknown>;
      const id = c['id'];
      const label = cleanLabel(c['label']);
      const box = isInteractionBox(c['box']) ? copyBox(c['box']) : null;
      if (!isValidInteractionId(id) || cardIds.has(id) || (!label && !box)) continue;
      cardIds.add(id);
      cards.push({ id, label, box });
    }
    if (targets.length === 0 || cards.length === 0) return null;
    return { version: 1, type: DRAG_PLACE, mode, reuse: parsed['reuse'] === true, targets, cards };
  } catch {
    return null;
  }
}

/**
 * Yerleşim listesini (`[{targetId, cardId}]`) plana göre temizler: bilinmeyen id, aynı hedefte yinelenen kart, kapasite
 * aşımı ve (tekrar kullanım kapalıyken) ikinci kez kullanılan kart atlanır. Plan yoksa yalnız biçim denetlenir.
 */
export function normalizeDragPlacements(value: unknown, plan?: DragPlacePlanV1 | null): DragPlaceMap {
  const result = emptyDragPlaceMap();
  if (!Array.isArray(value)) return result;
  const targets = plan ? new Map(plan.targets.map((t) => [t.id, t])) : null;
  const cards = plan ? new Set(plan.cards.map((c) => c.id)) : null;
  const used = new Set<string>();
  for (const raw of value as unknown[]) {
    if (!raw || typeof raw !== 'object') continue;
    const { targetId, cardId } = raw as Record<string, unknown>;
    if (!isValidInteractionId(targetId) || !isValidInteractionId(cardId)) continue;
    if (targets && !targets.has(targetId)) continue;
    if (cards && !cards.has(cardId)) continue;
    const current = cardsIn(result, targetId);
    if (current.includes(cardId)) continue;
    const target = targets?.get(targetId);
    if (target && current.length >= target.capacity) continue;
    if (plan && !plan.reuse && used.has(cardId)) continue;
    used.add(cardId);
    result[targetId] = [...current, cardId];
  }
  return result;
}

/** Eşlemeyi yerleşim listesine çevirir (plan verilirse plandaki hedef sırasıyla). */
export function toDragPlacements(map: DragPlaceMap, plan?: DragPlacePlanV1 | null): DragPlacement[] {
  const order = plan ? plan.targets.map((t) => t.id) : Object.keys(map);
  return order.flatMap((targetId) => cardsIn(map, targetId).map((cardId) => ({ targetId, cardId })));
}

/** Çözüm JSON'u (`{"t1":["c1","c3"]}`) — bozuk / boşsa `{}`. Plan verilirse aynı kurallarla temizlenir. */
export function parseDragPlaceSolution(json: string | null | undefined, plan?: DragPlacePlanV1 | null): DragPlaceMap {
  if (!json) return emptyDragPlaceMap();
  try {
    const parsed = JSON.parse(json) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return emptyDragPlaceMap();
    const list: DragPlacement[] = [];
    Object.entries(parsed as Record<string, unknown>).forEach(([targetId, cards]) => {
      if (!Array.isArray(cards)) return;
      cards.forEach((cardId) => list.push({ targetId, cardId: cardId as string }));
    });
    return normalizeDragPlacements(list, plan);
  } catch {
    return emptyDragPlaceMap();
  }
}

/** Öğrenci cevabından yerleşimler (`{"type":"dragPlace","placements":[...]}`) — başka tür / bozuksa `{}`. */
export function parseDragPlacePlacements(json: string | null | undefined, plan?: DragPlacePlanV1 | null): DragPlaceMap {
  if (!json) return emptyDragPlaceMap();
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isDragPlaceType(parsed['type'])) return emptyDragPlaceMap();
    return normalizeDragPlacements(parsed['placements'], plan);
  } catch {
    return emptyDragPlaceMap();
  }
}

export function buildDragPlacePayload(map: DragPlaceMap, plan?: DragPlacePlanV1 | null): string {
  const payload: DragPlaceAnswerPayload = { version: 1, type: DRAG_PLACE, placements: toDragPlacements(map, plan) };
  return JSON.stringify(payload);
}

/** Tür karşılaştırması büyük/küçük harf duyarsız: `DragPlace` de sürükle-yerleştir sayılır. */
export function isDragPlaceType(type: unknown): boolean {
  return isInteractionType(type, DRAG_PLACE);
}

/** Yerleşimsiz cevap (Temizle / son kartı havuza alma): sunucu boş cevap sayar, ekranlar da cevapsız saymalı. */
export function isBlankDragPlacePayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isDragPlaceType(parsed['type'])) return false;
    return placementCount(normalizeDragPlacements(parsed['placements'])) === 0;
  } catch {
    return false;
  }
}

/** Satır id'si: hedef + kart (sunucu geri bildirimiyle aynı biçim). */
export function dragPlaceRowId(targetId: string, cardId: string): string {
  return `${targetId}:${cardId}`;
}
