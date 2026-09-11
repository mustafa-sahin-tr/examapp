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
import { finalize, interval } from 'rxjs';
import { Booking } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { formatSlotDay, formatSlotRange } from '../../shared/utils/booking-format.util';
import {
  JOIN_WINDOW_TICK_MS,
  JoinWindowInfo,
  getJoinWindow,
} from '../../shared/utils/booking-join-window';

type BookingFilter = 'active' | 'all';

interface BookingRow {
  booking: Booking;
  day: string;
  range: string;
  statusLabel: string;
  statusClass: 'is-pending' | 'is-approved' | 'is-rejected';
  /** Issue #97: "Derse katıl" butonunun durumu. */
  join: JoinWindowInfo;
}

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
  ],
  templateUrl: './student-bookings.component.html',
  styleUrls: ['./student-bookings.component.scss'],
})
export class StudentBookingsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal<BookingFilter>('active');
  protected readonly bookings = signal<Booking[]>([]);

  /** Issue #97: katılım penceresi zamana bağlı — "şimdi" periyodik tazelenir. */
  private readonly now = signal(Date.now());

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
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, 'Randevuların yüklenemedi.')),
      });
  }

  /** Issue #97: onaylı randevunun görüşme odasına gider. */
  protected joinLesson(row: BookingRow): void {
    void this.router.navigate(['/lessons', row.booking.id, 'video']);
  }

  private toRow(booking: Booking, now: number): BookingRow {
    const map = {
      Approved: { statusLabel: 'Onaylandı', statusClass: 'is-approved' as const },
      Rejected: { statusLabel: 'Reddedildi', statusClass: 'is-rejected' as const },
      Pending: { statusLabel: 'Onay bekliyor', statusClass: 'is-pending' as const },
    };
    return {
      booking,
      day: formatSlotDay(booking.startUtc),
      range: formatSlotRange(booking.startUtc, booking.endUtc),
      join: getJoinWindow(booking.startUtc, booking.endUtc, now),
      ...map[booking.status],
    };
  }
}
