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
