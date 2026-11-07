import { CalendarEvent } from '../../models/calendar-event';
import { isSameDay } from '../../shared/utils/calendar-month.util';

/**
 * Issue #888: Planım "Bu hafta" ajandası — takvim etkinliklerini Pazartesi başlayan 7 güne dağıtır.
 * Saf fonksiyonlar; çeviri ve gezinme komponentte.
 */

/** Satırın renk ailesi; lejanttaki üç ana tür (ödev, program, ders) + hatırlatma. */
export type AgendaKind = 'assignment' | 'program' | 'booking' | 'reminder';

/** Satır eylemi: `start` testi açar, `program` program detayına, `bookings` randevu listesine gider. */
export type AgendaAction = 'start' | 'program' | 'bookings' | null;

export interface AgendaRow {
  event: CalendarEvent;
  kind: AgendaKind;
  icon: string;
  title: string;
  /** Saat (ders, hatırlatma, teslim); program planında null. */
  time: Date | null;
  /** Bitiş saati (yalnız ders). */
  endTime: Date | null;
  done: boolean;
  /** Teslim tarihi geçmiş ve tamamlanmamış ödev. */
  overdue: boolean;
  action: AgendaAction;
}

export interface AgendaDay {
  date: Date;
  isToday: boolean;
  isPast: boolean;
  rows: AgendaRow[];
}

/** Yerel takvimde `d`'nin haftasının Pazartesi'si (gün başı). */
export function startOfWeek(d: Date): Date {
  const day = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const offset = (day.getDay() + 6) % 7;
  return new Date(day.getFullYear(), day.getMonth(), day.getDate() - offset);
}

export function addDays(d: Date, days: number): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + days);
}

function dayStart(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

/** Program planı çok günlüdür: [başlangıç günü, bitiş günü] o günü kapsıyorsa; diğerleri tek an. */
function coversDay(ev: CalendarEvent, day: Date): boolean {
  const start = dayStart(new Date(ev.date));
  if (ev.kind !== 'program-study-page') {
    return isSameDay(start, day);
  }
  const end = dayStart(new Date(ev.endDate ?? ev.date));
  return day >= start && day <= end;
}

function toRow(ev: CalendarEvent, now: Date, canOpenBookings: boolean): AgendaRow {
  const at = new Date(ev.date);
  switch (ev.kind) {
    case 'booking':
      return {
        event: ev,
        kind: 'booking',
        icon: 'cast_for_education',
        title: ev.worksheetTitle,
        time: at,
        endTime: ev.endDate ? new Date(ev.endDate) : null,
        done: (ev.endDate ? new Date(ev.endDate) : at) < now,
        overdue: false,
        action: canOpenBookings ? 'bookings' : null,
      };
    case 'program-study-page': {
      const done = ev.isCompleted === true;
      return {
        event: ev,
        kind: 'program',
        icon: done ? 'check_circle' : 'menu_book',
        title: ev.studyItemTitle || ev.programName || '',
        time: null,
        endTime: null,
        done,
        overdue: false,
        action: ev.programId !== null ? 'program' : null,
      };
    }
    case 'reminder':
      return {
        event: ev,
        kind: 'reminder',
        icon: 'event_available',
        title: ev.worksheetTitle,
        time: at,
        endTime: null,
        done: ev.status === 'Sent' && at < now,
        overdue: false,
        action: 'start',
      };
    default: {
      const done = ev.isCompleted === true;
      return {
        event: ev,
        kind: 'assignment',
        icon: done ? 'check_circle' : 'flag',
        title: ev.worksheetTitle,
        time: at,
        endTime: null,
        done,
        overdue: !done && at < now,
        action: done ? null : 'start',
      };
    }
  }
}

/** Gün içi sıra: bitmemişler önce, sonra saat (saatsiz program planı en başta). */
function compareRows(a: AgendaRow, b: AgendaRow): number {
  if (a.done !== b.done) return a.done ? 1 : -1;
  return (a.time?.getTime() ?? 0) - (b.time?.getTime() ?? 0);
}

/** `weekStart`'tan (Pazartesi) itibaren 7 gün; her güne o günü kapsayan etkinlikler. */
export function buildWeekAgenda(
  events: readonly CalendarEvent[],
  weekStart: Date,
  now: Date,
  canOpenBookings: boolean,
): AgendaDay[] {
  const today = dayStart(now);
  return Array.from({ length: 7 }, (_, i) => {
    const date = addDays(weekStart, i);
    const rows = events
      .filter((ev) => coversDay(ev, date))
      .map((ev) => toRow(ev, now, canOpenBookings))
      .sort(compareRows);
    return { date, isToday: isSameDay(date, today), isPast: date < today, rows };
  });
}
