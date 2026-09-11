import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { finalize } from 'rxjs';
import { AvailabilitySlot } from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { formatSlotDay, formatSlotRange } from '../../utils/booking-format.util';

/** Dialog girdisi — hangi öğretmenin slotları gösterilecek. */
export interface BookingSlotDialogData {
  teacherId: number;
  teacherName: string;
}

/** Aynı güne düşen slotlar tek başlık altında. */
interface SlotGroup {
  day: string;
  slots: { slot: AvailabilitySlot; range: string }[];
}

/**
 * "Randevu Al" akışı (issue #96): öğretmenin gelecekteki boş müsaitlik aralıklarını
 * gün gün listeler, seçilen aralık için randevu talebi oluşturur.
 * 409 (aralık bu arada dolmuş) durumunda liste tazelenir ve kullanıcı bilgilendirilir;
 * başarıda dialog `true` ile kapanır.
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
  ],
  templateUrl: './booking-slot-dialog.component.html',
  styleUrls: ['./booking-slot-dialog.component.scss'],
})
export class BookingSlotDialogComponent {
  private readonly bookingService = inject(BookingService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogRef = inject<MatDialogRef<BookingSlotDialogComponent, boolean>>(MatDialogRef);

  protected readonly data = inject<BookingSlotDialogData>(MAT_DIALOG_DATA);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly slots = signal<AvailabilitySlot[]>([]);
  protected selectedId: number | null = null;

  protected readonly groups = computed<SlotGroup[]>(() => {
    const byDay = new Map<string, SlotGroup>();
    const sorted = [...this.slots()].sort(
      (a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime()
    );
    for (const slot of sorted) {
      const day = formatSlotDay(slot.startUtc);
      const group = byDay.get(day) ?? { day, slots: [] };
      group.slots.push({ slot, range: formatSlotRange(slot.startUtc, slot.endUtc) });
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
          this.error.set(this.bookingService.extractError(err, 'Müsait saatler yüklenemedi.')),
      });
  }

  protected submit(): void {
    if (this.selectedId === null || this.submitting()) {
      return;
    }
    this.submitError.set(null);
    this.submitting.set(true);
    this.bookingService
      .createBooking({ availabilitySlotId: this.selectedId })
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.submitError.set(res.message || 'Randevu talebi oluşturulamadı.');
            return;
          }
          this.dialogRef.close(true);
        },
        error: (err: HttpErrorResponse) => {
          this.submitError.set(this.bookingService.extractError(err, 'Randevu talebi oluşturulamadı.'));
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
}
