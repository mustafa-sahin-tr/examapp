/**
 * Issue #146 — bildirim listesi için göreli tarih ("3 dakika önce", "dün").
 * Saf fonksiyon: `now` ve `locale` dışarıdan verilir, test edilebilir.
 */

const UNITS: ReadonlyArray<{ unit: Intl.RelativeTimeFormatUnit; seconds: number }> = [
  { unit: 'year', seconds: 365 * 24 * 60 * 60 },
  { unit: 'month', seconds: 30 * 24 * 60 * 60 },
  { unit: 'week', seconds: 7 * 24 * 60 * 60 },
  { unit: 'day', seconds: 24 * 60 * 60 },
  { unit: 'hour', seconds: 60 * 60 },
  { unit: 'minute', seconds: 60 },
];

/**
 * Sunucu `DateTime` UTC değerini ISO string olarak döner; saat dilimi eki yoksa UTC kabul edilir
 * (tarayıcı eksiz ISO'yu yerel saat sayardı).
 */
export function parseUtcDate(iso: string): Date {
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(iso);
  return new Date(hasZone ? iso : `${iso}Z`);
}

/** `createdAt` için göreli tarih metni; geçersiz tarihte boş string. */
export function formatRelativeTime(iso: string, now: number, locale: string): string {
  const date = parseUtcDate(iso);
  const time = date.getTime();
  if (Number.isNaN(time)) {
    return '';
  }
  const diffSeconds = Math.round((time - now) / 1000);
  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  for (const { unit, seconds } of UNITS) {
    if (Math.abs(diffSeconds) >= seconds) {
      return formatter.format(Math.trunc(diffSeconds / seconds), unit);
    }
  }
  return formatter.format(0, 'second');
}
