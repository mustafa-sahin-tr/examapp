import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { DashboardComponent } from './dashboard.component';
import { TestService } from '../../services/test.service';
import { BadgeService, BadgeProgressItem, BadgeProgressResponse } from '../../services/badge.service';
import { StudentResetService } from '../../services/student-reset.service';
import { StudentService } from '../../services/student.service';

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
      imports: [DashboardComponent],
      providers: [
        { provide: TestService, useValue: testServiceSpy },
        { provide: BadgeService, useValue: badgeServiceSpy },
        { provide: StudentResetService, useValue: jasmine.createSpyObj('StudentResetService', ['resetMyData']) },
        { provide: StudentService, useValue: studentServiceSpy },
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
});
