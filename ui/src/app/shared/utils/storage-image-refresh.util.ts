import { Observable, catchError, defer, map, of, shareReplay, take } from 'rxjs';
import { isCrossOriginUrl, isStorageImageUrl } from './request-url.util';
import { storageImageKey } from './storage-image-url.util';

/**
 * MinIO görselinin süresi dolmuş/geçersiz imzalı URL'si yüklenemediğinde (issue #365 S3) taze URL'yi bulan fonksiyon.
 * Parametre yüklenemeyen `src`; dönen değer aynı nesnenin yeni imzalı URL'si (bulunamazsa null/undefined).
 * Bileşen başına bir kez oluşturulmalı (şablonda her CD'de yeni fonksiyon üretmeyin).
 */
export type StorageImageResolver = (failedUrl: string) => Observable<string | null | undefined>;

export interface StorageImageResolverOptions<T> {
  /** DTO'daki aday görsel adresleri; verilmezse DTO derinlemesine taranır ve `/img/...` olan tüm string'ler alınır. */
  collect?: (dto: T) => Iterable<string | null | undefined>;
  /**
   * Aynı anahtar için tek istek: aynı anda/arka arkaya hata veren N görsel DTO'yu bir kez yeniden çeker. Anahtar
   * değişirse (ör. başka bir oturum) önbellek atılır.
   */
  cacheKey?: () => unknown;
  /** Yeniden çekilen DTO'nun paylaşıldığı süre (ms). Varsayılan 30 sn. */
  ttlMs?: number;
  /** Test dikişi. */
  now?: () => number;
}

/** Karşılaştırma için kalıcı kimlik: query'siz yol, percent-decode edilmiş (sunucu ve istemci kodlaması farklı olabilir). */
function comparableKey(url: string): string {
  const key = storageImageKey(url);
  try {
    return decodeURIComponent(key);
  } catch {
    return key;
  }
}

/**
 * Adaylar arasından yüklenemeyen URL ile AYNI nesneye (aynı `/img/{bucket}/{key}` yolu) işaret eden ilk taze URL'yi
 * döner. Aday, yüklenemeyen URL'nin birebir aynısıysa (sunucu aynı imza dilimindeyse) döndürülmez — aynı adresi tekrar
 * denemek döngü olurdu.
 */
export function findFreshStorageUrl(
  candidates: Iterable<string | null | undefined>,
  failedUrl: string | null | undefined
): string | null {
  if (!failedUrl) {
    return null;
  }
  const failedKey = comparableKey(failedUrl);
  if (!failedKey) {
    return null;
  }
  for (const candidate of candidates) {
    if (candidate && candidate !== failedUrl && comparableKey(candidate) === failedKey) {
      return candidate;
    }
  }
  return null;
}

/** DTO içindeki tüm (aynı origin) `/img/...` string değerlerini toplar (dizi/nesne derinlemesine; döngüye karşı korumalı). */
export function collectStorageImageUrls(value: unknown): string[] {
  const found: string[] = [];
  const seen = new Set<object>();
  const walk = (node: unknown, depth: number): void => {
    if (depth > 12 || node === null || node === undefined) {
      return;
    }
    if (typeof node === 'string') {
      if (isStorageImageUrl(node) && !isCrossOriginUrl(node)) {
        found.push(node);
      }
      return;
    }
    if (typeof node !== 'object' || seen.has(node as object)) {
      return;
    }
    seen.add(node as object);
    const children = Array.isArray(node) ? node : Object.values(node as Record<string, unknown>);
    for (const child of children) {
      walk(child, depth + 1);
    }
  };
  walk(value, 0);
  return found;
}

/**
 * Sahip DTO'yu yeniden çekerek taze görsel URL'si bulan bir {@link StorageImageResolver} üretir. İstek hatası null'a
 * düşer (görsel yine kırık kalır, sayfa bozulmaz) ve önbelleği temizler.
 */
export function createStorageImageResolver<T>(
  fetch: () => Observable<T>,
  options: StorageImageResolverOptions<T> = {}
): StorageImageResolver {
  const ttlMs = options.ttlMs ?? 30_000;
  const now = options.now ?? (() => Date.now());
  const collect = options.collect ?? ((dto: T) => collectStorageImageUrls(dto));
  let cached: { at: number; key: unknown; dto$: Observable<T> } | null = null;

  const load = (): Observable<T> => {
    const key = options.cacheKey?.();
    const at = now();
    if (!cached || cached.key !== key || at - cached.at > ttlMs) {
      const entry: { at: number; key: unknown; dto$: Observable<T> } = { at, key, dto$: of() };
      entry.dto$ = defer(fetch).pipe(
        catchError((error: unknown) => {
          // Başarısız istek paylaşılmaz: sonraki hata yeniden dener.
          if (cached === entry) {
            cached = null;
          }
          throw error;
        }),
        shareReplay({ bufferSize: 1, refCount: false })
      );
      cached = entry;
    }
    return cached.dto$;
  };

  return (failedUrl: string) =>
    load().pipe(
      take(1),
      map((dto) => findFreshStorageUrl(collect(dto), failedUrl)),
      catchError(() => of(null))
    );
}
