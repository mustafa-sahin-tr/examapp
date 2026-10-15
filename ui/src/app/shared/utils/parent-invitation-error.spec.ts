import { HttpErrorResponse } from '@angular/common/http';

import { describeParentInvitationError } from './parent-invitation-error';

/** Issue #440: veli daveti hata kodu → sözlük anahtarı. */
describe('describeParentInvitationError', () => {
  const err = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it('knownCodes_MapToKeys', () => {
    expect(describeParentInvitationError(err(409, { errorCode: 'ParentEmailNotUsable' }), 'tr-TR').key).toBe(
      'parentRequirement.errors.parentEmailNotUsable'
    );
    expect(describeParentInvitationError(err(400, { errorCode: 'InvalidParentPhone' }), 'tr-TR').key).toBe(
      'parentRequirement.errors.invalidParentPhone'
    );
    expect(describeParentInvitationError(err(409, { errorCode: 'ParentAlreadyLinked' }), 'tr-TR').key).toBe(
      'parentRequirement.errors.parentAlreadyLinked'
    );
  });

  it('busy409_MapsToBusy (#440 review)', () => {
    expect(describeParentInvitationError(err(409, { errorCode: 'Busy' }), 'tr-TR').key).toBe('parentRequirement.errors.busy');
  });

  it('lock429_WithRetryAt_IncludesDate', () => {
    const text = describeParentInvitationError(
      err(429, { errorCode: 'ParentEmailChangeLocked', retryAt: '2026-10-15T12:00:00Z' }),
      'tr-TR'
    );
    expect(text.key).toBe('parentRequirement.errors.parentEmailChangeLocked');
    expect(text.params?.['date']).toContain('2026');
  });

  it('resendLimited429_WithoutRetryAt_UsesNoDateKey', () => {
    expect(
      describeParentInvitationError(err(429, { errorCode: 'ParentInvitationResendLimited', retryAt: null }), 'tr-TR').key
    ).toBe('parentRequirement.errors.resendLimitedNoDate');
  });

  it('bare502_IsUnavailable_UnknownIsGeneric', () => {
    expect(describeParentInvitationError(err(502, null), 'tr-TR').key).toBe('parentRequirement.errors.unavailable');
    expect(describeParentInvitationError(err(500, { errorCode: 'Whatever' }), 'tr-TR').key).toBe(
      'parentRequirement.errors.generic'
    );
    expect(describeParentInvitationError(new Error('x'), 'tr-TR').key).toBe('parentRequirement.errors.generic');
  });
});
