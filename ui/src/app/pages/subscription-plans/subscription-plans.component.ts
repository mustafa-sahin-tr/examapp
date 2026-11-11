import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PARENT_REQUIRED_PATH } from '../../models/parent-status.model';
import { PlacementStatus } from '../../models/placement.model';
import { CurrentSubscriptionPlan, planNameKey } from '../../models/subscription';
import { AuthService } from '../../services/auth.service';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { PlacementService } from '../../services/placement.service';
import { StudentParentService } from '../../services/student-parent.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PlanPickerComponent } from '../../shared/components/plan-picker/plan-picker.component';

/**
 * Issue #1028: kayıttaki plan adımına sonradan erişim (yükseltme). "Planım" (#1029) buraya bağlanır. Değişiklik sonrası seçici
 * yeni planı işaretler; burada yalnız onay notu gösterilir.
 */
@Component({
  selector: 'app-subscription-plans',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoDirective, PageHeaderComponent, PlanPickerComponent],
  providers: [provideTranslocoScope('subscription', 'placement')],
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
  private readonly auth = inject(AuthService);
  readonly lockedKey = this.auth.hasRealmRole('Student') ? 'page.lockedStudent' : 'page.lockedTeacher';

  /** Issue #1091: öğrencinin planını velisi alır (öğretmen / admin rolü de taşıyan hesap kendisi alır). */
  readonly parentPays =
    this.auth.hasRealmRole('Student') && !this.auth.hasRealmRole('Teacher') && !this.auth.hasRealmRole('Admin');
  /** Bağlı (aktif) velisi yok: plan seçimi için önce veli eklenir (#440 akışı). */
  readonly needsParent = signal(false);
  readonly parentRequiredLink = '/' + PARENT_REQUIRED_PATH;
  /** Issue #1114: öğrencinin sınıfı için yayımlanmış seviye tespiti (çözülmüşse rapora gider). */
  readonly placement = signal<PlacementStatus | null>(null);

  constructor() {
    this.entitlements.load().subscribe();
    if (this.parentPays) {
      inject(StudentParentService)
        .ensureLoaded()
        .subscribe((status) => this.needsParent.set(!!status && status.mode !== 'Ok'));
    }
    if (this.auth.hasRealmRole('Student')) {
      inject(PlacementService)
        .status()
        .subscribe({ next: (status) => this.placement.set(status.available ? status : null), error: () => this.placement.set(null) });
    }
  }

  onCompleted(plan: CurrentSubscriptionPlan | null): void {
    if (plan) this.changedTo.set(planNameKey(plan.displayNameKey));
  }
}
