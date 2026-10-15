import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Observable, finalize } from 'rxjs';
import {
  AvailabilitySlot,
  BOOKING_ERROR_CODES,
  BookingResult,
  bookingErrorCode,
} from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { ParentStatusStore } from '../../../services/parent-status.store';
import { ParentActivationNoticeComponent } from '../parent-activation-notice/parent-activation-notice.component';
import { SLOT_NEXT_DAY_KEY, formatSlotDay, formatSlotRange } from '../../utils/booking-format.util';

/** Issue #439: velinin çocuğu adına talep açtığı mod — talep veli onayını atlar. */
export interface BookingSlotDialogChild {
  studentId: number;
  name: string;
}

/** Dialog girdisi — hangi öğretmenin slotları gösterilecek; `child` verilirse talep o çocuk adına açılır. */
export interface BookingSlotDialogData {
  teacherId: number;
  teacherName: string;
  child?: BookingSlotDialogChild | null;
}

/** Başarıda dialog sunucunun sonucuyla kapanır (durum `AwaitingParent` / `Pending`); vazgeçilirse `false`/`undefined`. */
export type BookingSlotDialogResult = BookingResult | false;

/** Issue #439: öğrencinin veli bağlantılarını gördüğü sayfa ("Velilerim" kartı). */
export const STUDENT_PARENTS_ROUTE = '/student-profile';

/** Aynı güne düşen slotlar tek başlık altında. */
interface SlotGroup {
  day: string;
  slots: { slot: AvailabilitySlot; range: string }[];
}

/**
 * "Randevu Al" akışı (issue #96): öğretmenin gelecekteki boş müsaitlik aralıklarını
 * gün gün listeler, seçilen aralık için randevu talebi oluşturur.
 * 409 (aralık bu arada dolmuş) durumunda liste tazelenir ve kullanıcı bilgilendirilir;
 * başarıda dialog sunucunun `BookingResult`'ı ile kapanır.
 * Issue #439: `data.child` verilirse (veli) talep `POST parent/children/{studentId}/bookings` ile çocuk adına açılır. Öğrenci
 * akışında 403 `ParentRequired` (velisi olmayan, veliye tabi öğrenci) açık bir mesaj + "Velilerim" bağlantısıyla gösterilir;
 * 409 `TooManyAwaitingParent` (veli onayı bekleyen talep sınırı) sözlükteki açıklamayla gösterilir.
 */
@Component({
  selector: 'app-booking-slot-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    ParentActivationNoticeComponent,
    RouterLink,
    TranslocoDirective,
    TranslocoPipe,
  ],
  templateUrl: './booking-slot-dialog.component.html',
  styleUrls: ['./booking-slot-dialog.component.scss'],
})
export class BookingSlotDialogComponent {
  private readonly bookingService = inject(BookingService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogRef =
    inject<MatDialogRef<BookingSlotDialogComponent, BookingSlotDialogResult>>(MatDialogRef);
  private readonly transloco = inject(TranslocoService);

  protected readonly data = inject<BookingSlotDialogData>(MAT_DIALOG_DATA);
  private readonly parentStatus = inject(ParentStatusStore);
  /** Issue #440: öğrencinin veli daveti aktive edilmedi → talep gönderilemez (veli modunda geçerli değil). */
  protected readonly activationRestricted = computed(() => !this.data.child && this.parentStatus.activationRestricted());

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  /** Issue #439: 403 `ParentRequired` — mesaj sözlükten, "Velilerim" bağlantısıyla. */
  protected readonly parentRequired = signal(false);
  protected readonly parentsRoute = STUDENT_PARENTS_ROUTE;
  protected readonly slots = signal<AvailabilitySlot[]>([]);
  protected selectedId: number | null = null;

  protected readonly groups = computed<SlotGroup[]>(() => {
    const byDay = new Map<string, SlotGroup>();
    const sorted = [...this.slots()].sort(
      (a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime()
    );
    // Gün aşan slot (issue #300) aralığın sonuna "(+1 gün)" alır; metin kök sözlükten.
    const nextDay = this.transloco.translate<string>(SLOT_NEXT_DAY_KEY);
    for (const slot of sorted) {
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
    this.bookingService
      .getTeacherSlots(this.data.teacherId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          const items = (res.items ?? []).filter((s) => !s.isBooked);
          this.slots.set(items);
          if (!items.some((s) => s.id === this.selectedId)) {
            this.selectedId = null;
          }
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, this.t('shared.bookingSlotDialog.loadError'))),
      });
  }

  protected submit(): void {
    if (this.selectedId === null || this.submitting() || this.activationRestricted()) {
      return;
    }
    this.submitError.set(null);
    this.parentRequired.set(false);
    this.submitting.set(true);
    const body = { availabilitySlotId: this.selectedId };
    const child = this.data.child;
    const request$: Observable<BookingResult> = child
      ? this.bookingService.createBookingForChild(child.studentId, body)
      : this.bookingService.createBooking(body);
    request$
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.submitError.set(res.message || this.t('shared.bookingSlotDialog.submitError'));
            return;
          }
          this.dialogRef.close(res);
        },
        error: (err: HttpErrorResponse) => {
          if (
            !child &&
            err.status === 403 &&
            bookingErrorCode(err.error) === BOOKING_ERROR_CODES.parentRequired
          ) {
            this.parentRequired.set(true);
            return;
          }
          if (
            !child &&
            err.status === 409 &&
            bookingErrorCode(err.error) === BOOKING_ERROR_CODES.tooManyAwaitingParent
          ) {
            // Slot dolmadı — liste tazelenmez; sınır velinin kararıyla açılır.
            this.submitError.set(this.t('shared.bookingSlotDialog.tooManyAwaitingParent'));
            return;
          }
          this.submitError.set(
            this.bookingService.extractError(err, this.t('shared.bookingSlotDialog.submitError'))
          );
          if (err.status === 409 || err.status === 404) {
            // Aralık bu arada dolmuş/silinmiş olabilir — listeyi tazele.
            this.load();
          }
        },
      });
  }

  protected close(): void {
    this.dialogRef.close(false);
  }

  private t(key: string): string {
    return this.transloco.translate<string>(key) ?? '';
  }
}
