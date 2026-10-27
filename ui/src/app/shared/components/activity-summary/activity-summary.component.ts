import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { ActivityBadgeProgress, ActivityTodayStats } from '../../../models/activity-summary.model';
import { OpenTextStudentReview } from '../../../models/open-text';
import { ActivityCheckSummary } from '../activity-check-panel/activity-check-panel.component';
import { ActivityProgressComponent } from '../activity-progress/activity-progress.component';
import { OpenTextReviewStatusComponent } from '../open-text-review-status/open-text-review-status.component';

/** Issue #583: özetteki açık uçlu madde — etiket (a, b …), cevaplandı mı, öğretmen değerlendirmesi (yoksa null). */
export interface ActivitySummaryOpenTextItem {
  label: string;
  answered: boolean;
  review: OpenTextStudentReview | null;
}

/**
 * Issue #573: etkinlik sonu — "Etkinlik bitti", bilgi satırı ("8 / 10 doğru"; puan/yüzde/XP değil), "Bugün" ve rozet
 * kartları (#572, `ActivityProgressComponent`; veri yoksa gizli) ve Sıradaki etkinlik / Listeye dön.
 * Issue #609: tamamlanmış etkinlikte "Yeniden çöz" (`canRetake`; süresi dolan oturumda gizli).
 * Issue #583: açık uçlu maddeler bilgi satırına girmez (sayılmaz); ayrı listede değerlendirme durumu ve öğretmenin geri
 * bildirimi gösterilir.
 */
@Component({
  selector: 'app-activity-summary',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, ActivityProgressComponent, OpenTextReviewStatusComponent],
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
  /** Issue #609: oturum tamamlandı (süresi dolmadı) — "Yeniden çöz" gösterilir. */
  readonly canRetake = input(false);
  readonly retakeBusy = input(false);
  /** Issue #583: açık uçlu maddeler (yoksa liste çizilmez). */
  readonly openTextItems = input<ActivitySummaryOpenTextItem[]>([]);

  readonly next = output<void>();
  readonly backToList = output<void>();
  readonly retake = output<void>();
}
