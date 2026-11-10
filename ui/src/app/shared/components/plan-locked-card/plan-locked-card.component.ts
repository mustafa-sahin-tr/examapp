import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { EntitlementStateService } from '../../../services/entitlement-state.service';
import { EntitlementLimitHandler } from '../../interceptors/entitlement-limit.interceptor';

/**
 * Issue #1089: planda olmayan bölümün yerine gelen kart — hata yerine neyin eksik olduğu; "Planları karşılaştır" yükseltme
 * penceresini açar (hangi planda olduğu orada).
 */
@Component({
  selector: 'app-plan-locked-card',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './plan-locked-card.component.html',
  styleUrl: './plan-locked-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanLockedCardComponent {
  private readonly entitlements = inject(EntitlementStateService);
  private readonly limits = inject(EntitlementLimitHandler);

  /** Özellik anahtarı (ör. `analytics`). */
  readonly feature = input.required<string>();

  compare(): void {
    this.limits.showNotInPlan(this.feature(), this.entitlements.plan()?.planKey ?? null);
  }
}
