import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable, Subject, catchError, map, merge, of, switchMap, tap } from 'rxjs';
import { MonthCalendarGridComponent } from '../../shared/components/month-calendar-grid/month-calendar-grid.component';
import { CalendarLegendComponent } from '../../shared/components/calendar-legend/calendar-legend.component';
import { addMonths, buildMonthWeeks, formatMonthYear, startOfMonth } from '../../shared/utils/calendar-month.util';
import { TestService } from '../../services/test.service';
import { CalendarEvent } from '../../models/calendar-event';

type CalendarStatus = 'loading' | 'error' | 'empty' | 'ready';

interface CalendarResult {
  ok: boolean;
  events: CalendarEvent[];
}

/**
 * Öğrenci takvimi sayfası (issue #38) — planlanmış hatırlatmalar + atama teslim
 * tarihleri aylık grid'de gerçek veriyle. Ay değişince (veya "Tekrar dene")
 * `GET /api/exam/calendar/me` çağrılır; hızlı navigasyonda eski cevaplar
 * `switchMap` ile iptal edilir. Aralık, grid'in ilk/son hücresini kapsar
 * (önceki/sonraki aydan görünen günler dahil), local gün başı UTC'ye çevrilir; `to` exclusive.
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
    CalendarLegendComponent,
  ],
  templateUrl: './my-calendar.component.html',
  styleUrls: ['./my-calendar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyCalendarComponent {
  private readonly testService = inject(TestService);
  private readonly destroyRef = inject(DestroyRef);

  readonly viewMonth = signal<Date>(startOfMonth(new Date()));
  readonly status = signal<CalendarStatus>('loading');
  readonly events = signal<CalendarEvent[]>([]);
  readonly isMobile = signal(false);

  readonly monthLabel = computed(() => formatMonthYear(this.viewMonth()));

  private readonly retry$ = new Subject<void>();

  constructor() {
    const mql = window.matchMedia('(max-width: 767px)');
    const sync = (e: MediaQueryList | MediaQueryListEvent) => this.isMobile.set(e.matches);
    sync(mql);
    mql.addEventListener('change', sync);
    this.destroyRef.onDestroy(() => mql.removeEventListener('change', sync));

    merge(toObservable(this.viewMonth), this.retry$.pipe(map(() => this.viewMonth())))
      .pipe(
        tap(() => this.status.set('loading')),
        switchMap((month) => this.fetch(month)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        if (!result.ok) {
          this.status.set('error');
          return;
        }
        this.events.set(result.events);
        this.status.set(result.events.length ? 'ready' : 'empty');
      });
  }

  private fetch(month: Date): Observable<CalendarResult> {
    const cells = buildMonthWeeks(month).flat();
    const first = cells[0].date;
    const last = cells[cells.length - 1].date;
    // Grid'in ilk hücresinin local günü -> son hücresinin ertesi günü (exclusive).
    const from = new Date(first.getFullYear(), first.getMonth(), first.getDate());
    const to = new Date(last.getFullYear(), last.getMonth(), last.getDate() + 1);

    return this.testService.getMyCalendar(from, to).pipe(
      map((events) => ({ ok: true, events }) as CalendarResult),
      catchError(() => of({ ok: false, events: [] } as CalendarResult)),
    );
  }

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

  onDayClick(_date: Date): void {
    // Gün detayı bu issue kapsamı değil (issue #38).
  }

  retry(): void {
    this.retry$.next();
  }
}
