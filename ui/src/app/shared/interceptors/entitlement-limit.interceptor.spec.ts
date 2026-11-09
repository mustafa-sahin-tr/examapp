import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { entitlementLimitInterceptor } from './entitlement-limit.interceptor';
import { SubscriptionService } from '../../services/subscription.service';
import { SubscriptionPlan } from '../../models/subscription';
import { translocoTestingModule } from '../testing/transloco-testing';
import subscriptionTr from '../../../../public/i18n/subscription/tr.json';

const PLANS: SubscriptionPlan[] = [
  plan('free', 'subscription.plans.free', { 'book-import': 0, 'ai-classification': 20 }),
  plan('teacher-pro', 'subscription.plans.teacherPro', { 'book-import': 10 }),
  plan('student-plus', 'subscription.plans.studentPlus', { 'mock-exam': null }),
];

function plan(key: string, displayNameKey: string, monthlyLimits: Record<string, number | null>): SubscriptionPlan {
  return { key, name: key, displayNameKey, audience: 'Any', monthlyPrice: 0, currency: 'TRY', trialDays: null, isActive: true, monthlyLimits };
}

const LIMIT_BODY = {
  code: 'entitlement.limitReached',
  feature: 'book-import',
  limit: 10,
  used: 10,
  resetsAtUtc: '2026-12-01T00:00:00Z',
  planKey: 'teacher-pro',
  message: 'x',
};

/** Issue #1029: 429 `entitlement.limitReached` → ortak yükseltme penceresi. */
describe('entitlementLimitInterceptor (issue #1029)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [
        provideHttpClient(withXhr(), withInterceptors([entitlementLimitInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: SubscriptionService, useValue: { plans: vi.fn().mockReturnValue(of(PLANS)) } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.verify();
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
  });

  function fail(url: string, status: number, body: object): unknown {
    let received: unknown;
    http.post(url, {}).subscribe({ error: (e: unknown) => (received = e) });
    httpMock.expectOne(url).flush(body, { status, statusText: 'x' });
    TestBed.tick();
    return received;
  }

  const dialog = (): HTMLElement | null => document.querySelector('app-upgrade-dialog');

  it('opens the upgrade dialog with usage, reset date and plan comparison, and passes the error on', () => {
    const error = fail('/api/exam/book-imports', 429, LIMIT_BODY);

    expect(error).toBeTruthy();
    const el = dialog()!;
    expect(el.textContent).toContain('Bu ayki limitine ulaştın');
    expect(el.querySelector('[data-testid="upgrade-usage"]')!.textContent).toContain('Kitabı dijitale aktarma (PDF) için bu ayki hakkını kullandın (10 / 10)');
    expect(el.querySelector('[data-testid="upgrade-reset"]')).toBeTruthy();
    const rows = Array.from(el.querySelectorAll('[data-testid="upgrade-compare"] tr')).map((r) => r.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows).toEqual(['Ücretsiz Yok', 'Pro Mevcut planın Ayda 10', 'Öğrenci Plus Yok']);
  });

  it('opens a single dialog for repeated limit responses', () => {
    fail('/api/exam/a', 429, LIMIT_BODY);
    fail('/api/exam/b', 429, LIMIT_BODY);

    expect(document.querySelectorAll('app-upgrade-dialog').length).toBe(1);
  });

  it('upgrade button closes the dialog and goes to plan selection', () => {
    fail('/api/exam/book-imports', 429, LIMIT_BODY);

    (dialog()!.querySelector('[data-testid="upgrade-button"]') as HTMLButtonElement).click();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/plans');
  });

  it('ignores other 429s and other codes', () => {
    fail('/api/exam/a', 429, { errorCode: 'RateLimited' });
    fail('/api/exam/b', 409, { code: 'entitlement.limitReached', feature: 'book-import' });
    fail('/api/exam/c', 403, { code: 'entitlement.limitReached', feature: 'book-import' });
    fail('/api/exam/d', 429, { code: 'entitlement.notInPlan', feature: 'book-import' });

    expect(dialog()).toBeNull();
  });

  it('issue #1087: 403 entitlement.notInPlan opens the same dialog saying the feature is not in the plan', () => {
    fail('/api/exam/book-imports', 403, { ...LIMIT_BODY, code: 'entitlement.notInPlan', limit: 0, used: 0, resetsAtUtc: null, planKey: 'free' });

    const el = dialog()!;
    expect(el.textContent).toContain('Bu özellik planında yok');
    expect(el.querySelector('[data-testid="upgrade-usage"]')!.textContent).toContain('Kitabı dijitale aktarma (PDF) mevcut planında yok.');
    expect(el.querySelector('[data-testid="upgrade-reset"]')).toBeNull();
  });
});
