import { BadgeProgressItem, BadgeProgressSummary } from '../../services/badge.service';
import { dailyStreakFrom } from './daily-streak.util';

function badge(overrides: Partial<BadgeProgressItem>): BadgeProgressItem {
  return {
    badgeDefinitionId: 'b',
    name: '',
    description: '',
    iconUrl: '',
    pathKey: 'streak-path',
    pathName: null,
    pathOrder: null,
    currentValue: 0,
    targetValue: 5,
    isCompleted: false,
    earnedDateUtc: null,
    ...overrides,
  };
}

const summary = (current: number) => ({ currentActivityStreak: current, bestActivityStreak: 30 }) as BadgeProgressSummary;

describe('dailyStreakFrom', () => {
  const ladder = [
    badge({ badgeDefinitionId: 's1', targetValue: 5, currentValue: 5, isCompleted: true }),
    badge({ badgeDefinitionId: 's2', targetValue: 10, currentValue: 10, isCompleted: false }),
    badge({ badgeDefinitionId: 's3', targetValue: 15, currentValue: 12 }),
  ];

  it('count_ComesFromSummaryNotBadgeCurrentValue_NextIsSmallestOpenTargetAboveCount', () => {
    expect(dailyStreakFrom(summary(6), ladder)).toEqual({ count: 6, nextTarget: 10, remaining: 4 });
  });

  it('brokenStreak_CurrentZero_ReturnsNull', () => {
    expect(dailyStreakFrom(summary(0), ladder)).toBeNull();
  });

  it('noStreakBadges_ReturnsNull', () => {
    expect(dailyStreakFrom(summary(4), [badge({ pathKey: 'other' })])).toBeNull();
  });

  it('missingSummary_ReturnsNull', () => {
    expect(dailyStreakFrom(null, ladder)).toBeNull();
  });

  it('allTargetsPassed_NextIsNull', () => {
    expect(dailyStreakFrom(summary(20), ladder)).toEqual({ count: 20, nextTarget: null, remaining: null });
  });
});
