import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PlanCancellation, PurchaseStatus } from '../../../models/subscription';
import { SubscriptionService } from '../../../services/subscription.service';

const ERROR_KEYS: Record<string, string> = {
  'subscription.nothingToCancel': 'errors.nothingToCancel',
  'subscription.alreadyCancelled': 'errors.alreadyCancelled',
};

/**
 * Issue #1116: satın alınmış planın iptali (Aboneliğim; veli panelinde çocuğun planı). Onay adımı iptalin sonucunu söyler: aylık /
 * cayma süresi geçmiş plan dönem sonuna kadar sürer, 14 gün içindeki uzun plan hemen biter ve tam iade edilir. İptal edilmiş plan
 * yalnız durum satırını gösterir; iadeli iptalden sonra (satın alma kalkar) iade notu kalır.
 */
@Component({
  selector: 'app-plan-cancel',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, MatButtonModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './plan-cancel.component.html',
  styleUrl: './plan-cancel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanCancelComponent {
  private readonly subscriptions = inject(SubscriptionService);

  readonly purchase = input<PurchaseStatus | null | undefined>(null);
  /** Planın bitişi (aylık / geç iptalde erişimin sonu). */
  readonly validToUtc = input<string | null | undefined>(null);
  /** Veli panelinde çocuğun öğrenci id'si; boşsa kullanıcının kendi planı. */
  readonly studentId = input<number | null>(null);
  readonly cancelled = output<PlanCancellation>();

  readonly confirming = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  /** İadeli iptalin sonucu (plan bitti, satın alma artık gelmez). */
  readonly refund = signal<PlanCancellation | null>(null);

  /** Test için sabitlenebilir "şimdi". */
  now: () => Date = () => new Date();

  readonly inRefundWindow = computed(() => {
    const until = this.purchase()?.fullRefundUntilUtc;
    return !!until && new Date(until).getTime() > this.now().getTime();
  });

  constructor() {
    // Veli panelinde başka çocuğa geçince önceki çocuğun durumu kalmasın.
    effect(() => {
      this.studentId();
      untracked(() => {
        this.confirming.set(false);
        this.error.set(null);
        this.refund.set(null);
      });
    });
  }

  confirm(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    const id = this.studentId();
    const request = id == null ? this.subscriptions.cancel() : this.subscriptions.cancelChildPlan(id);
    request.subscribe({
      next: (result) => {
        this.busy.set(false);
        this.confirming.set(false);
        if (result.refundAmount != null) this.refund.set(result);
        this.cancelled.emit(result);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        const code = err instanceof HttpErrorResponse ? (err.error as { code?: string } | null)?.code : undefined;
        this.error.set((code && ERROR_KEYS[code]) || 'errors.generic');
      },
    });
  }
}
