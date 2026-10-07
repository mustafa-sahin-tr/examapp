import {
  BADGE_PROGRESS_ROUTE,
  STUDENT_BOOKINGS_ROUTE,
  describeNotification,
  formatRelativeTime,
  parseUtcDate,
} from './notification-format';

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
    expect(result.badge).toBeNull();
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

  // Issue #309: liste etiketi için soru sırası; derin linke yazılmaz.
  it('questionComment_QuestionOrderExposedButNotInLink', () => {
    const result = describeNotification({
      type: 'WorksheetCommentCreated',
      data: data({ worksheetId: 12, questionId: 34, commentId: 56, rootCommentId: 56, questionOrder: 7 }),
    });

    expect(result.questionOrder).toBe(7);
    expect(result.route).toEqual({ commands: ['/test', 12], queryParams: { commentId: 56, questionId: 34 } });
  });

  it('questionOrder_InvalidOrWorksheetLevel_Null', () => {
    for (const order of [0, -2, 1.5, '3', null, undefined, Number.MAX_SAFE_INTEGER + 2]) {
      const result = describeNotification({
        type: 'WorksheetCommentReplied',
        data: data({ worksheetId: 12, questionId: 34, commentId: 57, rootCommentId: 56, questionOrder: order }),
      });
      expect(result.questionOrder).withContext(String(order)).toBeNull();
    }
    const worksheetLevel = describeNotification({
      type: 'WorksheetCommentCreated',
      data: data({ worksheetId: 12, questionId: null, commentId: 56, rootCommentId: 56, questionOrder: 2 }),
    });
    expect(worksheetLevel.questionOrder).toBeNull();
    expect(describeNotification({ type: 'BadgeEarned', data: data({ questionOrder: 2 }) }).questionOrder).toBeNull();
  });

  it('BadgeEarned_KeepsProgressRoute', () => {
    const result = describeNotification({ type: 'BadgeEarned', data: data({ badgeDefinitionId: 'b1', iconUrl: null }) });

    expect(result.icon).toBe('emoji_events');
    expect(result.route).toEqual({ commands: [BADGE_PROGRESS_ROUTE] });
    expect(result.badge).toEqual({ icon: null, iconUrl: null });
  });

  // Issue #149: medalyon girdileri ham taşınır; biçim doğrulaması medalyonda (resolveBadgeIcon).
  it('BadgeEarned_WithIconAndIconUrl_PassesBothToBadge', () => {
    const result = describeNotification({
      type: 'BadgeEarned',
      data: data({ badgeDefinitionId: 'b1', icon: ' gps_fixed ', iconUrl: 'achievements/a.svg' }),
    });

    expect(result.badge).toEqual({ icon: 'gps_fixed', iconUrl: 'achievements/a.svg' });
  });

  it('BadgeEarned_MalformedData_StillShowsBadgeWithoutIcons', () => {
    const result = describeNotification({ type: 'BadgeEarned', data: '{not json' });

    expect(result.badge).toEqual({ icon: null, iconUrl: null });
    expect(result.route).toEqual({ commands: [BADGE_PROGRESS_ROUTE] });
  });

  it('UnknownType_GenericIconNoLink', () => {
    expect(describeNotification({ type: 'Other', data: data({ worksheetId: 1, commentId: 2 }) })).toEqual({
      icon: 'notifications',
      badge: null,
      route: null,
      questionOrder: null,
    });
  });

  // Issue #298: öğretmen askıya alındı — öğrencinin randevu listesine link; data güvenilmeyen veri.
  it('BookingTeacherUnavailable_ValidData_EventBusyIconAndStudentBookingsLink', () => {
    const result = describeNotification({
      type: 'BookingTeacherUnavailable',
      data: data({ teacherId: 7, bookingIds: [11, 12] }),
    });

    expect(STUDENT_BOOKINGS_ROUTE).toBe('/my-bookings');
    expect(result).toEqual({
      icon: 'event_busy',
      badge: null,
      route: { commands: [STUDENT_BOOKINGS_ROUTE] },
      questionOrder: null,
    });
  });

  it('BookingTeacherUnavailable_EmptyBookingIds_StillLinks', () => {
    const result = describeNotification({ type: 'BookingTeacherUnavailable', data: data({ teacherId: 7, bookingIds: [] }) });

    expect(result.route).toEqual({ commands: [STUDENT_BOOKINGS_ROUTE] });
  });

  it('BookingTeacherUnavailable_InvalidData_EventBusyIconWithoutLink', () => {
    const bad = [
      null,
      '',
      'not-json',
      'null',
      '[1,2]',
      data({ bookingIds: [1] }),
      data({ teacherId: '7', bookingIds: [1] }),
      data({ teacherId: 0, bookingIds: [1] }),
      data({ teacherId: 1.5, bookingIds: [1] }),
      data({ teacherId: 7 }),
      data({ teacherId: 7, bookingIds: '1,2' }),
      data({ teacherId: 7, bookingIds: [1, -2] }),
      data({ teacherId: 7, bookingIds: [1, '2'] }),
      data({ teacherId: 7, bookingIds: [1, null] }),
      data({ teacherId: 7, bookingIds: Array.from({ length: 501 }, (_, i) => i + 1) }),
    ];
    for (const value of bad) {
      const result = describeNotification({ type: 'BookingTeacherUnavailable', data: value });
      expect(result.icon).withContext(String(value)).toBe('event_busy');
      expect(result.badge).withContext(String(value)).toBeNull();
      expect(result.route).withContext(String(value)).toBeNull();
    }
  });

  it('BookingTeacherUnavailable_HostileExtraFields_IgnoredInLink', () => {
    const result = describeNotification({
      type: 'BookingTeacherUnavailable',
      data: data({ teacherId: 7, bookingIds: [3], url: 'https://evil.test', route: '/admin' }),
    });

    expect(result.route).toEqual({ commands: ['/my-bookings'] });
  });

  // Issue #106 dilim b
  it('DirectMessageReceived_StudentSender_LinksTeacherInbox', () => {
    const result = describeNotification({
      type: 'DirectMessageReceived',
      data: JSON.stringify({ conversationId: 7, messageId: 3, senderRole: 'Student' }),
    });

    expect(result.route).toEqual({ commands: ['/student-messages'], queryParams: { conversation: 7 } });
    expect(result.icon).toBe('chat');
  });

  it('DirectMessageReceived_TeacherSender_LinksStudentPage', () => {
    const result = describeNotification({
      type: 'DirectMessageReceived',
      data: JSON.stringify({ conversationId: 9, senderRole: 'Teacher' }),
    });

    expect(result.route).toEqual({ commands: ['/teacher-messages'], queryParams: { conversation: 9 } });
  });

  it('DirectMessageReceived_BrokenOrHostileData_NoRoute', () => {
    for (const data of [null, '{bad', JSON.stringify({ conversationId: 'x', senderRole: 'Student' }),
      JSON.stringify({ conversationId: 5, senderRole: 'Admin' }), JSON.stringify({ conversationId: 0, senderRole: 'Teacher' })]) {
      expect(describeNotification({ type: 'DirectMessageReceived', data }).route).withContext(String(data)).toBeNull();
    }
  });

  it('DirectMessageReported_HasNoRouteYet', () => {
    expect(describeNotification({ type: 'DirectMessageReported', data: '{}' }).route).toBeNull();
  });

  // Issue #423 (veli bildirimleri)
  it('ParentNotifications_ToParent_LinkParentPageWithChild', () => {
    for (const type of ['ParentLinkedToParent', 'ParentHomeworkOverdue', 'ParentChildTestCompleted']) {
      const result = describeNotification({ type, data: JSON.stringify({ studentId: 21, linkId: 5 }) });
      expect(result.route).withContext(type).toEqual({ commands: ['/parent'], queryParams: { child: 21 } });
    }
  });

  it('ParentUnlinkedToParent_LinksParentPageWithoutChild', () => {
    const result = describeNotification({ type: 'ParentUnlinkedToParent', data: JSON.stringify({ linkId: 5, studentId: null }) });

    expect(result.route).toEqual({ commands: ['/parent'] });
    expect(result.icon).toBe('link_off');
  });

  it('ParentNotifications_BrokenOrHostileData_FallBackToPlainParentPage', () => {
    for (const data of [null, '{bad', JSON.stringify({ studentId: 'x' }), JSON.stringify({ studentId: -3 }),
      JSON.stringify({ studentId: 1.5 }), JSON.stringify({ studentId: 7, route: '/admin', url: 'https://evil.test' })]) {
      const route = describeNotification({ type: 'ParentHomeworkOverdue', data }).route;
      // Hiçbir durumda başka bir hedefe gitmez: ya düz /parent ya geçerli ?child.
      expect(route?.commands).withContext(String(data)).toEqual(['/parent']);
      expect(Object.keys(route?.queryParams ?? {}).every((k) => k === 'child')).withContext(String(data)).toBeTrue();
    }
  });

  it('ParentNotifications_ToStudent_HaveNoRoute', () => {
    for (const type of ['ParentLinkedToStudent', 'ParentUnlinkedToStudent']) {
      expect(describeNotification({ type, data: JSON.stringify({ linkId: 5 }) }).route).withContext(type).toBeNull();
    }
  });
});
