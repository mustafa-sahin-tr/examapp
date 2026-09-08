import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { ScaleType } from '@swimlane/ngx-charts';

import { AdminDashboardComponent } from './admin-dashboard.component';
import { AdminService } from '../../../services/admin.service';
import {
  AdminDashboardSummary,
  AdminDashboardTrendPoint,
  AdminDashboardTrends,
} from '../../../models/admin-dashboard.model';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';

describe('AdminDashboardComponent', () => {
  let fixture: ComponentFixture<AdminDashboardComponent>;
  let component: AdminDashboardComponent;
  let adminService: jasmine.SpyObj<AdminService>;

  const summary: AdminDashboardSummary = {
    teacherCount: 12,
    studentCount: 340,
    worksheetCount: 87,
    questionCount: 500,
    aiClassifiedQuestionCount: 375,
    aiClassifiedRatio: 0.75,
  };

  /** Yerel takvim günü `yyyy-MM-dd` (bileşen `parseIsoDate`/`daysAgo` ile aynı gün tanımı). */
  function toLocalIsoDate(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  /** Bugünden geriye `days` günlük, `yyyy-MM-dd` tarihli seri; `counts[i]` i. güne (en eski → en yeni). */
  function makeSeries(days: number, counts: (index: number) => number): AdminDashboardTrendPoint[] {
    return Array.from({ length: days }, (_, i) => {
      const d = new Date();
      d.setHours(0, 0, 0, 0);
      d.setDate(d.getDate() - (days - 1 - i));
      return { date: toLocalIsoDate(d), count: counts(i) };
    });
  }

  /** 52 hafta × 7 gün — bileşenin backend'den istediği pencere. */
  const TREND_DAYS = 364;

  const trends: AdminDashboardTrends = {
    questionCreated: makeSeries(TREND_DAYS, (i) => (i === TREND_DAYS - 3 ? 9 : 1)), // tepe: 2 gün önce, 9 soru
    questionSolved: makeSeries(TREND_DAYS, (i) => (i === TREND_DAYS - 1 ? 5 : 2)), // tepe: bugün, 5 soru
  };

  const emptyTrends: AdminDashboardTrends = {
    questionCreated: makeSeries(TREND_DAYS, () => 0),
    questionSolved: makeSeries(TREND_DAYS, () => 0),
  };

  /** Heatmap boyutu viewport'a bağlı; testleri masaüstü (52 hafta) düzenine sabitle. */
  function useDesktopViewport(): void {
    component.viewportWidth.set(1280);
    fixture.detectChanges();
  }

  function configure(): ComponentFixture<AdminDashboardComponent> {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['getDashboardSummary', 'getDashboardTrends']);
    adminService.getDashboardSummary.and.returnValue(of(summary));
    adminService.getDashboardTrends.and.returnValue(of(trends));

    TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [{ provide: AdminService, useValue: adminService }, provideRouter([]), provideNoopAnimations()],
    });

    return TestBed.createComponent(AdminDashboardComponent);
  }

  // ── Route config: admin dışı kullanıcı erişemez ──────────────────────────

  it('routes_AdminDashboardPath_IsGuardedByAuthAndAdminGuard', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const dashboardRoute = layoutRoute?.children?.find((r) => r.path === 'admin/dashboard');

    expect(dashboardRoute).withContext('route tanımı bulunamadı').toBeDefined();
    expect(dashboardRoute?.canActivate).toEqual([authGuard, adminGuard]);
  });

  // ── Loading state ─────────────────────────────────────────────────────────

  it('loadSummary_WhileRequestPending_ShowsSkeleton', () => {
    fixture = configure();
    component = fixture.componentInstance;
    const pending$ = new Subject<AdminDashboardSummary>();
    adminService.getDashboardSummary.and.returnValue(pending$.asObservable());

    fixture.detectChanges(); // ngOnInit -> loadSummary, request stays pending

    expect(component.loading()).toBeTrue();

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

    expect(component.loading()).toBeFalse();
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
    adminService.getDashboardSummary.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.loading()).toBeFalse();
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
    adminService.getDashboardSummary.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();
    expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);

    adminService.getDashboardSummary.and.returnValue(of(summary));
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
    adminService.getDashboardSummary.and.returnValue(of(emptySummary));

    fixture.detectChanges();

    expect(component.isEmpty()).toBeTrue();

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

    expect(component.isEmpty()).toBeFalse();
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
    adminService.getDashboardSummary.and.returnValue(of(noQuestionsSummary));

    fixture.detectChanges();

    expect(component.hasQuestions()).toBeFalse();

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

    expect(component.hasQuestions()).toBeTrue();
  });

  // ── Phase 2 (Issue #88): 52 haftalık heatmap'ler ─────────────────────────

  it('ngOnInit_Always_RequestsSummaryAndTrendsInParallelWith52Weeks', () => {
    fixture = configure();
    fixture.detectChanges();

    expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);
    expect(adminService.getDashboardTrends).toHaveBeenCalledOnceWith(364);
  });

  it('loadTrends_WhileRequestPending_ShowsTrendSkeletonWithoutBlockingSummary', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardTrends.and.returnValue(new Subject<AdminDashboardTrends>().asObservable());

    fixture.detectChanges();

    expect(component.trendLoading()).toBeTrue();
    expect(component.loading()).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('.trend-card--skeleton').length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-key="teachers"]')).toBeTruthy();
  });

  it('loadTrends_SuccessfulResponse_RendersTwoHeatmapsWith52WeekColumns', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();
    useDesktopViewport();

    expect(component.trendLoading()).toBeFalse();
    expect(component.trendError()).toBeNull();
    expect(component.trends()).toEqual(trends);

    const cards = component.trendCards();
    expect(cards.map((c) => c.key)).toEqual(['created', 'solved']);
    expect(cards[0].isEmpty).toBeFalse();
    expect(cards[0].total).toBe(TREND_DAYS - 1 + 9);
    expect(cards[1].total).toBe((TREND_DAYS - 1) * 2 + 5);

    // ngx-charts heat-map formatı (dashboard.component ile aynı): dış dizi = 52 hafta, series = ≤7 gün
    const weeks = cards[0].results;
    expect(weeks.length).toBe(52);
    expect(weeks.every((w) => w.series.length >= 1 && w.series.length <= 7)).toBeTrue();
    expect(weeks[0].series.length).toBe(7);
    expect(weeks[0].name).toMatch(/^\d{1,2} \S+$/); // "10 Ağu"

    // Izgara serinin ilk gününden son gününe kadar tam 52×7 hücre: gün atılmaz, gün uydurulmaz.
    const cells = weeks.flatMap((w) => w.series);
    expect(cells.length).toBe(TREND_DAYS);
    expect(weeks.every((w) => w.series.length === 7)).toBeTrue();
    // İlk haftanın son hücresi (günler ters sıralı) = serinin ilk günü; son haftanın ilk hücresi = son günü
    expect(weeks[0].series[6].extra.date).toBe(trends.questionCreated[0].date);
    expect(weeks[weeks.length - 1].series[0].extra.date).toBe(trends.questionCreated[TREND_DAYS - 1].date);

    // Günler hafta içinde ters sırada (Pazartesi üstte kalsın diye): series[6] tarihi < series[0] tarihi
    expect(weeks[0].series[6].extra.date < weeks[0].series[0].extra.date).toBeTrue();

    // Tepe gün (2 gün önce, 9 soru): gerçek sayı tooltip payload'ında, hücre değeri en üst yoğunluk seviyesi
    const peak = cells.find((c) => c.extra.count === 9);
    expect(peak).toBeDefined();
    expect(peak?.value).toBe(4);
    expect(peak?.extra.date).toBe(trends.questionCreated[TREND_DAYS - 3].date);
    expect(peak?.extra.verb).toBe('oluşturuldu');
    // Sıradan günler (1 soru) en düşük sıfır-dışı seviye; tepe ile aynı renge çökmez
    expect(cells.filter((c) => c.extra.count === 1).every((c) => c.value === 1)).toBeTrue();

    const charts = fixture.nativeElement.querySelectorAll('ngx-charts-heat-map');
    expect(charts.length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-trend="created"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-trend="solved"]')).toBeTruthy();
  });

  it('trendCards_SuccessfulResponse_RenderedCellSumEqualsTotalAndEveryPointIsRendered', () => {
    // Regresyon: eski Pazar hizalaması, bugün Pazar değilken serinin en eski k gününü ızgara dışına
    // düşürüyordu; başlık toplamı (tüm 364 gün) ile görünen hücreler tutarsızlaşıyordu. Tepe gün en eski
    // güne konur ki hizalama kayması olursa yakalansın.
    fixture = configure();
    component = fixture.componentInstance;
    const oldestPeak = makeSeries(TREND_DAYS, (i) => (i === 0 ? 40 : i % 3));
    adminService.getDashboardTrends.and.returnValue(of({ questionCreated: oldestPeak, questionSolved: oldestPeak }));

    fixture.detectChanges();
    useDesktopViewport();

    const [created] = component.trendCards();
    const cells = created.results.flatMap((w) => w.series);
    // Hücre `value` yoğunluk seviyesidir; gerçek sayılar `extra.count`'ta toplanır ve başlık toplamına eşittir
    const renderedSum = cells.reduce((acc, c) => acc + c.extra.count, 0);
    const expectedTotal = oldestPeak.reduce((acc, p) => acc + p.count, 0);

    expect(created.total).toBe(expectedTotal);
    expect(renderedSum).toBe(created.total);
    expect(cells.every((c) => Number.isInteger(c.value) && c.value >= 0 && c.value <= 4)).toBeTrue();

    // Her backend günü tam olarak bir hücreye eşlenir, fazladan gün yok
    const renderedDates = new Set(cells.map((c) => c.extra.date));
    expect(renderedDates.size).toBe(TREND_DAYS);
    expect(oldestPeak.every((p) => renderedDates.has(p.date))).toBeTrue();
    expect(cells.find((c) => c.extra.count === 40)?.extra.date).toBe(oldestPeak[0].date);
  });

  it('transformToHeatmap_SkewedCountsWithOutlier_BucketsIntoDistinctLevelsAndKeepsRealCounts', () => {
    // Regresyon: ngx-charts renkleri [0,max] üzerinden doğrusal normalize eder; tek bir 79'luk gün diğer tüm
    // günleri (2, 3, 4, 8, 18) skalanın en altına sıkıştırıp boş günden ayırt edilemez yapıyordu. Hücre değeri
    // artık dağılımdan türetilen seviye (0..4): ara günler ayrı kovalara düşmeli, 79 en üstte olmalı.
    fixture = configure();
    component = fixture.componentInstance;
    const seeded: Record<number, number> = { 10: 2, 50: 3, 100: 4, 150: 8, 200: 18, [TREND_DAYS - 1]: 79 };
    const skewed = makeSeries(TREND_DAYS, (i) => seeded[i] ?? 0);
    adminService.getDashboardTrends.and.returnValue(of({ questionCreated: skewed, questionSolved: skewed }));

    fixture.detectChanges();
    useDesktopViewport();

    const [created] = component.trendCards();
    const cells = created.results.flatMap((w) => w.series);
    const levelOf = (count: number): number | undefined => cells.find((c) => c.extra.count === count)?.value;

    // Sıfır günler 0; tüm hücreler 0..4 tam sayı
    expect(cells.filter((c) => c.extra.count === 0).every((c) => c.value === 0)).toBeTrue();
    expect(cells.every((c) => Number.isInteger(c.value) && c.value >= 0 && c.value <= 4)).toBeTrue();

    // Farklı değerlerin çeyreklikleri (3.25 / 6 / 15.5) → 2,3 → 1; 4 → 2; 8 → 3; 18,79 → 4
    expect(levelOf(2)).toBe(1);
    expect(levelOf(3)).toBe(1);
    expect(levelOf(4)).toBe(2);
    expect(levelOf(8)).toBe(3);
    expect(levelOf(18)).toBe(4);
    expect(levelOf(79)).toBe(4);
    // Asıl hata: orta günler en düşük kovaya çökmemeli, tepe en üstte
    expect(levelOf(8)).toBeGreaterThan(levelOf(2) ?? 0);
    expect(levelOf(18)).toBeGreaterThan(levelOf(2) ?? 0);
    expect(levelOf(2)).toBeGreaterThan(0);

    // Toplam, özet ve tooltip gerçek sayıyı gösterir; seviye sızmaz
    expect(created.total).toBe(2 + 3 + 4 + 8 + 18 + 79);
    expect(created.summaryText).toContain(`toplam ${created.total} soru oluşturuldu`);
    expect(created.summaryText).toContain('bugün, 79 soru');
    const peakCell = cells.find((c) => c.extra.count === 79);
    expect(component.formatHeatmapTooltip({ cell: peakCell, data: peakCell?.value })).toBe(
      `${peakCell?.extra.label}: 79 soru oluşturuldu`,
    );
    const summaryEl: HTMLElement = fixture.nativeElement.querySelector('#trend-summary-created');
    expect(summaryEl.textContent).toContain('79 soru');
  });

  it('transformToHeatmap_EveryActiveDaySameCount_MarksThemAsTopLevel', () => {
    // Tek farklı değer: çeyreklik tanımsız, [0,max] eşit bölünür → her etkin gün tepe (seviye 4), boş günler 0
    fixture = configure();
    component = fixture.componentInstance;
    const uniform = makeSeries(TREND_DAYS, (i) => (i % 2 === 0 ? 2 : 0));
    adminService.getDashboardTrends.and.returnValue(of({ questionCreated: uniform, questionSolved: uniform }));

    fixture.detectChanges();
    useDesktopViewport();

    const [created] = component.trendCards();
    const cells = created.results.flatMap((w) => w.series);
    expect(cells.filter((c) => c.extra.count === 2).every((c) => c.value === 4)).toBeTrue();
    expect(cells.filter((c) => c.extra.count === 0).every((c) => c.value === 0)).toBeTrue();
  });

  it('trendCards_MobileViewport_ShowsOnlyLast17Weeks', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();
    component.viewportWidth.set(400);
    fixture.detectChanges();

    expect(component.isMobileViewport()).toBeTrue();
    const [created] = component.trendCards();
    expect(created.results.length).toBe(17);
    // Son hafta korunur (dilim sondan alınır)
    const lastCell = created.results[created.results.length - 1].series[0];
    expect(lastCell.extra.date).toBe(trends.questionCreated[TREND_DAYS - 1].date);
  });

  it('heatmapView_DesktopViewport_UsesFixedWideViewLikeStudentDashboard', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();
    useDesktopViewport();

    expect(component.heatmapView()).toEqual([1200, 210]);
  });

  it('trendCards_SuccessfulResponse_UsesLinearSchemeWithOneStopPerNonTopLevel', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    const [created, solved] = component.trendCards();
    expect(created.scheme.group).toBe(ScaleType.Linear);
    expect(solved.scheme.group).toBe(ScaleType.Linear);
    // Hücre değeri 0..4 seviyesi; 4 durak ile seviye 0..3 tam durağa denk gelir, seviye 4 ekstrapole edilip
    // tam rengi verir. Durak sayısı değişirse seviyeler duraklar arasına düşer ve tonlar öngörülemez olur.
    expect(created.scheme.domain.length).toBe(4);
    expect(created.scheme.domain.every((c) => c !== '')).toBeTrue();
    expect(new Set(created.scheme.domain).size).toBe(4);
  });

  it('xAxisTickFormatting_RepeatedMonth_CollapsesToSingleMonthLabel', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    const [created] = component.trendCards();
    const format = created.xAxisTickFormatting;
    const first = created.results[0].name;
    const firstMonth = first.split(' ')[1];

    expect(format(first)).toBe(firstMonth);
    expect(format(`99 ${firstMonth}`)).toBe('');
    expect(format('1 Zzz')).toBe('Zzz');
    // İlk hafta etiketi durumu sıfırlar (yeniden render'da ilk ay yine görünür)
    expect(format(first)).toBe(firstMonth);
  });

  it('formatHeatmapTooltip_CellWithExtra_ReturnsDateAndCountSentence', () => {
    fixture = configure();
    component = fixture.componentInstance;

    const text = component.formatHeatmapTooltip({
      cell: { value: 5, extra: { date: '2026-08-10', label: '10 Ağu 2026', count: 5, verb: 'oluşturuldu' } },
      data: 5,
    });

    expect(text).toBe('10 Ağu 2026: 5 soru oluşturuldu');
    expect(component.formatHeatmapTooltip({ data: 3 })).toBe('3');
    expect(component.formatHeatmapTooltip(null)).toBe('');
  });

  it('trendCards_SuccessfulResponse_ExposesAriaLabelAndDescribedBySummary', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    const createdCard: HTMLElement = fixture.nativeElement.querySelector('[data-trend="created"]');
    expect(createdCard.getAttribute('aria-label')).toContain('Soru Oluşturma');
    expect(createdCard.getAttribute('aria-label')).toContain('son 1 yıl');

    const describedBy = createdCard.getAttribute('aria-describedby');
    expect(describedBy).toBe('trend-summary-created');
    const summaryEl: HTMLElement = fixture.nativeElement.querySelector(`#${describedBy}`);
    expect(summaryEl).toBeTruthy();
    expect(summaryEl.textContent).toContain('Son 1 yıl');
    expect(summaryEl.textContent).toContain(`toplam ${TREND_DAYS - 1 + 9} soru oluşturuldu`);
    expect(summaryEl.textContent).toContain('2 gün önce, 9 soru');

    const solvedSummary: HTMLElement = fixture.nativeElement.querySelector('#trend-summary-solved');
    expect(solvedSummary.textContent).toContain('bugün, 5 soru');
  });

  it('loadTrends_RequestFails_ShowsTrendErrorOnlyAndKeepsSummaryCards', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardTrends.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    // Trend bölümü hata
    expect(component.trendLoading()).toBeFalse();
    expect(component.trendError()).toBe('Trend verileri alınırken bir sorun oluştu.');
    expect(component.trends()).toBeNull();
    const trendError = fixture.nativeElement.querySelector('.state-box--error[data-section="trend"]');
    expect(trendError).toBeTruthy();
    expect(trendError.querySelector('button')).toBeTruthy();
    expect(fixture.nativeElement.querySelectorAll('ngx-charts-heat-map').length).toBe(0);

    // Sayaç bölümü etkilenmedi
    expect(component.error()).toBeNull();
    expect(component.summary()).toEqual(summary);
    expect(fixture.nativeElement.querySelector('[data-key="teachers"] .summary-value').textContent).toContain('12');
    expect(fixture.nativeElement.querySelectorAll('.state-box--error').length).toBe(1);
  });

  it('trendRetryButtonClick_AfterError_CallsLoadTrendsAgainOnly', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardTrends.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();
    expect(adminService.getDashboardTrends).toHaveBeenCalledTimes(1);

    adminService.getDashboardTrends.and.returnValue(of(trends));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error[data-section="trend"] button');
    retry.click();
    fixture.detectChanges();

    expect(adminService.getDashboardTrends).toHaveBeenCalledTimes(2);
    expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);
    expect(component.trendError()).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('ngx-charts-heat-map').length).toBe(2);
  });

  it('loadSummary_RequestFailsButTrendsSucceed_StillRendersCharts', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardSummary.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.error()).not.toBeNull();
    expect(component.trendError()).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('ngx-charts-heat-map').length).toBe(2);
  });

  it('trendCards_AllCountsZero_ShowsNotEnoughDataInsteadOfChart', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardTrends.and.returnValue(of(emptyTrends));

    fixture.detectChanges();

    const cards = component.trendCards();
    expect(cards.every((c) => c.isEmpty)).toBeTrue();
    expect(cards[0].summaryText).toContain('henüz yeterli veri yok');

    const emptyBoxes = fixture.nativeElement.querySelectorAll('.trend-card__empty');
    expect(emptyBoxes.length).toBe(2);
    expect(emptyBoxes[0].textContent).toContain('Henüz yeterli veri yok');
    expect(fixture.nativeElement.querySelectorAll('ngx-charts-heat-map').length).toBe(0);
    expect(fixture.nativeElement.querySelector('.trend-card__total')).toBeFalsy();
  });

  it('trendCards_OnlyOneSeriesEmpty_ShowsChartForOtherSeries', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardTrends.and.returnValue(
      of({ questionCreated: emptyTrends.questionCreated, questionSolved: trends.questionSolved }),
    );

    fixture.detectChanges();

    const [created, solved] = component.trendCards();
    expect(created.isEmpty).toBeTrue();
    expect(solved.isEmpty).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('ngx-charts-heat-map').length).toBe(1);
    expect(fixture.nativeElement.querySelector('[data-trend="created"] .trend-card__empty')).toBeTruthy();
  });

  // ── Heatmap kaydırma: veri gelince en güncel hafta görünür ───────────────

  it('loadTrends_SuccessfulResponse_ScrollsEachHeatmapContainerToLatestWeek', () => {
    // Kap 52 hafta × 22px = 1200px'lik grafiği taşırır; tarayıcı varsayılanı scrollLeft=0 en ESKİ haftaları
    // gösterirdi. Host'u dar tutarak taşmayı garanti ederiz; render sonrası her kap sona kaydırılmış olmalı.
    fixture = configure();
    component = fixture.componentInstance;
    (fixture.nativeElement as HTMLElement).style.width = '400px';

    fixture.detectChanges(); // ngOnInit -> loadTrends -> trendCards effect -> afterNextRender (detectChanges render hook'larını çalıştırır)
    useDesktopViewport();

    const containers: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.heatmap-scroll'));
    expect(containers.length).toBe(2);
    for (const el of containers) {
      expect(el.scrollWidth).withContext('kap taşmalı ki kaydırma anlamlı olsun').toBeGreaterThan(el.clientWidth);
      expect(el.scrollLeft).toBe(el.scrollWidth - el.clientWidth);
    }
  });

  it('trendCards_ViewportCrossesMobileThreshold_RescrollsToLatestWeekAfterRedraw', () => {
    // Mobil (17 hafta) ↔ masaüstü (52 hafta) geçişinde grafik farklı genişlikte yeniden çizilir; eski
    // scrollLeft yeni genişlikte ortada kalırdı. trendCards değiştiği için effect tekrar sona kaydırmalı.
    fixture = configure();
    component = fixture.componentInstance;
    (fixture.nativeElement as HTMLElement).style.width = '300px';

    fixture.detectChanges();
    component.viewportWidth.set(400);
    fixture.detectChanges();
    // `track card.key` aynı DOM öğesini korur; ölçümleri geçişten ÖNCE sayı olarak yakala.
    const el: HTMLElement = fixture.nativeElement.querySelector('.heatmap-scroll');
    const mobileScrollWidth = el.scrollWidth;
    expect(el.scrollLeft).toBe(mobileScrollWidth - el.clientWidth);

    useDesktopViewport();
    expect(el.scrollWidth).toBeGreaterThan(mobileScrollWidth);
    expect(el.scrollLeft).toBe(el.scrollWidth - el.clientWidth);
  });

  it('loadTrends_SuccessfulResponse_DoesNotRescrollOnUnrelatedChangeDetection', () => {
    fixture = configure();
    component = fixture.componentInstance;
    (fixture.nativeElement as HTMLElement).style.width = '400px';

    fixture.detectChanges();
    useDesktopViewport();

    // Yönetici eski haftalara döndü; ilgisiz bir CD turu onu geri sürüklememeli.
    const el: HTMLElement = fixture.nativeElement.querySelector('.heatmap-scroll');
    el.scrollLeft = 0;
    fixture.detectChanges();

    expect(el.scrollLeft).toBe(0);
  });

});
