import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, interval, take } from 'rxjs';
import { Booking } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { LocaleService } from '../../services/locale.service';
import { JOIN_WINDOW_TICK_MS, JoinWindowInfo, getJoinWindow } from '../../shared/utils/booking-join-window';

type BookingFilter = 'active' | 'all';

interface BookingRow {
  booking: Booking;
  day: string;
  range: string;
  /** `student-bookings` scope'una göreli durum anahtarı. */
  statusLabelKey: string;
  statusClass: 'is-pending' | 'is-approved' | 'is-rejected';
  /** Issue #97: "Derse katıl" butonunun durumu; ipucu kök sözlükteki ortak anahtarlardan gelir. */
  join: JoinWindowInfo;
}

const SCOPE = 'student-bookings';

/**
 * Öğrencinin kendi ders randevusu talepleri (issue #96). Salt listeleme — talep
 * oluşturma öğretmen profilindeki "Randevu Al" akışından yapılır.
 * "Aktif" filtresi reddedilenleri gizler.
 */
@Component({
  selector: 'app-student-bookings',
  standalone: true,
  imports: [
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './student-bookings.component.html',
  styleUrls: ['./student-bookings.component.scss'],
})
export class StudentBookingsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal<BookingFilter>('active');
  protected readonly bookings = signal<Booking[]>([]);

  /** Issue #97: katılım penceresi zamana bağlı — "şimdi" periyodik tazelenir. */
  private readonly now = signal(Date.now());

  /**
   * Gün/saat biçimlendirmesi aktif dile bağlıdır (issue #183). Ortak
   * `booking-format.util` yardımcıları 'tr-TR' sabitiyle çalıştığı için burada
   * dile bağlı biçimlendirici kullanılır.
   */
  private readonly dayFormat = computed(
    () =>
      new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, {
        day: 'numeric',
        month: 'long',
        year: 'numeric',
        weekday: 'long',
      })
  );

  private readonly timeFormat = computed(
    () =>
      new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, {
        hour: '2-digit',
        minute: '2-digit',
      })
  );

  protected readonly rows = computed<BookingRow[]>(() => {
    const onlyActive = this.filter() === 'active';
    const now = this.now();
    return this.bookings()
      .filter((b) => !onlyActive || b.status !== 'Rejected')
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((booking) => this.toRow(booking, now));
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  constructor() {
    // Sunucuda periyodik timer uygulamayı "stable" olmaktan alıkoyar — yalnızca tarayıcıda.
    if (isPlatformBrowser(inject(PLATFORM_ID))) {
      interval(JOIN_WINDOW_TICK_MS)
        .pipe(takeUntilDestroyed())
        .subscribe(() => this.now.set(Date.now()));
    }
  }

  ngOnInit(): void {
    this.load();
  }

  protected setFilter(value: BookingFilter): void {
    this.filter.set(value);
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.bookingService
      .getStudentRequests()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => this.bookings.set(res.items ?? []),
        // Yedek mesaj sözlükten gelir; scope henüz yüklenmemiş olabileceği için `selectTranslate`.
        error: (err: HttpErrorResponse) =>
          this.transloco
            .selectTranslate<string>('loadError', {}, SCOPE)
            .pipe(take(1), takeUntilDestroyed(this.destroyRef))
            .subscribe((fallback) => this.error.set(this.bookingService.extractError(err, fallback))),
      });
  }

  /** Issue #97: onaylı randevunun görüşme odasına gider. */
  protected joinLesson(row: BookingRow): void {
    void this.router.navigate(['/lessons', row.booking.id, 'video']);
  }

  private toRow(booking: Booking, now: number): BookingRow {
    const map = {
      Approved: { statusLabelKey: 'status.approved', statusClass: 'is-approved' as const },
      Rejected: { statusLabelKey: 'status.rejected', statusClass: 'is-rejected' as const },
      Pending: { statusLabelKey: 'status.pending', statusClass: 'is-pending' as const },
    };
    // Ipucu metni ortak katilim penceresi anahtarlarindan gelir; kok sozluk bootstrap'te yuklenir.
    const join = getJoinWindow(
      booking.startUtc,
      booking.endUtc,
      now,
      (key, params) => this.transloco.translate<string>(key, params) ?? ''
    );
    return {
      booking,
      day: this.formatDay(booking.startUtc),
      range: this.formatRange(booking.startUtc, booking.endUtc),
      join,
      ...map[booking.status],
    };
  }

  private formatDay(utcIso: string): string {
    const date = new Date(utcIso);
    return Number.isNaN(date.getTime()) ? '—' : this.dayFormat().format(date);
  }

  private formatRange(startUtcIso: string, endUtcIso: string): string {
    const start = new Date(startUtcIso);
    const end = new Date(endUtcIso);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
      return '—';
    }
    const format = this.timeFormat();
    return `${format.format(start)} – ${format.format(end)}`;
  }
}
