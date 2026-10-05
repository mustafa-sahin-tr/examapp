import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { DailySet } from '../../../models/practice';
import { DailyProgressStepsComponent } from '../daily-progress-steps/daily-progress-steps.component';

/** Kartın görsel durumu (issue #99 tasarım notu, durum tablosu). */
export type DailyQuestionsCardState =
  | 'loading'
  | 'error'
  | 'notStarted'
  | 'inProgress'
  | 'completed'
  | 'lowPool'
  | 'empty';

/** Tahmini süre için soru başına dakika; "~8 dk" chip'i bundan hesaplanır. */
const MINUTES_PER_QUESTION = 1.5;

/**
 * Dashboard "Günün soruları" kartı (issue #99). Veri çekmez: dashboard seti/hata/yükleniyor durumunu
 * input olarak verir, kart yalnız durumları çizer ve CTA'ları output olarak yayar. Kartın tamamı
 * tıklanabilir değil; yalnız CTA (odak sırası ve yanlış tıklama riski).
 */
@Component({
  selector: 'app-daily-questions-card',
  standalone: true,
  imports: [MatButtonModule, MatChipsModule, MatIconModule, TranslocoDirective, DailyProgressStepsComponent],
  templateUrl: './daily-questions-card.component.html',
  styleUrls: ['./daily-questions-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DailyQuestionsCardComponent {
  readonly dailySet = input<DailySet | null>(null);
  readonly loading = input(false);
  readonly error = input(false);
  /** Günlük seri (gün); null/0 ise chip render edilmez. */
  readonly streak = input<number | null>(null);

  /** Başla / Devam et. */
  readonly start = output<void>();
  readonly retry = output<void>();
  readonly freePractice = output<void>();
  readonly goToProfile = output<void>();

  readonly state = computed<DailyQuestionsCardState>(() => {
    if (this.error()) return 'error';
    const set = this.dailySet();
    if (this.loading() || !set) return 'loading';
    switch (set.status) {
      case 'Empty':
        return 'empty';
      case 'Completed':
        return 'completed';
      case 'InProgress':
        return 'inProgress';
      default:
        if (set.total <= 0) return 'empty';
        return set.total < set.targetCount ? 'lowPool' : 'notStarted';
    }
  });

  readonly total = computed(() => Math.max(0, this.dailySet()?.total ?? 0));
  readonly answered = computed(() => Math.min(Math.max(0, this.dailySet()?.answered ?? 0), this.total()));
  readonly remaining = computed(() => Math.max(0, this.total() - this.answered()));
  readonly correct = computed(() => this.dailySet()?.correct ?? 0);
  readonly wrong = computed(() => this.dailySet()?.wrong ?? 0);
  readonly skipped = computed(() => this.dailySet()?.skipped ?? 0);
  readonly targetCount = computed(() => this.dailySet()?.targetCount ?? 0);
  readonly minutes = computed(() => Math.max(1, Math.ceil(this.total() * MINUTES_PER_QUESTION)));
  /** Devam eden sette şu anki parça; tamamlanmış/başlamamış sette yok. */
  readonly currentIndex = computed(() =>
    this.state() === 'inProgress' && this.answered() < this.total() ? this.answered() : null
  );
  readonly showStreak = computed(() => (this.streak() ?? 0) > 0);
}
