/**
 * Admin listeleri (öğretmen #152, öğrenci #153) için ortak server-side sayfalama yardımcıları.
 * URL query param'ları (`page`, `pageSize`) bu fonksiyonlarla okunur.
 */

export const DEFAULT_PAGE_SIZE = 20;
export const PAGE_SIZE_OPTIONS: readonly number[] = [10, 20, 50, 100];
/** Elle yazılmış absürt `?page=` değerleri için üst sınır. */
export const MAX_PAGE = 100_000;

/** Pozitif tamsayı okur; geçersiz, sıfır/negatif ya da `max`'ı aşan değer → `fallback`. */
export function parsePositiveInt(raw: string | null, fallback: number, max = MAX_PAGE): number {
  const n = Number(raw);
  return raw && Number.isInteger(n) && n > 0 && n <= max ? n : fallback;
}

/** 1-tabanlı `page` param'ını 0-tabanlı MatPaginator index'ine çevirir. */
export function parsePageIndex(raw: string | null): number {
  return parsePositiveInt(raw, 1) - 1;
}

/** Yalnız `PAGE_SIZE_OPTIONS` içindeki değerler kabul edilir; aksi → `DEFAULT_PAGE_SIZE`. */
export function parsePageSize(raw: string | null): number {
  const size = parsePositiveInt(raw, DEFAULT_PAGE_SIZE);
  return PAGE_SIZE_OPTIONS.includes(size) ? size : DEFAULT_PAGE_SIZE;
}

/** Son dolu sayfanın 0-tabanlı index'i (liste boşsa 0). */
export function lastPageIndex(totalCount: number, pageSize: number): number {
  return Math.max(0, Math.ceil(totalCount / pageSize) - 1);
}
