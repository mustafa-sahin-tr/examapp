/**
 * HTTP interceptor'ları için URL yardımcıları (issue #255). auth-ui ayrı bir uygulama olduğundan
 * `ui/src/app/shared/utils/request-url.util.ts` ile aynı içerik burada tutulur; değişiklikte ikisini birlikte güncelleyin.
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
