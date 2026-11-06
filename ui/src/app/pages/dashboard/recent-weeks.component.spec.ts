import { activityLevel, buildRecentWeeks, dayKey } from './recent-weeks.component';

describe('buildRecentWeeks (#835)', () => {
  // 2026-11-06 cuma.
  const now = new Date(2026, 10, 6, 15, 0, 0);

  it('fourMondayStartingWeeks_LastRowIsThisWeek_FutureDaysEmpty', () => {
    const rows = buildRecentWeeks([], 4, now);

    expect(rows.length).toBe(4);
    expect(rows.every((r) => r.length === 7)).toBe(true);
    expect(rows[0][0].key).toBe('2026-10-12');
    expect(rows[3][0].key).toBe('2026-11-02');
    expect(rows[3][4]).toMatchObject({ key: '2026-11-06', today: true, future: false });
    expect(rows[3][5].future).toBe(true);
    expect(rows[3][6].future).toBe(true);
  });

  it('sumsQuestionsPerUtcDayAndAssignsLevels', () => {
    const rows = buildRecentWeeks(
      [
        { dateUtc: '2026-11-05T00:00:00Z', questionCount: 3, correctCount: 0, totalTimeSeconds: 0, totalPoints: 0, activityScore: 0 },
        { dateUtc: '2026-11-04T00:00:00Z', questionCount: 20, correctCount: 0, totalTimeSeconds: 0, totalPoints: 0, activityScore: 0 },
        { dateUtc: '2026-08-01T00:00:00Z', questionCount: 99, correctCount: 0, totalTimeSeconds: 0, totalPoints: 0, activityScore: 0 },
      ],
      4,
      now
    );
    const cells = rows.flat();

    expect(cells.find((c) => c.key === '2026-11-05')).toMatchObject({ questions: 3, level: 1 });
    expect(cells.find((c) => c.key === '2026-11-04')).toMatchObject({ questions: 20, level: 3 });
    expect(cells.some((c) => c.key === '2026-08-01')).toBe(false);
  });

  it('levelsAndKeys', () => {
    expect([0, 1, 4, 5, 14, 15].map(activityLevel)).toEqual([0, 1, 1, 2, 2, 3]);
    expect(dayKey(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
});
