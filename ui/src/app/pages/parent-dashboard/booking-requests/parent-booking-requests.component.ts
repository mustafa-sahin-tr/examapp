import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, filter, finalize, skip } from 'rxjs';

import {
  BOOKING_ERROR_CODES,
  BookingResult,
  PARENT_BOOKING_REQUESTS_FRAGMENT,
  PARENT_REJECT_NOTE_MAX_LENGTH,
  ParentBookingRequest,
  bookingErrorCode,
} from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { LocaleService } from '../../../services/locale.service';
import { SLOT_NEXT_DAY_KEY, endsOnNextLocalDay } from '../../../shared/utils/booking-format.util';
import { formatDateTime } from '../parent-format';

/** Veli paneli Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`). */
const SCOPE = 'parent-dashboard';

/**
 * Karar sonrası listeyi yeniden yükleten 409 kodları: talep artık karar beklemiyor (başka veli karar verdi / süre doldu) ya da
 * öğretmen tarafı değişti (askı, silinen aralık) — satır sunucudan tazelenir.
 */
const RELOAD_ON_CONFLICT: readonly string[] = [
  BOOKING_ERROR_CODES.alreadyDecided,
  BOOKING_ERROR_CODES.requestExpired,
  BOOKING_ERROR_CODES.teacherUnavailable,
  BOOKING_ERROR_CODES.requestSlotDeleted,
];

/** Şablonun gösterdiği, önceden biçimlendirilmiş satır. */
interface RequestRow {
  request: ParentBookingRequest;
  day: string;
  range: string;
  expires: string;
}

/**
 * Issue #439: veli panelindeki "Onay bekleyen ders talepleri" kartı. Velinin Active bağlı TÜM çocuklarının `AwaitingParent`
 * talepleri (seçili çocuktan bağımsız). Onayla → talep öğretmene düşer; Reddet → opsiyonel not (en fazla 500) ile kapanır,
 * öğretmene hiç gitmez. İlk karar kazanır: 409 `AlreadyDecided` / `BookingRequestExpired` (ve öğretmen tarafı değişiklikleri)
 * mesaj gösterip listeyi yeniden yükler; 404 (bağlantı kaldırılmış / talep yok) de listeyi tazeler. Başarıda satır listeden düşer.
 */
@Component({
  selector: 'app-parent-booking-requests',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './parent-booking-requests.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./parent-booking-requests.component.scss'],
})
export class ParentBookingRequestsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute, { optional: true });
  /**
   * Bildirim derin linki (`/parent#booking-requests`) ile gelindiyse ilk yüklemeden sonra karta bir kez kaydırılır. Veli zaten
   * paneldeyken aynı linke tıklarsa (fragment değişimi) liste tazelenir ve yine kaydırılır — bkz. `ngOnInit`.
   */
  private scrollPending = this.route?.snapshot.fragment === PARENT_BOOKING_REQUESTS_FRAGMENT;

  protected readonly noteMaxLength = PARENT_REJECT_NOTE_MAX_LENGTH;
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly requests = signal<ParentBookingRequest[]>([]);
  /** Karar isteği süren talep (tüm düğmeler kilitli — çift karar önlenir). */
  protected readonly actingId = signal<number | null>(null);
  /** Ret notu açık olan talep. */
  protected readonly rejectingId = signal<number | null>(null);
  protected readonly note = signal('');
  /** Son karar hatası / bilgi (409 sonrası yenileme notu). */
  protected readonly notice = signal<string | null>(null);

  private readonly locale = computed(() => this.localeService.localeDefinition().angularLocale);

  protected readonly rows = computed<RequestRow[]>(() => {
    const locale = this.locale();
    const dayFormat = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    const timeFormat = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
    return [...this.requests()]
      .sort((a, b) => Date.parse(a.startUtc) - Date.parse(b.startUtc))
      .map((request) => {
        const start = new Date(request.startUtc);
        const end = new Date(request.endUtc);
        const valid = !Number.isNaN(start.getTime()) && !Number.isNaN(end.getTime());
        let range = valid ? `${timeFormat.format(start)} – ${timeFormat.format(end)}` : '—';
        if (valid && endsOnNextLocalDay(start, end)) {
          range = `${range} ${this.transloco.translate<string>(SLOT_NEXT_DAY_KEY) ?? ''}`.trimEnd();
        }
        return {
          request,
          day: valid ? dayFormat.format(start) : '—',
          range,
          expires: formatDateTime(request.expiresAt, locale),
        };
      });
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.requests().length === 0);

  ngOnInit(): void {
    this.load();
    // İlk değer (snapshot) yukarıda ele alındı; sonraki fragment değişimleri: panel açıkken gelen bildirim tıklaması.
    this.route?.fragment
      .pipe(
        skip(1),
        filter((fragment) => fragment === PARENT_BOOKING_REQUESTS_FRAGMENT),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => {
        this.scrollPending = true;
        this.load();
      });
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.bookingService
      .getParentBookingRequests()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          const items = res?.items ?? [];
          this.requests.set(items);
          if (!items.some((r) => r.id === this.rejectingId())) {
            this.cancelReject();
          }
          this.scrollIntoViewOnce();
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, this.text('bookingRequests.loadError'))),
      });
  }

  protected approve(request: ParentBookingRequest): void {
    this.decide(request, this.bookingService.parentApproveBooking(request.id), 'bookingRequests.approved');
  }

  protected startReject(request: ParentBookingRequest): void {
    this.rejectingId.set(request.id);
    this.note.set('');
  }

  protected cancelReject(): void {
    this.rejectingId.set(null);
    this.note.set('');
  }

  protected confirmReject(request: ParentBookingRequest): void {
    const note = this.note().trim().slice(0, PARENT_REJECT_NOTE_MAX_LENGTH);
    this.decide(request, this.bookingService.parentRejectBooking(request.id, note || null), 'bookingRequests.rejected');
  }

  private decide(request: ParentBookingRequest, call: Observable<BookingResult>, successKey: string): void {
    if (this.actingId() !== null) return;
    this.actingId.set(request.id);
    this.notice.set(null);
    call
      .pipe(
        finalize(() => this.actingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.requests.update((list) => list.filter((r) => r.id !== request.id));
          if (this.rejectingId() === request.id) this.cancelReject();
          this.toast(this.text(successKey));
        },
        error: (err: HttpErrorResponse) => {
          const code = bookingErrorCode(err?.error);
          if (err?.status === 409 && code !== null && RELOAD_ON_CONFLICT.includes(code)) {
            const fallback =
              code === BOOKING_ERROR_CODES.requestExpired
                ? this.text('bookingRequests.expired')
                : this.text('bookingRequests.alreadyDecided');
            this.notice.set(this.bookingService.extractError(err, fallback));
            this.load();
            return;
          }
          if (err?.status === 404) {
            this.notice.set(this.text('bookingRequests.notFound'));
            this.load();
            return;
          }
          this.notice.set(this.bookingService.extractError(err, this.text('bookingRequests.actionError')));
        },
      });
  }

  private scrollIntoViewOnce(): void {
    if (!this.scrollPending || typeof document === 'undefined') return;
    this.scrollPending = false;
    setTimeout(() =>
      document.getElementById(PARENT_BOOKING_REQUESTS_FRAGMENT)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    );
  }

  private toast(message: string): void {
    this.snackBar.open(message, this.text('bookingRequests.ok'), { duration: 4000 });
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`, params) ?? '';
  }
}
