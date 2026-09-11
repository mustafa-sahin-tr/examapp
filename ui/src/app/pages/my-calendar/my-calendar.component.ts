import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable, Subject, catchError, map, merge, of, switchMap, tap } from 'rxjs';
import { MonthCalendarGridComponent } from '../../shared/components/month-calendar-grid/month-calendar-grid.component';
import { CalendarLegendComponent } from '../../shared/components/calendar-legend/calendar-legend.component';
import { addMonths, buildMonthWeeks, formatMonthYear, startOfMonth, toLocalIso } from '../../shared/utils/calendar-month.util';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { CalendarEvent } from '../../models/calendar-event';
import {
  CalendarDayDialogComponent,
  CalendarDayDialogData,
} from '../../shared/components/calendar-day-dialog/calendar-day-dialog.component';

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
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly bottomSheet = inject(MatBottomSheet);
  /** Takvim artık öğretmene de açık (issue #96) — boş durum/aksiyon metinleri role göre değişir. */
  protected readonly isTeacher = inject(AuthService).hasRealmRole('Teacher');

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

  /**
   * Gün hücresine tıklandığında (issue #39): o güne düşen etkinliklerle
   * mobilde bottom-sheet, masaüstünde — tek etkinlik varsa doğrudan gezinme,
   * birden fazla varsa dialog açar. Etkinlik yoksa hiçbir şey yapmaz.
   */
  onDayClick(date: Date): void {
    const iso = toLocalIso(date);
    const dayEvents = this.events()
      .filter((ev) => this.coversDay(ev, date, iso))
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());

    if (dayEvents.length === 0) {
      return;
    }

    const data: CalendarDayDialogData = { date, events: dayEvents };

    if (this.isMobile()) {
      this.bottomSheet.open(CalendarDayDialogComponent, { data, restoreFocus: true });
      return;
    }

    if (dayEvents.length === 1) {
      this.navigateToEvent(dayEvents[0]);
      return;
    }

    this.dialog.open(CalendarDayDialogComponent, { data, restoreFocus: true, autoFocus: 'dialog', width: '32rem' });
  }

  /** Program planı bar'ına tıklama (issue #113) — her zaman program detayına gider. */
  onBarClick(event: CalendarEvent): void {
    this.navigateToEvent(event);
  }

  /**
   * Etkinlik türüne göre hedef: worksheet tabanlılar `/test/:id`, program planı
   * `/programs/:id/detail` (öğrenci için tekil study-page rotası yok).
   */
  private navigateToEvent(event: CalendarEvent): void {
    if (event.kind === 'booking') {
      // Randevunun worksheet'i yok (issue #96) — role göre randevu listesine gidilir.
      void this.router.navigate([this.isTeacher ? '/booking-requests' : '/my-bookings']);
      return;
    }
    if (event.kind === 'program-study-page') {
      if (event.programId !== null) {
        void this.router.navigate(['/programs', event.programId, 'detail']);
      }
      return;
    }
    void this.router.navigate(['/test', event.worksheetId]);
  }

  /**
   * Tek anlık etkinlikler o güne düşüyorsa; çok günlü program planları
   * [başlangıç günü, bitiş günü] aralığı (bitiş dahil) o günü kapsıyorsa.
   */
  private coversDay(event: CalendarEvent, day: Date, iso: string): boolean {
    if (event.kind !== 'program-study-page') {
      return toLocalIso(new Date(event.date)) === iso;
    }
    const start = new Date(event.date);
    const end = new Date(event.endDate ?? event.date);
    const startDay = new Date(start.getFullYear(), start.getMonth(), start.getDate());
    const endDay = new Date(end.getFullYear(), end.getMonth(), end.getDate());
    return day >= startDay && day <= endDay;
  }

  retry(): void {
    this.retry$.next();
  }
}
