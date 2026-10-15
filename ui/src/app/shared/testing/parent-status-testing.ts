import { ParentInvitationSummary, ParentStatus } from '../../models/parent-status.model';

/** Issue #440 / #441 spec'leri için veli durumu üreticileri. */
export function parentStatusOf(overrides: Partial<ParentStatus> = {}): ParentStatus {
  return { mode: 'Ok', requiresParent: true, graceDays: 30, isFinalWarning: false, ...overrides };
}

export function invitationOf(overrides: Partial<ParentInvitationSummary> = {}): ParentInvitationSummary {
  return {
    parentName: 'Ayşe Yılmaz',
    maskedEmail: 'a***@g***.com',
    expiresAt: '2026-10-22T10:00:00Z',
    expired: false,
    canResend: true,
    nextResendAt: null,
    resendsLeft: 5,
    emailChangeLockedUntil: null,
    devActivationUrl: null,
    ...overrides,
  };
}

export function awaitingStatusOf(invitation: Partial<ParentInvitationSummary> = {}): ParentStatus {
  return parentStatusOf({ mode: 'AwaitingActivation', invitation: invitationOf(invitation) });
}
