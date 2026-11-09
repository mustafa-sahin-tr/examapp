import { TestBed } from '@angular/core/testing';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';

import { EntitlementStateService } from './entitlement-state.service';
import { AuthService } from './auth.service';
import { SubscriptionService } from './subscription.service';
import { CurrentSubscriptionPlan, FeatureUsage } from '../models/subscription';

const usage = (featureKey: string, limit: number | null): FeatureUsage => ({
  featureKey,
  kind: 'Metered',
  unit: null,
  allowed: limit !== 0,
  limit,
  used: 0,
  remaining: limit,
  resetsAtUtc: null,
});

const BASIC: CurrentSubscriptionPlan = {
  planKey: 'teacher-basic',
  planName: 'Basic',
  displayNameKey: 'subscription.plans.teacherBasic',
  validToUtc: null,
  trialAvailable: false,
  features: [usage('manual-labeling', null), usage('auto-detect', 0), usage('canvas-ai', 0)],
};

/** Issue #1087: arayüzün "özellik planımda var mı" sorusu. */
describe('EntitlementStateService (issue #1087)', () => {
  let current: ReturnType<typeof vi.fn>;
  let roles: string[];

  function setup(): EntitlementStateService {
    TestBed.configureTestingModule({
      providers: [
        { provide: SubscriptionService, useValue: { current } },
        { provide: AuthService, useValue: { hasRealmRole: (r: string) => roles.includes(r) } },
      ],
    });
    return TestBed.inject(EntitlementStateService);
  }

  beforeEach(() => {
    roles = ['Teacher'];
    current = vi.fn().mockReturnValue(of(BASIC));
  });

  it('answers from the plan: limit 0 is locked, a row with a limit or null is open, a missing row is locked', async () => {
    const state = setup();
    await firstValueFrom(state.load());

    expect(state.has('manual-labeling')).toBe(true);
    expect(state.has('auto-detect')).toBe(false);
    expect(state.has('canvas-ai')).toBe(false);
    expect(state.has('mock-exam')).toBe(false);
  });

  it('lets an admin through features outside the role audience', async () => {
    roles = ['Admin'];
    const state = setup();
    await firstValueFrom(state.load());

    expect(state.has('mock-exam')).toBe(true);
  });

  it('stays open before loading and when the plan cannot be read (the server decides)', async () => {
    current.mockReturnValue(throwError(() => new Error('offline')));
    const state = setup();
    expect(state.has('auto-detect')).toBe(true);

    await firstValueFrom(state.load());
    expect(state.loaded()).toBe(true);
    expect(state.has('auto-detect')).toBe(true);
  });

  it('shares one request between concurrent loads and refreshes on force', async () => {
    const response = new Subject<CurrentSubscriptionPlan>();
    current.mockReturnValue(response);
    const state = setup();

    const a = firstValueFrom(state.load());
    const b = firstValueFrom(state.load());
    response.next(BASIC);
    response.complete();
    await Promise.all([a, b]);
    expect(current).toHaveBeenCalledTimes(1);

    await firstValueFrom(state.load());
    expect(current).toHaveBeenCalledTimes(1);

    current.mockReturnValue(of({ ...BASIC, features: [usage('auto-detect', null)] }));
    await firstValueFrom(state.load(true));
    expect(current).toHaveBeenCalledTimes(2);
    expect(state.has('auto-detect')).toBe(true);
  });

  it('opens gates right after a plan change', async () => {
    const state = setup();
    await firstValueFrom(state.load());

    state.set({ ...BASIC, planKey: 'teacher-pro', features: [usage('canvas-ai', 1000)] });

    expect(state.has('canvas-ai')).toBe(true);
  });
});
