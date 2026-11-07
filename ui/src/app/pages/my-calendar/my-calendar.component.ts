import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, Subject, catchError, map, merge, of, switchMap, tap } from 'rxjs';
import { MonthCalendarGridComponent } from '../../shared/components/month-calendar-grid/month-calendar-grid.component';
import { CalendarLegendComponent } from '../../shared/components/calendar-legend/calendar-legend.component';
import { addMonths, buildMonthWeeks, startOfMonth, toLocalIso } from '../../shared/utils/calendar-month.util';
import { AgendaRow, addDays, buildWeekAgenda, startOfWeek } from './week-agenda.util';
import { LocaleService } from '../../services/locale.service';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { CalendarEvent } from '../../models/calendar-event';
import {
  CalendarDayDialogComponent,
  CalendarDayDialogData,
} from '../../shared/components/calendar-day-dialog/calendar-day-dialog.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Sayfanın Transloco scope'u: `public/i18n/my-calendar/<lang>.json` (issue #183). */
const SCOPE = 'my-calendar';

type CalendarStatus = 'loading' | 'error' | 'empty' | 'ready';

/** Issue #888: varsayılan "Bu hafta" ajandası; ay ızgarası seçenek. */
export type CalendarView = 'week' | 'month';

interface CalendarRange {
  from: Date;
  to: Date;
}

interface CalendarResult {
  ok: boolean;
  events: CalendarEvent[];
}

/**
 * Öğrenci takvimi sayfası (issue #38). #888: açılışta haftalık ajanda (gün gün liste, bugün vurgulu, satırda
 * "Başla" / "Programa git"); ay ızgarası sağ üstteki seçenekle. — planlanmış hatırlatmalar + atama teslim
 * tarihleri aylık grid'de gerçek veriyle. Ay değişince (veya "Tekrar dene")
 * `GET /api/exam/calendar/me` çağrılır; hızlı navigasyonda eski cevaplar
 * `switchMap` ile iptal edilir. Aralık, grid'in ilk/son hücresini kapsar
 * (önceki/sonraki aydan görünen günler dahil), local gün başı UTC'ye çevrilir; `to` exclusive.
 */
@Component({
  selector: 'app-my-calendar',
  standalone: true,
  imports: [
    PageHeaderComponent,
    RouterLink,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatButtonToggleModule,
    MonthCalendarGridComponent,
    CalendarLegendComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(SCOPE)],
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
  private readonly authService = inject(AuthService);
  protected readonly isTeacher = this.authService.hasRealmRole('Teacher');
  /**
   * Issue #418: müsaitlik bağımsız öğretmen özelliği — okula bağlı olduğu BİLİNEN öğretmene boş durumda `/availability`
   * düğmesi gösterilmez (rota `independentTeacherGuard` ile kapalı). Bayrak bilinmiyorsa gösterilir; asıl kapı backend.
   */
  protected readonly showAvailabilityCta = computed(() => this.isTeacher && this.canOpenBookingRequests());

  /** Issue #418: randevu sayfaları (`/availability`, `/booking-requests`) — bayrak bilinmiyorsa açık, asıl kapı backend. */
  private canOpenBookingRequests(): boolean {
    return AuthService.isIndependentTutorOf(this.authService.user()) !== false;
  }
  private readonly localeService = inject(LocaleService);

  readonly view = signal<CalendarView>('week');
  readonly weekStart = signal<Date>(startOfWeek(new Date()));
  readonly viewMonth = signal<Date>(startOfMonth(new Date()));
  readonly status = signal<CalendarStatus>('loading');
  readonly events = signal<CalendarEvent[]>([]);
  readonly isMobile = signal(false);

  /**
   * Ay/yıl etiketi aktif dile bağlıdır (issue #183). Ortak `formatMonthYear` yardımcısı 'tr-TR'
   * sabitiyle çalıştığı için burada dile bağlı biçimlendirici kullanılır.
   */
  readonly monthLabel = computed(() =>
    new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, {
      month: 'long',
      year: 'numeric',
    }).format(this.viewMonth())
  );

  private get intlLocale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  /** Haftalık başlık: "3–9 Kasım" (ay/yıl taşarsa Intl ikisini de yazar). */
  readonly weekLabel = computed(() =>
    new Intl.DateTimeFormat(this.intlLocale, { day: 'numeric', month: 'long' }).formatRange(
      this.weekStart(),
      addDays(this.weekStart(), 6),
    ),
  );

  readonly periodLabel = computed(() => (this.view() === 'week' ? this.weekLabel() : this.monthLabel()));

  /** #888: haftanın 7 günü ve o güne düşen satırlar. */
  readonly agendaDays = computed(() =>
    buildWeekAgenda(this.events(), this.weekStart(), new Date(), !this.isTeacher || this.canOpenBookingRequests()),
  );

  /** Öğretmen takviminde yalnız ders randevuları var. */
  readonly agendaLegend: readonly string[] = this.isTeacher ? ['booking'] : ['assignment', 'program', 'booking'];

  /** Gün başlığı: "Pzt" + "3". */
  dayName(date: Date): string {
    return new Intl.DateTimeFormat(this.intlLocale, { weekday: 'short' }).format(date);
  }

  /** Satır alt bilgisi: "Ödev · Matematik · teslim 17:00" / "Program · LGS" / "Ders · 17:00–18:00". */
  metaText(row: AgendaRow, t: (key: string, params?: Record<string, unknown>) => string): string {
    const ev = row.event;
    const parts = [t('kind.' + row.kind), row.kind === 'program' ? ev.programName : ev.subject];
    if (row.time) {
      parts.push(row.kind === 'assignment' ? t('week.dueAt', { time: this.timeText(row) }) : this.timeText(row));
    }
    if (row.done && row.kind !== 'booking') parts.push(t('week.done'));
    return parts.filter((p) => !!p).join(' · ');
  }

  timeText(row: AgendaRow): string {
    if (!row.time) return '';
    const fmt = new Intl.DateTimeFormat(this.intlLocale, { hour: '2-digit', minute: '2-digit' });
    return row.endTime ? fmt.formatRange(row.time, row.endTime) : fmt.format(row.time);
  }

  /** Görünen aralık: hafta [Pzt, sonraki Pzt) ya da ay ızgarasının ilk/son hücresi. */
  private readonly range = computed<CalendarRange>(() => {
    if (this.view() === 'week') {
      const from = this.weekStart();
      return { from, to: addDays(from, 7) };
    }
    const cells = buildMonthWeeks(this.viewMonth()).flat();
    const first = cells[0].date;
    const last = cells[cells.length - 1].date;
    // Grid'in ilk hücresinin local günü -> son hücresinin ertesi günü (exclusive).
    return {
      from: new Date(first.getFullYear(), first.getMonth(), first.getDate()),
      to: new Date(last.getFullYear(), last.getMonth(), last.getDate() + 1),
    };
  }, { equal: (a, b) => a.from.getTime() === b.from.getTime() && a.to.getTime() === b.to.getTime() });

  private readonly retry$ = new Subject<void>();

  constructor() {
    const mql = window.matchMedia('(max-width: 767px)');
    const sync = (e: MediaQueryList | MediaQueryListEvent) => this.isMobile.set(e.matches);
    sync(mql);
    mql.addEventListener('change', sync);
    this.destroyRef.onDestroy(() => mql.removeEventListener('change', sync));

    merge(toObservable(this.range), this.retry$.pipe(map(() => this.range())))
      .pipe(
        tap(() => this.status.set('loading')),
        switchMap((range) => this.fetch(range)),
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

  private fetch({ from, to }: CalendarRange): Observable<CalendarResult> {
    return this.testService.getMyCalendar(from, to).pipe(
      map((events) => ({ ok: true, events }) as CalendarResult),
      catchError(() => of({ ok: false, events: [] } as CalendarResult)),
    );
  }

  /** Görünüm değişince yeni görünüm, eskisinin gösterdiği tarihten açılır (hafta → o haftanın ayı). */
  setView(view: CalendarView): void {
    if (view === this.view()) return;
    if (view === 'month') {
      this.viewMonth.set(startOfMonth(this.weekStart()));
    } else {
      const today = new Date();
      const month = this.viewMonth();
      const inMonth = today.getFullYear() === month.getFullYear() && today.getMonth() === month.getMonth();
      this.weekStart.set(startOfWeek(inMonth ? today : month));
    }
    this.view.set(view);
  }

  prev(): void {
    if (this.view() === 'week') this.weekStart.set(addDays(this.weekStart(), -7));
    else this.viewMonth.set(addMonths(this.viewMonth(), -1));
  }

  next(): void {
    if (this.view() === 'week') this.weekStart.set(addDays(this.weekStart(), 7));
    else this.viewMonth.set(addMonths(this.viewMonth(), 1));
  }

  goToday(): void {
    this.weekStart.set(startOfWeek(new Date()));
    this.viewMonth.set(startOfMonth(new Date()));
  }

  /** Ajanda satırı eylemi: test / program detayı / randevular (ay görünümündeki tıklamayla aynı hedefler). */
  openRow(row: AgendaRow): void {
    this.navigateToEvent(row.event);
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
      // Issue #418: `/booking-requests` bağımsız öğretmen sayfası; okula bağlı olduğu bilinen öğretmen gezinmez.
      if (!this.isTeacher) {
        void this.router.navigate(['/my-bookings']);
      } else if (this.canOpenBookingRequests()) {
        void this.router.navigate(['/booking-requests']);
      }
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
