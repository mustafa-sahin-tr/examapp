/**
 * Pure, framework-free helpers for building a monthly calendar grid.
 *
 * All date math runs in the USER'S LOCAL TIME ZONE (not UTC): grid days,
 * "today" detection and the ISO string are all local. This keeps the visible
 * calendar aligned with the user's wall clock regardless of server time.
 *
 * Week starts on MONDAY. JS `Date.getDay()` returns 0=Sunday, so the
 * Monday-based index is `(getDay() + 6) % 7` -> 0=Monday .. 6=Sunday.
 */

export interface CalendarCell {
  /** Local midnight of the day this cell represents. */
  date: Date;
  /** Day of month (1-31). */
  day: number;
  /** True when the day belongs to the grid's reference month. */
  inCurrentMonth: boolean;
  /** True when the day is today (local). */
  isToday: boolean;
  /** Local calendar date as `yyyy-mm-dd` (not UTC). */
  iso: string;
  /** Accessible full-date label, e.g. "15 Eylül 2026" or "15 Eylül 2026, bugün". */
  label: string;
}

/** First day (local midnight) of the month that contains `d`. */
export function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

/** Same month `d` normalized to the 1st, shifted by `delta` months. */
export function addMonths(month: Date, delta: number): Date {
  return new Date(month.getFullYear(), month.getMonth() + delta, 1);
}

/** True when `a` and `b` fall on the same local calendar day. */
export function isSameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  );
}

/** Monday-based weekday index: 0=Monday .. 6=Sunday. */
export function mondayIndex(d: Date): number {
  return (d.getDay() + 6) % 7;
}

/** e.g. "15 Eylül 2026" (tr-TR). */
export function formatFullDate(d: Date): string {
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' }).format(d);
}

/** Local `yyyy-mm-dd` for `d`. */
export function toLocalIso(d: Date): string {
  const y = d.getFullYear();
  const m = `${d.getMonth() + 1}`.padStart(2, '0');
  const day = `${d.getDate()}`.padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/**
 * Build the calendar grid for the month containing `month`.
 * Includes leading days from the previous month and trailing days from the
 * next month so every row is a full Monday-Sunday week. The grid spans only
 * the weeks the month actually touches (5 or 6 rows), always a multiple of 7.
 */
export function buildMonthGrid(month: Date): CalendarCell[] {
  const first = startOfMonth(month);
  const lead = mondayIndex(first);
  const gridStart = new Date(first.getFullYear(), first.getMonth(), 1 - lead);

  const monthEnd = new Date(month.getFullYear(), month.getMonth() + 1, 0);
  const trail = 6 - mondayIndex(monthEnd);
  const totalDays = lead + monthEnd.getDate() + trail;

  const today = new Date();
  const cells: CalendarCell[] = [];

  for (let i = 0; i < totalDays; i++) {
    const date = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + i);
    const isToday = isSameDay(date, today);
    cells.push({
      date,
      day: date.getDate(),
      inCurrentMonth: date.getMonth() === month.getMonth() && date.getFullYear() === month.getFullYear(),
      isToday,
      iso: toLocalIso(date),
      label: isToday ? `${formatFullDate(date)}, bugün` : formatFullDate(date),
    });
  }

  return cells;
}

/**
 * Same cells as `buildMonthGrid`, sliced into full Monday-Sunday weeks
 * (7 cells each). Used to wrap each week in a `role="row"` container.
 */
export function buildMonthWeeks(month: Date): CalendarCell[][] {
  const cells = buildMonthGrid(month);
  const weeks: CalendarCell[][] = [];
  for (let i = 0; i < cells.length; i += 7) {
    weeks.push(cells.slice(i, i + 7));
  }
  return weeks;
}

/** e.g. "Eylül 2026" (tr-TR). */
export function formatMonthYear(month: Date): string {
  return new Intl.DateTimeFormat('tr-TR', { month: 'long', year: 'numeric' }).format(month);
}
