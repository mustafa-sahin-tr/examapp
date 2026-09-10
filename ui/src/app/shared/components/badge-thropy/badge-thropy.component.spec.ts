import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { BadgeThropyComponent } from './badge-thropy.component';
import { BadgeService, BadgeProgressItem, BadgeProgressResponse } from '../../../services/badge.service';
import { AuthService } from '../../../services/auth.service';

function buildBadge(overrides: Partial<BadgeProgressItem> = {}): BadgeProgressItem {
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

describe('BadgeThropyComponent', () => {
  let component: BadgeThropyComponent;
  let fixture: ComponentFixture<BadgeThropyComponent>;
  let badgeServiceSpy: jasmine.SpyObj<BadgeService>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    badgeServiceSpy = jasmine.createSpyObj<BadgeService>('BadgeService', ['getUserBadgeProgress']);
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['getUserIdFromLocalStorage']);
    authServiceSpy.getUserIdFromLocalStorage.and.returnValue(null);

    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse([])));

    await TestBed.configureTestingModule({
      imports: [BadgeThropyComponent],
      providers: [
        { provide: BadgeService, useValue: badgeServiceSpy },
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BadgeThropyComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('loadBadges_ApiSucceeds_HasErrorStaysFalseAndBadgesPopulated', () => {
    const badges = [buildBadge({ badgeDefinitionId: 'a' })];
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(badgeProgressResponse(badges)));

    fixture.detectChanges();

    expect(component.hasError()).toBeFalse();
    expect(component.badges().length).toBe(1);
  });

  it('loadBadges_ApiErrors_HasErrorBecomesTrueAndBadgeSignalsAreEmpty', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(throwError(() => new Error('boom')));

    expect(() => fixture.detectChanges()).not.toThrow();

    expect(component.hasError()).toBeTrue();
    expect(component.badges()).toEqual([]);
    expect(component.badgePaths()).toEqual([]);
    expect(component.standaloneBadges()).toEqual([]);
  });
});
