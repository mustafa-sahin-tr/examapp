import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { signal } from '@angular/core';
import { of, Subject, throwError } from 'rxjs';

import { AdminDashboardComponent } from './admin-dashboard.component';
import { AdminService } from '../../../services/admin.service';
import { AdminPendingService } from '../../../services/admin-pending.service';
import { AdminPendingSummary } from '../../../models/admin-pending.model';
import { AdminDashboardSummary, AdminDashboardTrendPoint, AdminDashboardTrends, } from '../../../models/admin-dashboard.model';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import { DASHBOARD_TIME_ZONE, dashboardIsoDate } from '../../../shared/utils/dashboard-time-zone.util';
import { LocaleService } from '../../../services/locale.service';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('AdminDashboardComponent', () => {
    let fixture: ComponentFixture<AdminDashboardComponent>;
    let component: AdminDashboardComponent;
    let adminService: MockedObject<AdminService>;
    let pendingSummary: ReturnType<typeof signal<AdminPendingSummary | null>>;
    let pendingRefresh: ReturnType<typeof vi.fn>;

    const summary: AdminDashboardSummary = {
        teacherCount: 12,
        studentCount: 340,
        worksheetCount: 87,
        questionCount: 500,
        aiClassifiedQuestionCount: 375,
        aiClassifiedRatio: 0.75,
    };

    /**
     * Bugünden (dashboard saat dilimi, #265) geriye `days` günlük, `yyyy-MM-dd` tarihli seri; `counts[i]` i. güne
     * (en eski → en yeni). Bileşenle aynı gün tanımı: tarayıcının saat dilimi değil, `DASHBOARD_TIME_ZONE`.
     */
    function makeSeries(days: number, counts: (index: number) => number, endIsoDate = dashboardIsoDate(new Date())) {
        const [y, m, d] = endIsoDate.split('-').map(Number);
        return Array.from({ length: days }, (_, i): AdminDashboardTrendPoint => {
            const day = new Date(Date.UTC(y, m - 1, d - (days - 1 - i)));
            return { date: day.toISOString().slice(0, 10), count: counts(i) };
        });
    }

    /** 8 hafta × 7 gün — bileşenin backend'den istediği pencere (#887). */
    const TREND_DAYS = 56;

    const trends: AdminDashboardTrends = {
        questionCreated: makeSeries(TREND_DAYS, (i) => (i === TREND_DAYS - 3 ? 9 : 1)), // tepe: 2 gün önce, 9 soru
        questionSolved: makeSeries(TREND_DAYS, (i) => (i === TREND_DAYS - 1 ? 5 : 2)), // tepe: bugün, 5 soru
        studentLogin: makeSeries(TREND_DAYS, (i) => (i === TREND_DAYS - 2 ? 14 : 3)), // tepe: dün, 14 giriş
    };

    const emptyTrends: AdminDashboardTrends = {
        questionCreated: makeSeries(TREND_DAYS, () => 0),
        questionSolved: makeSeries(TREND_DAYS, () => 0),
        studentLogin: makeSeries(TREND_DAYS, () => 0),
    };

    /** Login grafiğinin eksen yoğunluğu viewport'a bağlı; testleri masaüstü düzenine sabitle. */
    function useDesktopViewport(): void {
        component.viewportWidth.set(1280);
        fixture.detectChanges();
    }

    function configure(): ComponentFixture<AdminDashboardComponent> {
        adminService = {
            getDashboardSummary: vi.fn().mockName("AdminService.getDashboardSummary"),
            getDashboardTrends: vi.fn().mockName("AdminService.getDashboardTrends")
        } as unknown as MockedObject<AdminService>;
        adminService.getDashboardSummary.mockReturnValue(of(summary));
        adminService.getDashboardTrends.mockReturnValue(of(trends));
        pendingSummary = signal<AdminPendingSummary | null>(null);
        pendingRefresh = vi.fn(() => of(null));

        TestBed.configureTestingModule({
            imports: [AdminDashboardComponent, translocoTestingModule({
                    langs: { 'admin/tr': adminTr },
                    // app.config.ts ile aynı: tireli scope önekleri camelCase'e çevrilmez.
                    translocoConfig: { scopes: { keepCasing: true } },
                })],
            providers: [
                { provide: AdminService, useValue: adminService },
                { provide: AdminPendingService, useValue: { summary: pendingSummary, refresh: pendingRefresh } },
                provideRouter([]),
                provideNoopAnimations(),
            ],
        });

        return TestBed.createComponent(AdminDashboardComponent);
    }

    /*
     * Issue #285: renk token'ları styles.scss'te yalnızca `.dark-theme` / `.light-theme` altında tanımlı; uygulamada
     * index.html'deki inline script <html>'e bu class'ı ekler, Karma'nın context.html'i eklemez. Token'ları test
     * içinde <html> inline stiline sabitleyerek stil/tema yüklemesinden bağımsız hale getiriyoruz. Değerler
     * test girdisidir (tema paletini temsil etmez); formatları üretimdekiyle aynıdır (6 haneli hex).
     */
    const TEST_COLOR_TOKENS: Record<string, string> = {
        '--primaryColor': '#1c43fe',
        '--ms-success-text-medium': '#02bb71',
    };

    beforeEach(() => {
        for (const [name, value] of Object.entries(TEST_COLOR_TOKENS)) {
            document.documentElement.style.setProperty(name, value);
        }
    });

    afterEach(() => {
        for (const name of Object.keys(TEST_COLOR_TOKENS)) {
            document.documentElement.style.removeProperty(name);
        }
    });

    // ── Route config: admin dışı kullanıcı erişemez ──────────────────────────

    it('routes_AdminDashboardPath_IsGuardedByAuthAndAdminGuard', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));
        const dashboardRoute = layoutRoute?.children?.find((r) => r.path === 'admin/dashboard');

        expect(dashboardRoute, 'route tanımı bulunamadı').toBeDefined();
        expect(dashboardRoute?.canActivate).toEqual([authGuard, adminGuard]);
    });

    // ── Loading state ─────────────────────────────────────────────────────────

    it('loadSummary_WhileRequestPending_ShowsSkeleton', () => {
        fixture = configure();
        component = fixture.componentInstance;
        const pending$ = new Subject<AdminDashboardSummary>();
        adminService.getDashboardSummary.mockReturnValue(pending$.asObservable());

        fixture.detectChanges(); // ngOnInit -> loadSummary, request stays pending

        expect(component.loading()).toBe(true);

        const skeletonCards = fixture.nativeElement.querySelectorAll('.summary-card--skeleton');
        expect(skeletonCards.length).toBeGreaterThan(0);
        expect(fixture.nativeElement.querySelectorAll('.skeleton-line--label').length).toBeGreaterThan(0);
        expect(fixture.nativeElement.querySelectorAll('.skeleton-line--value').length).toBeGreaterThan(0);
    });

    // ── Success state: 5 sayaç kartı ─────────────────────────────────────────

    it('loadSummary_SuccessfulResponse_RendersFourCounterCardsAndAiCard', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges(); // ngOnInit -> loadSummary

        expect(component.loading()).toBe(false);
        expect(component.error()).toBeNull();
        expect(component.summary()).toEqual(summary);

        const teacherCard = fixture.nativeElement.querySelector('[data-key="teachers"] .summary-value');
        const studentCard = fixture.nativeElement.querySelector('[data-key="students"] .summary-value');
        const worksheetCard = fixture.nativeElement.querySelector('[data-key="worksheets"] .summary-value');
        const questionCard = fixture.nativeElement.querySelector('[data-key="questions"] .summary-value');
        const aiCard = fixture.nativeElement.querySelector('[data-key="ai"]');

        expect(teacherCard.textContent).toContain('12');
        expect(studentCard.textContent).toContain('340');
        expect(worksheetCard.textContent).toContain('87');
        expect(questionCard.textContent).toContain('500');
        expect(aiCard).toBeTruthy();
        expect(aiCard.querySelector('.summary-badge').textContent).toContain('75');
    });

    it('aiPercent_QuestionCountAndAiClassifiedCountGiven_ComputesRatioAsPercentage', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        expect(component.aiPercent()).toBe(75);
    });

    it('cards_SuccessfulResponse_RendersExactlyFourFlatCounterCards', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        expect(component.cards().length).toBe(4);
        expect(component.cards().map((c) => c.key)).toEqual(['teachers', 'students', 'worksheets', 'questions']);
    });

    it('loadSummary_SuccessfulResponse_RendersAiProgressBarWidthMatchingRatio', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        const bar: HTMLElement = fixture.nativeElement.querySelector('.summary-progress__bar');
        expect(bar.style.width).toBe('75%');
    });

    // ── Error state ───────────────────────────────────────────────────────────

    it('loadSummary_RequestFails_ShowsErrorStateWithRetryButton', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardSummary.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();

        expect(component.loading()).toBe(false);
        expect(component.error()).toBe('Özet bilgileri alınırken bir sorun oluştu.');
        expect(component.summary()).toBeNull();

        const errorBox = fixture.nativeElement.querySelector('.state-box--error');
        expect(errorBox).toBeTruthy();
        const retryButton = errorBox.querySelector('button');
        expect(retryButton).toBeTruthy();
    });

    it('retryButtonClick_AfterError_CallsLoadSummaryAgain', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardSummary.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();
        expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);

        adminService.getDashboardSummary.mockReturnValue(of(summary));
        const retryButton: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error button');
        retryButton.click();
        fixture.detectChanges();

        expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(2);
        expect(component.error()).toBeNull();
        expect(component.summary()).toEqual(summary);
    });

    // ── Empty state ───────────────────────────────────────────────────────────

    it('isEmpty_AllCountersZero_ShowsEmptyStateBoxAndCardsShowZero', () => {
        fixture = configure();
        component = fixture.componentInstance;
        const emptySummary: AdminDashboardSummary = {
            teacherCount: 0,
            studentCount: 0,
            worksheetCount: 0,
            questionCount: 0,
            aiClassifiedQuestionCount: 0,
            aiClassifiedRatio: 0,
        };
        adminService.getDashboardSummary.mockReturnValue(of(emptySummary));

        fixture.detectChanges();

        expect(component.isEmpty()).toBe(true);

        const teacherCard = fixture.nativeElement.querySelector('[data-key="teachers"] .summary-value');
        const studentCard = fixture.nativeElement.querySelector('[data-key="students"] .summary-value');
        const worksheetCard = fixture.nativeElement.querySelector('[data-key="worksheets"] .summary-value');
        const questionCard = fixture.nativeElement.querySelector('[data-key="questions"] .summary-value');

        expect(teacherCard.textContent).toContain('0');
        expect(studentCard.textContent).toContain('0');
        expect(worksheetCard.textContent).toContain('0');
        expect(questionCard.textContent).toContain('0');

        const emptyBox = fixture.nativeElement.querySelector('.state-box--empty');
        expect(emptyBox).toBeTruthy();
    });

    it('isEmpty_SomeCountersNonZero_IsFalseAndNoEmptyStateBox', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        expect(component.isEmpty()).toBe(false);
        expect(fixture.nativeElement.querySelector('.state-box--empty')).toBeFalsy();
    });

    // ── AI card: soru yoksa ayırt edici boş hâl ─────────────────────────────

    it('hasQuestions_QuestionCountZero_ShowsNoQuestionsHintInsteadOfRatio', () => {
        fixture = configure();
        component = fixture.componentInstance;
        const noQuestionsSummary: AdminDashboardSummary = {
            teacherCount: 5,
            studentCount: 20,
            worksheetCount: 3,
            questionCount: 0,
            aiClassifiedQuestionCount: 0,
            aiClassifiedRatio: 0,
        };
        adminService.getDashboardSummary.mockReturnValue(of(noQuestionsSummary));

        fixture.detectChanges();

        expect(component.hasQuestions()).toBe(false);

        const aiCard = fixture.nativeElement.querySelector('[data-key="ai"]');
        const hint = aiCard.querySelector('.summary-hint');
        expect(hint).toBeTruthy();
        expect(hint.textContent).toContain('Henüz soru yok');
        expect(aiCard.querySelector('.summary-badge')).toBeFalsy();
        expect(aiCard.querySelector('.summary-progress__bar')).toBeFalsy();
    });

    it('hasQuestions_QuestionCountPositive_IsTrue', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        expect(component.hasQuestions()).toBe(true);
    });

    // ── Phase 2 (#88 → #887): son 8 haftanın haftalık çubukları ─────────────

    it('ngOnInit_Always_RequestsSummaryAndTrendsInParallelWith8Weeks', () => {
        fixture = configure();
        fixture.detectChanges();

        expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);
        expect(adminService.getDashboardTrends).toHaveBeenCalledTimes(1);
        expect(adminService.getDashboardTrends).toHaveBeenCalledWith(56);
    });

    it('loadTrends_WhileRequestPending_ShowsTrendSkeletonWithoutBlockingSummary', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardTrends.mockReturnValue(new Subject<AdminDashboardTrends>().asObservable());

        fixture.detectChanges();

        expect(component.trendLoading()).toBe(true);
        expect(component.loading()).toBe(false);
        expect(fixture.nativeElement.querySelectorAll('.trend-card--skeleton').length).toBe(3); // 2 haftalık + 1 bar chart
        expect(fixture.nativeElement.querySelector('[data-key="teachers"]')).toBeTruthy();
    });

    it('loadTrends_SuccessfulResponse_RendersTwoCardsWithEightWeekBars_issue887', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        expect(component.trendLoading()).toBe(false);
        expect(component.trendError()).toBeNull();

        const cards = component.trendCards();
        expect(cards.map((c) => c.key)).toEqual(['created', 'solved']);
        expect(cards[0].total).toBe(TREND_DAYS - 1 + 9);
        expect(cards[1].total).toBe((TREND_DAYS - 1) * 2 + 5);

        const weeks = cards[0].weeks;
        expect(weeks.length).toBe(8);
        // Her gün tam bir haftaya düşer: hafta toplamları kart toplamına eşit.
        expect(weeks.reduce((acc, w) => acc + w.count, 0)).toBe(cards[0].total);
        // Tepe (2 gün önce, 9 soru) son haftada: 6×1 + 9 = 15 → en yüksek çubuk %100.
        expect(weeks[7].count).toBe(15);
        expect(weeks[7].percent).toBe(100);
        expect(weeks[0].count).toBe(7);
        expect(weeks[0].percent).toBe(47);
        // Etiket aktif dile göre biçimlenir: "10 Ağu" (tr) / "Aug 10" (en)
        expect(weeks[0].label).toMatch(/^(\d{1,2} \S+|\S+ \d{1,2})$/);

        const created: HTMLElement = fixture.nativeElement.querySelector('[data-trend="created"]');
        const bars = created.querySelectorAll('.week-bars__item');
        expect(bars.length).toBe(8);
        expect((bars[7].querySelector('.week-bars__bar') as HTMLElement).style.height).toBe('100%');
        expect(bars[7].querySelector('.week-bars__count')?.textContent?.trim()).toBe('15');
        expect(fixture.nativeElement.querySelector('[data-trend="solved"] .week-bars__bar--solved')).toBeTruthy();
        expect(fixture.nativeElement.querySelector('ngx-charts-heat-map')).toBeFalsy();
    });

    it('trendCards_ShortSeries_MissingDaysCountAsZero_issue887', () => {
        fixture = configure();
        component = fixture.componentInstance;
        const short = makeSeries(10, () => 2);
        adminService.getDashboardTrends.mockReturnValue(of({ questionCreated: short, questionSolved: short, studentLogin: trends.studentLogin }));

        fixture.detectChanges();

        const [created] = component.trendCards();
        expect(created.weeks.map((w) => w.count)).toEqual([0, 0, 0, 0, 0, 0, 6, 14]);
        expect(created.weeks[6].percent).toBe(43);
    });

    it('trendCards_JustAfterTurkeyMidnight_TreatsTurkeyLocalDayAsTodayRegardlessOfBrowserZone', () => {
        // Issue #265: 2026-09-24T22:30Z = 25 Eylül 01:30 (Europe/Istanbul). Backend'in son kovası 2026-09-25;
        // "bugün" o gün olmalı (tarayıcı UTC'de olsa bile 24 Eylül'e kaymamalı).
        vi.useFakeTimers();
        vi.setSystemTime(new Date(Date.UTC(2026, 8, 24, 22, 30)));
        try {
            fixture = configure();
            component = fixture.componentInstance;
            const series = makeSeries(TREND_DAYS, (i) => (i === TREND_DAYS - 1 ? 7 : 1), '2026-09-25');
            adminService.getDashboardTrends.mockReturnValue(of({ questionCreated: series, questionSolved: series, studentLogin: series }));

            fixture.detectChanges();

            const [created] = component.trendCards();
            expect(created.summaryText).toContain('bugün, 7 soru');
            // Son hafta 19–25 Eylül: etiket kovanın ilk günü.
            const locale = TestBed.inject(LocaleService).localeDefinition().angularLocale;
            const firstDay = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(2026, 8, 19)));
            expect(created.weeks[7].label).toBe(firstDay);
        }
        finally {
            vi.useRealTimers();
        }
    });

    it('trendCards_SuccessfulResponse_ExposesAriaLabelAndDescribedBySummary', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();

        const createdCard: HTMLElement = fixture.nativeElement.querySelector('[data-trend="created"]');
        expect(createdCard.getAttribute('aria-label')).toContain('Soru Oluşturma');
        expect(createdCard.getAttribute('aria-label')).toContain('son 8 hafta');

        const describedBy = createdCard.getAttribute('aria-describedby');
        expect(describedBy).toBe('trend-summary-created');
        const summaryEl: HTMLElement = fixture.nativeElement.querySelector(`#${describedBy}`);
        expect(summaryEl).toBeTruthy();
        expect(summaryEl.textContent).toContain('Son 8 hafta');
        expect(summaryEl.textContent).toContain(`toplam ${TREND_DAYS - 1 + 9} soru oluşturuldu`);
        expect(summaryEl.textContent).toContain('2 gün önce, 9 soru');

        const solvedSummary: HTMLElement = fixture.nativeElement.querySelector('#trend-summary-solved');
        expect(solvedSummary.textContent).toContain('bugün, 5 soru');
    });

    it('loadTrends_RequestFails_ShowsTrendErrorOnlyAndKeepsSummaryCards', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardTrends.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();

        // Trend bölümü hata
        expect(component.trendLoading()).toBe(false);
        expect(component.trendError()).toBe('Trend verileri alınırken bir sorun oluştu.');
        expect(component.trends()).toBeNull();
        const trendError = fixture.nativeElement.querySelector('.state-box--error[data-section="trend"]');
        expect(trendError).toBeTruthy();
        expect(trendError.querySelector('button')).toBeTruthy();
        expect(fixture.nativeElement.querySelectorAll('.week-bars').length).toBe(0);

        // Sayaç bölümü etkilenmedi
        expect(component.error()).toBeNull();
        expect(component.summary()).toEqual(summary);
        expect(fixture.nativeElement.querySelector('[data-key="teachers"] .summary-value').textContent).toContain('12');
        expect(fixture.nativeElement.querySelectorAll('.state-box--error').length).toBe(1);
    });

    it('trendRetryButtonClick_AfterError_CallsLoadTrendsAgainOnly', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardTrends.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();
        expect(adminService.getDashboardTrends).toHaveBeenCalledTimes(1);

        adminService.getDashboardTrends.mockReturnValue(of(trends));
        const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error[data-section="trend"] button');
        retry.click();
        fixture.detectChanges();

        expect(adminService.getDashboardTrends).toHaveBeenCalledTimes(2);
        expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);
        expect(component.trendError()).toBeNull();
        expect(fixture.nativeElement.querySelectorAll('.week-bars').length).toBe(2);
    });

    it('loadSummary_RequestFailsButTrendsSucceed_StillRendersCharts', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardSummary.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();

        expect(component.error()).not.toBeNull();
        expect(component.trendError()).toBeNull();
        expect(fixture.nativeElement.querySelectorAll('.week-bars').length).toBe(2);
    });

    it('trendCards_AllCountsZero_ShowsNotEnoughDataInsteadOfChart', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardTrends.mockReturnValue(of(emptyTrends));

        fixture.detectChanges();

        const cards = component.trendCards();
        expect(cards.every((c) => c.isEmpty)).toBe(true);
        expect(cards[0].summaryText).toContain('henüz yeterli veri yok');

        const emptyBoxes = fixture.nativeElement.querySelectorAll('.trend-card__empty');
        expect(emptyBoxes.length).toBe(3); // 2 haftalık + 1 login bar chart
        expect(emptyBoxes[0].textContent).toContain('Henüz yeterli veri yok');
        expect(fixture.nativeElement.querySelectorAll('.week-bars').length).toBe(0);
        expect(fixture.nativeElement.querySelector('.trend-card__total')).toBeFalsy();
    });

    it('trendCards_OnlyOneSeriesEmpty_ShowsChartForOtherSeries', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardTrends.mockReturnValue(of({
            questionCreated: emptyTrends.questionCreated,
            questionSolved: trends.questionSolved,
            studentLogin: trends.studentLogin,
        }));

        fixture.detectChanges();

        const [created, solved] = component.trendCards();
        expect(created.isEmpty).toBe(true);
        expect(solved.isEmpty).toBe(false);
        expect(fixture.nativeElement.querySelectorAll('.week-bars').length).toBe(1);
        expect(fixture.nativeElement.querySelector('[data-trend="created"] .trend-card__empty')).toBeTruthy();
    });

    // ── Phase 3 (Issue #89): öğrenci login bar chart ─────────────────────────

    it('loginTrendCard_SuccessfulResponse_RendersLoginCardInTemplate', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();
        useDesktopViewport();

        const loginCard: HTMLElement = fixture.nativeElement.querySelector('[data-trend="login"]');
        expect(loginCard).toBeTruthy();
        expect(loginCard.querySelector('ngx-charts-bar-vertical')).toBeTruthy();
        expect(loginCard.textContent).toContain('Öğrenci Login Trendi');
    });

    it('loginTrendCard_SuccessfulResponse_SlicesStudentLoginSeriesToLast30Days', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();
        useDesktopViewport();

        const card = component.loginTrendCard();
        expect(card).not.toBeNull();
        expect(card?.results.length).toBe(30);
        // Son gün (bugün) her zaman dahil olmalı
        expect(card?.results[card.results.length - 1].name).toBe(trends.studentLogin[TREND_DAYS - 1].date);
    });

    it('loginTrendCard_MobileViewport_UsesFewerXAxisTicksThanDesktop', () => {
        fixture = configure();
        component = fixture.componentInstance;

        fixture.detectChanges();
        useDesktopViewport();
        const desktopTickCount = component.loginTrendCard()?.xAxisTicks.length ?? 0;

        component.viewportWidth.set(400);
        fixture.detectChanges();

        expect(component.isMobileViewport()).toBe(true);
        const mobileTickCount = component.loginTrendCard()?.xAxisTicks.length ?? 0;
        expect(mobileTickCount).toBeLessThan(desktopTickCount);
    });

    it('loginTrendCard_StudentLoginSeriesAllZero_ShowsEmptyStateInsteadOfChart', () => {
        fixture = configure();
        component = fixture.componentInstance;
        adminService.getDashboardTrends.mockReturnValue(of(emptyTrends));

        fixture.detectChanges();

        expect(component.loginTrendCard()?.isEmpty).toBe(true);

        const loginCard: HTMLElement = fixture.nativeElement.querySelector('[data-trend="login"]');
        expect(loginCard).toBeTruthy();
        expect(loginCard.querySelector('.trend-card__empty')).toBeTruthy();
        expect(loginCard.querySelector('ngx-charts-bar-vertical')).toBeFalsy();
    });

    // ── Issue #154: sayaç kartlarından liste ekranlarına navigasyon ─────────────

    it('cards_TeacherAndStudentCards_AreLinksToUnfilteredListRoutes', () => {
        fixture = configure();
        fixture.detectChanges();

        const teacherCard: HTMLAnchorElement = fixture.nativeElement.querySelector('[data-key="teachers"]');
        const studentCard: HTMLAnchorElement = fixture.nativeElement.querySelector('[data-key="students"]');

        // Gerçek <a href>: klavyede odaklanır ve Enter ile açılır, ek tabindex/keydown gerekmez.
        expect(teacherCard.tagName).toBe('A');
        expect(studentCard.tagName).toBe('A');
        expect(teacherCard.getAttribute('href')).toBe('/admin/teachers');
        expect(studentCard.getAttribute('href')).toBe('/admin/students');
    });

    it('cards_TeacherAndStudentCards_HaveDescriptiveAriaLabelWithCount', () => {
        fixture = configure();
        fixture.detectChanges();

        const teacherCard: HTMLElement = fixture.nativeElement.querySelector('[data-key="teachers"]');
        const studentCard: HTMLElement = fixture.nativeElement.querySelector('[data-key="students"]');

        expect(teacherCard.getAttribute('aria-label')).toBe('Öğretmen listesini aç (12)');
        expect(studentCard.getAttribute('aria-label')).toBe('Öğrenci listesini aç (340)');
    });

    it('cards_WorksheetAndQuestionCards_StayNonInteractive', () => {
        fixture = configure();
        fixture.detectChanges();

        for (const key of ['worksheets', 'questions']) {
            const card: HTMLElement = fixture.nativeElement.querySelector(`[data-key="${key}"]`);
            expect(card.tagName, key).toBe('DIV');
            expect(card.hasAttribute('href'), key).toBe(false);
        }
    });

    it('cardClick_TeacherAndStudentCards_NavigateToListRoutes', () => {
        fixture = configure();
        fixture.detectChanges();
        const router = TestBed.inject(Router);
        const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

        (fixture.nativeElement.querySelector('[data-key="teachers"]') as HTMLElement).click();
        (fixture.nativeElement.querySelector('[data-key="students"]') as HTMLElement).click();

        const urls = vi.mocked(navigateSpy).mock.calls.map(([url]) => router.serializeUrl(url as UrlTree));
        expect(urls).toEqual(['/admin/teachers', '/admin/students']);
    });

    it('routes_TeacherAndStudentListPaths_AreGuardedByAuthAndAdminGuard', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));

        for (const path of ['admin/teachers', 'admin/students']) {
            const route = layoutRoute?.children?.find((r) => r.path === path);
            expect(route, path).toBeDefined();
            expect(route?.canActivate, path).toEqual([authGuard, adminGuard]);
        }
    });

    // Issue #440/#441: veli durumu sayaçları.
    it('parentStatusCard_ServerSendsCounts_RendersFourCounts', () => {
        fixture = configure();
        adminService.getDashboardSummary.mockReturnValue(of({
            ...summary,
            studentsWithoutParent: 12,
            studentsInParentGrace: 7,
            studentsParentGraceExpired: 2,
            studentsAwaitingParentActivation: 5,
        }));
        component = fixture.componentInstance;
        fixture.detectChanges();

        const card: HTMLElement = fixture.nativeElement.querySelector('[data-key="parentStatus"]');
        expect(card).not.toBeNull();
        const value = (key: string) => card.querySelector(`[data-key="${key}"] .summary-value`)?.textContent?.trim();
        expect(value('withoutParent')).toBe('12');
        expect(value('inGrace')).toBe('7');
        expect(value('graceExpired')).toBe('2');
        expect(value('awaitingActivation')).toBe('5');
        expect(card.textContent).toContain('Veli durumu');
        // #917: her sayı, o kümeye süzülmüş öğrenci listesine gider.
        const link = card.querySelector('[data-key="awaitingActivation"] a');
        expect(link?.getAttribute('href')).toBe('/admin/students?parentStatus=awaitingActivation');
        expect(card.querySelector('[data-key="withoutParent"] a')?.getAttribute('href')).toBe('/admin/students?parentStatus=withoutParent');
        expect(card.querySelector('[data-key="inGrace"] a')?.getAttribute('aria-label')).toContain('öğrenci listesini aç');
    });

    it('parentStatusCard_OldServerWithoutCounts_Hidden', () => {
        fixture = configure();
        component = fixture.componentInstance;
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelector('[data-key="parentStatus"]')).toBeNull();
    });

    // ── #887: Bekleyenler satırı ve sistem durumu ───────────────────────────

    const pending: AdminPendingSummary = {
        teacherApplications: 3,
        schoolApplications: 0,
        commentReports: 1,
        questionReports: 2,
        openTextAnswers: 0,
        total: 6,
    };

    it('pendingRow_OnInit_RefreshesAndRendersFiveQueueTilesWithRoutes_issue887', () => {
        fixture = configure();
        component = fixture.componentInstance;
        pendingRefresh.mockImplementation(() => {
            pendingSummary.set(pending);
            return of(pending);
        });

        fixture.detectChanges();

        expect(pendingRefresh).toHaveBeenCalledTimes(1);
        const section: HTMLElement = fixture.nativeElement.querySelector('[data-test="pending"]');
        expect(section.textContent).toContain('Bekleyenler');
        expect(section.textContent).toContain('6 iş bekliyor');
        const tile = (key: string) => section.querySelector(`.pending-tile[data-key="${key}"]`) as HTMLAnchorElement;
        expect(section.querySelectorAll('.pending-tile').length).toBe(5);
        expect(tile('teacherApplications').getAttribute('href')).toBe('/admin/teacher-approvals');
        expect(tile('schoolApplications').getAttribute('href')).toBe('/admin/student-school-requests');
        expect(tile('questionReports').getAttribute('href')).toBe('/question-reports');
        expect(tile('commentReports').getAttribute('href')).toBe('/admin/comment-reports');
        expect(tile('openTextAnswers').getAttribute('href')).toBe('/open-text-reviews');
        expect(tile('teacherApplications').querySelector('.pending-tile__count')?.textContent?.trim()).toBe('3');
        expect(tile('teacherApplications').getAttribute('aria-label')).toBe('Öğretmen başvurusu: 3 bekliyor');
        // Boş kuyruk soluk ama yine link.
        expect(tile('schoolApplications').classList).toContain('pending-tile--idle');
        expect(tile('teacherApplications').classList).not.toContain('pending-tile--idle');
    });

    it('pendingRow_AllZero_SaysNothingPending_issue887', () => {
        fixture = configure();
        pendingSummary.set({ ...pending, teacherApplications: 0, commentReports: 0, questionReports: 0, total: 0 });

        fixture.detectChanges();

        const section: HTMLElement = fixture.nativeElement.querySelector('[data-test="pending"]');
        expect(section.textContent).toContain('Bekleyen iş yok');
        expect(section.querySelectorAll('.pending-tile--idle').length).toBe(5);
    });

    it('pendingRow_RefreshFails_RowHiddenAndRestStillRenders_issue887', () => {
        fixture = configure();
        pendingRefresh.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();

        expect(fixture.nativeElement.querySelector('[data-test="pending"]')).toBeNull();
        expect(fixture.nativeElement.querySelector('[data-key="teachers"]')).toBeTruthy();
    });

    it('systemStatus_RecentClassificationAndFailedOutbox_ShowsRelativeTimeAndWarning_issue887', () => {
        const at = new Date(Date.now() - 12 * 60_000).toISOString();
        fixture = configure();
        adminService.getDashboardSummary.mockReturnValue(of({ ...summary, lastAiClassificationAt: at, failedOutboxCount: 4 }));

        fixture.detectChanges();

        const status: HTMLElement = fixture.nativeElement.querySelector('[data-test="system-status"]');
        const locale = TestBed.inject(LocaleService).localeDefinition().angularLocale;
        const when = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(-12, 'minute');
        expect(status.querySelector('[data-key="classification"]')?.textContent).toContain(`Son AI sınıflandırma: ${when}`);
        const outbox = status.querySelector('[data-key="outbox"]') as HTMLElement;
        expect(outbox.textContent).toContain('4 olay yayımlanamadı');
        expect(outbox.classList).toContain('system-status__item--warn');
    });

    it('systemStatus_NoClassificationAndHealthyOutbox_ShowsCalmTexts_issue887', () => {
        fixture = configure();
        adminService.getDashboardSummary.mockReturnValue(of({ ...summary, lastAiClassificationAt: null, failedOutboxCount: 0 }));

        fixture.detectChanges();

        const status: HTMLElement = fixture.nativeElement.querySelector('[data-test="system-status"]');
        expect(status.textContent).toContain('Henüz AI sınıflandırması yok');
        const outbox = status.querySelector('[data-key="outbox"]') as HTMLElement;
        expect(outbox.textContent).toContain('Olay kuyruğu sorunsuz');
        expect(outbox.classList).not.toContain('system-status__item--warn');
    });

    it('systemStatus_OldServerWithoutFields_Hidden_issue887', () => {
        fixture = configure();
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelector('[data-test="system-status"]')).toBeNull();
    });
});
