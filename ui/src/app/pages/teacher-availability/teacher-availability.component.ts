import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AvailabilitySlot } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import {
  formatSlotDay,
  formatSlotRange,
  isPastSlot,
  parseMinutes,
  toDateOnly,
  toTimeOnly,
} from '../../shared/utils/booking-format.util';

/** Listede tek satır — slotun türetilmiş gösterim alanlarıyla. */
interface SlotRow {
  slot: AvailabilitySlot;
  day: string;
  range: string;
  past: boolean;
  statusLabel: string;
  statusClass: 'is-free' | 'is-pending' | 'is-approved';
  /** Aktif randevusu olan slot silinemez (backend de engeller). */
  deletable: boolean;
}

/**
 * Öğretmenin müsaitlik takvimi (issue #96): yeni aralık tanımlama + mevcut aralıkların
 * randevu durumuyla listesi. Silme yalnızca aktif randevusu olmayan aralıklarda açıktır.
 * Gelen randevu talepleri ayrı sayfada (`/booking-requests`).
 */
@Component({
  selector: 'app-teacher-availability',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './teacher-availability.component.html',
  styleUrls: ['./teacher-availability.component.scss'],
})
export class TeacherAvailabilityComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly deletingId = signal<number | null>(null);
  protected readonly slots = signal<AvailabilitySlot[]>([]);

  /** Geçmiş tarih seçilemesin diye datepicker alt sınırı. */
  protected readonly minDate = new Date();

  protected readonly form = this.fb.nonNullable.group({
    date: this.fb.control<Date | null>(null, Validators.required),
    startTime: ['', Validators.required],
    endTime: ['', Validators.required],
  });

  /** Saat alanları dolu ama bitiş başlangıçtan sonra değilse gösterilecek uyarı. */
  protected readonly timeRangeError = signal<string | null>(null);

  protected readonly rows = computed<SlotRow[]>(() =>
    [...this.slots()]
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((slot) => this.toRow(slot))
  );

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.slots().length === 0);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.bookingService
      .getMySlots()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => this.slots.set(res.items ?? []),
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, 'Müsaitlik aralıkları yüklenemedi.')),
      });
  }

  protected submit(): void {
    this.timeRangeError.set(null);
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const { date, startTime, endTime } = this.form.getRawValue();
    const start = parseMinutes(startTime);
    const end = parseMinutes(endTime);
    if (start === null || end === null) {
      this.timeRangeError.set('Saatleri SS:DD biçiminde girin.');
      return;
    }
    if (end <= start) {
      this.timeRangeError.set('Bitiş saati başlangıçtan sonra olmalı.');
      return;
    }
    if (!date) {
      return;
    }

    this.saving.set(true);
    this.bookingService
      .createSlot({ date: toDateOnly(date), startTime: toTimeOnly(startTime), endTime: toTimeOnly(endTime) })
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.snackBar.open(res.message || 'Aralık eklenemedi.', 'Tamam', { duration: 4000 });
            return;
          }
          this.snackBar.open('Müsaitlik aralığı eklendi.', 'Tamam', { duration: 3000 });
          this.form.reset({ date: null, startTime: '', endTime: '' });
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.bookingService.extractError(err, 'Aralık eklenemedi.'), 'Tamam', {
            duration: 4000,
          });
        },
      });
  }

  protected remove(row: SlotRow): void {
    if (this.deletingId() !== null) {
      return;
    }
    this.deletingId.set(row.slot.id);
    this.bookingService
      .deleteSlot(row.slot.id)
      .pipe(
        finalize(() => this.deletingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.snackBar.open('Müsaitlik aralığı silindi.', 'Tamam', { duration: 3000 });
          this.slots.update((items) => items.filter((s) => s.id !== row.slot.id));
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.bookingService.extractError(err, 'Aralık silinemedi.'), 'Tamam', {
            duration: 4000,
          });
        },
      });
  }

  private toRow(slot: AvailabilitySlot): SlotRow {
    const student = slot.studentName ? ` · ${slot.studentName}` : '';
    let statusLabel = 'Boş';
    let statusClass: SlotRow['statusClass'] = 'is-free';

    if (slot.isBooked) {
      if (slot.bookingStatus === 'Approved') {
        statusLabel = `Onaylı randevu${student}`;
        statusClass = 'is-approved';
      } else {
        statusLabel = `Bekleyen talep${student}`;
        statusClass = 'is-pending';
      }
    }

    return {
      slot,
      day: formatSlotDay(slot.startUtc),
      range: formatSlotRange(slot.startUtc, slot.endUtc),
      past: isPastSlot(slot.startUtc),
      statusLabel,
      statusClass,
      deletable: !slot.isBooked,
    };
  }
}
