import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Cevaplanmış bir adımın sonucu. */
export type DailyStepResult = 'correct' | 'wrong' | 'skipped';

/** Şeritte bir adımın görünür durumu. */
export type DailyStepState = DailyStepResult | 'current' | 'pending';

export interface DailyStepView {
  /** 1 tabanlı soru sırası. */
  number: number;
  state: DailyStepState;
}

/** Adımın erişilebilir etiketi; çağıran kendi çeviri scope'undan üretir (ör. "3. soru: yanlış"). */
export type DailyStepAriaLabelFn = (number: number, state: DailyStepState) => string;

/**
 * Günün soruları ilerleme şeridi (issue #99). Aynı veri modeli iki görünüm:
 * - `bar`:   dashboard kartındaki N parçalı çubuk; tek `role="progressbar"`.
 * - `steps`: practice daily modundaki numaralı adımlar; `role="list"`, şu anki adım `aria-current="step"`.
 * Renk tek başına anlam taşımaz: adımlarda ikon + renk birlikte kullanılır.
 */
@Component({
  selector: 'app-daily-progress-steps',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './daily-progress-steps.component.html',
  styleUrls: ['./daily-progress-steps.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DailyProgressStepsComponent {
  readonly variant = input<'bar' | 'steps'>('steps');
  /** Setteki soru sayısı. */
  readonly total = input.required<number>();
  /** Cevaplanmış adımlar, sırasıyla. `bar` görünümünde yalnız uzunluğu kullanılır. */
  readonly results = input<readonly DailyStepResult[]>([]);
  /** `bar` görünümünde çözülen sayısı; verilmezse `results.length`. */
  readonly done = input<number | null>(null);
  /** Şu anki soru (0 tabanlı); yoksa null. */
  readonly currentIndex = input<number | null>(null);
  /** `bar`: progressbar etiketi + değer metni. */
  readonly ariaLabel = input<string>('');
  readonly valueText = input<string>('');
  /** `steps`: adım etiketi üretici. */
  readonly stepAriaLabel = input<DailyStepAriaLabelFn | null>(null);

  readonly doneCount = computed(() => {
    const total = Math.max(0, this.total());
    const done = this.done() ?? this.results().length;
    return Math.min(Math.max(0, done), total);
  });

  readonly steps = computed<DailyStepView[]>(() => {
    const total = Math.max(0, this.total());
    const results = this.results();
    const current = this.currentIndex();
    const done = this.doneCount();
    return Array.from({ length: total }, (_, i) => {
      let state: DailyStepState = 'pending';
      if (i < results.length) state = results[i];
      else if (this.variant() === 'bar' && i < done) state = 'correct';
      else if (current === i) state = 'current';
      return { number: i + 1, state };
    });
  });

  labelOf(step: DailyStepView): string | null {
    const fn = this.stepAriaLabel();
    return fn ? fn(step.number, step.state) : null;
  }

  iconOf(state: DailyStepState): string | null {
    switch (state) {
      case 'correct':
        return 'check';
      case 'wrong':
        return 'close';
      case 'skipped':
        return 'skip_next';
      default:
        return null;
    }
  }
}
