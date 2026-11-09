import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import {
  ENTITLEMENT_NOT_IN_PLAN_CODE,
  EntitlementLimitReached,
  SubscriptionPlan,
  planNameKey,
} from '../../../models/subscription';
import { SubscriptionService } from '../../../services/subscription.service';

export const PLANS_URL = '/plans';

/** Karşılaştırma satırı: planın bu özellikteki aylık sınırı (null = sınırsız). */
interface PlanLimitRow {
  key: string;
  nameKey: string;
  limit: number | null;
}

/**
 * Issue #1029: plan sınırına takılan her isteğin ortak penceresi (`entitlementLimitInterceptor` açar). Özelliğin bu ayki
 * kullanımı, sıfırlanma tarihi ve çağırana açık planların aynı özellikteki sınırı; "Planı yükselt" plan seçimine gider.
 */
@Component({
  selector: 'app-upgrade-dialog',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatDialogModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './upgrade-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './upgrade-dialog.component.scss',
})
export class UpgradeDialogComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);
  private readonly router = inject(Router);
  private readonly dialogRef = inject<MatDialogRef<UpgradeDialogComponent>>(MatDialogRef);

  protected readonly data = inject<EntitlementLimitReached>(MAT_DIALOG_DATA);
  /** Issue #1087: özellik planda hiç yok (403) — kullanım satırı yerine "planında yok". */
  protected readonly notInPlan = this.data.code === ENTITLEMENT_NOT_IN_PLAN_CODE || this.data.limit === 0;
  /** Plan listesi yüklenemezse karşılaştırma gizlenir; pencere yine yükseltmeye götürür. */
  readonly rows = signal<PlanLimitRow[]>([]);

  ngOnInit(): void {
    this.subscriptions.plans().subscribe({
      next: (plans) => this.rows.set(plans.map((plan) => this.rowOf(plan))),
      error: () => this.rows.set([]),
    });
  }

  upgrade(): void {
    this.dialogRef.close();
    void this.router.navigateByUrl(PLANS_URL);
  }

  private rowOf(plan: SubscriptionPlan): PlanLimitRow {
    const limits = plan.monthlyLimits;
    return {
      key: plan.key,
      nameKey: planNameKey(plan.displayNameKey),
      // Sınır satırı olmayan özellik planda yoktur (0).
      limit: this.data.feature in limits ? limits[this.data.feature] : 0,
    };
  }
}
