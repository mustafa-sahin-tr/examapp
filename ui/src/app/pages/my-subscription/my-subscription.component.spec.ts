import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { MySubscriptionComponent } from './my-subscription.component';
import { AuthService } from '../../services/auth.service';
import { SubscriptionService } from '../../services/subscription.service';
import { CurrentSubscriptionPlan } from '../../models/subscription';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import subscriptionTr from '../../../../public/i18n/subscription/tr.json';

const TRIAL: CurrentSubscriptionPlan = {
  planKey: 'trial',
  planName: 'Trial',
  displayNameKey: 'subscription.plans.trial',
  validToUtc: '2026-11-22T12:00:00Z',
  trialAvailable: false,
  features: [
    { featureKey: 'book-import', kind: 'Metered', unit: 'book', allowed: false, limit: 1, used: 1, remaining: 0, resetsAtUtc: '2026-12-01T00:00:00Z' },
    { featureKey: 'ai-classification', kind: 'Metered', unit: 'question', allowed: true, limit: 1000, used: 120, remaining: 880, resetsAtUtc: '2026-12-01T00:00:00Z' },
    { featureKey: 'mock-exam', kind: 'Metered', unit: 'exam', allowed: true, limit: null, used: 3, remaining: null, resetsAtUtc: null },
    { featureKey: 'study-tools', kind: 'Toggle', unit: null, allowed: true, limit: null, used: 0, remaining: null, resetsAtUtc: null },
    { featureKey: 'analytics', kind: 'Toggle', unit: null, allowed: false, limit: 0, used: 0, remaining: 0, resetsAtUtc: null },
  ],
};

/** Issue #1029: Aboneliğim — plan, deneme bitişi, kullanım ve sıfırlanma tarihi. */
describe('MySubscriptionComponent (issue #1029)', () => {
  let fixture: ComponentFixture<MySubscriptionComponent>;

  function create(current: ReturnType<SubscriptionService['current']>, roles: string[] = ['Teacher']): void {
    TestBed.configureTestingModule({
      imports: [MySubscriptionComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [
        provideRouter([]),
        { provide: SubscriptionService, useValue: { current: vi.fn().mockReturnValue(current), cancel: vi.fn() } },
        { provide: AuthService, useValue: { hasRealmRole: (role: string) => roles.includes(role) } },
      ],
    });
    fixture = TestBed.createComponent(MySubscriptionComponent);
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (id: string): string => el().querySelector(`[data-testid="${id}"]`)!.textContent!.replace(/\s+/g, ' ').trim();

  it('shows the plan, trial end, per-feature usage, reset date and the upgrade link', () => {
    create(of(TRIAL));

    expect(text('my-plan-name')).toBe('Ücretsiz deneme');
    expect(text('my-plan-validity')).toMatch(/^Deneme süren 22 \S+ 2026 tarihinde biter\.$/);
    expect(text('usage-book-import')).toBe('Kitabı dijitale aktarma (PDF) 1 / 1');
    expect(el().querySelector('[data-testid="usage-book-import"] .is-blocked')).toBeTruthy();
    expect(text('usage-ai-classification')).toBe('Yapay zekâ ile konu ve kazanım 120 / 1000');
    expect(text('usage-mock-exam')).toBe('Deneme sınavı Sınırsız');
    // #1093: açık / kapalı özellik "Dahil" ya da "Yok" yazar, sayaç göstermez.
    expect(text('usage-study-tools')).toBe('Çalışma araçları (sayfalar, yanlışlarım, bilgi kartları) Dahil');
    expect(text('usage-analytics')).toBe('Ayrıntılı analiz (konu, gelişim, kazanım) Yok');
    expect(text('my-plan-reset')).toMatch(/^Sayaçlar 1 \S+ 2026 tarihinde sıfırlanır\.$/);
    expect(el().querySelector('[data-testid="my-plan-upgrade"]')!.getAttribute('href')).toBe('/plans');
  });

  it("notes a plan that comes from the school's license (issue #1092)", () => {
    create(of({ ...TRIAL, planKey: 'student-pro', displayNameKey: 'subscription.plans.studentPro', schoolLicense: true }));

    expect(text('my-plan-school-license')).toBe('Okulunun lisansıyla');
  });

  it('notes a teacher plan sponsored by paid students and the grace end (issue #1115)', () => {
    create(
      of({
        ...TRIAL,
        planKey: 'teacher-essential',
        displayNameKey: 'subscription.plans.teacherEssential',
        validToUtc: '2027-03-01T00:00:00Z',
        studentSponsored: true,
      }),
    );

    expect(text('my-plan-student-sponsored')).toBe('Ücretli planı olan öğrencilerin sayesinde');
    expect(text('my-plan-validity')).toMatch(/^Ücretli planlı öğrencin kalmadı; bu plan 1 \S+ 2027 tarihine kadar sürer\.$/);
  });

  it('shows a retry on load failure', () => {
    create(throwError(() => new Error('x')));

    expect(el().textContent).toContain('Abonelik bilgin yüklenemedi.');
  });

  // Issue #1116
  it('offers cancelling a purchased plan to a teacher but not to a student (the parent cancels)', () => {
    const bought: CurrentSubscriptionPlan = {
      ...TRIAL,
      planKey: 'teacher-basic',
      validToUtc: '2026-12-11T12:00:00Z',
      purchase: { duration: 'Monthly', pricePaid: 200, currency: 'TRY', cancelledAtUtc: null, fullRefundUntilUtc: null },
    };
    create(of(bought));
    expect(el().querySelector('[data-testid="plan-cancel"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="purchase-terms"]')).not.toBeNull();

    TestBed.resetTestingModule();
    create(of(bought), ['Student']);
    expect(el().querySelector('[data-testid="plan-cancel"]')).toBeNull();
  });
});
