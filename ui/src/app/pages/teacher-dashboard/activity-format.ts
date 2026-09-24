/**
 * Issue #56: öğretmen dashboard aktivite kartları/tablosu için saf biçimlendirme yardımcıları.
 * Metin üretmez; Transloco anahtarı + parametre döndürür, çeviri şablonda yapılır (dil değişimine duyarlı).
 */

import { dashboardIsoDate } from '../../shared/utils/dashboard-time-zone.util';

/** Scope'a göreli çeviri anahtarı + parametreleri; şablonda `t(key, params)` ile çözülür. */
export interface DurationLabel {
  key:
    | 'activity.duration.hoursMinutes'
    | 'activity.duration.hours'
    | 'activity.duration.minutesSeconds'
    | 'activity.duration.minutes'
    | 'activity.duration.seconds';
  params: { h?: number; m?: number; s?: number };
}

/**
 * Saniyeyi okunur süre anahtarına çevirir:
 * - ≥ 1 saat → "2 sa 5 dk" (dakika 0 ise "2 sa"; saniye atılır)
 * - ≥ 1 dakika → "2 dk 30 sn" (saniye 0 ise "2 dk")
 * - aksi → "45 sn"
 * Negatif / NaN / sonsuz değerler 0 kabul edilir; küsurat aşağı yuvarlanır.
 */
export function formatActivityDuration(totalSeconds: number): DurationLabel {
  const safe = Number.isFinite(totalSeconds) && totalSeconds > 0 ? Math.floor(totalSeconds) : 0;
  const h = Math.floor(safe / 3600);
  const m = Math.floor((safe % 3600) / 60);
  const s = safe % 60;

  if (h > 0) {
    return m > 0 ? { key: 'activity.duration.hoursMinutes', params: { h, m } } : { key: 'activity.duration.hours', params: { h } };
  }
  if (m > 0) {
    return s > 0
      ? { key: 'activity.duration.minutesSeconds', params: { m, s } }
      : { key: 'activity.duration.minutes', params: { m } };
  }
  return { key: 'activity.duration.seconds', params: { s } };
}

/**
 * Doğruluk yüzdesi (0-100, tam sayıya yuvarlanır). Hiç soru çözülmemişse (0'a bölme) `null` döner;
 * şablon bu durumda kartta "Henüz çözülen soru yok" yazar, tabloda yüzdeyi hiç göstermez.
 */
export function accuracyPercent(correctCount: number, questionsSolved: number): number | null {
  if (!Number.isFinite(questionsSolved) || questionsSolved <= 0) {
    return null;
  }
  const ratio = Math.max(0, Math.min(correctCount, questionsSolved)) / questionsSolved;
  return Math.round(ratio * 100);
}

const MS_PER_DAY = 24 * 60 * 60 * 1000;

/**
 * Aktivite penceresinin okunur tarih aralığı: bugün dahil son `days` Türkiye yerel takvim günü
 * (ör. "17–23 Eylül 2026"). Backend penceresi de aynı tanımı kullanır (issue #265: son N yerel gün, bugün dahil,
 * yerel gece yarısından başlar; saat dilimi `Dashboard:TimeZone` → `DASHBOARD_TIME_ZONE`).
 *
 * "Bugün" `now` anının `DASHBOARD_TIME_ZONE`'daki takvim günüdür; gün aritmetiği ve biçimleme o takvim
 * gününün UTC gece yarısı temsili üzerinde yapılır. Böylece tarayıcının saat dilimi ve sunucu/tarayıcı farkı
 * (SSR hydration) sonucu değiştirmez, DST geçişleri de gün sayısını kaydırmaz. Aralık birleştirmesi
 * `formatRange` ile dile göre yapılır.
 */
export function formatActivityPeriod(days: number, now: Date, locale: string): string {
  const [y, m, d] = dashboardIsoDate(now).split('-').map(Number);
  const endMs = Date.UTC(y, m - 1, d);
  const startMs = endMs - (Math.max(1, Math.floor(days)) - 1) * MS_PER_DAY;
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    // Tarihler zaten yerel takvim gününün UTC gece yarısı temsili; burada UTC ile biçimlemek günü korur.
    timeZone: 'UTC',
  }).formatRange(new Date(startMs), new Date(endMs));
}
