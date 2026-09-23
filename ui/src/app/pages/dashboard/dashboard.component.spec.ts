import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { DashboardComponent } from './dashboard.component';
import { AssignedWorksheet } from '../../models/assignment';
import { TestService } from '../../services/test.service';
import { BadgeService, BadgeProgressItem, BadgeProgressResponse } from '../../services/badge.service';
import { StudentResetService } from '../../services/student-reset.service';
import { StudentService } from '../../services/student.service';
import { LocaleService } from '../../services/locale.service';
import { TranslocoTestingModule } from '@jsverse/transloco';
import trTranslations from '../../../../public/i18n/tr.json';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES, localeDefinitionOf } from '../../models/locale';

/** Tarih biçimi tarayıcı diline bağlı kalmasın: aktif dil sabit 'tr' (issue #188 kompakt kart bitiş etiketi). */
const localeServiceStub = {
  locale: signal('tr' as const).asReadonly(),
  localeDefinition: signal(localeDefinitionOf('tr')).asReadonly(),
};

/**
 * Testler Turkce metinleri dogrudan assert ettigi icin gercek `public/i18n/tr.json` sozlugu yuklenir
 * (issue #180) — sozlukteki bir anahtar bozulursa test kirilir, sahte ceviri kullanilmaz.
 */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: trTranslations },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
  },
  preloadLangs: true,
});

/** Fixed "now" so relative last-login labels are deterministic. */
const NOW_ISO = '2026-09-09T12:00:00Z';

function buildBadge(overrides: Partial<BadgeProgressItem>): BadgeProgressItem {
  return {
    badgeDefinitionId: 'id',
    name: 'name',
    description: 'desc',
    iconUrl: 'icon.svg',
    pathKey: null,
    pathName: null,
    pathOrder: null,
    currentValue: 0,
    targetValue: 10,
    isCompleted: false,
    earnedDateUtc: null,
    ...overrides,
  };
}

function badgeProgressResponse(badges: BadgeProgressItem[]): BadgeProgressResponse {
  return {
    summary: {
      userId: 1,
      totalQuestions: 0,
      correctQuestions: 0,
      accuracyPercentage: 0,
      totalPoints: 0,
      currentCorrectStreak: 0,
      bestCorrectStreak: 0,
      totalTimeSeconds: 0,
      totalActiveDays: 0,
      currentActivityStreak: 0,
      bestActivityStreak: 0,
      lastAnsweredAtUtc: null,
      lastUpdatedUtc: null,
    },
    badgeProgress: badges,
    subjectBreakdown: [],
  };
}

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let testServiceSpy: jasmine.SpyObj<TestService>;
  let badgeServiceSpy: jasmine.SpyObj<BadgeService>;
  let studentServiceSpy: jasmine.SpyObj<StudentService>;

  function createComponent(): DashboardComponent {
    const fixture = TestBed.createComponent(DashboardComponent);
    return fixture.componentInstance;
  }

  beforeEach(() => {
    testServiceSpy = jasmine.createSpyObj<TestService>('TestService', ['getActiveAssignments']);
    badgeServiceSpy = jasmine.createSpyObj<BadgeService>('BadgeService', [
      'getUserActivity',
      'getUserBadgeProgress',
    ]);
    studentServiceSpy = jasmine.createSpyObj<StudentService>('StudentService', ['getLastLogin']);

    testServiceSpy.getActiveAssignments.and.returnValue(of([]));
    badgeServiceSpy.getUserActivity.and.returnValue(
      of({ userId: 16, startDateUtc: NOW_ISO, endDateUtc: NOW_ISO, days: [] })
    );
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse([])));
    studentServiceSpy.getLastLogin.and.returnValue(of({ lastLoginAtUtc: null }));

    TestBed.configureTestingModule({
      imports: [DashboardComponent, translocoTesting],
      providers: [
        { provide: TestService, useValue: testServiceSpy },
        { provide: BadgeService, useValue: badgeServiceSpy },
        { provide: StudentResetService, useValue: jasmine.createSpyObj('StudentResetService', ['resetMyData']) },
        { provide: StudentService, useValue: studentServiceSpy },
        { provide: LocaleService, useValue: localeServiceStub },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
      ],
    });
  });

  let clockInstalled = false;

  afterEach(() => {
    if (clockInstalled) {
      jasmine.clock().uninstall();
      clockInstalled = false;
    }
  });

  describe('lastLoginLabel', () => {
    beforeEach(() => {
      jasmine.clock().install();
      clockInstalled = true;
      jasmine.clock().mockDate(new Date(NOW_ISO));
    });

    it('lastLoginLabel_OneDayAgo_ReturnsDayLabel', () => {
      studentServiceSpy.getLastLogin.and.returnValue(of({ lastLoginAtUtc: '2026-09-08T12:00:00Z' }));
      component = createComponent();

      component.ngOnInit();

      expect(component.lastLoginLabel()).toBe('1 gün önce');
    });

    it('lastLoginLabel_TwoHoursAgo_ReturnsHourLabel', () => {
      studentServiceSpy.getLastLogin.and.returnValue(of({ lastLoginAtUtc: '2026-09-09T10:00:00Z' }));
      component = createComponent();

      component.ngOnInit();

      expect(component.lastLoginLabel()).toBe('2 saat önce');
    });

    it('lastLoginLabel_FiveMinutesAgo_ReturnsMinuteLabel', () => {
      studentServiceSpy.getLastLogin.and.returnValue(of({ lastLoginAtUtc: '2026-09-09T11:55:00Z' }));
      component = createComponent();

      component.ngOnInit();

      expect(component.lastLoginLabel()).toBe('5 dakika önce');
    });

    it('lastLoginLabel_JustNow_ReturnsAzOnceLabel', () => {
      studentServiceSpy.getLastLogin.and.returnValue(of({ lastLoginAtUtc: NOW_ISO }));
      component = createComponent();

      component.ngOnInit();

      expect(component.lastLoginLabel()).toBe('az önce');
    });

    it('lastLoginLabel_LastLoginAtUtcIsNull_ReturnsNull', () => {
      studentServiceSpy.getLastLogin.and.returnValue(of({ lastLoginAtUtc: null }));
      component = createComponent();

      component.ngOnInit();

      expect(component.lastLoginAtUtc()).toBeNull();
      expect(component.lastLoginLabel()).toBeNull();
    });
  });

  describe('loadLastLogin error handling', () => {
    it('ngOnInit_GetLastLoginFails_LastLoginAtUtcStaysNullWithoutThrowing', () => {
      studentServiceSpy.getLastLogin.and.returnValue(throwError(() => new Error('network down')));
      component = createComponent();

      expect(() => component.ngOnInit()).not.toThrow();

      expect(component.lastLoginAtUtc()).toBeNull();
      expect(component.lastLoginLabel()).toBeNull();
    });
  });

  describe('upcomingBadges', () => {
    it('upcomingBadges_MixOfCompletedAndIncomplete_ExcludesCompletedSortsByRemainingAscAndLimitsToThree', () => {
      const badges = [
        buildBadge({ badgeDefinitionId: 'far', currentValue: 10, targetValue: 100, isCompleted: false }), // remaining 90
        buildBadge({ badgeDefinitionId: 'done', currentValue: 10, targetValue: 10, isCompleted: true }),
        buildBadge({ badgeDefinitionId: 'near', currentValue: 9, targetValue: 10, isCompleted: false }), // remaining 1
        buildBadge({ badgeDefinitionId: 'mid', currentValue: 5, targetValue: 10, isCompleted: false }), // remaining 5
        buildBadge({ badgeDefinitionId: 'furthest', currentValue: 0, targetValue: 200, isCompleted: false }), // remaining 200
      ];
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse(badges)));
      component = createComponent();

      component.ngOnInit();

      const upcoming = component.upcomingBadges();
      expect(upcoming.length).toBe(3);
      expect(upcoming.map((item) => item.badge.badgeDefinitionId)).toEqual(['near', 'mid', 'far']);
      expect(upcoming.some((item) => item.badge.badgeDefinitionId === 'done')).toBeFalse();
    });

    it('upcomingBadges_IncompleteBadge_ComputesPercentFromCurrentAndTarget', () => {
      const badges = [buildBadge({ badgeDefinitionId: 'half', currentValue: 5, targetValue: 10, isCompleted: false })];
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse(badges)));
      component = createComponent();

      component.ngOnInit();

      expect(component.upcomingBadges()[0].percent).toBe(50);
      expect(component.upcomingBadges()[0].remaining).toBe(5);
    });
  });

  describe('allBadgesCompleted', () => {
    it('allBadgesCompleted_AllBadgesCompleted_ReturnsTrueAndUpcomingBadgesEmpty', () => {
      const badges = [
        buildBadge({ badgeDefinitionId: 'a', currentValue: 10, targetValue: 10, isCompleted: true }),
        buildBadge({ badgeDefinitionId: 'b', currentValue: 5, targetValue: 5, isCompleted: true }),
      ];
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse(badges)));
      component = createComponent();

      component.ngOnInit();

      expect(component.allBadgesCompleted()).toBeTrue();
      expect(component.upcomingBadges()).toEqual([]);
    });

    it('allBadgesCompleted_SomeBadgesIncomplete_ReturnsFalse', () => {
      const badges = [
        buildBadge({ badgeDefinitionId: 'a', currentValue: 10, targetValue: 10, isCompleted: true }),
        buildBadge({ badgeDefinitionId: 'b', currentValue: 1, targetValue: 5, isCompleted: false }),
      ];
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse(badges)));
      component = createComponent();

      component.ngOnInit();

      expect(component.allBadgesCompleted()).toBeFalse();
    });

    it('allBadgesCompleted_NoBadgesAtAll_ReturnsFalse', () => {
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse([])));
      component = createComponent();

      component.ngOnInit();

      expect(component.allBadgesCompleted()).toBeFalse();
    });
  });

  describe('earnedBadges (regression)', () => {
    it('earnedBadges_MixOfCompletedAndIncomplete_ContainsOnlyCompletedSortedByEarnedDateDesc', () => {
      const badges = [
        buildBadge({
          badgeDefinitionId: 'older',
          isCompleted: true,
          earnedDateUtc: '2026-01-01T00:00:00Z',
        }),
        buildBadge({ badgeDefinitionId: 'incomplete', isCompleted: false, earnedDateUtc: null }),
        buildBadge({
          badgeDefinitionId: 'newer',
          isCompleted: true,
          earnedDateUtc: '2026-06-01T00:00:00Z',
        }),
      ];
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse(badges)));
      component = createComponent();

      component.ngOnInit();

      const earned = component.earnedBadges();
      expect(earned.map((b) => b.badgeDefinitionId)).toEqual(['newer', 'older']);
    });

    it('loadUserBadgeProgress_ApiErrors_EarnedBadgesAndAllBadgeProgressBecomeEmpty', () => {
      badgeServiceSpy.getUserBadgeProgress.and.returnValue(throwError(() => new Error('boom')));
      component = createComponent();

      expect(() => component.ngOnInit()).not.toThrow();

      expect(component.earnedBadges()).toEqual([]);
      expect(component.allBadgeProgress()).toEqual([]);
      expect(component.badgeProgressError()).toBeTrue();
    });
  });

  describe('assignment compact cards (issue #188)', () => {
    function assignment(overrides: Partial<AssignedWorksheet>): AssignedWorksheet {
      return {
        assignmentId: 1,
        worksheetId: 100,
        name: 'Atanan Test',
        description: '',
        gradeId: 1,
        maxDurationSeconds: 600,
        isPracticeTest: false,
        questionCount: 10,
        startAt: '2026-01-01T00:00:00Z',
        endAt: null,
        isGradeAssignment: false,
        assignmentStatus: 'NotStarted',
        hasStarted: false,
        isCompleted: false,
        ...overrides,
      };
    }

    function compactCards(fixture: ComponentFixture<DashboardComponent>): HTMLElement[] {
      const root = fixture.nativeElement as HTMLElement;
      return Array.from(root.querySelectorAll<HTMLElement>('app-compact-test-card'));
    }

    it('render_ThreeAssignments_RendersOneCompactCardPerAssignmentWithTitleAndDue', () => {
      const endAt = '2026-03-12T10:00:00Z';
      testServiceSpy.getActiveAssignments.and.returnValue(
        of([
          assignment({ assignmentId: 1, worksheetId: 100, name: 'Birinci', endAt }),
          assignment({ assignmentId: 2, worksheetId: 200, name: 'İkinci', imageUrl: 'b.png' }),
          assignment({ assignmentId: 3, worksheetId: 300, name: 'Üçüncü' }),
        ])
      );
      const fixture = TestBed.createComponent(DashboardComponent);

      fixture.detectChanges();

      const cards = compactCards(fixture);
      expect(cards.length).toBe(3);
      expect(cards.map((c) => c.querySelector('.ctc__title')?.textContent?.trim())).toEqual([
        'Birinci',
        'İkinci',
        'Üçüncü',
      ]);
      expect(cards[0].querySelector('.ctc__due')?.textContent?.trim()).toBe(
        `Bitiş: ${new Date(endAt).toLocaleDateString('tr')}`
      );
      expect(cards[2].querySelector('.ctc__due')).toBeNull();
      // Dashboard kartında ilerleme rozeti yok — mevcut görünümle aynı.
      expect(fixture.nativeElement.querySelector('.ctc__progress')).toBeNull();
    });

    it('render_NoAssignments_ShowsEmptyMessageWithoutCards', () => {
      const fixture = TestBed.createComponent(DashboardComponent);

      fixture.detectChanges();

      expect(compactCards(fixture).length).toBe(0);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Aktif atanmış test bulunmuyor.');
    });

    it('cardActivated_NavigatesToWorksheetRoute', () => {
      testServiceSpy.getActiveAssignments.and.returnValue(
        of([assignment({ assignmentId: 9, worksheetId: 456, name: 'Tıklanan' })])
      );
      const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
      const fixture = TestBed.createComponent(DashboardComponent);
      fixture.detectChanges();

      compactCards(fixture)[0].click();

      expect(router.navigate).toHaveBeenCalledOnceWith(['/test', 456]);
    });
  });

  describe('loadUserActivityHeatmap error handling', () => {
    it('loadUserActivityHeatmap_ApiErrors_ActivityApiErrorBecomesTrueAndActivityDataStaysEmpty', () => {
      badgeServiceSpy.getUserActivity.and.returnValue(throwError(() => new Error('boom')));
      component = createComponent();

      expect(() => component.ngOnInit()).not.toThrow();

      expect(component.activityApiError()).toBeTrue();
      expect(component.activityDataFromApi()).toEqual([]);
    });

    it('loadUserActivityHeatmap_ApiSucceeds_ActivityApiErrorStaysFalse', () => {
      component = createComponent();

      component.ngOnInit();

      expect(component.activityApiError()).toBeFalse();
    });
  });

  describe('responsive activity summary (issue #197)', () => {
    const activityResponse = {
      userId: 16,
      startDateUtc: '2026-09-01T00:00:00Z',
      endDateUtc: NOW_ISO,
      days: [
        {
          dateUtc: '2026-09-08T00:00:00Z',
          questionCount: 12,
          correctCount: 9,
          totalTimeSeconds: 600,
          totalPoints: 30,
          activityScore: 4,
        },
        {
          dateUtc: '2026-09-09T00:00:00Z',
          questionCount: 8,
          correctCount: 5,
          totalTimeSeconds: 300,
          totalPoints: 20,
          activityScore: 3,
        },
      ],
    };

    /** Komponent host'unu verilen genişlikte bir kapsayıcıya koyar; grid sütun sayısı kapsayıcı genişliğine bağlıdır. */
    function renderAtWidth(widthPx: number): ComponentFixture<DashboardComponent> {
      const fixture = TestBed.createComponent(DashboardComponent);
      const host = fixture.nativeElement as HTMLElement;
      host.style.display = 'block';
      host.style.width = `${widthPx}px`;
      fixture.detectChanges();
      return fixture;
    }

    function statCards(fixture: ComponentFixture<DashboardComponent>): HTMLElement[] {
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.stat-card'));
    }

    /** auto-fit boş izleri 0px'e çöker; yalnızca gerçekten kart taşıyan sütunlar sayılır. */
    function visibleColumnCount(fixture: ComponentFixture<DashboardComponent>): number {
      const grid = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.activity-stats');
      const tracks = getComputedStyle(grid!).gridTemplateColumns.split(' ');
      return tracks.filter((track) => parseFloat(track) > 0).length;
    }

    beforeEach(() => {
      badgeServiceSpy.getUserActivity.and.returnValue(of(activityResponse));
    });

    it('render_ActivityLoaded_RendersFourNativeStatCardsInsteadOfFixedSizeNumberCardChart', () => {
      const fixture = renderAtWidth(1000);

      const cards = statCards(fixture);
      expect(cards.map((card) => card.getAttribute('data-key'))).toEqual([
        'questions',
        'correct',
        'minutes',
        'activityScore',
      ]);
      expect(cards.map((card) => card.querySelector('.stat-card__value')?.textContent?.trim())).toEqual([
        '20',
        '14',
        '15',
        '7',
      ]);
      expect(cards[0].querySelector('.stat-card__label')?.textContent?.trim()).toBe('Toplam Soru');
      expect((fixture.nativeElement as HTMLElement).querySelector('ngx-charts-number-card')).toBeNull();
    });

    it('statGrid_WideContainer_ShowsAllFourCardsInOneRow', () => {
      const fixture = renderAtWidth(1000);

      expect(visibleColumnCount(fixture)).toBe(4);
    });

    it('statGrid_PhoneWidthContainer_WrapsToTwoColumns', () => {
      const fixture = renderAtWidth(320);

      expect(visibleColumnCount(fixture)).toBe(2);
    });

    it('statCard_LongLabelAndLargeValue_DoesNotOverflowCard', () => {
      const fixture = renderAtWidth(320);
      fixture.componentInstance.activityNumberCardData.set([
        {
          key: 'questions',
          icon: 'quiz',
          name: 'Çokuzunbirçevirimetnikartıntaşmamasıgerekenetiketdeğeri',
          value: 123456789012,
        },
      ]);
      fixture.detectChanges();

      const card = statCards(fixture)[0];
      expect(card.scrollWidth).toBeLessThanOrEqual(card.clientWidth);
      const grid = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.activity-stats')!;
      expect(grid.scrollWidth).toBeLessThanOrEqual(grid.clientWidth);
    });

    it('heatmapView_ContainerMeasured_WidthFollowsContainerAndWeeksFit', () => {
      component = createComponent();
      component.ngOnInit();

      component.heatmapContainerWidth.set(360);

      // (360 - 48 eksen payı) / 22 px en küçük hafta sütunu = 14 hafta; en yeni haftalar kalır.
      expect(component.activityHeatmapData().length).toBe(14);
      expect(component.activityHeatmapData()).toEqual(component.activityDataFromApi().slice(-14));
      expect(component.activityHeatmapView()[0]).toBe(360);
    });

    it('heatmapView_WideContainer_ShowsAllWeeksAtContainerWidth', () => {
      component = createComponent();
      component.ngOnInit();

      component.heatmapContainerWidth.set(1400);

      expect(component.activityHeatmapData().length).toBe(52);
      expect(component.activityHeatmapView()[0]).toBe(1400);
    });

    it('heatmapView_VeryNarrowContainer_KeepsMinimumWeeksAndScrollsOnlyInsideContainer', () => {
      component = createComponent();
      component.ngOnInit();

      component.heatmapContainerWidth.set(100);

      expect(component.activityHeatmapData().length).toBe(12);
      expect(component.activityHeatmapView()[0]).toBe(12 * 22 + 48);
    });

    it('heatmapScroll_Rendered_MeasuresItsContainerWidth', () => {
      const fixture = renderAtWidth(600);

      const scroll = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.heatmap-scroll')!;
      expect(fixture.componentInstance.heatmapContainerWidth()).toBe(scroll.clientWidth);
      expect(fixture.componentInstance.heatmapContainerWidth()).toBeGreaterThan(0);
    });
  });
});
