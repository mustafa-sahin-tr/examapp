import { TestBed } from '@angular/core/testing';

import { invitationOf, parentStatusOf } from '../shared/testing/parent-status-testing';
import { ParentStatusStore } from './parent-status.store';

/** Issue #440 / #441: veli durumu store'unun türetilmiş bayrakları. */
describe('ParentStatusStore', () => {
  let store: ParentStatusStore;

  beforeEach(() => {
    store = TestBed.inject(ParentStatusStore);
  });

  it('initially_NotLoaded_AllFlagsFalse', () => {
    expect(store.loaded()).toBeFalse();
    expect(store.activationRestricted()).toBeFalse();
    expect(store.expired()).toBeFalse();
    expect(store.graceBanner()).toBeFalse();
    expect(store.finalWarning()).toBeFalse();
    expect(store.invitation()).toBeNull();
  });

  it('awaitingActivation_RestrictedWithInvitation', () => {
    store.set(
      parentStatusOf({
        mode: 'AwaitingActivation',
        invitation: {
          parentName: 'Ayşe',
          maskedEmail: 'a***@g***.com',
          expiresAt: '2026-10-22T10:00:00Z',
          expired: false,
          canResend: true,
          resendsLeft: 5,
        },
      })
    );
    expect(store.activationRestricted()).toBeTrue();
    expect(store.invitation()?.maskedEmail).toBe('a***@g***.com');
  });

  it('grace_NotFinal_ShowsBannerOnly', () => {
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 12 }));
    expect(store.graceBanner()).toBeTrue();
    expect(store.finalWarning()).toBeFalse();
    expect(store.daysLeft()).toBe(12);
  });

  it('grace_FinalWarning_ModalInsteadOfBanner', () => {
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 5, isFinalWarning: true }));
    expect(store.graceBanner()).toBeFalse();
    expect(store.finalWarning()).toBeTrue();
  });

  it('expired_ExpiredFlagButNoModal', () => {
    store.set(parentStatusOf({ mode: 'Expired', daysLeft: 0, isFinalWarning: true }));
    expect(store.expired()).toBeTrue();
    expect(store.finalWarning()).toBeFalse();
  });

  it('set_IgnoresMissingOrMalformedStatus_ResetClears', () => {
    store.set(parentStatusOf({ mode: 'Grace' }));
    store.set(null);
    store.set(undefined);
    expect(store.mode()).toBe('Grace');

    store.reset();
    expect(store.loaded()).toBeFalse();
  });

  it('expiredWithExpiredInvitation_ExposesInvitation (#440 review)', () => {
    store.set(parentStatusOf({ mode: 'Expired', daysLeft: 0, isFinalWarning: true, invitation: invitationOf({ expired: true }) }));
    expect(store.invitation()?.expired).toBeTrue();
    expect(store.activationRestricted()).toBeFalse();
  });

  it('awaitingDeadline_OnlyInAwaitingActivationWithDaysLeft', () => {
    store.set(parentStatusOf({ mode: 'AwaitingActivation', daysLeft: 6, isFinalWarning: true, invitation: invitationOf() }));
    expect(store.awaitingDeadline()).toEqual({ daysLeft: 6, warning: true });
    expect(store.finalWarning()).toBeFalse();

    store.set(parentStatusOf({ mode: 'AwaitingActivation', invitation: invitationOf() }));
    expect(store.awaitingDeadline()).toBeNull();

    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 6 }));
    expect(store.awaitingDeadline()).toBeNull();
  });
});
