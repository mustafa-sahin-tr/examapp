import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { CurrentSubscriptionPlan, FeatureUsage, TRIAL_PLAN_KEY, planNameKey } from '../../models/subscription';
import { SubscriptionService } from '../../services/subscription.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PLANS_URL } from '../../shared/components/upgrade-dialog/upgrade-dialog.component';

/**
 * Issue #1029: "Aboneliğim" (profil menüsü). Plan, geçerlilik / deneme bitişi, bu ayki kullanım ve sayaçların sıfırlanma
 * tarihi; "Planı yükselt" plan seçimine (#1028 `/plans`) gider. Sözlükte "Planım" çalışma planına ayrılmıştır.
 */
@Component({
  selector: 'app-my-subscription',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, RouterLink, TranslocoDirective, PageHeaderComponent],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './my-subscription.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-subscription.component.scss',
})
export class MySubscriptionComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly current = signal<CurrentSubscriptionPlan | null>(null);

  readonly nameKey = computed(() => planNameKey(this.current()?.displayNameKey));
  readonly isTrial = computed(() => this.current()?.planKey === TRIAL_PLAN_KEY);
  /** Sınırlı özelliklerin ortak sıfırlanma anı (UTC takvim ayı; hepsinde aynı). */
  readonly resetsAtUtc = computed(() => this.current()?.features.find((f) => f.resetsAtUtc)?.resetsAtUtc ?? null);

  protected readonly plansUrl = PLANS_URL;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.subscriptions.current().subscribe({
      next: (current) => {
        this.current.set(current);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  /** İlerleme çubuğu yüzdesi; sınırsız ya da 0 sınırlı özellikte çubuk gösterilmez. */
  percent(feature: FeatureUsage): number {
    return feature.limit ? Math.min(100, Math.round((feature.used / feature.limit) * 100)) : 0;
  }
}
