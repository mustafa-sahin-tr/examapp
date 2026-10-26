import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { ActivityBadgeProgress, ActivityTodayStats } from '../../../models/activity-summary.model';
import { ActivityCheckSummary } from '../activity-check-panel/activity-check-panel.component';
import { ActivityProgressComponent } from '../activity-progress/activity-progress.component';

/**
 * Issue #573: etkinlik sonu — "Etkinlik bitti", bilgi satırı ("8 / 10 doğru"; puan/yüzde/XP değil), "Bugün" ve rozet
 * kartları (#572, `ActivityProgressComponent`; veri yoksa gizli) ve Sıradaki etkinlik / Listeye dön.
 */
@Component({
  selector: 'app-activity-summary',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, ActivityProgressComponent],
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
}
