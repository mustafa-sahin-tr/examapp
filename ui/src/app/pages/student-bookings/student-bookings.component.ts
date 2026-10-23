import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, PLATFORM_ID, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, filter as rxFilter, finalize, interval, take } from 'rxjs';
import { Booking, BookingResult, BookingStatus, CLOSED_BOOKING_STATUSES } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { LocaleService } from '../../services/locale.service';
import { openBookingCancelDialog } from '../../shared/components/booking-cancel-dialog/booking-cancel-dialog.component';
import { openBookingRescheduleDialog } from '../../shared/components/booking-reschedule-dialog/booking-reschedule-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { JOIN_WINDOW_TICK_MS, JoinWindowInfo, getJoinWindow } from '../../shared/utils/booking-join-window';
import { SLOT_NEXT_DAY_KEY, endsOnNextLocalDay } from '../../shared/utils/booking-format.util';
import { BookingLifecycleView, bookingLifecycleView } from '../../shared/utils/booking-lifecycle.util';

type BookingFilter = 'active' | 'all';

type BookingStatusClass = 'is-pending' | 'is-approved' | 'is-rejected' | 'is-awaiting-parent' | 'is-closed' | 'is-cancelled';

/**
 * Durum → `student-bookings` scope'una göreli etiket + chip sınıfı. `Record` her durumun eşlendiğini derleme anında garanti eder
 * (issue #439: AwaitingParent / ParentRejected / Expired).
 */
export const STUDENT_BOOKING_STATUS_VIEW: Record<BookingStatus, { statusLabelKey: string; statusClass: BookingStatusClass }> = {
  Pending: { statusLabelKey: 'status.pending', statusClass: 'is-pending' },
  Approved: { statusLabelKey: 'status.approved', statusClass: 'is-approved' },
  Rejected: { statusLabelKey: 'status.rejected', statusClass: 'is-rejected' },
  AwaitingParent: { statusLabelKey: 'status.awaitingParent', statusClass: 'is-awaiting-parent' },
  ParentRejected: { statusLabelKey: 'status.parentRejected', statusClass: 'is-rejected' },
  Expired: { statusLabelKey: 'status.expired', statusClass: 'is-closed' },
  // Issue #315: iptal edildi / talep geri çekildi.
  Cancelled: { statusLabelKey: 'status.cancelled', statusClass: 'is-cancelled' },
};

/** Bilinmeyen (yeni) bir durum gelirse "bekliyor" yerine nötr kapalı görünüm. */
const UNKNOWN_STATUS_VIEW = { statusLabelKey: 'status.unknown', statusClass: 'is-closed' as const };

interface BookingRow {
  booking: Booking;
  day: string;
  range: string;
  /** `student-bookings` scope'una göreli durum anahtarı. */
  statusLabelKey: string;
  statusClass: BookingStatusClass;
  /** Issue #439: `AwaitingParent` iken "velin şu tarihe kadar onaylamazsa düşer" için yerel tarih+saat; diğerlerinde null. */
  expiresAt: string | null;
  /** Issue #97: "Derse katıl" butonunun durumu; ipucu kök sözlükteki ortak anahtarlardan gelir. */
  join: JoinWindowInfo;
  /** Issue #315: iptal / erteleme / no-show aksiyonlarının görünürlüğü. */
  lifecycle: BookingLifecycleView;
  /** Issue #315: bekleyen erteleme önerisinin biçimlenmiş zamanı; öneri yoksa null. */
  proposalWhen: string | null;
}

const SCOPE = 'student-bookings';

/**
 * Öğrencinin kendi ders randevusu talepleri (issue #96). Salt listeleme — talep
 * oluşturma öğretmen profilindeki "Randevu Al" akışından yapılır.
 * "Aktif" filtresi kapanmış talepleri (reddedilen, velinin reddettiği, süresi dolan) gizler.
 * Issue #439: `AwaitingParent` "Veli onayı bekleniyor" (+ bitiş), `ParentRejected` "Velin reddetti" (+ not), `Expired`.
 * Issue #315: onaylı randevu iptal / erteleme önerisi / "öğretmen katılmadı"; bekleyen talep geri çekme; karşı tarafın
 * erteleme önerisini kabul/ret, kendi önerisini geri çekme. Her aksiyondan sonra liste yeniden yüklenir.
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
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./student-bookings.component.scss'],
})
export class StudentBookingsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly loading = signal(false);
  /** Issue #315: aksiyonu süren randevu (çift tıklamaya karşı tüm aksiyonlar pasif). */
  protected readonly actingId = signal<number | null>(null);
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
      .filter((b) => !onlyActive || !CLOSED_BOOKING_STATUSES.includes(b.status))
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((booking) => this.toRow(booking, now));
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  constructor() {
    // Snackbar/dialog metinleri senkron `translate()` ile okunur; scope şablondan bağımsız yüklensin.
    this.transloco.load(`${SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
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

  // ---------------- Issue #315: iptal / erteleme / no-show ----------------

  /** Onaylı randevuyu iptal eder ya da bekleyen talebi geri çeker (gerekçe opsiyonel). */
  protected cancel(row: BookingRow, mode: 'cancel' | 'withdraw'): void {
    if (this.actingId() !== null) {
      return;
    }
    openBookingCancelDialog(this.dialog, {
      bookingId: row.booking.id,
      mode,
      counterpart: row.booking.teacherName || this.text('teacherFallback'),
      when: `${row.day} · ${row.range}`,
    })
      .afterClosed()
      // Dialog içinde hata gösterilip kapatıldıysa (undefined) da durum değişmiş olabilir → tazele.
      .subscribe((res) => (res ? this.afterAction(res) : this.load()));
  }

  /** Öğretmenin açık slotlarından erteleme önerir. */
  protected propose(row: BookingRow): void {
    if (this.actingId() !== null) {
      return;
    }
    openBookingRescheduleDialog(this.dialog, {
      booking: row.booking,
      source: 'teacherOpenSlots',
      counterpart: row.booking.teacherName || this.text('teacherFallback'),
    })
      .afterClosed()
      .pipe(rxFilter((res): res is BookingResult => !!res))
      .subscribe((res) => this.afterAction(res));
  }

  /** Karşı tarafın önerisini kabul/ret eder; kendi önerisinde yalnız `false` (geri çekme). */
  protected respond(row: BookingRow, accept: boolean): void {
    this.act(row.booking.id, this.bookingService.respondReschedule(row.booking.id, accept));
  }

  /** Ders bitişinden sonra öğretmenin katılmadığını bildirir (onaylı). */
  protected reportNoShow(row: BookingRow): void {
    if (this.actingId() !== null) {
      return;
    }
    const data: ConfirmDialogData = {
      title: this.text('noShow.confirmTitle'),
      message: this.text('noShow.confirmMessage', { teacher: row.booking.teacherName || this.text('teacherFallback') }),
      confirmText: this.text('noShow.confirm'),
      icon: 'person_off',
      confirmColor: 'warn',
    };
    this.dialog
      .open<ConfirmDialogComponent, ConfirmDialogData, boolean>(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(rxFilter((ok) => ok === true))
      .subscribe(() => this.act(row.booking.id, this.bookingService.reportNoShow(row.booking.id)));
  }

  private act(id: number, call: Observable<BookingResult>): void {
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
            this.notify(res.message || this.text('actions.failed'));
            this.load();
            return;
          }
          this.afterAction(res);
        },
        error: (err: HttpErrorResponse) => {
          this.notify(this.bookingService.extractError(err, this.text('actions.failed')));
          // Durum karşı taraftan değişmiş olabilir (409) — listeyi tazele.
          this.load();
        },
      });
  }

  private afterAction(res: BookingResult): void {
    this.notify(res.message || this.text('actions.done'));
    this.load();
  }

  private notify(message: string): void {
    this.snackBar.open(message, this.text('actions.ok'), { duration: 4000 });
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`, params) ?? '';
  }

  private toRow(booking: Booking, now: number): BookingRow {
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
      expiresAt:
        booking.status === 'AwaitingParent' && booking.expiresAt ? this.formatDateTime(booking.expiresAt) : null,
      ...(STUDENT_BOOKING_STATUS_VIEW[booking.status] ?? UNKNOWN_STATUS_VIEW),
      ...this.lifecycleOf(booking, now),
    };
  }

  private lifecycleOf(booking: Booking, now: number): Pick<BookingRow, 'lifecycle' | 'proposalWhen'> {
    const lifecycle = bookingLifecycleView(booking, 'Student', now);
    const proposal = lifecycle.proposal;
    return {
      lifecycle,
      proposalWhen: proposal
        ? `${this.formatDay(proposal.startUtc)} · ${this.formatRange(proposal.startUtc, proposal.endUtc)}`
        : null,
    };
  }

  private formatDay(utcIso: string): string {
    const date = new Date(utcIso);
    return Number.isNaN(date.getTime()) ? '—' : this.dayFormat().format(date);
  }

  private formatDateTime(utcIso: string): string | null {
    const date = new Date(utcIso);
    return Number.isNaN(date.getTime())
      ? null
      : `${this.dayFormat().format(date)} ${this.timeFormat().format(date)}`;
  }

  private formatRange(startUtcIso: string, endUtcIso: string): string {
    const start = new Date(startUtcIso);
    const end = new Date(endUtcIso);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) {
      return '—';
    }
    const format = this.timeFormat();
    const range = `${format.format(start)} – ${format.format(end)}`;
    // Gün aşan ders (issue #300): "23:30 – 00:30 (+1 gün)"; metin kök sözlükten.
    return endsOnNextLocalDay(start, end)
      ? `${range} ${this.transloco.translate<string>(SLOT_NEXT_DAY_KEY) ?? ''}`.trimEnd()
      : range;
  }
}
