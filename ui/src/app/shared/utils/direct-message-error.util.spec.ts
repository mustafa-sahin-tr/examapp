import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { directMessageError } from './direct-message-error.util';

describe('directMessageError (issue #106)', () => {
  const t = (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key);

  const httpError = (status: number, error: unknown, headers?: HttpHeaders) =>
    new HttpErrorResponse({ status, error, headers });

  it('CannotMessageTeacher_AlwaysLocalNeutralText_IgnoresBackendMessage', () => {
    const info = directMessageError(
      httpError(403, { success: false, message: 'Öğretmen sizi engelledi', errorCode: 'CannotMessageTeacher' }),
      t,
    );

    expect(info.code).toBe('CannotMessageTeacher');
    expect(info.message).toBe('errors.codes.CannotMessageTeacher');
    expect(info.message).not.toContain('engel');
  });

  it('RelationshipEnded_MapsToLocalCode', () => {
    const info = directMessageError(httpError(403, { errorCode: 'RelationshipEnded', message: 'x' }), t);
    expect(info.code).toBe('RelationshipEnded');
    expect(info.message).toBe('errors.codes.RelationshipEnded');
  });

  it('ConversationNotFound404_And_NameLookupUnavailable503_AreRecognized', () => {
    expect(directMessageError(httpError(404, { errorCode: 'ConversationNotFound' }), t).message).toBe(
      'errors.codes.ConversationNotFound',
    );
    expect(directMessageError(httpError(503, { errorCode: 'NameLookupUnavailable' }), t).message).toBe(
      'errors.codes.NameLookupUnavailable',
    );
  });

  it('429_WithRetryAfter_UsesSecondsText_WithoutLeakingBackendMessage', () => {
    const info = directMessageError(
      httpError(429, { errorCode: 'RateLimited', message: 'günlük yeni konuşma sınırı' }, new HttpHeaders({ 'Retry-After': '42' })),
      t,
    );
    expect(info.code).toBe('RateLimited');
    expect(info.message).toBe('errors.rateLimitedRetry:{"seconds":42}');
  });

  it('429_WithoutRetryAfter_GenericRateLimited', () => {
    expect(directMessageError(httpError(429, null), t).message).toBe('errors.rateLimited');
  });

  it('network_And_NonHttpErrors_UseLocalText', () => {
    expect(directMessageError(httpError(0, null), t).message).toBe('errors.network');
    expect(directMessageError(new Error('x'), t, 'conversation.loadError').message).toBe('conversation.loadError');
  });

  it('unknownOrMissingCode_NeverShowsBackendMessage_UsesFallback', () => {
    expect(directMessageError(httpError(400, { message: 'Bilinmeyen hata', errorCode: 'Other' }), t).message).toBe('errors.generic');
    expect(directMessageError(httpError(500, { message: 'Npgsql timeout at ...' }), t, 'teacher.error').message).toBe(
      'teacher.error',
    );
  });

  it('removedNotConversationParty_IsUnknown; newBackendCodes_Recognized', () => {
    expect(directMessageError(httpError(403, { errorCode: 'NotConversationParty' }), t).code).toBeNull();
    for (const code of ['BlockTeacherOnly', 'SearchTooShort', 'InvalidUpToMessageId']) {
      expect(directMessageError(httpError(400, { errorCode: code }), t).message).toBe(`errors.codes.${code}`);
    }
  });
});
