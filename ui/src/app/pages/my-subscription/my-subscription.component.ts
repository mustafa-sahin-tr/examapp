import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { CurrentSubscriptionPlan, FeatureUsage, TRIAL_PLAN_KEY, planNameKey } from '../../models/subscription';
import { AuthService } from '../../services/auth.service';
import { SubscriptionService } from '../../services/subscription.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PlanCancelComponent } from '../../shared/components/plan-cancel/plan-cancel.component';
import { PurchaseTermsComponent } from '../../shared/components/purchase-terms/purchase-terms.component';
import { PLANS_URL } from '../../shared/components/upgrade-dialog/upgrade-dialog.component';

/**
 * Issue #1029: "Aboneliğim" (profil menüsü). Plan, geçerlilik / deneme bitişi, bu ayki kullanım ve sayaçların sıfırlanma
 * tarihi; "Planı yükselt" plan seçimine (#1028 `/plans`) gider. Sözlükte "Planım" çalışma planına ayrılmıştır.
 * Issue #1116: satın alınmış planın iptali ve satın alma koşulları.
 */
@Component({
  selector: 'app-my-subscription',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, RouterLink, TranslocoDirective, PageHeaderComponent,
    PlanCancelComponent, PurchaseTermsComponent],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './my-subscription.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-subscription.component.scss',
})
export class MySubscriptionComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);
  private readonly auth = inject(AuthService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly current = signal<CurrentSubscriptionPlan | null>(null);

  readonly nameKey = computed(() => planNameKey(this.current()?.displayNameKey));
  readonly isTrial = computed(() => this.current()?.planKey === TRIAL_PLAN_KEY);
  /** Sınırlı özelliklerin ortak sıfırlanma anı (UTC takvim ayı; hepsinde aynı). */
  readonly resetsAtUtc = computed(() => this.current()?.features.find((f) => f.resetsAtUtc)?.resetsAtUtc ?? null);

  protected readonly plansUrl = PLANS_URL;
  /** Issue #1116: öğrencinin planını velisi iptal eder (öğretmen / admin rolü de taşıyan hesap kendisi). */
  protected readonly parentPays =
    this.auth.hasRealmRole('Student') && !this.auth.hasRealmRole('Teacher') && !this.auth.hasRealmRole('Admin');

  ngOnInit(): void {
    this.load();
  }

  /** `silent`: iptal sonrası sayfa yerinde yenilenir (iptal kutusunun iade notu kaybolmasın). */
  load(silent = false): void {
    if (!silent) this.loading.set(true);
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
