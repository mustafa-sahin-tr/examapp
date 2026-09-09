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
 * Çok günlü program planı (issue #113) — bir hafta satırındaki bar segmenti.
 * Bir etkinlik birden fazla haftaya taşarsa her hafta için ayrı segment üretilir.
 */
export interface ProgramBar {
  event: CalendarEvent;
  /** `weeks()` içindeki satır indeksi (0 tabanlı). */
  weekRow: number;
  /** CSS grid kolonu (1-7). */
  startCol: number;
  /** CSS grid bitiş çizgisi (exclusive, 2-8). */
  endCol: number;
  /** Aynı satırda çakışan bar'lar için katman sırası (0 tabanlı). */
  stack: number;
  completed: boolean;
  /** Bar üzerinde gösterilen kısa metin. */
  label: string;
  /** Segment etkinliğin ilk/son gününü içeriyor mu — köşe yuvarlatma için. */
  startsHere: boolean;
  endsHere: boolean;
}

/** Bar yüksekliği + boşluk (px). SCSS'teki `--bar-height`/`--bar-gap` ile eşleşir. */
const BAR_STEP_PX = 24;

/**
 * Static monthly calendar grid (no event data). Monday-first.
 *
 * Inputs/outputs: takes `month` (any Date within the month to render),
 * emits `dayClick` when a day is activated and `monthChange` when keyboard
 * paging (PageUp/PageDown) crosses a month boundary. The month navigation
 * toolbar lives in the parent page, not here.
 *
 * İki katman: hücre içindeki rozetler (reminder / assignment-deadline) ve
 * hafta satırının altındaki program planı bar'ları (`program-study-page`).
 * Program etkinlikleri rozet listesine girmez; bar tıklaması `barClick` ile
 * ayrı emit edilir (worksheet id'si yok, program detayına gider).
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
  /** Program planı bar'ına tıklanınca (kind === 'program-study-page'). */
  readonly barClick = output<CalendarEvent>();

  readonly weekdays = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];

  private readonly injector = inject(Injector);

  readonly weeks = computed<CalendarCell[][]>(() => buildMonthWeeks(this.month()));
  readonly cells = computed<CalendarCell[]>(() => this.weeks().flat());

  /** Rozet katmanı: reminder + assignment-deadline. */
  private readonly badgeEvents = computed<CalendarEvent[]>(() =>
    this.events().filter((ev) => ev.kind !== 'program-study-page'),
  );

  /** Bar katmanı: program-study-page. */
  private readonly programEvents = computed<CalendarEvent[]>(() =>
    this.events().filter((ev) => ev.kind === 'program-study-page'),
  );

  /** Local gün ISO (yyyy-mm-dd) -> o güne düşen rozet etkinlikleri, sıralı. */
  readonly eventsByDay = computed<Map<string, CalendarEvent[]>>(() => {
    const map = new Map<string, CalendarEvent[]>();
    for (const ev of this.badgeEvents()) {
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

  /**
   * Hafta satırı indeksi -> o satırdaki program bar segmentleri (stack atanmış).
   * Aralık: başlangıç gününden bitiş gününe kadar, bitiş günü DAHİL
   * (backend `EndDate >= from` ile sorgular; program-detail ile aynı semantik).
   */
  readonly barsByWeek = computed<Map<number, ProgramBar[]>>(() => {
    const weeks = this.weeks();
    const cells = this.cells();
    if (cells.length === 0) {
      return new Map();
    }

    const gridStart = cells[0].date;
    const gridEnd = cells[cells.length - 1].date;
    const bars: ProgramBar[] = [];

    for (const ev of this.programEvents()) {
      const start = this.localDay(new Date(ev.date));
      const end = this.localDay(new Date(ev.endDate ?? ev.date));
      if (end < gridStart || start > gridEnd) {
        continue;
      }

      const startIndex = Math.max(0, this.dayDiff(gridStart, start));
      const endIndex = Math.min(cells.length - 1, this.dayDiff(gridStart, end));
      const startRow = Math.floor(startIndex / 7);
      const endRow = Math.floor(endIndex / 7);
      const completed = ev.isCompleted === true;
      const title = ev.studyPageTitle || ev.programName || 'Çalışma planı';

      for (let row = startRow; row <= endRow; row++) {
        const rowStart = row * 7;
        const segmentStart = Math.max(startIndex, rowStart);
        const segmentEnd = Math.min(endIndex, rowStart + 6);
        bars.push({
          event: ev,
          weekRow: row,
          startCol: segmentStart - rowStart + 1,
          endCol: segmentEnd - rowStart + 2,
          stack: 0,
          completed,
          label: completed ? `✓ ${title} (tamamlandı)` : title,
          startsHere: segmentStart === startIndex,
          endsHere: segmentEnd === endIndex,
        });
      }
    }

    const byWeek = new Map<number, ProgramBar[]>();
    for (const bar of bars) {
      const bucket = byWeek.get(bar.weekRow);
      if (bucket) {
        bucket.push(bar);
      } else {
        byWeek.set(bar.weekRow, [bar]);
      }
    }
    for (const weekBars of byWeek.values()) {
      this.assignStackPositions(weekBars);
    }
    // Satırı olmayan haftalar da map'te bulunsun ki template'te boş katman yüksekliği 0 kalsın.
    for (let i = 0; i < weeks.length; i++) {
      if (!byWeek.has(i)) {
        byWeek.set(i, []);
      }
    }
    return byWeek;
  });

  barsFor(weekIndex: number): ProgramBar[] {
    return this.barsByWeek().get(weekIndex) ?? [];
  }

  /** Bar katmanının yüksekliği: en yüksek stack + 1 adet bar. */
  barLayerHeight(weekIndex: number): number {
    const bars = this.barsFor(weekIndex);
    if (bars.length === 0) {
      return 0;
    }
    const maxStack = bars.reduce((m, b) => Math.max(m, b.stack), 0);
    return (maxStack + 1) * BAR_STEP_PX;
  }

  barOffsetY(bar: ProgramBar): number {
    return bar.stack * BAR_STEP_PX;
  }

  barTrackKey(bar: ProgramBar): string {
    return `${bar.event.programId}-${bar.event.studyPageId}-${bar.event.date}-${bar.weekRow}`;
  }

  eventsFor(cell: CalendarCell): CalendarEvent[] {
    return this.eventsByDay().get(cell.iso) ?? [];
  }

  visibleEvents(cell: CalendarCell): CalendarEvent[] {
    return this.eventsFor(cell).slice(0, this.maxBadges);
  }

  overflowCount(cell: CalendarCell): number {
    return Math.max(0, this.eventsFor(cell).length - this.maxBadges);
  }

  /** O günü kapsayan program planı sayısı (bitiş günü dahil). */
  programCountFor(cell: CalendarCell): number {
    const day = cell.date;
    let count = 0;
    for (const ev of this.programEvents()) {
      const start = this.localDay(new Date(ev.date));
      const end = this.localDay(new Date(ev.endDate ?? ev.date));
      if (day >= start && day <= end) {
        count++;
      }
    }
    return count;
  }

  /** Hücrenin erişilebilir etiketi — tarih + varsa etkinlik sayısı (rozetler + plan bar'ları). */
  cellAriaLabel(cell: CalendarCell): string {
    const count = this.eventsFor(cell).length + this.programCountFor(cell);
    return count ? `${cell.label}, ${count} etkinlik` : cell.label;
  }

  onBarClick(bar: ProgramBar, event: Event): void {
    event.stopPropagation();
    this.barClick.emit(bar.event);
  }

  onBarKeydown(bar: ProgramBar, event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      event.stopPropagation();
      this.barClick.emit(bar.event);
    }
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

  /**
   * program-detail `assignStackPositions` ile aynı: başlangıç kolonuna, sonra uzunluğa göre sırala;
   * ilk boş katmana yerleştir.
   */
  private assignStackPositions(weekBars: ProgramBar[]): void {
    weekBars.sort((a, b) => {
      if (a.startCol !== b.startCol) {
        return a.startCol - b.startCol;
      }
      return b.endCol - b.startCol - (a.endCol - a.startCol);
    });

    const stacks: number[] = []; // her katmanın dolu olduğu son endCol (exclusive)
    for (const bar of weekBars) {
      let assigned = stacks.findIndex((endCol) => endCol <= bar.startCol);
      if (assigned === -1) {
        assigned = stacks.length;
        stacks.push(bar.endCol);
      } else {
        stacks[assigned] = bar.endCol;
      }
      bar.stack = assigned;
    }
  }

  private localDay(d: Date): Date {
    return new Date(d.getFullYear(), d.getMonth(), d.getDate());
  }

  /** İki local gün başlangıcı arasındaki gün farkı (DST'den etkilenmesin diye yuvarlanır). */
  private dayDiff(from: Date, to: Date): number {
    return Math.round((to.getTime() - from.getTime()) / 86_400_000);
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
