import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';

import { ActivityCheckSummary } from '../activity-check-panel/activity-check-panel.component';

/** Issue #572 (henüz yok): bugünkü etkinlik özeti. Veri gelene dek null → kart gizli. */
export interface ActivityTodayStats {
  activityCount: number;
  activeMinutes: number;
  streakDays: number;
}

/** Issue #572 (henüz yok): rozet ilerlemesi. Veri gelene dek null → kart gizli. */
export interface ActivityBadgeProgress {
  name: string;
  current: number;
  target: number;
}

/**
 * Issue #573: etkinlik sonu — "Etkinlik bitti", bilgi satırı ("8 / 10 doğru"; puan/yüzde/XP değil), "Bugün" ve rozet
 * kartları (#572 verisi gelene dek gizli) ve Sıradaki etkinlik / Listeye dön.
 */
@Component({
  selector: 'app-activity-summary',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoDirective],
  templateUrl: './activity-summary.component.html',
  styleUrl: './activity-summary.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivitySummaryComponent {
  readonly result = input<ActivityCheckSummary | null>(null);
  readonly today = input<ActivityTodayStats | null>(null);
  readonly badge = input<ActivityBadgeProgress | null>(null);
  /** Sıradaki atanmış etkinlik varsa düğme gösterilir. */
  readonly hasNext = input(false);
  readonly nextBusy = input(false);

  readonly next = output<void>();
  readonly backToList = output<void>();

  readonly badgePercent = computed(() => {
    const b = this.badge();
    return b && b.target > 0 ? Math.min(100, (b.current / b.target) * 100) : 0;
  });
  readonly badgeRemaining = computed(() => {
    const b = this.badge();
    return b ? Math.max(0, b.target - b.current) : 0;
  });
}
