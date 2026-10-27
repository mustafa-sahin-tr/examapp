import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { OpenTextEvaluation, OpenTextStudentReview } from '../../../models/open-text';

/** Değerlendirme rozetinin ikonu (renk tek sinyal değil: ikon + metin). */
const EVALUATION_ICON: Record<OpenTextEvaluation, string> = {
  sufficient: 'check_circle',
  partial: 'adjust',
  insufficient: 'error_outline',
};

/**
 * Issue #583: açık uçlu cevabın değerlendirme durumu — "Öğretmen değerlendirmesi bekliyor" ya da Yeterli / Kısmen / Eksik rozeti
 * + öğretmenin geri bildirimi; eskimiş değerlendirmede ("outdated") not. Geri bildirim düz metindir, yalnız interpolasyonla
 * (satır sonları `pre-wrap`) gösterilir. Öğrenci oynatıcısı, etkinlik özeti ve öğretmen kuyruğu ortak kullanır.
 */
@Component({
  selector: 'app-open-text-review-status',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  templateUrl: './open-text-review-status.component.html',
  styleUrl: './open-text-review-status.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpenTextReviewStatusComponent {
  readonly review = input<OpenTextStudentReview | null>(null);
  /** `outdated` notunun dili: öğrenciye "Cevabın değişti", öğretmene "Önceki metne ait". */
  readonly audience = input<'student' | 'teacher'>('student');

  readonly evaluation = computed(() => {
    const review = this.review();
    return review && review.status !== 'pending' ? review.evaluation : null;
  });
  readonly icon = computed(() => {
    const evaluation = this.evaluation();
    return evaluation ? EVALUATION_ICON[evaluation] : 'hourglass_top';
  });
}
