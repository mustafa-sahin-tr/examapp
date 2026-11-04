import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, finalize } from 'rxjs';

import { BookingResult, ParentRescheduleRequest } from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { LocaleService } from '../../../services/locale.service';
import { formatDateTime } from '../parent-format';

const SCOPE = 'parent-dashboard';

interface RescheduleRow {
  request: ParentRescheduleRequest;
  current: string;
  proposed: string;
}

/**
 * Issue #486: karşı tarafça kabul edilmiş, veli onayı bekleyen ertelemeler (Active bağlı tüm çocuklar). Onayla → ders yeni saate
 * taşınır; Reddet → ders eski saatinde kalır. Bekleyen yoksa kart görünmez. Karar başarısız olursa (başka veli karar verdi, öneri
 * geri çekildi, slot doldu) mesaj gösterilir ve liste sunucudan tazelenir.
 */
@Component({
  selector: 'app-parent-reschedule-requests',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './parent-reschedule-requests.component.html',
  styleUrl: './parent-reschedule-requests.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ParentRescheduleRequestsComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly requests = signal<ParentRescheduleRequest[]>([]);
  protected readonly actingId = signal<number | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly rows = computed<RescheduleRow[]>(() => {
    const locale = this.localeService.localeDefinition().angularLocale;
    return this.requests().map((request) => ({
      request,
      current: formatDateTime(request.startUtc, locale),
      proposed: formatDateTime(request.proposedStartUtc, locale),
    }));
  });

  /** Bekleyen yokken kart yer kaplamaz; hata ve bilgi notu yine görünür. */
  protected readonly visible = computed(() => this.rows().length > 0 || !!this.error() || !!this.notice());

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.bookingService
      .getParentRescheduleRequests()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => this.requests.set(res?.items ?? []),
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, this.text('rescheduleRequests.loadError'))),
      });
  }

  protected approve(request: ParentRescheduleRequest): void {
    this.decide(request, this.bookingService.parentApproveReschedule(request.id, request.proposedSlotId), 'rescheduleRequests.approved');
  }

  protected reject(request: ParentRescheduleRequest): void {
    this.decide(request, this.bookingService.parentRejectReschedule(request.id, request.proposedSlotId), 'rescheduleRequests.rejected');
  }

  private decide(request: ParentRescheduleRequest, call: Observable<BookingResult>, successKey: string): void {
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
          this.snackBar.open(this.text(successKey), this.text('bookingRequests.ok'), { duration: 4000 });
        },
        error: (err: HttpErrorResponse) => {
          this.notice.set(this.bookingService.extractError(err, this.text('rescheduleRequests.actionError')));
          if (err?.status === 404 || err?.status === 409) this.load();
        },
      });
  }

  private text(key: string): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`) ?? '';
  }
}
