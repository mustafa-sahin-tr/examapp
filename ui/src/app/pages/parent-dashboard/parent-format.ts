/**
 * Veli paneli (issue #421) ortak biçimlendirme yardımcıları — liste ve test sonucu dialog'u aynı kuralı kullanır.
 */

/** ISO-8601 (UTC) zaman damgası → yerel tarih + saat ("7 Eki 2026 14:05"); geçersizse boş. */
export function formatDateTime(iso: string | null | undefined, locale: string): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(date);
}

/**
 * Süre çeviri anahtarı (`duration.*`) + parametreleri. Sunucu süreyi dakikaya aşağı yuvarlar (#421 review — saate kesilmiş
 * başlangıç/bitişle dakika geri hesaplanamasın); saniye gösterilmez: 1 dakikanın altı "1 dk'dan kısa", 1 saat altı dakika,
 * üstü saat + dakika.
 */
export function durationKey(seconds: number): { key: string; params: Record<string, number> } {
  const minutes = Math.floor(Math.max(0, Math.floor(seconds || 0)) / 60);
  if (minutes < 1) return { key: 'duration.underMinute', params: {} };
  if (minutes < 60) return { key: 'duration.minutes', params: { m: minutes } };
  return { key: 'duration.hoursMinutes', params: { h: Math.floor(minutes / 60), m: minutes % 60 } };
}

/**
 * Saate kesilmiş (sunucu, #421 review) zaman damgası → `{ date, time }` ("7 Eki 2026", "14:00"); şablon "yaklaşık" ile
 * gösterir. Dakika gösterilmez. Geçersizse null.
 */
export function approxHourParts(iso: string | null | undefined, locale: string): { date: string; time: string } | null {
  if (!iso) return null;
  const value = new Date(iso);
  if (Number.isNaN(value.getTime())) return null;
  const hour = new Date(value.getFullYear(), value.getMonth(), value.getDate(), value.getHours());
  return {
    date: new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric' }).format(hour),
    time: new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(hour),
  };
}

/**
 * Issue #422: "yyyy-MM-dd" (saat dilimsiz takvim günü) → YEREL gece yarısı. `new Date('2026-10-05')` UTC gece yarısı olarak
 * çözülür ve UTC'nin gerisindeki bölgelerde bir önceki güne kayar; bileşenlerden kurmak her bölgede aynı günü verir.
 */
export function parseDay(value: string | null | undefined): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? '');
  if (!match) return null;
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Yerel gün → "yyyy-MM-dd" (sunucuya giden aralık parametreleri). */
export function toDayString(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Takvim günü ("yyyy-MM-dd") → "6 Eki 2026"; geçersizse boş. Saat gösterilmez. */
export function formatDay(value: string | null | undefined, locale: string): string {
  const date = parseDay(value);
  return date ? new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric' }).format(date) : '';
}

/**
 * Issue #422 review: veli programı Türkiye saatine göre gösterilir (sunucunun gün/hafta kuralı Europe/Istanbul); tarayıcı
 * başka bir saat diliminde olsa da ders saati ve günü kaymaz.
 */
export const PARENT_TIME_ZONE = 'Europe/Istanbul';

/** ISO-8601 (UTC) → saat ("14:00"), varsayılan Europe/Istanbul; geçersizse boş. */
export function formatTime(iso: string | null | undefined, locale: string, timeZone = PARENT_TIME_ZONE): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }).format(date);
}

/** Bir anın Europe/Istanbul takvim günü ("yyyy-MM-dd"). */
export function dayInTimeZone(date: Date, timeZone = PARENT_TIME_ZONE): string {
  // en-CA biçimi "yyyy-MM-dd" verir.
  return new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone }).format(date);
}

const numberFormats = new Map<string, Intl.NumberFormat>();

/** Sayı biçimlendirme; `Intl.NumberFormat` dil başına bir kez kurulur. */
export function formatNumber(value: number, locale: string): string {
  let fmt = numberFormats.get(locale);
  if (!fmt) {
    fmt = new Intl.NumberFormat(locale);
    numberFormats.set(locale, fmt);
  }
  return fmt.format(value);
}
