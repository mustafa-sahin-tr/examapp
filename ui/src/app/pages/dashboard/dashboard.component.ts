import { MatButtonModule } from '@angular/material/button';
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
  effect,
  inject,
  signal,
  untracked,
  ChangeDetectionStrategy
} from '@angular/core';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CompactTestCardComponent } from '../../shared/components/compact-test-card/compact-test-card.component';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { AssignedWorksheet } from '../../models/assignment';
import { Test } from '../../models/test-instance';
import {
  BadgeProgressItem,
  BadgeProgressSummary,
  BadgeService,
  UserActivityDay,
  UserActivityResponse,
} from '../../services/badge.service';
import { RecentWeeksComponent, dayKey } from './recent-weeks.component';
import { finalize } from 'rxjs';
import { StudentResetService } from '../../services/student-reset.service';
import { StudentService } from '../../services/student.service';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LocaleService } from '../../services/locale.service';
import { FlashcardService } from '../../services/flashcard.service';
import { PracticeService } from '../../services/practice.service';
import { DailySet, DailyStartScope } from '../../models/practice';
import { DailyQuestionsCardComponent } from '../../shared/components/daily-questions-card/daily-questions-card.component';
import { CoachGoalsCardComponent } from '../../shared/components/coach-goals-card/coach-goals-card.component';
import { dailyStreakFrom } from '../../shared/utils/daily-streak.util';
import { currentUserId } from '../../shared/utils/current-user-id.util';
import { stripInvisibleControls } from '../../shared/utils/display-text.util';
import { BadgeMedallionComponent } from '../../shared/components/badge-medallion/badge-medallion.component';
import {
  BadgeMedallionState,
  badgeAriaLabel,
  badgeStateText,
  deriveBadgeState,
} from '../../shared/components/badge-medallion/badge-state.util';
import { SchoolPendingBannerComponent } from '../../shared/components/school-pending-banner/school-pending-banner.component';
import { ParentStatusCardComponent } from '../../shared/components/parent-status-card/parent-status-card.component';

interface AssignmentCardViewModel {
  assignment: AssignedWorksheet;
  test: Test;
}

/** Issue #721: öğrenci ana sayfasının "sıradaki iş" alanı. */
interface NextStep {
  kind: 'dueSoon' | 'review' | 'assignment' | 'practice';
  name: string;
  count: number;
  assignment: AssignmentCardViewModel | null;
}

const NEXT_STEP_DUE_SOON_MS = 2 * 24 * 60 * 60 * 1000;

function dueTime(vm: AssignmentCardViewModel): number {
  return vm.assignment.endAt ? new Date(vm.assignment.endAt).getTime() : Number.MAX_SAFE_INTEGER;
}

/**
 * Issue #197 / #835 — aktivite özet kartı (son 4 hafta: soru / doğru oranı / dakika). `display` verilirse değer yerine
 * gösterilir (ör. "%8"), `detail` küçük ek satırdır (ör. "2 / 25").
 */
export interface ActivityStatCard {
  key: 'questions' | 'accuracy' | 'minutes';
  icon: string;
  name: string;
  value: number;
  display?: string;
  detail?: string;
}

/** #835: panelde istatistik ve ızgara son 4 haftayı (28 gün) kapsar. */
export const RECENT_ACTIVITY_DAYS = 28;

/** "Sıradaki Rozetler" listesi için görünüm modeli — ilerleme yüzdesi şablonda hesaplanmasın diye burada türetilir. */
interface UpcomingBadgeViewModel {
  badge: BadgeProgressItem;
  remaining: number;
  percent: number;
  /** Issue #149: `in-progress` ya da `locked` (currentValue = 0). */
  state: BadgeMedallionState;
  /** Her zaman görünen durum metni: "180 / 250" ya da "Kilitli · 0 / 500". */
  stateText: string;
}

/** Issue #149 — "Kazanılan Rozetler" ızgarası: 56px medalyon + erişilebilir ad + tooltip (hover ve odak). */
export interface EarnedBadgeViewModel {
  badge: BadgeProgressItem;
  state: BadgeMedallionState;
  ariaLabel: string;
  tooltip: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    MatButtonModule,
    CommonModule,
    MatIconModule,
    MatTooltipModule,
    BadgeMedallionComponent,
    CompactTestCardComponent,
    SectionHeaderComponent,
    RecentWeeksComponent,
    TranslocoPipe,
    DailyQuestionsCardComponent,
    CoachGoalsCardComponent,
    SchoolPendingBannerComponent,
    ParentStatusCardComponent,
  ],
  templateUrl: './dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
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
  private readonly practiceService = inject(PracticeService);
  private readonly flashcardService = inject(FlashcardService);
  private readonly auth = inject(AuthService);

  /**
   * Rapor uçları (`reports/users/{id}`) için kullanıcı id'si (#556). Girişten hemen sonra profil henüz yazılmamış
   * olabilir; id gelene kadar istek atılmaz, gelince bir kez yüklenir. Kayıt sayfaları localStorage'a doğrudan
   * yazdığı için oradaki id de okunur.
   */
  private readonly profileUserId = computed(() => {
    const id = Number(this.auth.user()?.id);
    return Number.isFinite(id) && id > 0 ? id : null;
  });
  private reportsLoadedFor: number | null = null;

  private readonly loadReportsWhenUserKnown = effect(() => {
    this.profileUserId();
    untracked(() => this.loadReportsIfUserKnown());
  });

  /** `toLocaleDateString` gibi Intl API'lerine verilecek aktif dil etiketi (örn. 'tr', 'en-US'). */
  private get intlLocale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  private readonly assignments = signal<AssignmentCardViewModel[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  /** #835: son 4 haftanın günleri (ızgara ve istatistik). */
  readonly recentActivityDays = signal<UserActivityDay[]>([]);
  readonly activityApiLoading = signal(false);
  readonly activityApiError = signal(false);
  readonly activityNumberCardData = signal<ActivityStatCard[]>([]);
  readonly badgeProgressLoading = signal(false);
  readonly badgeProgressError = signal(false);
  readonly earnedBadges = signal<BadgeProgressItem[]>([]);
  /** Ham rozet ilerleme listesi (tamamlanan + tamamlanmayan); earnedBadges ve upcomingBadges buradan türer. */
  readonly allBadgeProgress = signal<BadgeProgressItem[]>([]);
  /** Rozet ilerleme cevabının özeti; günlük seri (`currentActivityStreak`) buradan okunur (#99). */
  readonly badgeSummary = signal<BadgeProgressSummary | null>(null);
  readonly resetInProgress = signal(false);
  readonly resetMessage = signal<string | null>(null);

  /** Issue #126 — önceki giriş zamanı; null ise chip hiç render edilmez. */
  readonly lastLoginAtUtc = signal<string | null>(null);
  readonly lastLoginLabel = computed(() => this.formatLastLogin(this.lastLoginAtUtc()));
  /** #835: karşılamada adın ilk kelimesi ("Hoş geldin, Pilot"); ad yoksa yalnız "Hoş geldin". */
  readonly welcomeName = computed(() => (this.auth.user()?.fullName ?? '').trim().split(/\s+/)[0] ?? '');

  /**
   * Issue #99 — "Günün soruları" kartı. Kendi yükleniyor/hata durumuna sahiptir; diğer bölümleri etkilemez.
   * Seri, zaten çekilen rozet ilerlemesindeki `DailyStreak` rozetlerinden türetilir (yoksa null → chip gizli).
   */
  readonly dailySet = signal<DailySet | null>(null);
  readonly dailyLoading = signal(true);
  readonly dailyError = signal(false);
  readonly dailyStreak = computed(() => dailyStreakFrom(this.badgeSummary(), this.allBadgeProgress())?.count ?? null);

  readonly upcomingBadgesLimit = 3;
  readonly upcomingBadges = computed<UpcomingBadgeViewModel[]>(() => {
    // CR U5: sayılar aktif dile göre biçimlenir (1.250 / 1,250).
    const numberFormat = new Intl.NumberFormat(this.intlLocale);
    return this.allBadgeProgress()
      .filter((badge) => !badge.isCompleted)
      .map((badge) => {
        const target = Math.max(badge.targetValue ?? 0, 0);
        const current = Math.min(Math.max(badge.currentValue ?? 0, 0), target);
        const state = deriveBadgeState(badge);
        return {
          badge,
          remaining: target - current,
          percent: target > 0 ? Math.round((current / target) * 100) : 0,
          state,
          stateText: badgeStateText((key, params) => this.transloco.translate<string>(key, params), {
            state,
            current: numberFormat.format(badge.currentValue ?? 0),
            target: numberFormat.format(badge.targetValue ?? 0),
            locale: this.intlLocale,
          }),
        };
      })
      .sort((a, b) => a.remaining - b.remaining)
      .slice(0, this.upcomingBadgesLimit);
  });
  readonly earnedBadgeViews = computed<EarnedBadgeViewModel[]>(() => {
    const now = Date.now();
    const locale = this.intlLocale;
    const translate = (key: string, params?: Record<string, unknown>) => this.transloco.translate<string>(key, params);
    return this.earnedBadges().map((badge) => {
      const state = deriveBadgeState(badge, now);
      // Sec D1: tooltip/aria düz metin yüzeyi (<bdi> yok) — bidi/sıfır genişlikli kontroller silinir.
      const name = stripInvisibleControls(badge.name ?? '').trim();
      const description = stripInvisibleControls(badge.description ?? '').trim();
      const lines = [name];
      if (state === 'new') {
        lines.push(translate('dashboard.badges.newEarned'));
      }
      if (description) {
        lines.push(description);
      }
      return {
        badge,
        state,
        ariaLabel: badgeAriaLabel(translate, {
          state,
          name,
          current: badge.currentValue,
          target: badge.targetValue,
          earnedDateUtc: badge.earnedDateUtc,
          locale,
        }),
        tooltip: lines.filter((line) => !!line).join('\n'),
      };
    });
  });
  readonly allBadgesCompleted = computed(
    () => this.allBadgeProgress().length > 0 && this.upcomingBadges().length === 0
  );

  readonly assignmentCards = computed(() => this.assignments());
  /** Issue #529: tekrar zamanı gelen soru sayısı. */
  readonly dueReviewCount = signal(0);
  /** Issue #699: bugün gelen bilgi kartı sayısı. */
  readonly dueFlashcardCount = signal(0);
  readonly hasActivityStats = computed(() => this.activityNumberCardData().some((card) => card.value > 0));

  /**
   * Issue #721: öğrencinin tek sıradaki işi. Sıra: teslimi yaklaşan ödev → günlük set (kendi kartı var, burada çizilmez) →
   * tekrar zamanı gelen sorular → sıradaki ödev → serbest pratik.
   */
  readonly nextStep = computed<NextStep | null>(() => {
    if (this.loading() || this.dailyLoading()) {
      return null;
    }
    const assignments = [...this.assignments()].sort((a, b) => dueTime(a) - dueTime(b));
    const first = assignments[0];
    if (first && dueTime(first) - Date.now() <= NEXT_STEP_DUE_SOON_MS) {
      return { kind: 'dueSoon', name: first.test.name, count: 0, assignment: first };
    }
    const status = this.dailySet()?.status;
    if (status === 'NotStarted' || status === 'InProgress') {
      return null;
    }
    if (this.dueReviewCount() > 0) {
      return { kind: 'review', name: '', count: this.dueReviewCount(), assignment: null };
    }
    if (first) {
      return { kind: 'assignment', name: first.test.name, count: 0, assignment: first };
    }
    return { kind: 'practice', name: '', count: 0, assignment: null };
  });
  readonly canScrollLeft = signal(false);
  readonly canScrollRight = signal(false);
  readonly scrollDistance = 600;
  private resizeObserver?: ResizeObserver;
  private assignmentContainerRef?: ElementRef<HTMLDivElement>;

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

  ngOnInit(): void {
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

    this.loadReportsIfUserKnown();
    this.loadLastLogin();
    this.loadDailySet();
    this.practiceService.getDueReview().subscribe({
      next: (due) => this.dueReviewCount.set(due.totalCount),
      // Alan isteğe bağlı: hata ana sayfayı etkilemez.
      error: () => this.dueReviewCount.set(0),
    });
    this.flashcardService.getDueCount().subscribe({
      next: (count) => this.dueFlashcardCount.set(count),
      // Alan isteğe bağlı: hata ana sayfayı etkilemez.
      error: () => this.dueFlashcardCount.set(0),
    });
  }

  /** Yalnız kartın isteğini (yeniden) yapar; "Tekrar dene" de bunu çağırır. */
  loadDailySet(): void {
    this.dailyLoading.set(true);
    this.dailyError.set(false);
    this.practiceService.getDailySet().subscribe({
      next: (set) => {
        this.dailySet.set(set);
        this.dailyLoading.set(false);
      },
      error: (error) => {
        console.error('Günün soruları alınamadı', error);
        this.dailyError.set(true);
        this.dailyLoading.set(false);
      },
    });
  }

  /** #66: karttan gelen kapsam (ders/konu/alt konu) practice sayfasına query param olarak taşınır; null = rastgele. */
  onDailyStart(scope: DailyStartScope | null = null): void {
    const queryParams: Record<string, number> = { daily: 1 };
    if (scope) {
      queryParams['subjectId'] = scope.subjectId;
      if (scope.topicId != null) queryParams['topicId'] = scope.topicId;
      if (scope.topicId != null && scope.subTopicId != null) queryParams['subTopicId'] = scope.subTopicId;
    }
    void this.router.navigate(['/practice'], { queryParams });
  }

  onDailyFreePractice(): void {
    void this.router.navigate(['/practice']);
  }

  onDailyGoToProfile(): void {
    void this.router.navigate(['/student-profile']);
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
    this.recentActivityDays.set([]);
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
          const userId = this.reportUserId();
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
            if (userId !== null) {
              this.loadUserActivity(userId);
              this.loadUserBadgeProgress(userId);
            }
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
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.scheduleScrollIndicatorUpdate();
  }

  trackAssignment(index: number, item: AssignmentCardViewModel): number {
    return item.assignment.assignmentId;
  }

  trackBadge(index: number, view: EarnedBadgeViewModel): string {
    return view.badge.badgeDefinitionId;
  }

  trackStatCard(index: number, card: ActivityStatCard): string {
    return card.key;
  }

  trackUpcomingBadge(index: number, item: UpcomingBadgeViewModel): string {
    return item.badge.badgeDefinitionId;
  }

  onNextStep(step: NextStep): void {
    if (step.assignment) {
      this.onCardClick(step.assignment);
      return;
    }
    void this.router.navigate(['/practice']);
  }

  onFlashcardReview(): void {
    void this.router.navigate(['/flashcards/review']);
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
      kind: assignment.kind, // issue #573
    };
  }

  /** Kullanıcı id'si biliniyorsa raporları o id için bir kez yükler (#556). */
  /** Profil signal'ı, yoksa localStorage (signal değil; her çağrıda taze okunur). */
  private reportUserId(): number | null {
    return this.profileUserId() ?? currentUserId();
  }

  private loadReportsIfUserKnown(): void {
    const userId = this.reportUserId();
    if (userId === null || userId === this.reportsLoadedFor) {
      return;
    }
    this.reportsLoadedFor = userId;
    this.loadUserActivity(userId);
    this.loadUserBadgeProgress(userId);
  }

  private loadUserActivity(userId: number): void {
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
          const recent = recentDays(response, RECENT_ACTIVITY_DAYS);
          this.recentActivityDays.set(recent);
          this.activityNumberCardData.set(this.buildNumberCardData(recent));
        },
        error: (error) => {
          console.error('Öğrenci aktivite verisi alınamadı', error);
          this.activityApiError.set(true);
          this.recentActivityDays.set([]);
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
    this.badgeSummary.set(null);

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
          this.badgeSummary.set(response?.summary ?? null);
          this.earnedBadges.set(earned);
        },
        error: (error) => {
          console.error('Kullanıcı rozeti bilgisi alınamadı', error);
          this.badgeProgressError.set(true);
          this.earnedBadges.set([]);
          this.allBadgeProgress.set([]);
          this.badgeSummary.set(null);
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

  private buildNumberCardData(days: readonly UserActivityDay[]): ActivityStatCard[] {
    const totals = days.reduce(
      (acc, day) => {
        acc.questions += day?.questionCount ?? 0;
        acc.correct += day?.correctCount ?? 0;
        acc.timeSeconds += day?.totalTimeSeconds ?? 0;
        return acc;
      },
      { questions: 0, correct: 0, timeSeconds: 0 }
    );

    const totalMinutes = totals.timeSeconds > 0 ? Math.max(1, Math.round(totals.timeSeconds / 60)) : 0;
    const numberFormat = new Intl.NumberFormat(this.intlLocale);
    // #835: doğru sayısı tek başına anlamsız ("2"); oran + "2 / 25".
    const accuracy = totals.questions > 0 ? Math.round((totals.correct / totals.questions) * 100) : 0;

    return [
      {
        key: 'questions',
        icon: 'quiz',
        name: this.transloco.translate('dashboard.activity.totalQuestions'),
        value: totals.questions,
      },
      {
        key: 'accuracy',
        icon: 'check_circle',
        name: this.transloco.translate('dashboard.activity.accuracy'),
        value: accuracy,
        display: new Intl.NumberFormat(this.intlLocale, { style: 'percent' }).format(accuracy / 100),
        detail: this.transloco.translate('dashboard.activity.correctOf', {
          correct: numberFormat.format(totals.correct),
          total: numberFormat.format(totals.questions),
        }),
      },
      {
        key: 'minutes',
        icon: 'schedule',
        name: this.transloco.translate('dashboard.activity.studyMinutes'),
        value: totalMinutes,
      },
    ];
  }
}

/** #835: yanıttan son `count` günün kayıtları (bugün dahil). */
export function recentDays(response: UserActivityResponse | null | undefined, count: number, now: Date = new Date()): UserActivityDay[] {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (count - 1));
  const from = dayKey(start);
  return (response?.days ?? []).filter((day) => (day?.dateUtc ?? '').slice(0, 10) >= from);
}
