import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { TopicPerformance, TopicProgressTimeline, TopicProgressWeek } from '../../../models/topic-performance.model';
import { LocaleService } from '../../../services/locale.service';
import { TopicPerformanceService } from '../../../services/topic-performance.service';

const SCOPE = 'topic-progress';

export const TOPIC_PROGRESS_WEEKS = 12;
/** Seçici 12 haftayı kapsar: en çok 90 gün isteyebildiğimiz konu listesi bu pencereye denk düşer. */
const TOPIC_LIST_DAYS = 90;
const TOPIC_LIST_MIN_QUESTIONS = 1;

const CHART_HEIGHT = 200;
const CHART_LEFT = 44;
const CHART_RIGHT = 12;
const CHART_TOP = 12;
const CHART_BOTTOM = 26;
const NARROW_CHART_WIDTH = 420;
const Y_TICKS = [0, 50, 100];
/** Ölçülene kadar kullanılan genişlik; ResizeObserver hemen gerçeğiyle değiştirir. */
const DEFAULT_CHART_WIDTH = 600;

interface TopicOption {
  topicId: number;
  label: string;
  answered: number;
}

interface ChartPoint {
  x: number;
  y: number | null;
  week: TopicProgressWeek;
  dateLabel: string;
  accuracyLabel: string | null;
}

/**
 * Issue #531: seçilen konunun haftalık doğruluğu. Cevapsız hafta boşluk bırakır, sıfır çizilmez. Az cevaplı hafta soluk ve içi boş
 * işaretle ayrılır; renk tek başına anlam taşımaz, aynı bilgi ipucunda ve tablo görünümünde de vardır.
 */
@Component({
  selector: 'app-topic-progress-card',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatProgressSpinnerModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './topic-progress-card.component.html',
  styleUrl: './topic-progress-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TopicProgressCardComponent {
  private readonly service = inject(TopicPerformanceService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly plot = viewChild<ElementRef<HTMLElement>>('plot');

  protected readonly weeks = TOPIC_PROGRESS_WEEKS;
  protected readonly chartHeight = CHART_HEIGHT;
  protected readonly plotLeft = CHART_LEFT;
  protected readonly plotBottom = CHART_HEIGHT - CHART_BOTTOM;

  protected readonly topics = signal<TopicOption[]>([]);
  protected readonly selectedTopicId = signal<number | null>(null);
  protected readonly timeline = signal<TopicProgressTimeline | null>(null);
  protected readonly loadingTopics = signal(true);
  protected readonly loadingTimeline = signal(false);
  protected readonly error = signal(false);
  protected readonly chartWidth = signal(DEFAULT_CHART_WIDTH);
  protected readonly activeIndex = signal<number | null>(null);

  protected readonly plotRight = computed(() => this.chartWidth() - CHART_RIGHT);

  protected readonly selectedTopicLabel = computed(
    () => this.topics().find((topic) => topic.topicId === this.selectedTopicId())?.label ?? '',
  );

  private readonly percent = computed(
    () =>
      new Intl.NumberFormat(this.localeService.localeDefinition().angularLocale, {
        style: 'percent',
        maximumFractionDigits: 0,
      }),
  );

  protected readonly points = computed<ChartPoint[]>(() => {
    const weeks = this.timeline()?.weeks ?? [];
    const dates = new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, { day: 'numeric', month: 'short' });
    const step = weeks.length > 1 ? (this.plotRight() - CHART_LEFT) / (weeks.length - 1) : 0;
    return weeks.map((week, index) => {
      const [year, month, day] = week.weekStart.split('-').map(Number);
      return {
        x: CHART_LEFT + step * index,
        y: week.accuracyPct == null ? null : this.yOf(week.accuracyPct),
        week,
        dateLabel: dates.format(new Date(year, month - 1, day)),
        accuracyLabel: week.accuracyPct == null ? null : this.percent().format(week.accuracyPct / 100),
      };
    });
  });

  /** Ardışık cevaplı haftalar birer parça: cevapsız hafta çizgiyi böler, tek kalan hafta yalnız işaretle görünür. */
  protected readonly linePaths = computed<string[]>(() => {
    const paths: string[] = [];
    let run: string[] = [];
    const flush = () => {
      if (run.length > 1) paths.push(run.join(' '));
      run = [];
    };
    for (const point of this.points()) {
      if (point.y == null) {
        flush();
      } else {
        run.push(`${run.length === 0 ? 'M' : 'L'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`);
      }
    }
    flush();
    return paths;
  });

  protected readonly markers = computed(() =>
    this.points()
      .map((point, index) => ({ ...point, index }))
      .filter((point) => point.y != null),
  );

  protected readonly yTicks = computed(() =>
    Y_TICKS.map((value) => ({ y: this.yOf(value), label: this.percent().format(value / 100) })),
  );

  /** Son hafta hep etiketli kalır; dar alanda etiketler çakışmasın diye daha seyrek. */
  protected readonly xTicks = computed(() => {
    const points = this.points();
    const every = this.chartWidth() < NARROW_CHART_WIDTH ? 4 : 2;
    return points
      .map((point, index) => ({ point, index }))
      .filter(({ index }) => (points.length - 1 - index) % every === 0)
      .map(({ point, index }) => ({
        x: point.x,
        label: point.dateLabel,
        anchor: index === points.length - 1 ? 'end' : index === 0 ? 'start' : 'middle',
      }));
  });

  /** Yeterli cevabı olan ilk ve son haftadan; az cevaplı hafta eğilim söylemez. */
  protected readonly summary = computed(() => {
    const reliable = (this.timeline()?.weeks ?? []).filter((week) => week.accuracyPct != null && !week.lowSample);
    if (reliable.length < 2) return null;
    return {
      from: this.percent().format(reliable[0].accuracyPct! / 100),
      to: this.percent().format(reliable[reliable.length - 1].accuracyPct! / 100),
    };
  });

  protected readonly active = computed(() => {
    const index = this.activeIndex();
    return index == null ? null : (this.points()[index] ?? null);
  });

  /** İpucu kartın dışına taşmasın diye sütunun yanında, sağ yarıda sola açılır. */
  protected readonly tooltipOpensLeft = computed(() => (this.active()?.x ?? 0) > this.chartWidth() / 2);

  private readonly topicRequests = new Subject<void>();
  private readonly timelineRequests = new Subject<number>();

  constructor() {
    this.topicRequests
      .pipe(
        switchMap(() => this.fetchTopics()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.timelineRequests
      .pipe(
        switchMap((topicId) => this.fetchTimeline(topicId)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    effect((onCleanup) => {
      const element = this.plot()?.nativeElement;
      if (!element) return;
      const observer = new ResizeObserver(() => {
        if (element.clientWidth > 0) this.chartWidth.set(element.clientWidth);
      });
      observer.observe(element);
      onCleanup(() => observer.disconnect());
    });

    this.topicRequests.next();
  }

  protected selectTopic(topicId: number): void {
    this.selectedTopicId.set(topicId);
    this.timelineRequests.next(topicId);
  }

  protected reload(): void {
    const topicId = this.selectedTopicId();
    if (topicId == null) this.topicRequests.next();
    else this.timelineRequests.next(topicId);
  }

  /** İşaretçi çizgiye değil haftaya hedeflenir: en yakın hafta seçilir. */
  protected onPointerMove(event: PointerEvent): void {
    const points = this.points();
    if (points.length === 0) return;
    const left = (event.currentTarget as HTMLElement).getBoundingClientRect().left;
    const x = event.clientX - left;
    let nearest = 0;
    points.forEach((point, index) => {
      if (Math.abs(point.x - x) < Math.abs(points[nearest].x - x)) nearest = index;
    });
    this.activeIndex.set(nearest);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const last = this.points().length - 1;
    if (last < 0) return;
    const current = this.activeIndex() ?? last;
    if (event.key === 'ArrowLeft') this.activeIndex.set(Math.max(0, current - 1));
    else if (event.key === 'ArrowRight') this.activeIndex.set(Math.min(last, current + 1));
    else return;
    event.preventDefault();
  }

  protected onFocus(): void {
    if (this.activeIndex() == null && this.points().length > 0) this.activeIndex.set(this.points().length - 1);
  }

  protected clearActive(): void {
    this.activeIndex.set(null);
  }

  private yOf(accuracy: number): number {
    const span = CHART_HEIGHT - CHART_TOP - CHART_BOTTOM;
    return CHART_TOP + span * (1 - accuracy / 100);
  }

  private fetchTopics(): Observable<unknown> {
    return defer(() => {
      this.loadingTopics.set(true);
      this.error.set(false);
      return this.service.getMine({ days: TOPIC_LIST_DAYS, minQuestions: TOPIC_LIST_MIN_QUESTIONS }).pipe(
        tap((performance) => {
          const topics = this.toOptions(performance);
          this.topics.set(topics);
          if (topics.length > 0) this.selectTopic(topics[0].topicId);
        }),
        catchError((_: HttpErrorResponse) => {
          this.error.set(true);
          return EMPTY;
        }),
        finalize(() => this.loadingTopics.set(false)),
      );
    });
  }

  private fetchTimeline(topicId: number): Observable<unknown> {
    return defer(() => {
      this.loadingTimeline.set(true);
      this.error.set(false);
      this.activeIndex.set(null);
      this.timeline.set(null);
      return this.service.getMyTimeline(topicId, TOPIC_PROGRESS_WEEKS).pipe(
        tap((timeline) => this.timeline.set(timeline)),
        catchError((_: HttpErrorResponse) => {
          this.error.set(true);
          return EMPTY;
        }),
        finalize(() => this.loadingTimeline.set(false)),
      );
    });
  }

  /** En çok çözülen konu önce: açılışta en çok şey söyleyen grafik görünür. */
  private toOptions(performance: TopicPerformance): TopicOption[] {
    const options: TopicOption[] = [];
    for (const item of performance.items) {
      if (item.topicId == null) continue;
      options.push({
        topicId: item.topicId,
        label: item.subjectName ? `${item.subjectName} · ${item.topicName ?? ''}` : (item.topicName ?? ''),
        answered: item.correct + item.wrong,
      });
    }
    return options.sort((a, b) => b.answered - a.answered || a.label.localeCompare(b.label));
  }
}
