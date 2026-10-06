/**
 * HTTP interceptor'ları için ortak URL yardımcıları (issue #241, #255).
 *
 * Exclude listeleri `url.includes(...)` ile taranınca alt dize çakışmaları (ör. `/terms` içeren bir API
 * yolu) yanlış eşleşme üretir; bu yüzden karşılaştırma her zaman yolun (pathname) tamamı üzerinden yapılır.
 */

/** URL'nin yolunu query/hash ve sondaki `/` olmadan döner; göreli ve mutlak URL'leri destekler. */
export function pathnameOf(url: string): string {
  let path: string;
  try {
    // Taban yalnızca göreli URL'leri çözmek için; SSR'da `window` olmadığından sabit.
    path = new URL(url, 'http://localhost').pathname;
  } catch {
    path = url.split(/[?#]/)[0];
  }
  return path.length > 1 ? path.replace(/\/+$/, '') : path;
}

/**
 * URL uygulamanın origin'i dışına mı gidiyor? Göreli URL'ler aynı origin sayılır.
 * Uygulama ve API aynı gateway origin'inden servis edilir; dış origin'e (Jitsi, MinIO presigned URL,
 * üçüncü taraf) Bearer token/çerez gönderilmez ve 401'inde refresh denenmez.
 * SSR'da `location` yoktur: yalnız mutlak `http(s)://` veya protokol-göreli `//` URL'ler dış sayılır.
 */
export function isCrossOriginUrl(url: string): boolean {
  if (typeof location === 'undefined') {
    return /^(https?:)?\/\//i.test(url);
  }
  try {
    return new URL(url, location.origin).origin !== location.origin;
  } catch {
    return true;
  }
}

/** Gateway'in MinIO'ya ilettiği nesne yolu öneki (`/img/{bucket}/{key}`, issue #365). */
const STORAGE_IMAGE_PATH_PREFIX = '/img/';

/**
 * İstek gateway üzerinden MinIO'ya mı gidiyor (`/img/...`)? Bu adresler imzalı (presigned) URL'lerdir:
 * kimlik query'deki SigV4 imzasıdır. Bearer başlığı eklenirse MinIO iki kimlik mekanizmasını birden gördüğü
 * için isteği reddeder ve JWT MinIO erişim loglarına düşer; 401/403'ü de bizim oturumumuzla ilgili değildir.
 */
export function isStorageImageUrl(url: string): boolean {
  return pathnameOf(url).startsWith(STORAGE_IMAGE_PATH_PREFIX);
}
