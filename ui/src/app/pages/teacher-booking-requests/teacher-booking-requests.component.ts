import { DatePipe, isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, PLATFORM_ID, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, filter as rxFilter, finalize, interval, take } from 'rxjs';
import { Booking, BookingResult, BookingStatus } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { openBookingCancelDialog } from '../../shared/components/booking-cancel-dialog/booking-cancel-dialog.component';
import { openBookingRescheduleDialog } from '../../shared/components/booking-reschedule-dialog/booking-reschedule-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  JOIN_WINDOW_TICK_MS,
  JoinWindowInfo,
  getJoinWindow,
} from '../../shared/utils/booking-join-window';
import { SLOT_NEXT_DAY_KEY, formatSlotDay, formatSlotRange, slotEndsNextDay } from '../../shared/utils/booking-format.util';
import { BookingLifecycleView, bookingLifecycleView } from '../../shared/utils/booking-lifecycle.util';

type RequestFilter = 'pending' | 'all';

type BookingStatusClass = 'is-pending' | 'is-approved' | 'is-rejected' | 'is-cancelled';

/**
 * Durum → `teacher-bookings` scope'una göreli etiket + chip sınıfı. Issue #439: sunucu öğretmene `AwaitingParent` /
 * `ParentRejected` / talepleri göndermez (veli kararı öncesi öğretmene düşmez) ve `Expired` yalnız veli onayı bekleyen talepte
 * oluşur; yine de birlik tipi genişlediği için hepsi eşlenir (`Record` derleme anında garanti eder).
 */
export const TEACHER_BOOKING_STATUS_VIEW: Record<BookingStatus, { statusKey: string; statusClass: BookingStatusClass }> = {
  Pending: { statusKey: 'status.pending', statusClass: 'is-pending' },
  Approved: { statusKey: 'status.approved', statusClass: 'is-approved' },
  Rejected: { statusKey: 'status.rejected', statusClass: 'is-rejected' },
  AwaitingParent: { statusKey: 'status.awaitingParent', statusClass: 'is-pending' },
  ParentRejected: { statusKey: 'status.parentRejected', statusClass: 'is-rejected' },
  Expired: { statusKey: 'status.expired', statusClass: 'is-rejected' },
  // Issue #315: öğrenci ya da öğretmen iptal etti (öğretmene ulaşmış talebin geri çekilmesi de).
  Cancelled: { statusKey: 'status.cancelled', statusClass: 'is-cancelled' },
};

interface BookingRow {
  booking: Booking;
  /** Scope'a göreli çeviri anahtarı; şablonda `t()` ile çözülür. */
  statusKey: string;
  statusClass: BookingStatusClass;
  /** Issue #97: "Derse katıl" butonunun durumu (ipucu metni kök `shared.joinWindow.*` sözlüğünden). */
  join: JoinWindowInfo;
  /** Bitiş ertesi yerel günde (issue #300): aralığa "(+1 gün)" eklenir. */
  endsNextDay: boolean;
  /** Issue #315: iptal / erteleme / no-show aksiyonlarının görünürlüğü. */
  lifecycle: BookingLifecycleView;
  /** Issue #315: bekleyen erteleme önerisinin biçimlenmiş zamanı; öneri yoksa null. */
  proposalWhen: string | null;
}

/** Ret gerekçesi için backend sınırı. */
const REJECTION_REASON_MAX = 500;

/**
 * Öğretmenin randevu talepleri gelen kutusu (issue #96). Bekleyen talepler onaylanır
 * veya (opsiyonel gerekçeyle) reddedilir; karar sonrası liste tazelenir.
 * Filtreleme client-side — backend tüm talepleri tek uçtan döner.
 * Issue #315: onaylı randevu iptal / erteleme önerisi (kendi boş, gelecekteki slotlarından) / "öğrenci katılmadı"; öğrencinin
 * erteleme önerisi kabul/ret, kendi önerisini geri çekme. "Bekleyenler" filtresi öğretmenin yanıtını bekleyen erteleme
 * önerilerini de gösterir. Her aksiyondan sonra liste yeniden yüklenir.
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
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./teacher-booking-requests.component.scss'],
})
export class TeacherBookingRequestsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(MatDialog);

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
      .filter((b) => !onlyPending || b.status === 'Pending' || awaitsTeacherReply(b))
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

  // ---------------- Issue #315: iptal / erteleme / no-show ----------------

  /** Onaylı randevuyu iptal eder (gerekçe opsiyonel, öğrenciye bildirilir). */
  protected cancelBooking(row: BookingRow): void {
    if (this.actingId() !== null) {
      return;
    }
    openBookingCancelDialog(this.dialog, {
      bookingId: row.booking.id,
      mode: 'cancel',
      counterpart: row.booking.studentName || this.text('row.studentFallback'),
      when: this.formatFull(row.booking.startUtc, row.booking.endUtc),
    })
      .afterClosed()
      // Dialog içinde hata gösterilip kapatıldıysa (undefined) da durum değişmiş olabilir → tazele.
      .subscribe((res) => (res ? this.afterLifecycleAction(res) : this.load()));
  }

  /** Kendi boş, gelecekteki slotlarından erteleme önerir. */
  protected propose(row: BookingRow): void {
    if (this.actingId() !== null) {
      return;
    }
    openBookingRescheduleDialog(this.dialog, {
      booking: row.booking,
      source: 'mySlots',
      counterpart: row.booking.studentName || this.text('row.studentFallback'),
    })
      .afterClosed()
      .pipe(rxFilter((res): res is BookingResult => !!res))
      .subscribe((res) => this.afterLifecycleAction(res));
  }

  /** Öğrencinin önerisini kabul/ret eder; kendi önerisinde yalnız `false` (geri çekme). */
  protected respond(row: BookingRow, accept: boolean): void {
    this.act(this.bookingService.respondReschedule(row.booking.id, accept), row.booking.id, null);
  }

  /** Ders bitişinden sonra öğrencinin katılmadığını bildirir. */
  protected reportNoShow(row: BookingRow): void {
    if (this.actingId() !== null) {
      return;
    }
    const data: ConfirmDialogData = {
      title: this.text('noShow.confirmTitle'),
      message: this.text('noShow.confirmMessage', {
        student: row.booking.studentName || this.text('row.studentFallback'),
      }),
      confirmText: this.text('noShow.confirm'),
      icon: 'person_off',
      confirmColor: 'warn',
    };
    this.dialog
      .open<ConfirmDialogComponent, ConfirmDialogData, boolean>(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(rxFilter((ok) => ok === true))
      .subscribe(() => this.act(this.bookingService.reportNoShow(row.booking.id), row.booking.id, null));
  }

  private afterLifecycleAction(res: BookingResult): void {
    this.snackBar.open(res.message || this.text('messages.actionDone'), this.text('messages.ok'), { duration: 3000 });
    this.load();
  }

  private formatFull(startUtc: string, endUtc: string): string {
    const nextDay = this.transloco.translate<string>(SLOT_NEXT_DAY_KEY);
    return `${formatSlotDay(startUtc)} · ${formatSlotRange(startUtc, endUtc, nextDay)}`;
  }

  /**
   * `successMsg` null ise sunucunun yerelleştirilmiş mesajı gösterilir (issue #315). Hata sonrası liste tazelenir: durum karşı
   * taraftan değişmiş olabilir (409).
   */
  private act(call: Observable<BookingResult>, id: number, successMsg: string | null): void {
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
          this.snackBar.open(successMsg ?? (res?.message || this.text('messages.actionDone')), this.text('messages.ok'), {
            duration: 3000,
          });
          this.cancelReject();
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(
            this.bookingService.extractError(err, this.text('messages.actionFailed')),
            this.text('messages.ok'),
            { duration: 4000 }
          );
          // Durum karşı taraftan değişmiş olabilir (409; geri çekilmiş talep 404) — listeyi tazele.
          if (err.status === 409 || err.status === 404) {
            this.load();
          }
        },
      });
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${TEACHER_BOOKINGS_SCOPE}.${key}`, params) ?? '';
  }

  private toRow(booking: Booking, now: number): BookingRow {
    const join = getJoinWindow(booking.startUtc, booking.endUtc, now, (key, params) =>
      this.transloco.translate<string>(key, params) ?? ''
    );
    return {
      booking,
      join,
      endsNextDay: slotEndsNextDay(booking.startUtc, booking.endUtc),
      ...(TEACHER_BOOKING_STATUS_VIEW[booking.status] ?? { statusKey: 'status.unknown', statusClass: 'is-rejected' }),
      ...this.lifecycleOf(booking, now),
    };
  }

  private lifecycleOf(booking: Booking, now: number): Pick<BookingRow, 'lifecycle' | 'proposalWhen'> {
    const lifecycle = bookingLifecycleView(booking, 'Teacher', now);
    const proposal = lifecycle.proposal;
    return { lifecycle, proposalWhen: proposal ? this.formatFull(proposal.startUtc, proposal.endUtc) : null };
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

/** Issue #315: öğrencinin önerdiği, öğretmenin yanıtını bekleyen erteleme ("Bekleyenler" filtresinde görünür). */
export function awaitsTeacherReply(booking: Booking): boolean {
  return (
    booking.status === 'Approved' &&
    booking.proposedSlotId != null &&
    booking.proposedByRole === 'Student' &&
    booking.canRespondReschedule === true
  );
}
