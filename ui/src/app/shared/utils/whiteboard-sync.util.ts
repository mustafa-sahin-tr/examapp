import {
  WHITEBOARD_ALLOWED_ELEMENT_TYPES,
  WHITEBOARD_CLOSE_REASONS,
  WHITEBOARD_ERROR_CODES,
  WhiteboardCloseReason,
  WhiteboardElement,
  WhiteboardErrorCode,
  WhiteboardPeerPointer,
  WhiteboardRole,
} from '../../models/whiteboard.model';

/**
 * Ortak tahta senkronizasyonunun saf kuralları (issue #98). Excalidraw'a ve SignalR'a bağımlı değildir;
 * `WhiteboardSyncService` ve tuval modülü bunları kullanır.
 */

/** Tek `SendElements` çağrısındaki azami eleman (sunucu `MaxElementsPerMessage` = 500). */
export const WHITEBOARD_MAX_ELEMENTS_PER_CHUNK = 500;

/** Tek `SendElements` parçasının hedef boyutu (UTF-8 JSON). Hub mesaj sınırı 128 KB; zarf + pay için ~100 KB. */
export const WHITEBOARD_MAX_CHUNK_BYTES = 100 * 1024;

/**
 * Tek elemanın azami boyutu. Hub tek mesajı 128 KB ile sınırlar (`MaxReceiveMessageBytes`); SignalR çağrı zarfı için
 * 1 KB pay bırakılır. Daha büyük eleman (pratikte çok uzun serbest çizim) gönderilmez, kullanıcı uyarılır.
 */
export const WHITEBOARD_MAX_ELEMENT_BYTES = 128 * 1024 - 1024;

/** Sunucunun `link` üst sınırı (`WhiteboardStore.MaxLinkLength`). */
export const WHITEBOARD_MAX_LINK_LENGTH = 2048;

/** `SendPointer` koordinat sınırı (`WhiteboardHub.MaxCoordinate`). */
export const WHITEBOARD_MAX_COORDINATE = 1_000_000;

/** Eleman sürüm damgası (son gönderilen/alınan). */
export interface ElementStamp {
  readonly version: number;
  readonly versionNonce: number;
}

export function stampOf(element: Pick<WhiteboardElement, 'version' | 'versionNonce'>): ElementStamp {
  return { version: element.version, versionNonce: element.versionNonce };
}

export function sameStamp(a: ElementStamp | undefined, b: ElementStamp): boolean {
  return !!a && a.version === b.version && a.versionNonce === b.versionNonce;
}

/**
 * Son gönderilen/alınan duruma göre değişen elemanlar (version ya da versionNonce farkı; bilinmeyen id = yeni).
 * Uzaktan gelip sahneye giren elemanlar `known`'a yazıldığı için bu hesap onları geri göndermez (echo yok).
 */
export function changedElements<T extends WhiteboardElement>(
  elements: readonly T[],
  known: ReadonlyMap<string, ElementStamp>
): T[] {
  return elements.filter((element) => !sameStamp(known.get(element.id), stampOf(element)));
}

/**
 * Uzak (güvenilmez) JSON'daki `id` → version/versionNonce damgaları — `restoreElements`'ten ÖNCEKİ değerler.
 * Restore bir elemanı onarırsa (ör. geçersiz fractional index → version artışı) yerel kopya bu damgadan farklı olur ve
 * onarılmış hâli geri gönderilir; böylece taraflar sessizce ayrışmaz.
 */
export function rawStampsOf(raw: readonly unknown[]): Map<string, ElementStamp> {
  const stamps = new Map<string, ElementStamp>();
  for (const item of raw) {
    if (!item || typeof item !== 'object') {
      continue;
    }
    const { id, version, versionNonce } = item as { id?: unknown; version?: unknown; versionNonce?: unknown };
    if (typeof id === 'string' && typeof version === 'number' && Number.isInteger(version)) {
      const nonce = typeof versionNonce === 'number' && Number.isInteger(versionNonce) ? versionNonce : 0;
      stamps.set(id, { version, versionNonce: nonce });
    }
  }
  return stamps;
}

/**
 * Elemanları Excalidraw fractional `index`'ine göre (dize karşılaştırması, kararlı) sıralar; index'siz olanlar sona.
 * `restoreElements` sıra dışı index'leri yeniden üretip version'ı artırdığı için (ör. sunucu sahnesi sözlük
 * sırasıyla gelir) restore'dan önce uygulanır.
 */
export function sortByFractionalIndex(raw: readonly unknown[]): unknown[] {
  const indexOf = (item: unknown): string | null => {
    const index = item && typeof item === 'object' ? (item as { index?: unknown }).index : undefined;
    return typeof index === 'string' ? index : null;
  };
  return [...raw].sort((a, b) => {
    const left = indexOf(a);
    const right = indexOf(b);
    if (left === null || right === null) {
      return left === right ? 0 : left === null ? 1 : -1;
    }
    return left < right ? -1 : left > right ? 1 : 0;
  });
}

/** Sunucunun kabul ettiği (tip izin listesi + dosya referansı yok) eleman mı. */
export function isSyncableElement(element: Pick<WhiteboardElement, 'type' | 'fileId'>): boolean {
  return WHITEBOARD_ALLOWED_ELEMENT_TYPES.has(element.type) && (element.fileId === undefined || element.fileId === null);
}

/** Yalnızca mutlak `http:`/`https:` adresler (sunucu kuralı ile aynı: en fazla 2048 karakter). */
export function isSafeHttpUrl(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > WHITEBOARD_MAX_LINK_LENGTH) {
    return false;
  }
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

/** `window.open` imzasının kullandığımız kısmı (testte sahte verilir). */
export type OpenWindowFn = (url: string, target: string, features: string) => unknown;

/**
 * Tahtadaki bağlantıyı açar: yalnızca http(s), yeni sekmede, `noopener,noreferrer` ile.
 * Açıldıysa `true`; şema geçersizse hiçbir şey açılmaz ve `false` döner.
 */
export function openWhiteboardLink(link: unknown, open: OpenWindowFn): boolean {
  if (!isSafeHttpUrl(link)) {
    return false;
  }
  open(link, '_blank', 'noopener,noreferrer');
  return true;
}

/** Nesneden bir anahtarı çıkarılmış sığ kopya (değer okunmaz). */
function withoutKey(source: object, key: string): Record<string, unknown> {
  return Object.fromEntries(Object.entries(source).filter(([name]) => name !== key));
}

/**
 * Gönderilecek kopya: `customData` taşınmaz (sunucu reddeder, karşı tarafta da okunmaz), http(s) olmayan `link`
 * `null`'a çekilir. Yerel eleman değiştirilmez.
 */
export function toOutgoingElement(element: WhiteboardElement): Record<string, unknown> {
  const payload = withoutKey(element, 'customData');
  if (payload['link'] !== undefined && payload['link'] !== null && !isSafeHttpUrl(payload['link'])) {
    payload['link'] = null;
  }
  return payload;
}

/**
 * Uzaktan gelen (restoreElements'ten geçmiş) elemanın savunma amaçlı temizliği: `customData` düşürülür,
 * http(s) olmayan bağlantı kaldırılır. Değişiklik gerekmiyorsa aynı nesne döner.
 */
export function sanitizeIncomingElement<T extends WhiteboardElement>(element: T): T {
  const hasCustomData = Object.prototype.hasOwnProperty.call(element, 'customData');
  const unsafeLink = element.link !== undefined && element.link !== null && !isSafeHttpUrl(element.link);
  if (!hasCustomData && !unsafeLink) {
    return element;
  }
  const copy = withoutKey(element, 'customData');
  if (unsafeLink) {
    copy['link'] = null;
  }
  return copy as unknown as T;
}

const encoder = new TextEncoder();

/** Serileştirilmiş (UTF-8 JSON) bayt boyutu. */
export function jsonByteLength(value: unknown): number {
  return encoder.encode(JSON.stringify(value)).length;
}

/** Boyutu önceden hesaplanmış gönderim kalemi. */
export interface OutgoingItem<T> {
  readonly item: T;
  readonly bytes: number;
}

/**
 * Kalemleri sırayı koruyarak parçalara böler: her parça ≤ `maxCount` kalem ve ≤ `maxBytes` (tek başına sınırı aşan
 * kalem kendi parçasına düşer — çağıran bu tür kalemleri önceden elemelidir).
 */
export function chunkOutgoing<T>(
  items: readonly OutgoingItem<T>[],
  maxCount = WHITEBOARD_MAX_ELEMENTS_PER_CHUNK,
  maxBytes = WHITEBOARD_MAX_CHUNK_BYTES
): T[][] {
  const chunks: T[][] = [];
  let current: T[] = [];
  let currentBytes = 0;
  for (const { item, bytes } of items) {
    // JSON dizi ayırıcısı (virgül) için +1.
    const cost = bytes + 1;
    if (current.length > 0 && (current.length >= maxCount || currentBytes + cost > maxBytes)) {
      chunks.push(current);
      current = [];
      currentBytes = 0;
    }
    current.push(item);
    currentBytes += cost;
  }
  if (current.length > 0) {
    chunks.push(current);
  }
  return chunks;
}

/**
 * SignalR hata mesajından hub hata kodunu çıkarır. `HubException` için sunucu metni
 * `"An unexpected error occurred invoking 'X' on the server. HubException: <Kod>"` biçimindedir.
 * Bilinmeyen kod ya da taşıma hatası `null` döner.
 */
export function parseHubErrorCode(error: unknown): WhiteboardErrorCode | null {
  const message = (error instanceof Error ? error.message : typeof error === 'string' ? error : '').trim();
  const candidate = /HubException:\s*([A-Za-z]+)$/.exec(message)?.[1] ?? message;
  return candidate && (WHITEBOARD_ERROR_CODES as readonly string[]).includes(candidate)
    ? (candidate as WhiteboardErrorCode)
    : null;
}

export function isCloseReason(value: unknown): value is WhiteboardCloseReason {
  return typeof value === 'string' && (WHITEBOARD_CLOSE_REASONS as readonly string[]).includes(value);
}

export function isWhiteboardRole(value: unknown): value is WhiteboardRole {
  return value === 'teacher' || value === 'student';
}

/** Karşı tarafın imleci — güvenilmez girdi: sonlu, sınır içi koordinat; tanınmayan araç `pointer` sayılır. */
export function parsePeerPointer(role: unknown, pointer: unknown): WhiteboardPeerPointer | null {
  if (!isWhiteboardRole(role) || !pointer || typeof pointer !== 'object') {
    return null;
  }
  const { x, y, tool } = pointer as { x?: unknown; y?: unknown; tool?: unknown };
  if (!isCoordinate(x) || !isCoordinate(y)) {
    return null;
  }
  return { x, y, tool: tool === 'laser' ? 'laser' : 'pointer', role };
}

export function isCoordinate(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && Math.abs(value) <= WHITEBOARD_MAX_COORDINATE;
}

/**
 * .NET `DateTime` (UTC) metnini `Date`'e çevirir. Saat dilimi eki yoksa (Kind=Unspecified serileştirmesi) UTC kabul
 * edilir; geçersizse `null`.
 */
export function parseUtcDate(value: unknown): Date | null {
  if (typeof value !== 'string' || value.length === 0) {
    return null;
  }
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}
