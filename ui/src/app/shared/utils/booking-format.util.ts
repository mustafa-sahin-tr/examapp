/**
 * Randevu/müsaitlik aralıklarının ortak gösterim yardımcıları (issue #96).
 * Backend `startUtc`/`endUtc` alanlarını hazır döndüğü için gösterimde bunlar kullanılır;
 * `date`/`startTime` alanları yalnızca form/DTO tarafında iş görür.
 */

const DAY_FMT = new Intl.DateTimeFormat('tr-TR', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  weekday: 'long',
});

const TIME_FMT = new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' });

/** "20 Eylül 2026 Pazar" */
export function formatSlotDay(utcIso: string): string {
  const d = new Date(utcIso);
  return Number.isNaN(d.getTime()) ? '—' : DAY_FMT.format(d);
}

/** "14:00 – 15:00" */
export function formatSlotRange(startUtcIso: string, endUtcIso: string): string {
  const start = new Date(startUtcIso);
  const end = new Date(endUtcIso);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
    return '—';
  }
  return `${TIME_FMT.format(start)} – ${TIME_FMT.format(end)}`;
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

/** `Date` -> "2026-09-20" (local gün, backend DateOnly bekliyor). */
export function toDateOnly(date: Date): string {
  const y = date.getFullYear();
  const m = `${date.getMonth() + 1}`.padStart(2, '0');
  const d = `${date.getDate()}`.padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** "14:00" -> "14:00:00" (backend TimeOnly bekliyor). */
export function toTimeOnly(hhmm: string): string {
  return /^\d{2}:\d{2}$/.test(hhmm) ? `${hhmm}:00` : hhmm;
}

/** "14:00" biçimindeki saati dakikaya çevirir; geçersizse null. */
export function parseMinutes(hhmm: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(hhmm.trim());
  if (!match) {
    return null;
  }
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (hours > 23 || minutes > 59) {
    return null;
  }
  return hours * 60 + minutes;
}
