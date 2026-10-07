/**
 * Randevu/müsaitlik aralıklarının ortak gösterim yardımcıları (issue #96).
 * Backend `startUtc`/`endUtc` alanlarını hazır döndüğü için gösterimde bunlar kullanılır;
 * `date`/`startTime` alanları UTC duvar saatidir (backend `ToUtc`) ve gösterimde kullanılmaz.
 */

import { activeIntlLocale } from './active-locale.util';

/**
 * Biçimlendiriciler aktif dile bağlıdır (issue #183) ve dil başına önbelleğe alınır; SSR'da
 * `DEFAULT_LOCALE` kullanılır. Uygulamada dil değişimi sayfayı yeniden yüklese de önbellek dile
 * göre anahtarlanır: ilk çağrıdaki dil kalıcı olarak kilitlenmez (#394 — testlerde `<html lang>`
 * spec'ler arasında değiştiğinde saatler yanlış dilde/12 saat biçiminde kalıyordu).
 */
let dayFmt: { locale: string; fmt: Intl.DateTimeFormat } | null = null;
let timeFmt: { locale: string; fmt: Intl.DateTimeFormat } | null = null;

function dayFormatter(): Intl.DateTimeFormat {
  const locale = activeIntlLocale();
  if (dayFmt?.locale !== locale) {
    dayFmt = {
      locale,
      fmt: new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric', weekday: 'long' }),
    };
  }
  return dayFmt.fmt;
}

function timeFormatter(): Intl.DateTimeFormat {
  const locale = activeIntlLocale();
  if (timeFmt?.locale !== locale) {
    timeFmt = { locale, fmt: new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit' }) };
  }
  return timeFmt.fmt;
}

/** "20 Eylül 2026 Pazar" */
export function formatSlotDay(utcIso: string): string {
  const d = new Date(utcIso);
  return Number.isNaN(d.getTime()) ? '—' : dayFormatter().format(d);
}

/**
 * Kök sözlükteki "(+1 gün)" anahtarı (issue #300). Util saf kalsın diye metni çağıran çevirir ve
 * `formatSlotRange`'e verir.
 */
export const SLOT_NEXT_DAY_KEY = 'shared.slotRange.nextDay';

function sameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/**
 * Bitiş, başlangıcın ERTESİ yerel gününde mi (gösterimde "(+1 gün)" ipucu, issue #300). Tam yerel gece yarısında
 * biten aralık (23:00 – 00:00) aynı güne sayılır: son anı hâlâ başlangıç günündedir. Yerel saate göredir — UTC'de
 * gün aşan ama yerelde aynı gündeki aralık (TR'de 02:30 – 03:30 = UTC 23:30 – 00:30) ipucu almaz.
 */
export function endsOnNextLocalDay(start: Date, end: Date): boolean {
  const lastInstant = new Date(end.getTime() - 1);
  return end.getTime() > start.getTime() && !sameLocalDay(start, lastInstant);
}

/** `startUtc`/`endUtc` dizeleri için `endsOnNextLocalDay`; geçersiz tarihte false. */
export function slotEndsNextDay(startUtcIso: string, endUtcIso: string): boolean {
  const start = new Date(startUtcIso);
  const end = new Date(endUtcIso);
  return !Number.isNaN(start.getTime()) && !Number.isNaN(end.getTime()) && endsOnNextLocalDay(start, end);
}

/**
 * "14:00 – 15:00". `nextDayLabel` verilirse ve bitiş ertesi yerel gündeyse sona eklenir: "23:30 – 00:30 (+1 gün)".
 * Bitiş her zaman `endUtc`'den gelir; `date + endTime` birleştirilmez (gün aşan slotta yanlış an verir).
 */
export function formatSlotRange(startUtcIso: string, endUtcIso: string, nextDayLabel?: string): string {
  const start = new Date(startUtcIso);
  const end = new Date(endUtcIso);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
    return '—';
  }
  const range = `${timeFormatter().format(start)} – ${timeFormatter().format(end)}`;
  return nextDayLabel && endsOnNextLocalDay(start, end) ? `${range} ${nextDayLabel}` : range;
}

/** "20 Eylül 2026 Pazar · 14:00 – 15:00" */
export function formatSlotFull(startUtcIso: string, endUtcIso: string, nextDayLabel?: string): string {
  return `${formatSlotDay(startUtcIso)} · ${formatSlotRange(startUtcIso, endUtcIso, nextDayLabel)}`;
}

/** Slot/randevu geçmişte mi (başlangıcı şu andan önce). */
export function isPastSlot(startUtcIso: string): boolean {
  const d = new Date(startUtcIso);
  return !Number.isNaN(d.getTime()) && d.getTime() < Date.now();
}
