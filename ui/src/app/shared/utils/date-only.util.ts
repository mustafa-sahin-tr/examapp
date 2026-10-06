/**
 * Takvim günü ("date-only") değerleri için saf yardımcılar (issue #386).
 *
 * Backend program/plan tarihlerini gün olarak saklar ve `2026-09-09T00:00:00Z` ya da `2026-09-09` biçiminde döner.
 * `new Date(value)` bu değeri UTC gece yarısı olarak yorumlar; UTC'nin batısındaki bir saat diliminde gün bir
 * önceki güne kayar. Bu yüzden yalnız `yyyy-MM-dd` kısmı okunur ve YEREL gece yarısına çevrilir — gösterilen gün
 * kullanıcının saat diliminden bağımsız olarak sunucudaki günle aynıdır.
 */

const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})/;

/** `yyyy-MM-dd…` metnini yerel gece yarısı `Date`'e çevirir; tanınmayan/geçersiz değerde `null`. */
export function parseDateOnly(value: string | null | undefined): Date | null {
  const match = value ? DATE_ONLY_PATTERN.exec(value.trim()) : null;
  if (!match) {
    return null;
  }
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(year, month - 1, day);
  // 2026-02-31 gibi taşan değerler başka güne dönüşür — geçersiz say.
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? date : null;
}

/** Gün değerini aktif dile göre kısa biçimde yazar (TR: "9 Eyl 2026", EN: "Sep 9, 2026"); geçersizse boş metin. */
export function formatDateOnly(value: string | null | undefined, locale: string): string {
  const date = parseDateOnly(value);
  return date
    ? new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric' }).format(date)
    : '';
}
