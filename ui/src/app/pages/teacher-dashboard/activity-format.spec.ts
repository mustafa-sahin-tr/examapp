import { accuracyPercent, formatActivityDuration, formatActivityPeriod } from './activity-format';

describe('activity-format (issue #56)', () => {
  describe('formatActivityDuration', () => {
    it('formatActivityDuration_ZeroSeconds_ReturnsSecondsKey', () => {
      expect(formatActivityDuration(0)).toEqual({ key: 'activity.duration.seconds', params: { s: 0 } });
    });

    it('formatActivityDuration_UnderOneMinute_ReturnsSecondsOnly', () => {
      expect(formatActivityDuration(45)).toEqual({ key: 'activity.duration.seconds', params: { s: 45 } });
    });

    it('formatActivityDuration_MinutesAndSeconds_ReturnsMinutesSeconds', () => {
      expect(formatActivityDuration(150)).toEqual({ key: 'activity.duration.minutesSeconds', params: { m: 2, s: 30 } });
    });

    it('formatActivityDuration_WholeMinutes_OmitsSeconds', () => {
      expect(formatActivityDuration(120)).toEqual({ key: 'activity.duration.minutes', params: { m: 2 } });
    });

    it('formatActivityDuration_HoursAndMinutes_DropsSeconds', () => {
      // 2 sa 5 dk 59 sn → "2 sa 5 dk"
      expect(formatActivityDuration(2 * 3600 + 5 * 60 + 59)).toEqual({
        key: 'activity.duration.hoursMinutes',
        params: { h: 2, m: 5 },
      });
    });

    it('formatActivityDuration_WholeHours_OmitsMinutes', () => {
      expect(formatActivityDuration(3600 + 30)).toEqual({ key: 'activity.duration.hours', params: { h: 1 } });
    });

    it('formatActivityDuration_NegativeOrNaN_TreatedAsZero', () => {
      expect(formatActivityDuration(-10)).toEqual({ key: 'activity.duration.seconds', params: { s: 0 } });
      expect(formatActivityDuration(Number.NaN)).toEqual({ key: 'activity.duration.seconds', params: { s: 0 } });
    });
  });

  describe('accuracyPercent', () => {
    it('accuracyPercent_NoQuestionsSolved_ReturnsNullInsteadOfDividingByZero', () => {
      expect(accuracyPercent(0, 0)).toBeNull();
    });

    it('accuracyPercent_HalfCorrect_ReturnsFifty', () => {
      expect(accuracyPercent(8, 16)).toBe(50);
    });

    it('accuracyPercent_NonIntegerRatio_RoundsToWholeNumber', () => {
      expect(accuracyPercent(1, 3)).toBe(33);
      expect(accuracyPercent(2, 3)).toBe(67);
    });

    it('accuracyPercent_ZeroCorrect_ReturnsZeroNotNull', () => {
      expect(accuracyPercent(0, 5)).toBe(0);
    });
  });

  describe('formatActivityPeriod', () => {
    it('formatActivityPeriod_SevenDaysSameMonth_CoversTodayAndSixDaysBefore', () => {
      const text = formatActivityPeriod(7, new Date(Date.UTC(2026, 8, 23, 15, 30)), 'tr');

      expect(text).toContain('17');
      expect(text).toContain('23 Eylül 2026');
    });

    it('formatActivityPeriod_CrossesMonth_ShowsBothMonths', () => {
      const text = formatActivityPeriod(7, new Date(Date.UTC(2026, 9, 2, 12)), 'tr');

      expect(text).toContain('26 Eylül');
      expect(text).toContain('2 Ekim 2026');
    });

    it('formatActivityPeriod_SingleDay_ShowsOnlyToday', () => {
      expect(formatActivityPeriod(1, new Date(Date.UTC(2026, 8, 23, 12)), 'tr')).toBe('23 Eylül 2026');
    });

    it('formatActivityPeriod_AfterTurkeyMidnight_UsesTurkeyLocalDayLikeBackend', () => {
      // 2026-09-24T22:30Z = 25 Eylül 01:30 (Europe/Istanbul, UTC+3) → pencere 25 Eylül'de kapanır (#265).
      expect(formatActivityPeriod(1, new Date(Date.UTC(2026, 8, 24, 22, 30)), 'tr')).toBe('25 Eylül 2026');

      const week = formatActivityPeriod(7, new Date(Date.UTC(2026, 8, 24, 22, 30)), 'tr');
      expect(week).toContain('19');
      expect(week).toContain('25 Eylül 2026');
    });

    it('formatActivityPeriod_BeforeTurkeyMidnight_StaysOnSameLocalDay', () => {
      // 20:59Z = 23:59 TR → hâlâ 24 Eylül; 21:00Z = 00:00 TR → 25 Eylül.
      expect(formatActivityPeriod(1, new Date(Date.UTC(2026, 8, 24, 20, 59)), 'tr')).toBe('24 Eylül 2026');
      expect(formatActivityPeriod(1, new Date(Date.UTC(2026, 8, 24, 21, 0)), 'tr')).toBe('25 Eylül 2026');
    });
  });
});
