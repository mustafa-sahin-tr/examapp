import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Booking } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { formatSlotDay, formatSlotRange } from '../../shared/utils/booking-format.util';

type RequestFilter = 'pending' | 'all';

interface BookingRow {
  booking: Booking;
  day: string;
  range: string;
  statusLabel: string;
  statusClass: 'is-pending' | 'is-approved' | 'is-rejected';
}

/** Ret gerekçesi için backend sınırı. */
const REJECTION_REASON_MAX = 500;

/**
 * Öğretmenin randevu talepleri gelen kutusu (issue #96). Bekleyen talepler onaylanır
 * veya (opsiyonel gerekçeyle) reddedilir; karar sonrası liste tazelenir.
 * Filtreleme client-side — backend tüm talepleri tek uçtan döner.
 */
@Component({
  selector: 'app-teacher-booking-requests',
  standalone: true,
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './teacher-booking-requests.component.html',
  styleUrls: ['./teacher-booking-requests.component.scss'],
})
export class TeacherBookingRequestsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly reasonMaxLength = REJECTION_REASON_MAX;

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly actingId = signal<number | null>(null);
  protected readonly filter = signal<RequestFilter>('pending');
  protected readonly bookings = signal<Booking[]>([]);

  /** Ret gerekçesi formu açık olan talep. */
  protected readonly rejectingId = signal<number | null>(null);
  protected rejectionReason = '';

  protected readonly rows = computed<BookingRow[]>(() => {
    const onlyPending = this.filter() === 'pending';
    return this.bookings()
      .filter((b) => !onlyPending || b.status === 'Pending')
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((booking) => this.toRow(booking));
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  ngOnInit(): void {
    this.load();
  }

  protected setFilter(value: RequestFilter): void {
    this.filter.set(value);
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.bookingService
      .getTeacherRequests()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => this.bookings.set(res.items ?? []),
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, 'Randevu talepleri yüklenemedi.')),
      });
  }

  protected approve(row: BookingRow): void {
    this.act(this.bookingService.approveBooking(row.booking.id), row.booking.id, 'Randevu onaylandı.');
  }

  protected startReject(row: BookingRow): void {
    this.rejectionReason = '';
    this.rejectingId.set(row.booking.id);
  }

  protected cancelReject(): void {
    this.rejectingId.set(null);
    this.rejectionReason = '';
  }

  protected confirmReject(row: BookingRow): void {
    this.act(
      this.bookingService.rejectBooking(row.booking.id, this.rejectionReason),
      row.booking.id,
      'Randevu reddedildi.'
    );
  }

  private act(call: ReturnType<BookingService['approveBooking']>, id: number, successMsg: string): void {
    if (this.actingId() !== null) {
      return;
    }
    this.actingId.set(id);
    call
      .pipe(
        finalize(() => this.actingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.snackBar.open(res.message || 'İşlem tamamlanamadı.', 'Tamam', { duration: 4000 });
            return;
          }
          this.snackBar.open(successMsg, 'Tamam', { duration: 3000 });
          this.cancelReject();
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.bookingService.extractError(err, 'İşlem tamamlanamadı.'), 'Tamam', {
            duration: 4000,
          });
        },
      });
  }

  private toRow(booking: Booking): BookingRow {
    const map = {
      Approved: { statusLabel: 'Onaylandı', statusClass: 'is-approved' as const },
      Rejected: { statusLabel: 'Reddedildi', statusClass: 'is-rejected' as const },
      Pending: { statusLabel: 'Bekliyor', statusClass: 'is-pending' as const },
    };
    return {
      booking,
      day: formatSlotDay(booking.startUtc),
      range: formatSlotRange(booking.startUtc, booking.endUtc),
      ...map[booking.status],
    };
  }
}
