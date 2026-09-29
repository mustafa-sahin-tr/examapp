import { formatRelativeTime, parseUtcDate } from './notification-format';

describe('notification-format (issue #146)', () => {
  const now = Date.UTC(2026, 8, 30, 12, 0, 0);

  it('parseUtcDate_NoZoneSuffix_TreatsAsUtc', () => {
    expect(parseUtcDate('2026-09-30T11:00:00').getTime()).toBe(Date.UTC(2026, 8, 30, 11, 0, 0));
    expect(parseUtcDate('2026-09-30T11:00:00Z').getTime()).toBe(Date.UTC(2026, 8, 30, 11, 0, 0));
  });

  it('formatRelativeTime_PicksLargestUnit', () => {
    expect(formatRelativeTime('2026-09-30T11:57:00Z', now, 'en-US')).toBe('3 minutes ago');
    expect(formatRelativeTime('2026-09-30T09:00:00Z', now, 'en-US')).toBe('3 hours ago');
    expect(formatRelativeTime('2026-09-29T12:00:00Z', now, 'en-US')).toBe('yesterday');
  });

  it('formatRelativeTime_UnderAMinute_ReturnsNow', () => {
    expect(formatRelativeTime('2026-09-30T11:59:40Z', now, 'en-US')).toBe('now');
  });

  it('formatRelativeTime_InvalidDate_ReturnsEmpty', () => {
    expect(formatRelativeTime('not-a-date', now, 'en-US')).toBe('');
  });
});
