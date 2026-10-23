import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { finalize } from 'rxjs';
import { BOOKING_CANCEL_REASON_MAX_LENGTH, BookingResult } from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';

/**
 * `cancel`: onaylı randevunun iptali (öğrenci ya da öğretmen). `withdraw`: öğrencinin henüz onaylanmamış
 * (`Pending`/`AwaitingParent`) talebini geri çekmesi. İkisi de aynı uç (`POST requests/{id}/cancel`).
 */
export type BookingCancelMode = 'cancel' | 'withdraw';

export interface BookingCancelDialogData {
  bookingId: number;
  mode: BookingCancelMode;
  /** Karşı tarafın adı (öğretmen ya da öğrenci) — açıklama metninde. */
  counterpart: string;
  /** Biçimlenmiş ders zamanı ("20 Eylül 2030 Cuma · 14:00 – 15:00"). */
  when: string;
}

/** Başarıda sunucunun sonucuyla kapanır; vazgeçilirse `undefined`. */
export type BookingCancelDialogResult = BookingResult | undefined;

/**
 * Issue #315 — randevu iptali / talebi geri çekme onayı. Gerekçe opsiyonel (en fazla 500 karakter, sayaçlı) ve karşı tarafa
 * bildirilir. İstek dialog içinde yapılır; hata (ör. 409 `CancellationWindowClosed`) sunucu mesajıyla dialog'da gösterilir.
 */
@Component({
  selector: 'app-booking-cancel-dialog',
  standalone: true,
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  templateUrl: './booking-cancel-dialog.component.html',
  styleUrls: ['./booking-cancel-dialog.component.scss'],
})
export class BookingCancelDialogComponent {
  private readonly bookingService = inject(BookingService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogRef =
    inject<MatDialogRef<BookingCancelDialogComponent, BookingCancelDialogResult>>(MatDialogRef);

  protected readonly data = inject<BookingCancelDialogData>(MAT_DIALOG_DATA);
  protected readonly max = BOOKING_CANCEL_REASON_MAX_LENGTH;
  protected readonly reason = signal('');
  protected readonly length = computed(() => this.reason().length);
  protected readonly tooLong = computed(() => this.length() > this.max);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canSubmit = computed(() => !this.tooLong() && !this.submitting());

  protected onInput(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
  }

  protected close(): void {
    if (!this.submitting()) {
      this.dialogRef.close(undefined);
    }
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.bookingService
      .cancelBooking(this.data.bookingId, this.reason())
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.error.set(res.message || this.fallbackError());
            return;
          }
          this.dialogRef.close(res);
        },
        error: (err: HttpErrorResponse) => this.error.set(this.bookingService.extractError(err, this.fallbackError())),
      });
  }

  private fallbackError(): string {
    return this.transloco.translate<string>('shared.bookingCancelDialog.error') ?? '';
  }
}

export function openBookingCancelDialog(
  dialog: MatDialog,
  data: BookingCancelDialogData
): MatDialogRef<BookingCancelDialogComponent, BookingCancelDialogResult> {
  return dialog.open<BookingCancelDialogComponent, BookingCancelDialogData, BookingCancelDialogResult>(
    BookingCancelDialogComponent,
    { data, autoFocus: 'first-tabbable', restoreFocus: true, width: '480px', maxWidth: '94vw' }
  );
}
