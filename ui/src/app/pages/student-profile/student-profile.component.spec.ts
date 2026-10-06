import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { StudentProfileComponent } from './student-profile.component';
import { StudentService } from '../../services/student.service';
import { TestService } from '../../services/test.service';
import { BadgeProgressItem, BadgeService } from '../../services/badge.service';
import { StudentStatisticsResponse } from '../../models/statistics';
import { StudentProfile } from '../../models/student-profile';
import { BadgeThropyComponent } from '../../shared/components/badge-thropy/badge-thropy.component';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import studentProfileTr from '../../../../public/i18n/student-profile/tr.json';
import studentProfileEn from '../../../../public/i18n/student-profile/en.json';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({
  langs: { 'student-profile/tr': studentProfileTr, 'student-profile/en': studentProfileEn },
});

describe('StudentProfileComponent', () => {
  let component: StudentProfileComponent;
  let fixture: ComponentFixture<StudentProfileComponent>;
  let studentService: jasmine.SpyObj<StudentService>;
  let testService: jasmine.SpyObj<TestService>;
  let badgeService: jasmine.SpyObj<BadgeService>;

  const emptyStatistics: StudentStatisticsResponse = {
    total: {
      totalSolvedTests: 0,
      completedTests: 0,
      totalTimeSpentMinutes: 0,
      totalCorrectAnswers: 0,
      totalWrongAnswers: 0,
    },
    grouped: [],
  };

  beforeEach(async () => {
    studentService = jasmine.createSpyObj<StudentService>('StudentService', [
      'loadGrades',
      'getProfile',
      'updateGrade',
      'updateAvatar',
    ]);
    studentService.loadGrades.and.returnValue(of([]));
    studentService.getProfile.and.returnValue(of({} as StudentProfile));

    testService = jasmine.createSpyObj<TestService>('TestService', ['studentStatistics']);
    testService.studentStatistics.and.returnValue(of(emptyStatistics));

    badgeService = jasmine.createSpyObj<BadgeService>('BadgeService', ['getUserActivity', 'getUserBadgeProgress']);
    badgeService.getUserActivity.and.returnValue(
      of({ userId: 1, startDateUtc: '', endDateUtc: '', days: [] })
    );
    badgeService.getUserBadgeProgress.and.returnValue(
      of({
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
        badgeProgress: [],
        subjectBreakdown: [],
      })
    );

    await TestBed.configureTestingModule({
      imports: [StudentProfileComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: StudentService, useValue: studentService },
        { provide: TestService, useValue: testService },
        { provide: BadgeService, useValue: badgeService },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({}), data: {} }, params: of({}), queryParams: of({}) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StudentProfileComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  // "Badges" is the second mat-tab (index 1; issue #381 removed "Neler Var"); Angular Material lazily renders
  // tab content, so it must be selected before app-badge-thropy appears in the DOM.
  function activateBadgesTab(): void {
    component.activeTab = 1;
    fixture.detectChanges();
    tick(500); // mat-tab-group lazy-renders body content after the tab switch animation
    fixture.detectChanges();
  }

  it('activeTab_SetToBadgesTab_RendersBadgeThropyComponentInsteadOfMockBadgeBox', fakeAsync(() => {
    activateBadgesTab();

    const badgeThropyDebugElements = fixture.debugElement.nativeElement.querySelectorAll('app-badge-thropy');
    expect(badgeThropyDebugElements.length).toBeGreaterThan(0);
    expect(fixture.debugElement.nativeElement.querySelector('app-badge-box')).toBeNull();
  }));

  it('activeTab_SetToBadgesTab_RendersOnlyOneBadgeThropyInstance_NoDuplicateInInfoTab', fakeAsync(() => {
    activateBadgesTab();

    const badgeThropyInstances = fixture.debugElement.nativeElement.querySelectorAll('app-badge-thropy');
    expect(badgeThropyInstances.length).toBe(1);
  }));

  // #149 CR Ö1: sayfanın "Rozetlerim" bölüm başlığı varken bileşen kendi başlığını tekrar etmez.
  it('activeTab_SetToBadgesTab_BadgeThropyRendersWithoutOwnHeading', fakeAsync(() => {
    activateBadgesTab();

    const thropy = fixture.debugElement.query((debugEl) => debugEl.componentInstance instanceof BadgeThropyComponent);
    expect((thropy.componentInstance as BadgeThropyComponent).showHeading).toBeFalse();
    expect((thropy.nativeElement as HTMLElement).querySelector('.ms-badge-title')).toBeNull();
  }));

  // #322: "Bilgi" sekmesindeki eski `student.badges[].imageUrl` blogu kaldirildi; rozetler yalnizca
  // "Rozetler" sekmesindeki app-badge-thropy ile (tek bolum) gosterilir.
  it('activeTab_SetToInfoTab_DoesNotRenderLegacyBadgeImageBlock', fakeAsync(() => {
    studentService.getProfile.and.returnValue(of({ fullName: 'Ada Lovelace', gradeId: 5 } as StudentProfile));
    fixture.detectChanges();
    component.activeTab = 0;
    fixture.detectChanges();
    tick(500);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Ada Lovelace');
    expect(host.querySelector('.badges-container')).toBeNull();
    expect(host.querySelector('img.badge')).toBeNull();
    expect(host.textContent).not.toContain('Kazanılan Rozetler');
    expect(host.querySelectorAll('app-badge-thropy').length).toBe(0);
  }));

  describe('report user id (auth user id, not StudentProfileDto.id)', () => {
    let previousUser: string | null;

    beforeEach(() => {
      previousUser = localStorage.getItem('user');
      localStorage.setItem('user', JSON.stringify({ id: 7 }));
    });

    afterEach(() => {
      if (previousUser === null) {
        localStorage.removeItem('user');
      } else {
        localStorage.setItem('user', previousUser);
      }
    });

    // API (StudentProfileDto.Id) ogrenci KAYIT id'sini de gonderir; frontend modelinde alan yok, gercek yuku taklit ediyoruz.
    const profileWithStudentRecordId3 = { id: 3, fullName: 'Ada' } as unknown as StudentProfile;

    function findBadgeThropy(): BadgeThropyComponent {
      const debugEl = fixture.debugElement.query((el) => el.componentInstance instanceof BadgeThropyComponent);
      expect(debugEl).toBeTruthy();
      return debugEl.componentInstance as BadgeThropyComponent;
    }

    it('getProfile_ReturnsStudentRecordId3_DoesNotOverrideStoredUserId7', fakeAsync(() => {
      studentService.getProfile.and.returnValue(of(profileWithStudentRecordId3));

      fixture.detectChanges();

      expect(component.reportUserId).toBe(7);
      expect(badgeService.getUserActivity).toHaveBeenCalledOnceWith(7);
      expect(badgeService.getUserActivity).not.toHaveBeenCalledWith(3);
    }));

    it('activeTab_SetToBadgesTab_PassesStoredUserIdToBadgeThropy', fakeAsync(() => {
      studentService.getProfile.and.returnValue(of(profileWithStudentRecordId3));

      activateBadgesTab();

      expect(findBadgeThropy().userId).toBe(7);
      expect(badgeService.getUserBadgeProgress).toHaveBeenCalledWith(7);
      expect(badgeService.getUserBadgeProgress).not.toHaveBeenCalledWith(3);
    }));

    it('noStoredUser_DoesNotRequestActivityReport', () => {
      localStorage.removeItem('user');

      fixture.detectChanges();

      expect(component.reportUserId).toBeNull();
      expect(badgeService.getUserActivity).not.toHaveBeenCalled();
    });
  });

  // ── Issue #381 ───────────────────────────────────────────────────────────

  function tabLabels(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.mat-mdc-tab')).map(
      (tab) => tab.textContent?.trim() ?? ''
    );
  }

  function selectTab(index: number): void {
    component.activeTab = index;
    fixture.detectChanges();
    tick(500);
    fixture.detectChanges();
  }

  it('tabs_NoWhatsNewTab_InfoTabIsFirst', () => {
    fixture.detectChanges();

    expect(tabLabels()[0]).toBe(studentProfileTr.tabs.info);
    expect(tabLabels()).not.toContain('Neler Var');
    expect((fixture.nativeElement as HTMLElement).querySelector('.learning-container, .features-container')).toBeNull();
    expect('what' in studentProfileTr.tabs).toBeFalse();
    expect('learning' in studentProfileTr).toBeFalse();
  });

  it('badgesTab_NoFakePointCards_AndSectionIconsAreMaterialIcons', fakeAsync(() => {
    activateBadgesTab();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('app-point-card')).toBeNull();
    for (const fake of ['2,500', '3rd', '128']) {
      expect(host.textContent).withContext(fake).not.toContain(fake);
    }
    // Repoda olmayan assets/icons/*.png yerine Material ikon: kırık <img> yok.
    expect(host.querySelector('app-section-header img')).toBeNull();
    const icons = Array.from(host.querySelectorAll('app-section-header .section-material-icon')).map((i) =>
      i.textContent?.trim()
    );
    expect(icons).toEqual(['military_tech', 'leaderboard']);
  }));

  it('infoAndBadgesTabs_NoImagePointsToMissingAssets', fakeAsync(() => {
    const missing = /assets\/(experts|certification|learning-path|hands-on|icons\/)/;
    for (const index of [0, 1]) {
      selectTab(index);
      const sources = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('img')).map(
        (img) => img.getAttribute('src') ?? ''
      );
      for (const src of sources) {
        expect(src).withContext(`tab ${index}`).not.toMatch(missing);
      }
    }
  }));

  it('statistics_LabelsComeFromDictionary_NotRawKeys', () => {
    fixture.detectChanges();

    expect(component.single.map((c) => c.name)).toEqual([
      studentProfileTr.stats.completedTests,
      studentProfileTr.stats.studyMinutes,
      studentProfileTr.stats.solvedQuestions,
      studentProfileTr.stats.correctAnswers,
    ]);
    expect(component.single.some((c) => c.name.startsWith('stats.') || c.name.startsWith('student-profile.'))).toBeFalse();
  });

  it('statistics_LanguageChange_RelabelsCards', () => {
    fixture.detectChanges();

    TestBed.inject(TranslocoService).setActiveLang('en');

    expect(component.single.map((c) => c.name)).toEqual([
      studentProfileEn.stats.completedTests,
      studentProfileEn.stats.studyMinutes,
      studentProfileEn.stats.solvedQuestions,
      studentProfileEn.stats.correctAnswers,
    ]);
  });

  describe('real-data charts only', () => {
    let previousUser: string | null;

    beforeEach(() => {
      previousUser = localStorage.getItem('user');
      localStorage.setItem('user', JSON.stringify({ id: 7 }));
    });

    afterEach(() => {
      if (previousUser === null) {
        localStorage.removeItem('user');
      } else {
        localStorage.setItem('user', previousUser);
      }
    });

    it('weeklyCalendarTab_RendersOnlyTheBadgeServiceHeatmap', fakeAsync(() => {
      fixture.detectChanges();
      selectTab(3);
      const host = fixture.nativeElement as HTMLElement;

      expect(host.querySelectorAll('ngx-charts-heat-map').length).toBe(1);
      expect(host.querySelectorAll('.heatmap-container h2').length).toBe(1);
      expect(host.querySelector('.heatmap-container h2')?.textContent?.trim()).toBe(studentProfileTr.heatmap.title);
      expect(component.activityDataFromApi.length).toBe(52);
    }));

    it('assignmentsTab_NoMockBubbleChart', fakeAsync(() => {
      fixture.detectChanges();
      selectTab(2);

      expect((fixture.nativeElement as HTMLElement).querySelector('app-bubble-chart, ngx-charts-bubble-chart')).toBeNull();
    }));

    function shortDayNames(locale: string): Set<string> {
      return new Set(
        Array.from({ length: 7 }, (_, i) => new Date(2024, 0, 1 + i).toLocaleDateString(locale, { weekday: 'short' }))
      );
    }

    function expectHeatmapIn(locale: string): void {
      const dayNames = shortDayNames(locale);
      for (const week of component.activityDataFromApi) {
        expect(week.name).not.toContain('Week');
        for (const day of week.series) {
          expect(dayNames.has(day.name)).withContext(`${locale}: ${day.name}`).toBeTrue();
        }
      }
    }

    it('apiHeatmap_DayNamesInActiveLanguage_WeekNamesNotEnglishWeekN', () => {
      fixture.detectChanges();

      expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('tr');
      expectHeatmapIn('tr');
      expect(component.activityDataFromApi.some((w) => w.series.some((d) => d.name === 'Pzt'))).toBeTrue();
    });

    it('apiHeatmap_LanguageChange_RegeneratesLabels_WithoutRefetch', () => {
      fixture.detectChanges();

      TestBed.inject(TranslocoService).setActiveLang('en');

      expectHeatmapIn('en-US');
      expect(component.activityDataFromApi.some((w) => w.series.some((d) => d.name === 'Mon'))).toBeTrue();
      expect(badgeService.getUserActivity).toHaveBeenCalledTimes(1);
      // Tikler de yeni dilin hafta adlarıyla eşleşir.
      const ticks = component.activityDataFromApi.map((w) => component.xAxisTickFormatting(w.name)).filter((t) => t);
      expect(ticks.length).toBeGreaterThanOrEqual(11);
    });

    it('xAxisTickFormatting_MonthOnlyOnFirstWeekOfMonth_AndStateless', () => {
      fixture.detectChanges();
      const names = component.activityDataFromApi.map((w) => w.name);
      const ticks = names.map((n) => component.xAxisTickFormatting(n));
      const nonEmpty = ticks.filter((t) => t !== '');

      // 52 haftada 11-13 ay başı; ikinci tur aynı sonucu verir (önceki çizime bağlı durum yok).
      expect(nonEmpty.length).toBeGreaterThanOrEqual(11);
      expect(nonEmpty.length).toBeLessThanOrEqual(13);
      expect(names.map((n) => component.xAxisTickFormatting(n))).toEqual(ticks);
    });
  });

  describe('Rozetlerim box states', () => {
    const emptySummary = {
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
    };

    function badgeHost(): HTMLElement {
      return (fixture.nativeElement as HTMLElement).querySelector('app-badge-thropy') as HTMLElement;
    }

    it('withBadges_RendersBadgeItems', fakeAsync(() => {
      const badge = {
        badgeDefinitionId: 'b1',
        name: 'İlk Adım',
        description: 'İlk sorunu çöz',
        currentValue: 1,
        targetValue: 1,
        isCompleted: true,
        earnedDateUtc: '2026-01-01T00:00:00Z',
      } as BadgeProgressItem;
      badgeService.getUserBadgeProgress.and.returnValue(
        of({ summary: emptySummary, badgeProgress: [badge], subjectBreakdown: [] })
      );

      activateBadgesTab();

      expect(badgeHost().querySelectorAll('.ms-badge-item').length).toBe(1);
      expect(badgeHost().textContent).toContain('İlk Adım');
    }));

    it('noBadges_RendersEmptyState', fakeAsync(() => {
      activateBadgesTab();

      expect(badgeHost().querySelector('.ms-badge-empty')).not.toBeNull();
    }));

    it('loadError_RendersErrorWithRetry', fakeAsync(() => {
      spyOn(console, 'error');
      badgeService.getUserBadgeProgress.and.returnValue(throwError(() => new Error('500')));

      activateBadgesTab();

      expect(badgeHost().querySelector('.ms-badge-error[role="alert"] button')).not.toBeNull();
    }));
  });
});
