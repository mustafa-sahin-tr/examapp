import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { CurrentSubscriptionPlan, planNameKey } from '../../models/subscription';
import { AuthService } from '../../services/auth.service';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PlanPickerComponent } from '../../shared/components/plan-picker/plan-picker.component';

/**
 * Issue #1028: kayıttaki plan adımına sonradan erişim (yükseltme). "Planım" (#1029) buraya bağlanır. Değişiklik sonrası seçici
 * yeni planı işaretler; burada yalnız onay notu gösterilir.
 */
@Component({
  selector: 'app-subscription-plans',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective, PageHeaderComponent, PlanPickerComponent],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './subscription-plans.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './subscription-plans.component.scss',
})
export class SubscriptionPlansComponent {
  private readonly entitlements = inject(EntitlementStateService);

  /** Son değişiklikle etkinleşen planın scope içi ad anahtarı. */
  readonly changedTo = signal<string | null>(null);

  /** Issue #1090: deneme bitti, plan yok; uygulamanın geri kalanı bu sayfaya yönlenir. Öğrencide planı veli seçer. */
  readonly locked = computed(() => this.entitlements.plan()?.locked === true);
  readonly lockedKey = inject(AuthService).hasRealmRole('Student') ? 'page.lockedStudent' : 'page.lockedTeacher';

  constructor() {
    this.entitlements.load().subscribe();
  }

  onCompleted(plan: CurrentSubscriptionPlan | null): void {
    if (plan) this.changedTo.set(planNameKey(plan.displayNameKey));
  }
}
