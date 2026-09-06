import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MonthCalendarGridComponent } from '../../shared/components/month-calendar-grid/month-calendar-grid.component';
import { addMonths, formatMonthYear, startOfMonth } from '../../shared/utils/calendar-month.util';

type CalendarStatus = 'loading' | 'error' | 'empty' | 'ready';

/**
 * Student calendar page — STATIC skeleton (issue #36).
 * No event data yet; real fetch/wiring lands in issue #38. The `status`
 * signal already models loading/error/empty so the follow-up issue only
 * needs to flip it. All date state is local time (see calendar-month.util).
 */
@Component({
  selector: 'app-my-calendar',
  standalone: true,
  imports: [
    RouterLink,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MonthCalendarGridComponent,
  ],
  templateUrl: './my-calendar.component.html',
  styleUrls: ['./my-calendar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyCalendarComponent {
  readonly viewMonth = signal<Date>(startOfMonth(new Date()));
  readonly status = signal<CalendarStatus>('empty');

  readonly monthLabel = computed(() => formatMonthYear(this.viewMonth()));

  prevMonth(): void {
    this.viewMonth.set(addMonths(this.viewMonth(), -1));
  }

  nextMonth(): void {
    this.viewMonth.set(addMonths(this.viewMonth(), 1));
  }

  goToday(): void {
    this.viewMonth.set(startOfMonth(new Date()));
  }

  onMonthChange(month: Date): void {
    this.viewMonth.set(startOfMonth(month));
  }

  onDayClick(date: Date): void {
    // No-op for now; day -> detail navigation arrives with issue #38.
    console.debug('calendar day clicked', date);
  }

  retry(): void {
    // Stub until issue #38 wires a real data source.
    this.status.set('empty');
  }
}
