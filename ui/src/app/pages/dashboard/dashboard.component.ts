import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { CompactTestCardComponent } from '../../shared/components/compact-test-card/compact-test-card.component';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { TestService } from '../../services/test.service';
import { AssignedWorksheet } from '../../models/assignment';
import { Test } from '../../models/test-instance';
import { NgxChartsModule, Color, ScaleType } from '@swimlane/ngx-charts';
import { BadgeProgressItem, BadgeService, UserActivityResponse } from '../../services/badge.service';
import { finalize } from 'rxjs';
import { StudentResetService } from '../../services/student-reset.service';
import { StudentService } from '../../services/student.service';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LocaleService } from '../../services/locale.service';

interface AssignmentCardViewModel {
  assignment: AssignedWorksheet;
  test: Test;
}

/** Issue #197 — aktivite özet kartı (soru/doğru/dakika/skor); ngx-charts number-card yerine yerel grid kartı. */
export interface ActivityStatCard {
  key: 'questions' | 'correct' | 'minutes' | 'activityScore';
  icon: string;
  name: string;
  value: number;
}

/** "Sıradaki Rozetler" listesi için görünüm modeli — ilerleme yüzdesi şablonda hesaplanmasın diye burada türetilir. */
interface UpcomingBadgeViewModel {
  badge: BadgeProgressItem;
  remaining: number;
  percent: number;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatIconModule, CompactTestCardComponent, SectionHeaderComponent, NgxChartsModule, TranslocoPipe],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss'],
})
export class DashboardComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly testService = inject(TestService);
  private readonly router = inject(Router);
  private readonly badgeService = inject(BadgeService);
  private readonly studentResetService = inject(StudentResetService);
  private readonly studentService = inject(StudentService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  /** `toLocaleDateString` gibi Intl API'lerine verilecek aktif dil etiketi (örn. 'tr', 'en-US'). */
  private get intlLocale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  private readonly assignments = signal<AssignmentCardViewModel[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly activityDataFromApi = signal<
    Array<{ name: string; series: Array<{ name: string; value: number; extra?: any }> }>
  >([]);
  readonly activityApiLoading = signal(false);
  readonly activityApiError = signal(false);
  readonly activityNumberCardData = signal<ActivityStatCard[]>([]);
  readonly badgeProgressLoading = signal(false);
  readonly badgeProgressError = signal(false);
  readonly earnedBadges = signal<BadgeProgressItem[]>([]);
  /** Ham rozet ilerleme listesi (tamamlanan + tamamlanmayan); earnedBadges ve upcomingBadges buradan türer. */
  readonly allBadgeProgress = signal<BadgeProgressItem[]>([]);
  readonly resetInProgress = signal(false);
  readonly resetMessage = signal<string | null>(null);

  /** Issue #126 — önceki giriş zamanı; null ise chip hiç render edilmez. */
  readonly lastLoginAtUtc = signal<string | null>(null);
  readonly lastLoginLabel = computed(() => this.formatLastLogin(this.lastLoginAtUtc()));

  readonly upcomingBadgesLimit = 3;
  readonly upcomingBadges = computed<UpcomingBadgeViewModel[]>(() =>
    this.allBadgeProgress()
      .filter((badge) => !badge.isCompleted)
      .map((badge) => {
        const target = Math.max(badge.targetValue ?? 0, 0);
        const current = Math.min(Math.max(badge.currentValue ?? 0, 0), target);
        return {
          badge,
          remaining: target - current,
          percent: target > 0 ? Math.round((current / target) * 100) : 0,
        };
      })
      .sort((a, b) => a.remaining - b.remaining)
      .slice(0, this.upcomingBadgesLimit)
  );
  readonly allBadgesCompleted = computed(
    () => this.allBadgeProgress().length > 0 && this.upcomingBadges().length === 0
  );

  readonly assignmentCards = computed(() => this.assignments());
  readonly canScrollLeft = signal(false);
  readonly canScrollRight = signal(false);
  readonly viewportWidth = signal(typeof window !== 'undefined' ? window.innerWidth : 1280);
  readonly isMobileViewport = computed(() => this.viewportWidth() < 768);
  /**
   * Issue #197 — heat map sabit piksel yerine kapsayıcısının gerçek genişliğine göre çizilir.
   * 0 = henüz ölçülmedi; şablon bu durumda grafiği hiç çizmez.
   */
  readonly heatmapContainerWidth = signal(0);
  /**
   * Bir hafta sütununun en küçük genişliği (px). ngx-charts hücreler arasında 8px iç boşluk bırakır;
   * 22px ile hücre ~14px kalır (eski masaüstü yoğunluğu); daha dar değerde hücreler çizgiye döner.
   */
  private readonly heatmapMinCellPx = 22;
  /** Y ekseni gün etiketleri + kenar boşluğu için ayrılan pay (px). */
  private readonly heatmapAxisAllowancePx = 48;
  private readonly heatmapMinWeeks = 12;
  /** Kapsayıcıya sığan hafta sayısı; dar ekranda en yeni haftalar gösterilir, eski haftalar düşer. */
  readonly visibleHeatmapWeeks = computed(() => {
    const width = this.heatmapContainerWidth();
    if (width <= 0) {
      return Number.POSITIVE_INFINITY;
    }

    const fit = Math.floor((width - this.heatmapAxisAllowancePx) / this.heatmapMinCellPx);
    return Math.max(this.heatmapMinWeeks, fit);
  });
  readonly activityHeatmapData = computed(() => {
    const data = this.activityDataFromApi();
    const weeks = this.visibleHeatmapWeeks();
    return data.length > weeks ? data.slice(-weeks) : data;
  });
  readonly showActivitySummary = computed(
    () =>
      this.activityNumberCardData().length > 0 ||
      this.badgeProgressLoading() ||
      this.badgeProgressError() ||
      this.earnedBadges().length > 0
  );
  /** Kapsayıcıyı doldurur; en az hafta sayısı bile sığmıyorsa yalnızca kendi kapsayıcısında (.heatmap-scroll) kayar. */
  readonly activityHeatmapView = computed<[number, number]>(() => {
    const height = this.isMobileViewport() ? 190 : 210;
    const minWidth = this.heatmapMinWeeks * this.heatmapMinCellPx + this.heatmapAxisAllowancePx;
    return [Math.max(Math.floor(this.heatmapContainerWidth()), minWidth), height];
  });
  readonly showHeatmapYAxis = computed(() => true);
  readonly showHeatmapAxisLabels = computed(() => !this.isMobileViewport());

  readonly scrollDistance = 600;
  private readonly demoActivityUserId = 16;
  private activityLastMonthDisplayed = '';
  private resizeObserver?: ResizeObserver;
  private assignmentContainerRef?: ElementRef<HTMLDivElement>;
  private heatmapResizeObserver?: ResizeObserver;
  private heatmapScrollEl?: HTMLDivElement;

  /** Heat map kapsayıcısı veri gelince render edilir; her yeni element için genişliği izlenir. */
  @ViewChild('heatmapScroll', { static: false })
  set heatmapScroll(ref: ElementRef<HTMLDivElement> | undefined) {
    const element = ref?.nativeElement;
    if (element === this.heatmapScrollEl) {
      return;
    }

    if (this.heatmapScrollEl) {
      this.heatmapResizeObserver?.unobserve(this.heatmapScrollEl);
    }
    this.heatmapScrollEl = element;

    if (!element) {
      return;
    }

    this.heatmapContainerWidth.set(element.clientWidth ?? 0);
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    this.heatmapResizeObserver ??= new ResizeObserver((entries) => {
      const width = entries[entries.length - 1]?.contentRect.width ?? 0;
      if (Math.floor(width) !== Math.floor(this.heatmapContainerWidth())) {
        this.heatmapContainerWidth.set(width);
      }
    });
    this.heatmapResizeObserver.observe(element);
  }

  @ViewChild('assignmentContainer', { static: false })
  set assignmentContainer(ref: ElementRef<HTMLDivElement> | undefined) {
    if (ref?.nativeElement === this.assignmentContainerRef?.nativeElement) {
      return;
    }

    if (this.resizeObserver && this.assignmentContainerRef) {
      this.resizeObserver.unobserve(this.assignmentContainerRef.nativeElement);
    }

    this.assignmentContainerRef = ref;

    if (ref && this.resizeObserver) {
      this.resizeObserver.observe(ref.nativeElement);
    }

    this.scheduleScrollIndicatorUpdate();
  }

  readonly activityColorScheme: Color = {
    name: 'sunset',
    selectable: false,
    group: ScaleType.Linear,
    domain: ['#38346fff', '#5a5a5aff', '#808080ff', '#b3b3b3ff', '#e6e6e6ff', '#ffffff'],
  };

  ngOnInit(): void {
    this.updateViewportWidth();

    this.testService.getActiveAssignments().subscribe({
      next: (assignments) => {
        const viewModels = assignments.map((assignment) => ({
          assignment,
          test: this.mapToTest(assignment),
        }));
        this.assignments.set(viewModels);
        this.loading.set(false);
        this.scheduleScrollIndicatorUpdate();
      },
      error: () => {
        this.error.set(this.transloco.translate('dashboard.assignments.error'));
        this.loading.set(false);
      },
    });

    const resolvedUserId = this.getUserIdFromLocalStorage() ?? this.demoActivityUserId;
    this.loadUserActivityHeatmap(resolvedUserId);
    this.loadUserBadgeProgress(resolvedUserId);
    this.loadLastLogin();
  }

  onResetMyActivity(): void {
    if (this.resetInProgress()) {
      return;
    }

    const confirmed =
      typeof window !== 'undefined'
        ? window.confirm(this.transloco.translate('dashboard.reset.confirm'))
        : false;

    if (!confirmed) {
      return;
    }

    this.resetInProgress.set(true);
    this.resetMessage.set(null);

    // Clear local UI data immediately; backend reset happens async via Hangfire.
    this.assignments.set([]);
    this.activityDataFromApi.set([]);
    this.activityNumberCardData.set([]);
    this.earnedBadges.set([]);
    this.allBadgeProgress.set([]);

    this.studentResetService
      .resetMyData()
      .pipe(finalize(() => this.resetInProgress.set(false)))
      .subscribe({
        next: (res) => {
          this.resetMessage.set(res?.message || this.transloco.translate('dashboard.reset.queued'));
          // Try to refresh after a short delay.
          const userId = this.getUserIdFromLocalStorage() ?? this.demoActivityUserId;
          setTimeout(() => {
            this.testService.getActiveAssignments().subscribe({
              next: (assignments) => {
                const viewModels = assignments.map((assignment) => ({
                  assignment,
                  test: this.mapToTest(assignment),
                }));
                this.assignments.set(viewModels);
                this.scheduleScrollIndicatorUpdate();
              },
              error: () => {
                // keep silent; user can refresh later
              },
            });
            this.loadUserActivityHeatmap(userId);
            this.loadUserBadgeProgress(userId);
          }, 2500);
        },
        error: () => {
          this.resetMessage.set(this.transloco.translate('dashboard.reset.failed'));
        },
      });
  }

  ngAfterViewInit(): void {
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.updateScrollIndicators());
      const element = this.assignmentContainerElement;
      if (element) {
        this.resizeObserver.observe(element);
      }
    }
    this.scheduleScrollIndicatorUpdate();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.heatmapResizeObserver?.disconnect();
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.updateViewportWidth();
    this.scheduleScrollIndicatorUpdate();
  }

  trackAssignment(index: number, item: AssignmentCardViewModel): number {
    return item.assignment.assignmentId;
  }

  trackBadge(index: number, badge: BadgeProgressItem): string {
    return badge.badgeDefinitionId;
  }

  trackStatCard(index: number, card: ActivityStatCard): string {
    return card.key;
  }

  trackUpcomingBadge(index: number, item: UpcomingBadgeViewModel): string {
    return item.badge.badgeDefinitionId;
  }

  onCardClick(assignment: AssignmentCardViewModel): void {
    this.router.navigate(['/test', assignment.assignment.worksheetId]);
  }

  handleLeftNavigation(): void {
    const container = this.assignmentContainerElement;
    if (!container) {
      return;
    }
    container.scrollBy({ left: -this.scrollDistance, behavior: 'smooth' });
    this.scheduleScrollIndicatorUpdate(250);
  }

  handleRightNavigation(): void {
    const container = this.assignmentContainerElement;
    if (!container) {
      return;
    }
    container.scrollBy({ left: this.scrollDistance, behavior: 'smooth' });
    this.scheduleScrollIndicatorUpdate(250);
  }

  onAssignmentScroll(): void {
    this.updateScrollIndicators();
  }

  activityXAxisTickFormatting = (value: string): string => {
    const parts = value.trim().split(' ');
    if (parts.length < 2) {
      return value;
    }

    const monthName = parts[1];
    if (this.activityLastMonthDisplayed === monthName) {
      return '';
    }

    this.activityLastMonthDisplayed = monthName;
    return monthName;
  };

  activityHeatmapTooltip = (tooltip: any): string => {
    if (!tooltip) {
      return '';
    }

    const cell = tooltip.cell ?? tooltip.data ?? tooltip;
    const extra = cell?.extra ?? {};
    const date = extra.date ? new Date(extra.date) : new Date();
    const dateLabel = extra.label ?? date.toLocaleDateString(this.intlLocale, { day: '2-digit', month: 'short' });
    const duration = this.formatDuration(extra.totalTimeSeconds ?? 0);
    const questionsLabel = this.transloco.translate('dashboard.tooltip.questions');
    const correctLabel = this.transloco.translate('dashboard.tooltip.correct');
    const durationLabel = this.transloco.translate('dashboard.tooltip.duration');
    const activityScoreLabel = this.transloco.translate('dashboard.tooltip.activityScore');

    return `${dateLabel}\n${questionsLabel}: ${extra.questionCount ?? 0}\n${correctLabel}: ${
      extra.correctCount ?? 0
    }\n${durationLabel}: ${duration}\n${activityScoreLabel}: ${cell?.value ?? 0}`;
  };

  private mapToTest(assignment: AssignedWorksheet): Test {
    return {
      id: assignment.worksheetId,
      name: assignment.name,
      description: assignment.description,
      gradeId: assignment.gradeId,
      maxDurationSeconds: assignment.maxDurationSeconds,
      isPracticeTest: assignment.isPracticeTest,
      imageUrl: assignment.imageUrl ?? undefined,
      subtitle: assignment.subtitle ?? undefined,
      badgeText: assignment.badgeText ?? undefined,
      bookId: assignment.bookId ?? undefined,
      bookTestId: assignment.bookTestId ?? undefined,
      questionCount: assignment.questionCount,
      subjectId: assignment.subjectId ?? undefined,
      topicId: assignment.topicId ?? undefined,
      subTopicId: assignment.subTopicId ?? undefined,
    };
  }

  private loadUserActivityHeatmap(userId: number): void {
    if (!userId || userId <= 0) {
      return;
    }

    this.activityApiLoading.set(true);
    this.activityApiError.set(false);
    this.activityNumberCardData.set([]);

    this.badgeService
      .getUserActivity(userId)
      .pipe(finalize(() => this.activityApiLoading.set(false)))
      .subscribe({
        next: (response) => {
          this.activityLastMonthDisplayed = '';
          this.activityDataFromApi.set(this.transformActivityToHeatmap(response));
          this.activityNumberCardData.set(this.buildNumberCardData(response));
        },
        error: (error) => {
          console.error('Öğrenci aktivite verisi alınamadı', error);
          this.activityApiError.set(true);
          this.activityDataFromApi.set([]);
          this.activityNumberCardData.set([]);
        },
      });
  }

  private loadUserBadgeProgress(userId: number): void {
    if (!userId || userId <= 0) {
      return;
    }

    this.badgeProgressLoading.set(true);
    this.badgeProgressError.set(false);
    this.earnedBadges.set([]);
    this.allBadgeProgress.set([]);

    this.badgeService
      .getUserBadgeProgress(userId)
      .pipe(finalize(() => this.badgeProgressLoading.set(false)))
      .subscribe({
        next: (response) => {
          // Tek response, iki signal: earnedBadges (kazanılan) + allBadgeProgress (upcomingBadges computed'ı buradan türer).
          const all = response?.badgeProgress ?? [];
          const earned = all
            .filter((badge) => badge.isCompleted && !!badge.earnedDateUtc)
            .sort((a, b) => {
              const aTime = a.earnedDateUtc ? new Date(a.earnedDateUtc).getTime() : 0;
              const bTime = b.earnedDateUtc ? new Date(b.earnedDateUtc).getTime() : 0;
              return bTime - aTime;
            });
          this.allBadgeProgress.set(all);
          this.earnedBadges.set(earned);
        },
        error: (error) => {
          console.error('Kullanıcı rozeti bilgisi alınamadı', error);
          this.badgeProgressError.set(true);
          this.earnedBadges.set([]);
          this.allBadgeProgress.set([]);
        },
      });
  }

  /** Hata durumunda sessizce yutulur: chip render edilmez, sadece loglanır. */
  private loadLastLogin(): void {
    this.studentService.getLastLogin().subscribe({
      next: (response) => this.lastLoginAtUtc.set(response?.lastLoginAtUtc ?? null),
      error: (error) => {
        console.error('Son giriş bilgisi alınamadı', error);
        this.lastLoginAtUtc.set(null);
      },
    });
  }

  /** "X gün önce" / "X saat önce" / "X dakika önce" — ekstra kütüphane olmadan basit fark. */
  private formatLastLogin(isoUtc: string | null): string | null {
    if (!isoUtc) {
      return null;
    }

    const then = new Date(isoUtc).getTime();
    if (!Number.isFinite(then)) {
      return null;
    }

    const diffMs = Math.max(Date.now() - then, 0);
    const minutes = Math.floor(diffMs / 60_000);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (days >= 1) {
      return this.transloco.translate('dashboard.lastLogin.daysAgo', { count: days });
    }
    if (hours >= 1) {
      return this.transloco.translate('dashboard.lastLogin.hoursAgo', { count: hours });
    }
    if (minutes >= 1) {
      return this.transloco.translate('dashboard.lastLogin.minutesAgo', { count: minutes });
    }
    return this.transloco.translate('dashboard.lastLogin.justNow');
  }

  private scheduleScrollIndicatorUpdate(delay: number = 0): void {
    if (delay > 0) {
      setTimeout(() => this.updateScrollIndicators(), delay);
    } else {
      setTimeout(() => this.updateScrollIndicators(), 0);
    }
  }

  private updateScrollIndicators(): void {
    const container = this.assignmentContainerElement;
    if (!container) {
      this.canScrollLeft.set(false);
      this.canScrollRight.set(false);
      return;
    }

    const maxScrollLeft = container.scrollWidth - container.clientWidth;
    if (maxScrollLeft <= 0) {
      this.canScrollLeft.set(false);
      this.canScrollRight.set(false);
      return;
    }

    const currentScroll = container.scrollLeft;
    const threshold = 2;
    this.canScrollLeft.set(currentScroll > threshold);
    this.canScrollRight.set(currentScroll < maxScrollLeft - threshold);
  }

  private get assignmentContainerElement(): HTMLDivElement | undefined {
    return this.assignmentContainerRef?.nativeElement;
  }

  private transformActivityToHeatmap(response: UserActivityResponse): Array<{ name: string; series: any[] }> {
    const totalWeeks = 52;
    const daysPerWeek = 7;

    const rawEndDate = response?.endDateUtc ? this.normalizeDate(response.endDateUtc) : this.normalizeDate(new Date());
    const alignedEndDate = this.endOfWeek(rawEndDate);
    const alignedStartDate = this.addDays(alignedEndDate, -(totalWeeks * daysPerWeek - 1));

    const activityMap = new Map<string, (typeof response.days)[number]>();
    (response?.days ?? []).forEach((day) => {
      activityMap.set(this.toIsoDateKey(this.normalizeDate(day.dateUtc)), day);
    });

    const heatmap: Array<{ name: string; series: any[] }> = [];

    for (let weekIndex = 0; weekIndex < totalWeeks; weekIndex++) {
      const weekStart = this.addDays(alignedStartDate, weekIndex * daysPerWeek);
      const weekSeries = Array.from({ length: daysPerWeek }, (_, dayOffset) => {
        const currentDate = this.addDays(weekStart, dayOffset);
        const key = this.toIsoDateKey(currentDate);
        const dayActivity = activityMap.get(key);
        const activityScore = dayActivity?.activityScore ?? 0;

        if (currentDate > rawEndDate) {
          return null;
        }

        return {
          name: this.formatDayLabel(currentDate),
          value: activityScore,
          extra: {
            date: currentDate.toISOString(),
            label: currentDate.toLocaleDateString(this.intlLocale, { day: '2-digit', month: 'short' }),
            questionCount: dayActivity?.questionCount ?? 0,
            correctCount: dayActivity?.correctCount ?? 0,
            totalTimeSeconds: dayActivity?.totalTimeSeconds ?? 0,
            totalPoints: dayActivity?.totalPoints ?? 0,
          },
        };
      }).filter((entry): entry is { name: string; value: number; extra: any } => entry !== null);

      heatmap.push({
        name: this.formatWeekLabel(weekStart),
        series: weekSeries.slice().reverse(),
      });
    }

    return heatmap;
  }

  private buildNumberCardData(response: UserActivityResponse): ActivityStatCard[] {
    const totals = (response?.days ?? []).reduce(
      (acc, day) => {
        const questionCount = day?.questionCount ?? 0;
        const correctCount = day?.correctCount ?? 0;
        const totalSeconds = day?.totalTimeSeconds ?? 0;
        const activityScore = day?.activityScore ?? 0;

        acc.questions += questionCount;
        acc.correct += correctCount;
        acc.timeSeconds += totalSeconds;
        acc.activityScore += activityScore;
        return acc;
      },
      { questions: 0, correct: 0, timeSeconds: 0, activityScore: 0 }
    );

    const totalMinutes = totals.timeSeconds > 0 ? Math.max(1, Math.round(totals.timeSeconds / 60)) : 0;

    return [
      {
        key: 'questions',
        icon: 'quiz',
        name: this.transloco.translate('dashboard.activity.totalQuestions'),
        value: totals.questions,
      },
      {
        key: 'correct',
        icon: 'check_circle',
        name: this.transloco.translate('dashboard.activity.correctAnswers'),
        value: totals.correct,
      },
      {
        key: 'minutes',
        icon: 'schedule',
        name: this.transloco.translate('dashboard.activity.studyMinutes'),
        value: totalMinutes,
      },
      {
        key: 'activityScore',
        icon: 'bolt',
        name: this.transloco.translate('dashboard.activity.activityScore'),
        value: totals.activityScore,
      },
    ];
  }

  private addDays(date: Date, days: number): Date {
    const result = new Date(date);
    result.setDate(result.getDate() + days);
    result.setHours(0, 0, 0, 0);
    return result;
  }

  private normalizeDate(date: string | Date): Date {
    const normalized = new Date(date);
    normalized.setHours(0, 0, 0, 0);
    return normalized;
  }

  private toIsoDateKey(date: Date): string {
    const utc = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
    return new Date(utc).toISOString().split('T')[0];
  }

  private formatDayLabel(date: Date): string {
    return date.toLocaleDateString(this.intlLocale, { weekday: 'short' });
  }

  private formatWeekLabel(weekStart: Date): string {
    return weekStart.toLocaleDateString(this.intlLocale, { day: '2-digit', month: 'short' });
  }

  private formatDuration(totalSeconds: number): string {
    if (!totalSeconds) {
      return this.transloco.translate('dashboard.duration.seconds', { seconds: 0 });
    }

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;

    if (minutes && seconds) {
      return this.transloco.translate('dashboard.duration.minutesSeconds', { minutes, seconds });
    }

    if (minutes) {
      return this.transloco.translate('dashboard.duration.minutes', { minutes });
    }

    return this.transloco.translate('dashboard.duration.seconds', { seconds });
  }

  private endOfWeek(date: Date): Date {
    const target = new Date(date);
    const distanceToSunday = (7 - target.getDay()) % 7;
    return this.addDays(target, distanceToSunday);
  }

  private getUserIdFromLocalStorage(): number | null {
    if (typeof window === 'undefined') {
      return null;
    }

    try {
      const stored = window.localStorage.getItem('user');
      if (!stored) {
        return null;
      }

      const parsed = JSON.parse(stored);
      const userId = Number(parsed?.id);
      return Number.isFinite(userId) && userId > 0 ? userId : null;
    } catch (error) {
      console.warn('DashboardComponent: localStorage user verisi okunamadı', error);
      return null;
    }
  }

  private updateViewportWidth(): void {
    if (typeof window === 'undefined') {
      return;
    }

    this.viewportWidth.set(window.innerWidth);
  }
}
