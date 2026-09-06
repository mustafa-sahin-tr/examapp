import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  output,
  signal,
} from '@angular/core';
import {
  addMonths,
  buildMonthWeeks,
  CalendarCell,
  mondayIndex,
  startOfMonth,
  toLocalIso,
} from '../../utils/calendar-month.util';
import { CalendarEvent } from '../../../models/calendar-event';
import { CalendarEventBadgeComponent } from '../calendar-event-badge/calendar-event-badge.component';

/**
 * Static monthly calendar grid (no event data). Monday-first.
 *
 * Inputs/outputs: takes `month` (any Date within the month to render),
 * emits `dayClick` when a day is activated and `monthChange` when keyboard
 * paging (PageUp/PageDown) crosses a month boundary. The month navigation
 * toolbar lives in the parent page, not here.
 *
 * Accessibility: `role="grid"` with a roving tabindex over day cells.
 * Arrow keys move by day/week, Home/End jump to week edges, PageUp/PageDown
 * page months. Enter/Space activate the focused day.
 */
@Component({
  selector: 'app-month-calendar-grid',
  standalone: true,
  imports: [CalendarEventBadgeComponent],
  templateUrl: './month-calendar-grid.component.html',
  styleUrls: ['./month-calendar-grid.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MonthCalendarGridComponent {
  readonly month = input.required<Date>();
  /** Aralık dışı günlere denk gelen etkinlikler yok sayılır (görünen hücre yoksa gösterilmez). */
  readonly events = input<CalendarEvent[]>([]);
  /** Mobil: rozetler nokta olarak render edilir (parent geçer). */
  readonly compact = input(false);

  /** Maksimum kaç rozet gösterilecek; fazlası "+N daha" olur. */
  readonly maxBadges = 3;

  readonly dayClick = output<Date>();
  readonly monthChange = output<Date>();

  readonly weekdays = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];

  private readonly injector = inject(Injector);

  readonly weeks = computed<CalendarCell[][]>(() => buildMonthWeeks(this.month()));
  readonly cells = computed<CalendarCell[]>(() => this.weeks().flat());

  /** Local gün ISO (yyyy-mm-dd) -> o güne düşen etkinlikler, sıralı. */
  readonly eventsByDay = computed<Map<string, CalendarEvent[]>>(() => {
    const map = new Map<string, CalendarEvent[]>();
    for (const ev of this.events()) {
      const iso = toLocalIso(new Date(ev.date));
      const bucket = map.get(iso);
      if (bucket) {
        bucket.push(ev);
      } else {
        map.set(iso, [ev]);
      }
    }
    for (const bucket of map.values()) {
      bucket.sort((a, b) => {
        if (a.kind !== b.kind) {
          return a.kind === 'reminder' ? -1 : 1;
        }
        return new Date(a.date).getTime() - new Date(b.date).getTime();
      });
    }
    return map;
  });

  eventsFor(cell: CalendarCell): CalendarEvent[] {
    return this.eventsByDay().get(cell.iso) ?? [];
  }

  visibleEvents(cell: CalendarCell): CalendarEvent[] {
    return this.eventsFor(cell).slice(0, this.maxBadges);
  }

  overflowCount(cell: CalendarCell): number {
    return Math.max(0, this.eventsFor(cell).length - this.maxBadges);
  }

  /** Hücrenin erişilebilir etiketi — tarih + varsa etkinlik sayısı. */
  cellAriaLabel(cell: CalendarCell): string {
    const count = this.eventsFor(cell).length;
    return count ? `${cell.label}, ${count} etkinlik` : cell.label;
  }

  /** ISO (yyyy-mm-dd, local) of the day that currently owns tabindex=0. */
  readonly focusedIso = signal<string | null>(null);

  private readonly resolvedFocusIso = computed(() => {
    const cells = this.cells();
    const current = this.focusedIso();
    if (current && cells.some((c) => c.iso === current)) {
      return current;
    }
    const today = cells.find((c) => c.isToday && c.inCurrentMonth);
    if (today) {
      return today.iso;
    }
    const firstOfMonth = cells.find((c) => c.inCurrentMonth);
    return firstOfMonth ? firstOfMonth.iso : cells[0]?.iso ?? null;
  });

  isFocusTarget(cell: CalendarCell): boolean {
    return cell.iso === this.resolvedFocusIso();
  }

  onDayClick(cell: CalendarCell): void {
    this.focusedIso.set(cell.iso);
    this.dayClick.emit(cell.date);
  }

  onKeydown(event: KeyboardEvent, cell: CalendarCell): void {
    const handlers: Record<string, () => void> = {
      ArrowLeft: () => this.moveFocus(cell.date, -1),
      ArrowRight: () => this.moveFocus(cell.date, 1),
      ArrowUp: () => this.moveFocus(cell.date, -7),
      ArrowDown: () => this.moveFocus(cell.date, 7),
      Home: () => this.moveFocus(cell.date, -mondayIndex(cell.date)),
      End: () => this.moveFocus(cell.date, 6 - mondayIndex(cell.date)),
      PageUp: () => this.pageMonth(-1),
      PageDown: () => this.pageMonth(1),
      Enter: () => this.onDayClick(cell),
      ' ': () => this.onDayClick(cell),
    };

    const handler = handlers[event.key];
    if (handler) {
      event.preventDefault();
      handler();
    }
  }

  private moveFocus(from: Date, deltaDays: number): void {
    const target = new Date(from.getFullYear(), from.getMonth(), from.getDate() + deltaDays);
    const iso = toLocalIso(target);

    if (this.cells().some((c) => c.iso === iso)) {
      this.focusedIso.set(iso);
      this.focusCell(iso);
      return;
    }

    // Target lies outside the rendered grid -> page the month, then focus.
    this.monthChange.emit(startOfMonth(target));
    this.focusedIso.set(iso);
    afterNextRender(() => this.focusCell(iso), { injector: this.injector });
  }

  private pageMonth(delta: number): void {
    const next = addMonths(this.month(), delta);
    this.monthChange.emit(next);
    const currentFocus = this.resolvedFocusIso();
    if (currentFocus) {
      const parsed = new Date(`${currentFocus}T00:00:00`);
      const lastDay = new Date(next.getFullYear(), next.getMonth() + 1, 0).getDate();
      const moved = new Date(next.getFullYear(), next.getMonth(), Math.min(parsed.getDate(), lastDay));
      const iso = toLocalIso(moved);
      this.focusedIso.set(iso);
      afterNextRender(() => this.focusCell(iso), { injector: this.injector });
    }
  }

  private focusCell(iso: string): void {
    const el = document.getElementById(`cal-day-${iso}`);
    el?.focus();
  }
}
