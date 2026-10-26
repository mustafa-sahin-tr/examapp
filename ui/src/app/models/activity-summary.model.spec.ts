import { ActivitySummaryResponse, toActivityProgressView } from './activity-summary.model';

describe('toActivityProgressView (#572)', () => {
  const badge = (code: string, current: number, target: number, earned = false) => ({
    code,
    name: code,
    current,
    target,
    earned,
  });

  it('mapsTodayAndClosestNotEarnedBadge', () => {
    const response: ActivitySummaryResponse = {
      today: { activityCount: 2, activeMinutes: 14, streakDays: 3 },
      badges: [badge('done', 10, 10, true), badge('activity-count-1', 8, 10), badge('other', 1, 50)],
    };

    expect(toActivityProgressView(response)).toEqual({
      today: { activityCount: 2, activeMinutes: 14, streakDays: 3 },
      badge: badge('activity-count-1', 8, 10),
    });
  });

  it('noActivityToday_HidesTodayCard', () => {
    const view = toActivityProgressView({ today: { activityCount: 0, activeMinutes: 0, streakDays: 4 }, badges: [] });

    expect(view).toEqual({ today: null, badge: null });
  });

  it('missingResponse_HidesBoth', () => {
    expect(toActivityProgressView(null)).toEqual({ today: null, badge: null });
    expect(toActivityProgressView({} as ActivitySummaryResponse)).toEqual({ today: null, badge: null });
  });
});
