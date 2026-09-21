import { DatePipe, isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, interval, take } from 'rxjs';
import { Booking } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import {
  JOIN_WINDOW_TICK_MS,
  JoinWindowInfo,
  getJoinWindow,
} from '../../shared/utils/booking-join-window';

type RequestFilter = 'pending' | 'all';

interface BookingRow {
  booking: Booking;
  /** Scope'a göreli çeviri anahtarı; şablonda `t()` ile çözülür. */
  statusKey: string;
  statusClass: 'is-pending' | 'is-approved' | 'is-rejected';
  /** Issue #97: "Derse katıl" butonunun durumu (ipucu metni kök `shared.joinWindow.*` sözlüğünden). */
  join: JoinWindowInfo;
}

/** Ret gerekçesi için backend sınırı. */
const REJECTION_REASON_MAX = 500;

/**
 * Öğretmenin randevu talepleri gelen kutusu (issue #96). Bekleyen talepler onaylanır
 * veya (opsiyonel gerekçeyle) reddedilir; karar sonrası liste tazelenir.
 * Filtreleme client-side — backend tüm talepleri tek uçtan döner.
 *
 * Çeviriler kendi Transloco scope'unda: `public/i18n/teacher-bookings/<lang>.json` (issue #183).
 * Katılım penceresi ipucu paylaşılan yardımcıya verilen çevirmenden (kök `shared.joinWindow.*`)
 * gelir; tarih/saat `date` pipe'ı ile aktif dilde biçimlenir.
 */
const TEACHER_BOOKINGS_SCOPE = 'teacher-bookings';

@Component({
  selector: 'app-teacher-booking-requests',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(TEACHER_BOOKINGS_SCOPE)],
  templateUrl: './teacher-booking-requests.component.html',
  styleUrls: ['./teacher-booking-requests.component.scss'],
})
export class TeacherBookingRequestsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);

  protected readonly reasonMaxLength = REJECTION_REASON_MAX;

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly actingId = signal<number | null>(null);
  protected readonly filter = signal<RequestFilter>('pending');
  protected readonly bookings = signal<Booking[]>([]);

  /** Ret gerekçesi formu açık olan talep. */
  protected readonly rejectingId = signal<number | null>(null);
  protected rejectionReason = '';

  /** Issue #97: katılım penceresi zamana bağlı — "şimdi" periyodik tazelenir. */
  private readonly now = signal(Date.now());

  protected readonly rows = computed<BookingRow[]>(() => {
    const onlyPending = this.filter() === 'pending';
    const now = this.now();
    return this.bookings()
      .filter((b) => !onlyPending || b.status === 'Pending')
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((booking) => this.toRow(booking, now));
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  constructor() {
    this.preloadScope();
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
          this.error.set(this.bookingService.extractError(err, this.text('messages.loadFailed'))),
      });
  }

  protected approve(row: BookingRow): void {
    this.act(this.bookingService.approveBooking(row.booking.id), row.booking.id, this.text('messages.approved'));
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
      this.text('messages.rejected')
    );
  }

  /** Issue #97: onaylı randevunun görüşme odasına gider. */
  protected joinLesson(row: BookingRow): void {
    void this.router.navigate(['/lessons', row.booking.id, 'video']);
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
            this.snackBar.open(res.message || this.text('messages.actionFailed'), this.text('messages.ok'), {
              duration: 4000,
            });
            return;
          }
          this.snackBar.open(successMsg, this.text('messages.ok'), { duration: 3000 });
          this.cancelReject();
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(
            this.bookingService.extractError(err, this.text('messages.actionFailed')),
            this.text('messages.ok'),
            { duration: 4000 }
          );
        },
      });
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${TEACHER_BOOKINGS_SCOPE}.${key}`) ?? '';
  }

  private toRow(booking: Booking, now: number): BookingRow {
    const map = {
      Approved: { statusKey: 'status.approved', statusClass: 'is-approved' as const },
      Rejected: { statusKey: 'status.rejected', statusClass: 'is-rejected' as const },
      Pending: { statusKey: 'status.pending', statusClass: 'is-pending' as const },
    };
    const join = getJoinWindow(booking.startUtc, booking.endUtc, now, (key, params) =>
      this.transloco.translate<string>(key, params) ?? ''
    );
    return {
      booking,
      join,
      ...map[booking.status],
    };
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${TEACHER_BOOKINGS_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}
