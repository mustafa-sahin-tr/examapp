import { BADGE_PROGRESS_ROUTE, describeNotification, formatRelativeTime, parseUtcDate } from './notification-format';

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

describe('describeNotification (issue #105)', () => {
  const data = (value: unknown) => JSON.stringify(value);

  it('WorksheetCommentCreated_QuestionComment_ForumIconAndDeepLinkWithQuestion', () => {
    const result = describeNotification({
      type: 'WorksheetCommentCreated',
      data: data({ worksheetId: 12, questionId: 34, commentId: 56, rootCommentId: 56 }),
    });

    expect(result.icon).toBe('forum');
    expect(result.iconUrl).toBeNull();
    expect(result.route).toEqual({ commands: ['/test', 12], queryParams: { commentId: 56, questionId: 34 } });
  });

  it('WorksheetCommentReplied_WorksheetLevelReply_DeepLinkWithRootAndNoQuestion', () => {
    const result = describeNotification({
      type: 'WorksheetCommentReplied',
      data: data({ worksheetId: 12, questionId: null, commentId: 57, rootCommentId: 56 }),
    });

    expect(result.icon).toBe('forum');
    expect(result.route).toEqual({ commands: ['/test', 12], queryParams: { commentId: 57, rootCommentId: 56 } });
  });

  it('WorksheetComment_InvalidOrMissingData_ForumIconWithoutLink', () => {
    for (const bad of [null, 'not-json', data({ worksheetId: 'x', commentId: 1 }), data({ worksheetId: 1 }), data({ worksheetId: -1, commentId: 2 })]) {
      const result = describeNotification({ type: 'WorksheetCommentCreated', data: bad });
      expect(result.icon).withContext(String(bad)).toBe('forum');
      expect(result.route).withContext(String(bad)).toBeNull();
    }
  });

  it('WorksheetComment_ExtraOrHostileFields_Ignored', () => {
    const result = describeNotification({
      type: 'WorksheetCommentReplied',
      data: data({ worksheetId: 3, commentId: 4, questionId: '../x', rootCommentId: 1.5, url: 'https://evil.test' }),
    });

    expect(result.route).toEqual({ commands: ['/test', 3], queryParams: { commentId: 4 } });
  });

  it('BadgeEarned_KeepsProgressRoute', () => {
    const result = describeNotification({ type: 'BadgeEarned', data: data({ badgeDefinitionId: 'b1', iconUrl: null }) });

    expect(result.icon).toBe('emoji_events');
    expect(result.route).toEqual({ commands: [BADGE_PROGRESS_ROUTE] });
  });

  it('UnknownType_GenericIconNoLink', () => {
    expect(describeNotification({ type: 'Other', data: data({ worksheetId: 1, commentId: 2 }) })).toEqual({
      icon: 'notifications',
      iconUrl: null,
      route: null,
    });
  });
});
