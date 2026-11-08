import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { PlanPickerComponent } from './plan-picker.component';
import { SubscriptionService } from '../../../services/subscription.service';
import { CurrentSubscriptionPlan, SubscriptionPlan } from '../../../models/subscription';
import { translocoTestingModule } from '../../testing/transloco-testing';
import subscriptionTr from '../../../../../public/i18n/subscription/tr.json';
import subscriptionEn from '../../../../../public/i18n/subscription/en.json';

const PLANS: SubscriptionPlan[] = [
  plan('free', 'subscription.plans.free', 0, null, { 'book-import': 0, 'ai-classification': 20, 'mock-exam': 0 }),
  plan('trial', 'subscription.plans.trial', 0, 14, { 'book-import': 1 }),
  plan('teacher-pro', 'subscription.plans.teacherPro', 199, null, { 'book-import': 10, 'ai-classification': 1000 }),
];

function plan(key: string, displayNameKey: string, monthlyPrice: number, trialDays: number | null,
  monthlyLimits: Record<string, number | null>): SubscriptionPlan {
  return { key, name: key, displayNameKey, audience: 'Any', monthlyPrice, currency: 'TRY', trialDays, isActive: true, monthlyLimits };
}

function current(planKey: string, trialAvailable = true): CurrentSubscriptionPlan {
  return { planKey, planName: planKey, displayNameKey: `subscription.plans.${planKey}`, validToUtc: null, trialAvailable, features: [] };
}

/** Issue #1028: plan kartları, deneme, sahte ödeme ve Free ile geçiş. */
describe('PlanPickerComponent (issue #1028)', () => {
  let fixture: ComponentFixture<PlanPickerComponent>;
  let component: PlanPickerComponent;
  let service: MockedObject<SubscriptionService>;
  let emitted: (CurrentSubscriptionPlan | null)[];

  function create(mode: 'signup' | 'change' = 'signup', me = current('free')): void {
    service = {
      plans: vi.fn().mockReturnValue(of(PLANS)),
      current: vi.fn().mockReturnValue(of(me)),
      startTrial: vi.fn(),
      checkout: vi.fn(),
    } as unknown as MockedObject<SubscriptionService>;
    TestBed.configureTestingModule({
      imports: [PlanPickerComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [{ provide: SubscriptionService, useValue: service }],
    });
    fixture = TestBed.createComponent(PlanPickerComponent);
    fixture.componentRef.setInput('mode', mode);
    component = fixture.componentInstance;
    emitted = [];
    component.completed.subscribe((p) => emitted.push(p));
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const card = (key: string): HTMLElement => el().querySelector(`[data-plan="${key}"]`) as HTMLElement;
  const button = (root: HTMLElement): HTMLButtonElement => root.querySelector('button') as HTMLButtonElement;

  it('renders a card per plan with its limits', () => {
    create();
    expect(el().querySelectorAll('.plan-card').length).toBe(3);
    expect(card('free').textContent).toContain('Ücretsiz');
    expect(card('free').textContent).toContain('Ayda 20');
    expect(card('teacher-pro').textContent).toContain('Ayda 10');
    expect(card('trial').textContent).toContain('14 gün ücretsiz');
  });

  it('signup: "Ücretsiz devam et" emits null without calling the API', () => {
    create();
    button(card('free')).click();
    expect(emitted).toEqual([null]);
    expect(service.checkout).not.toHaveBeenCalled();
  });

  it('starts the trial and emits the new plan', () => {
    create();
    service.startTrial.mockReturnValue(of(current('trial', false)));
    button(card('trial')).click();
    expect(service.startTrial).toHaveBeenCalledTimes(1);
    expect(emitted[0]?.planKey).toBe('trial');
  });

  it('hides the trial button once the trial was used', () => {
    create('signup', current('free', false));
    expect(card('trial').querySelector('button')).toBeNull();
    expect(card('trial').textContent).toContain('Deneme hakkını kullandın');
  });

  it('trialUsed conflict shows the mapped error and hides the button', () => {
    create();
    service.startTrial.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'subscription.trialUsed' } })));
    button(card('trial')).click();
    fixture.detectChanges();
    expect(el().textContent).toContain('daha önce kullandı');
    expect(card('trial').querySelector('button')).toBeNull();
    expect(emitted).toEqual([]);
  });

  it('paid plan opens a clearly marked demo checkout without card fields, then completes', () => {
    create();
    button(card('teacher-pro')).click();
    fixture.detectChanges();
    const checkout = el().querySelector('[data-testid="checkout"]') as HTMLElement;
    expect(checkout.textContent).toContain('demo ödeme ekranıdır');
    expect(checkout.querySelectorAll('input').length).toBe(0);

    service.checkout.mockReturnValue(of(current('teacher-pro')));
    (el().querySelector('[data-testid="complete-checkout"]') as HTMLButtonElement).click();
    expect(service.checkout).toHaveBeenCalledWith('teacher-pro');
    expect(emitted[0]?.planKey).toBe('teacher-pro');
  });

  it('change mode marks the current plan and offers no free fallback', () => {
    create('change', current('teacher-pro'));
    expect(card('teacher-pro').classList).toContain('plan-card--current');
    expect(card('teacher-pro').querySelector('button')).toBeNull();
    expect(card('free').querySelector('button')).toBeNull();
    // Ücretli plandayken deneme önerilmez (planın yerine geçerdi).
    expect(card('trial').querySelector('button')).toBeNull();
    expect(card('trial').textContent).not.toContain('kullandın');
  });

  it('i18n: tr and en have the same keys', () => {
    const keys = (o: object, prefix = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) => (typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]));
    expect(keys(subscriptionEn).sort()).toEqual(keys(subscriptionTr).sort());
  });
});
