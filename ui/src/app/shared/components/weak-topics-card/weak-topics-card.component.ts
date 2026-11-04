import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { TopicPerformance } from '../../../models/topic-performance.model';
import { LocaleService } from '../../../services/locale.service';
import { TopicPerformanceService } from '../../../services/topic-performance.service';

const SCOPE = 'weak-topics';

export const WEAK_TOPICS_DAYS = 30;
export const WEAK_TOPICS_MIN_QUESTIONS = 5;
export const WEAK_TOPICS_LIMIT = 5;
/** Bu oranın altındaki konu "zorlanılan" sayılır: %90 doğru bir konuyu bu başlıkla göstermek yanlış bir mesaj olur. */
export const WEAK_TOPICS_MAX_ACCURACY = 70;

interface WeakTopicView {
  topicId: number;
  subjectId: number | null;
  name: string;
  subjectName: string | null;
  accuracy: string;
  answered: number;
}

/**
 * Issue #521: en zayıf konular kartı. `childId` yoksa öğrencinin kendisi (pratik bağlantısıyla), varsa velinin seçili çocuğu
 * (salt okunur; 404'te kart gizlenir, hata gösterilmez). Sıra sunucudan gelir, istemci yeniden sıralamaz.
 */
@Component({
  selector: 'app-weak-topics-card',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './weak-topics-card.component.html',
  styleUrl: './weak-topics-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class WeakTopicsCardComponent {
  readonly childId = input<number | null>(null);

  private readonly service = inject(TopicPerformanceService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly days = WEAK_TOPICS_DAYS;
  protected readonly minQuestions = WEAK_TOPICS_MIN_QUESTIONS;

  protected readonly performance = signal<TopicPerformance | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly hidden = signal(false);

  protected readonly audience = computed(() => (this.childId() == null ? 'self' : 'child'));

  protected readonly hasData = computed(() => (this.performance()?.items.length ?? 0) > 0);

  protected readonly topics = computed<WeakTopicView[]>(() => {
    const percent = new Intl.NumberFormat(this.localeService.localeDefinition().angularLocale, {
      style: 'percent',
      maximumFractionDigits: 0,
    });
    const views: WeakTopicView[] = [];
    for (const item of this.performance()?.items ?? []) {
      if (item.topicId == null || item.accuracyPct >= WEAK_TOPICS_MAX_ACCURACY) continue;
      views.push({
        topicId: item.topicId,
        subjectId: item.subjectId,
        name: item.topicName ?? '',
        subjectName: item.subjectName,
        accuracy: percent.format(item.accuracyPct / 100),
        answered: item.correct + item.wrong,
      });
    }
    return views.slice(0, WEAK_TOPICS_LIMIT);
  });

  private readonly requests = new Subject<number | null>();

  constructor() {
    this.requests
      .pipe(
        switchMap((childId) => this.fetch(childId)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    effect(() => {
      const childId = this.childId();
      untracked(() => this.requests.next(childId));
    });
  }

  protected reload(): void {
    this.requests.next(this.childId());
  }

  private fetch(childId: number | null): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(false);
      this.hidden.set(false);
      this.performance.set(null);
      const window = { days: WEAK_TOPICS_DAYS, minQuestions: WEAK_TOPICS_MIN_QUESTIONS };
      const request = childId == null ? this.service.getMine(window) : this.service.getChild(childId, window);
      return request.pipe(
        tap((performance) => this.performance.set(performance)),
        catchError((err: HttpErrorResponse) => {
          if (err?.status === 404) this.hidden.set(true);
          else this.error.set(true);
          return EMPTY;
        }),
        finalize(() => this.loading.set(false))
      );
    });
  }
}
