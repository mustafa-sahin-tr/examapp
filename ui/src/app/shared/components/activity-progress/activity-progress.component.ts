import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';

import { ActivityBadgeProgress, ActivityTodayStats } from '../../../models/activity-summary.model';

/**
 * Issue #572: "Bugün" (etkinlik sayısı, etkin dakika, seri) ve en yakın etkinlik rozeti ilerlemesi ("Kâşif rozeti: 8/10
 * etkinlik"). Puan/yüzde/XP yok (#569). Veri null ise ilgili kart gizli. Etkinlik sonu özeti ve Etkinlikler sekmesi kullanır.
 */
@Component({
  selector: 'app-activity-progress',
  standalone: true,
  imports: [MatProgressBarModule, TranslocoDirective],
  templateUrl: './activity-progress.component.html',
  styleUrl: './activity-progress.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityProgressComponent {
  readonly today = input<ActivityTodayStats | null>(null);
  readonly badge = input<ActivityBadgeProgress | null>(null);

  readonly badgePercent = computed(() => {
    const b = this.badge();
    return b && b.target > 0 ? Math.min(100, (b.current / b.target) * 100) : 0;
  });
  readonly badgeRemaining = computed(() => {
    const b = this.badge();
    return b ? Math.max(0, b.target - b.current) : 0;
  });
}
