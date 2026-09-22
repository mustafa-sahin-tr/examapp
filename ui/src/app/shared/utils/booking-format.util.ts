/**
 * Randevu/müsaitlik aralıklarının ortak gösterim yardımcıları (issue #96).
 * Backend `startUtc`/`endUtc` alanlarını hazır döndüğü için gösterimde bunlar kullanılır;
 * `date`/`startTime` alanları UTC duvar saatidir (backend `ToUtc`) ve gösterimde kullanılmaz.
 */

import { activeIntlLocale } from './active-locale.util';

/**
 * Biçimlendiriciler aktif dile bağlıdır (issue #183). Dil değişiminde sayfa yeniden yüklendiği
 * için ilk kullanımda üretilip önbelleğe alınır; SSR'da `DEFAULT_LOCALE` kullanılır.
 */
let dayFmt: Intl.DateTimeFormat | null = null;
let timeFmt: Intl.DateTimeFormat | null = null;

function dayFormatter(): Intl.DateTimeFormat {
  dayFmt ??= new Intl.DateTimeFormat(activeIntlLocale(), {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    weekday: 'long',
  });
  return dayFmt;
}

function timeFormatter(): Intl.DateTimeFormat {
  timeFmt ??= new Intl.DateTimeFormat(activeIntlLocale(), { hour: '2-digit', minute: '2-digit' });
  return timeFmt;
}

/** "20 Eylül 2026 Pazar" */
export function formatSlotDay(utcIso: string): string {
  const d = new Date(utcIso);
  return Number.isNaN(d.getTime()) ? '—' : dayFormatter().format(d);
}

/** "14:00 – 15:00" */
export function formatSlotRange(startUtcIso: string, endUtcIso: string): string {
  const start = new Date(startUtcIso);
  const end = new Date(endUtcIso);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
    return '—';
  }
  return `${timeFormatter().format(start)} – ${timeFormatter().format(end)}`;
}

/** "20 Eylül 2026 Pazar · 14:00 – 15:00" */
export function formatSlotFull(startUtcIso: string, endUtcIso: string): string {
  return `${formatSlotDay(startUtcIso)} · ${formatSlotRange(startUtcIso, endUtcIso)}`;
}

/** Slot/randevu geçmişte mi (başlangıcı şu andan önce). */
export function isPastSlot(startUtcIso: string): boolean {
  const d = new Date(startUtcIso);
  return !Number.isNaN(d.getTime()) && d.getTime() < Date.now();
}
