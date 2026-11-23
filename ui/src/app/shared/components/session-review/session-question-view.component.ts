import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { SessionReviewQuestion } from '../../../models/session-review';
import { strokeCss, strokePath } from '../../utils/pen-color';

/** Kayıtlı çizgilerin genişliği (`test-solve-scratch-store.ts` SCRATCH_VIEW_WIDTH ile aynı). */
const VIEW_WIDTH = 1000;
const MIN_PAD_HEIGHT = 500;

/** Çizgilerdeki en büyük y değeri (karalama alanının yüksekliği kayıtlı değil; çizilen kadar gösterilir). */
export function maxStrokeY(strokes: readonly string[]): number {
  let max = 0;
  for (const stroke of strokes) {
    const numbers = strokePath(stroke).match(/\d+/g) ?? [];
    for (let i = 1; i < numbers.length; i += 2) max = Math.max(max, Number(numbers[i]));
  }
  return max;
}

/**
 * Issue #1310: öğrencinin bir sorudaki çizimleri, salt okunur — soru görseli ve üstüne çizdikleri, yanında karalama alanı
 * (çizgiler + not). Öğretmenin çözüm penceresi ve velinin sonuç penceresi kullanır.
 */
@Component({
  selector: 'app-session-question-view',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope('session-review')],
  templateUrl: './session-question-view.component.html',
  styleUrl: './session-question-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionQuestionViewComponent {
  readonly question = input.required<SessionReviewQuestion>();

  protected readonly viewWidth = VIEW_WIDTH;
  /** Görselin oranına göre çizim katmanının yüksekliği (görsel yüklenince). */
  protected readonly inkHeight = signal(VIEW_WIDTH);
  protected readonly padHeight = computed(() => Math.max(MIN_PAD_HEIGHT, maxStrokeY(this.question().strokes) + 40));
  protected readonly hasPad = computed(() => this.question().strokes.length > 0 || !!this.question().text?.trim());
  protected readonly strokePath = strokePath;
  protected readonly strokeCss = strokeCss;

  protected imageLoaded(image: HTMLImageElement): void {
    if (image.naturalWidth > 0) this.inkHeight.set(Math.round((image.naturalHeight / image.naturalWidth) * VIEW_WIDTH));
  }
}
