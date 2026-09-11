import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Booking } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { formatSlotDay, formatSlotRange } from '../../shared/utils/booking-format.util';

type BookingFilter = 'active' | 'all';

interface BookingRow {
  booking: Booking;
  day: string;
  range: string;
  statusLabel: string;
  statusClass: 'is-pending' | 'is-approved' | 'is-rejected';
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
  ],
  templateUrl: './student-bookings.component.html',
  styleUrls: ['./student-bookings.component.scss'],
})
export class StudentBookingsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal<BookingFilter>('active');
  protected readonly bookings = signal<Booking[]>([]);

  protected readonly rows = computed<BookingRow[]>(() => {
    const onlyActive = this.filter() === 'active';
    return this.bookings()
      .filter((b) => !onlyActive || b.status !== 'Rejected')
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((booking) => this.toRow(booking));
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

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

  private toRow(booking: Booking): BookingRow {
    const map = {
      Approved: { statusLabel: 'Onaylandı', statusClass: 'is-approved' as const },
      Rejected: { statusLabel: 'Reddedildi', statusClass: 'is-rejected' as const },
      Pending: { statusLabel: 'Onay bekliyor', statusClass: 'is-pending' as const },
    };
    return {
      booking,
      day: formatSlotDay(booking.startUtc),
      range: formatSlotRange(booking.startUtc, booking.endUtc),
      ...map[booking.status],
    };
  }
}
