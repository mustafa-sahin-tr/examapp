import { HttpErrorResponse } from '@angular/common/http';

import {
  PARENT_INVITATION_ERROR_CODES,
  ParentInvitationErrorBody,
  formatServerDateTime,
} from '../../models/parent-status.model';

/** Kök sözlükte (`parentRequirement.errors.*`) çevrilecek anahtar + parametreler. */
export interface ParentInvitationErrorText {
  key: string;
  params?: Record<string, string>;
}

const PREFIX = 'parentRequirement.errors.';

const SIMPLE_KEYS: Readonly<Record<string, string>> = {
  [PARENT_INVITATION_ERROR_CODES.parentInfoRequired]: 'parentInfoRequired',
  [PARENT_INVITATION_ERROR_CODES.invalidParentName]: 'invalidParentName',
  [PARENT_INVITATION_ERROR_CODES.invalidParentEmail]: 'invalidParentEmail',
  [PARENT_INVITATION_ERROR_CODES.invalidParentPhone]: 'invalidParentPhone',
  [PARENT_INVITATION_ERROR_CODES.parentEmailNotUsable]: 'parentEmailNotUsable',
  [PARENT_INVITATION_ERROR_CODES.parentAlreadyLinked]: 'parentAlreadyLinked',
  [PARENT_INVITATION_ERROR_CODES.parentNotRequired]: 'parentNotRequired',
  [PARENT_INVITATION_ERROR_CODES.noOpenInvitation]: 'noOpenInvitation',
  [PARENT_INVITATION_ERROR_CODES.unavailable]: 'unavailable',
  [PARENT_INVITATION_ERROR_CODES.busy]: 'busy',
};

/**
 * Issue #440: öğrencinin veli daveti uçlarının (`student-parent/invitation[/resend]`) hata kodu → sözlük anahtarı. Kilit /
 * gönderim sınırı 429'larında `retryAt` aktif dilde tarihe çevrilir. Bilinmeyen kod → genel metin; 502 → `unavailable`.
 */
export function describeParentInvitationError(err: unknown, locale: string): ParentInvitationErrorText {
  const body =
    err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
      ? (err.error as ParentInvitationErrorBody)
      : null;
  const code = body?.errorCode ?? '';
  const date = formatServerDateTime(body?.retryAt, locale);

  if (code === PARENT_INVITATION_ERROR_CODES.parentEmailChangeLocked) {
    return date ? { key: `${PREFIX}parentEmailChangeLocked`, params: { date } } : { key: `${PREFIX}parentEmailChangeLockedNoDate` };
  }
  if (code === PARENT_INVITATION_ERROR_CODES.resendLimited) {
    return date ? { key: `${PREFIX}resendLimited`, params: { date } } : { key: `${PREFIX}resendLimitedNoDate` };
  }
  const simple = SIMPLE_KEYS[code];
  if (simple) {
    return { key: `${PREFIX}${simple}` };
  }
  if (err instanceof HttpErrorResponse && err.status === 502) {
    return { key: `${PREFIX}unavailable` };
  }
  return { key: `${PREFIX}generic` };
}
