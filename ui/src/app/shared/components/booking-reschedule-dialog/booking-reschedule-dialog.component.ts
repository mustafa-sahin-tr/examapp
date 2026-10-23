import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Observable, finalize } from 'rxjs';
import {
  AvailabilitySlot,
  AvailabilitySlotListResult,
  BOOKING_ERROR_CODES,
  Booking,
  BookingResult,
  bookingErrorCode,
} from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { SLOT_NEXT_DAY_KEY, formatSlotDay, formatSlotRange } from '../../utils/booking-format.util';

/**
 * Slot kaynağı: öğrenci öğretmenin açık slotlarını (`GET teachers/{teacherId}/slots`), öğretmen kendi slotlarını
 * (`slots/mine`, tüm sayfalar) görür.
 */
export type BookingRescheduleSlotSource = 'teacherOpenSlots' | 'mySlots';

export interface BookingRescheduleDialogData {
  booking: Booking;
  source: BookingRescheduleSlotSource;
  /** Karşı tarafın adı — açıklama metninde. */
  counterpart: string;
}

/** Başarıda sunucunun sonucuyla kapanır; vazgeçilirse `undefined`. */
export type BookingRescheduleDialogResult = BookingResult | undefined;

interface SlotGroup {
  day: string;
  slots: { slot: AvailabilitySlot; range: string }[];
}

/**
 * Issue #315 — onaylı randevu için erteleme önerisi: aynı öğretmenin boş, gelecekteki ve mevcut randevudan farklı bir slotu
 * seçilir, `POST requests/{id}/reschedule` ile önerilir. Karşı taraf kabul edene kadar randevu eski saatte kalır.
 * 400/409 `ProposedSlotUnavailable` (slot bu arada doldu/silindi) → mesaj + liste tazelenir.
 */
@Component({
  selector: 'app-booking-reschedule-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, MatRadioModule, TranslocoDirective],
  templateUrl: './booking-reschedule-dialog.component.html',
  styleUrls: ['./booking-reschedule-dialog.component.scss'],
})
export class BookingRescheduleDialogComponent {
  private readonly bookingService = inject(BookingService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogRef =
    inject<MatDialogRef<BookingRescheduleDialogComponent, BookingRescheduleDialogResult>>(MatDialogRef);

  protected readonly data = inject<BookingRescheduleDialogData>(MAT_DIALOG_DATA);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly slots = signal<AvailabilitySlot[]>([]);
  protected readonly selectedId = signal<number | null>(null);

  /** Mevcut randevunun zamanı (açıklama metni). */
  protected readonly currentWhen = this.formatFull(this.data.booking.startUtc, this.data.booking.endUtc);

  protected readonly groups = computed<SlotGroup[]>(() => {
    const byDay = new Map<string, SlotGroup>();
    const nextDay = this.transloco.translate<string>(SLOT_NEXT_DAY_KEY);
    for (const slot of this.slots()) {
      const day = formatSlotDay(slot.startUtc);
      const group = byDay.get(day) ?? { day, slots: [] };
      group.slots.push({ slot, range: formatSlotRange(slot.startUtc, slot.endUtc, nextDay) });
      byDay.set(day, group);
    }
    return [...byDay.values()];
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.slots().length === 0);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    const request$: Observable<AvailabilitySlotListResult> =
      this.data.source === 'mySlots'
        ? this.bookingService.getAllMySlots()
        : this.bookingService.getTeacherSlots(this.data.booking.teacherId);
    request$
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          const items = selectableRescheduleSlots(res.items ?? [], this.data.booking, Date.now());
          this.slots.set(items);
          if (!items.some((s) => s.id === this.selectedId())) {
            this.selectedId.set(null);
          }
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, this.t('loadError'))),
      });
  }

  protected select(event: MatRadioChange): void {
    this.selectedId.set(typeof event.value === 'number' ? event.value : null);
  }

  protected submit(): void {
    const slotId = this.selectedId();
    if (slotId === null || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);
    this.bookingService
      .proposeReschedule(this.data.booking.id, slotId)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.submitError.set(res.message || this.t('submitError'));
            return;
          }
          this.dialogRef.close(res);
        },
        error: (err: HttpErrorResponse) => {
          this.submitError.set(this.bookingService.extractError(err, this.t('submitError')));
          if (bookingErrorCode(err.error) === BOOKING_ERROR_CODES.proposedSlotUnavailable) {
            // Slot bu arada doldu/silindi — listeyi tazele.
            this.load();
          }
        },
      });
  }

  protected close(): void {
    if (!this.submitting()) {
      this.dialogRef.close(undefined);
    }
  }

  private formatFull(startUtc: string, endUtc: string): string {
    const nextDay = this.transloco.translate<string>(SLOT_NEXT_DAY_KEY);
    return `${formatSlotDay(startUtc)} · ${formatSlotRange(startUtc, endUtc, nextDay)}`;
  }

  private t(key: string): string {
    return this.transloco.translate<string>(`shared.bookingRescheduleDialog.${key}`) ?? '';
  }
}

/**
 * Önerilebilir slotlar: boş (`!isBooked`), başlangıcı gelecekte, mevcut randevunun slotu değil, aynı öğretmenin ve mevcut
 * dersle aynı sürede (backend farklı süreyi 400 `ProposedSlotDurationMismatch` ile reddeder). Başlangıca göre artan sırada.
 */
export function selectableRescheduleSlots(slots: AvailabilitySlot[], booking: Booking, now: number): AvailabilitySlot[] {
  const duration = new Date(booking.endUtc).getTime() - new Date(booking.startUtc).getTime();
  const earliestMs = booking.rescheduleEarliestStartUtc ? new Date(booking.rescheduleEarliestStartUtc).getTime() : Number.NaN;
  const earliest = Number.isNaN(earliestMs) ? null : earliestMs;
  return slots
    .filter((s) => {
      const start = new Date(s.startUtc).getTime();
      const end = new Date(s.endUtc).getTime();
      return (
        !s.isBooked &&
        end - start === duration &&
        s.id !== booking.availabilitySlotId &&
        s.teacherId === booking.teacherId &&
        !Number.isNaN(start) &&
        start > now &&
        // Sunucu bildirim süresinden erken slotu reddeder (ProposedSlotUnavailable).
        (earliest === null || start >= earliest)
      );
    })
    .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime());
}

export function openBookingRescheduleDialog(
  dialog: MatDialog,
  data: BookingRescheduleDialogData
): MatDialogRef<BookingRescheduleDialogComponent, BookingRescheduleDialogResult> {
  return dialog.open<BookingRescheduleDialogComponent, BookingRescheduleDialogData, BookingRescheduleDialogResult>(
    BookingRescheduleDialogComponent,
    { data, autoFocus: 'first-tabbable', restoreFocus: true, width: '520px', maxWidth: '94vw' }
  );
}
