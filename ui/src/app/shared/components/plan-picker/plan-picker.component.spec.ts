import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { PlanPickerComponent } from './plan-picker.component';
import { SubscriptionService } from '../../../services/subscription.service';
import {
  ChildSubscriptionPlan,
  CurrentSubscriptionPlan,
  EntitlementFeature,
  StudentPlanOffer,
  SubscriptionPlan,
} from '../../../models/subscription';
import { translocoTestingModule } from '../../testing/transloco-testing';
import subscriptionTr from '../../../../../public/i18n/subscription/tr.json';
import subscriptionEn from '../../../../../public/i18n/subscription/en.json';

const PLANS: SubscriptionPlan[] = [
  plan('free', 'subscription.plans.free', 0, null, { 'book-import': 0, 'ai-classification': 20, 'mock-exam': 0 }),
  plan('trial', 'subscription.plans.trial', 0, 14, { 'book-import': 1 }),
  plan('teacher-pro', 'subscription.plans.teacherPro', 199, null, { 'book-import': 10, 'ai-classification': 1000 }),
  plan('teacher-basic', 'subscription.plans.teacherBasic', 149, null, { 'manual-labeling': null, 'book-import': 0 }),
];

const FEATURES: EntitlementFeature[] = [
  { key: 'manual-labeling', audience: 'Teacher', kind: 'Toggle', unit: null },
  { key: 'ai-classification', audience: 'Teacher', kind: 'Metered', unit: 'question' },
  { key: 'book-import', audience: 'Teacher', kind: 'Metered', unit: 'book' },
  { key: 'mock-exam', audience: 'Student', kind: 'Metered', unit: 'exam' },
];

function plan(key: string, displayNameKey: string, monthlyPrice: number, trialDays: number | null,
  monthlyLimits: Record<string, number | null>): SubscriptionPlan {
  return { key, name: key, displayNameKey, audience: 'Any', monthlyPrice, currency: 'TRY', trialDays, isActive: true, monthlyLimits };
}

let PLANS_OVERRIDE: SubscriptionPlan[] | null = null;

const STUDENT_PLANS: SubscriptionPlan[] = [
  plan('student-basic', 'subscription.plans.studentBasic', 99, null, { 'mock-exam': 2 }),
  plan('student-pro', 'subscription.plans.studentPro', 349, null, { 'mock-exam': null }),
];

const CHILD: ChildSubscriptionPlan = {
  planKey: 'student-basic', displayNameKey: 'subscription.plans.studentBasic', validToUtc: '2026-12-10T00:00:00Z', isTrial: false,
  plans: STUDENT_PLANS, features: FEATURES.filter((f) => f.audience === 'Student'),
};

function current(planKey: string, trialAvailable = true): CurrentSubscriptionPlan {
  return { planKey, planName: planKey, displayNameKey: `subscription.plans.${planKey}`, validToUtc: null, trialAvailable, features: [] };
}

/** Issue #1028: plan kartları, deneme, sahte ödeme ve Free ile geçiş. */
describe('PlanPickerComponent (issue #1028)', () => {
  let fixture: ComponentFixture<PlanPickerComponent>;
  let component: PlanPickerComponent;
  let service: MockedObject<SubscriptionService>;
  let emitted: (CurrentSubscriptionPlan | null)[];

  function create(mode: 'signup' | 'change' = 'signup', me = current('free'), inputs: Record<string, unknown> = {}): void {
    service = {
      plans: vi.fn().mockReturnValue(of(PLANS_OVERRIDE ?? PLANS)),
      current: vi.fn().mockReturnValue(of(me)),
      features: vi.fn().mockReturnValue(of(FEATURES)),
      startTrial: vi.fn(),
      checkout: vi.fn(),
      childPlan: vi.fn().mockReturnValue(of(CHILD)),
      childCheckout: vi.fn(),
    } as unknown as MockedObject<SubscriptionService>;
    TestBed.configureTestingModule({
      imports: [PlanPickerComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [{ provide: SubscriptionService, useValue: service }],
    });
    fixture = TestBed.createComponent(PlanPickerComponent);
    fixture.componentRef.setInput('mode', mode);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
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
    expect(el().querySelectorAll('.plan-card').length).toBe(4);
    expect(card('free').textContent).toContain('Ücretsiz');
    expect(card('free').textContent).toContain('Ayda 20 soru');
    expect(card('teacher-pro').textContent).toContain('Ayda 10 kitap');
    expect(card('trial').textContent).toContain('14 gün ücretsiz');
  });

  it('lists features in registry order, marks on/off ones as included and hides features not in the plan (issue #1093)', () => {
    create();
    const basic = card('teacher-basic');
    const lines = [...basic.querySelectorAll('.limits li')].map((li) => li.textContent?.trim());
    expect(lines.length).toBe(1);
    expect(lines[0]).toContain('Elle soru işaretleme');
    expect(basic.querySelector('mat-icon.included')).not.toBeNull();
    expect(basic.textContent).not.toContain('Kitabı dijitale aktarma');
    const pro = [...card('teacher-pro').querySelectorAll('.limits li span')].map((s) => s.textContent?.trim());
    expect(pro).toEqual(['Yapay zekâ ile konu ve kazanım', 'Kitabı dijitale aktarma (PDF)']);
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

  it('issue #1090: when the trial started on its own at signup, offers to continue with it', () => {
    create('signup', current('trial', false));

    const block = el().querySelector('[data-testid="trial-started"]') as HTMLElement;
    expect(block.textContent).toContain('Deneme süren başladı');
    button(block).click();
    expect(emitted).toEqual([null]);
  });

  it('issue #1090: no trial note on a free account or on the change page', () => {
    create('signup', current('free'));
    expect(el().querySelector('[data-testid="trial-started"]')).toBeNull();
    TestBed.resetTestingModule();
    create('change', current('trial', false));
    expect(el().querySelector('[data-testid="trial-started"]')).toBeNull();
  });

  it('issue #1090: without a Free plan in the catalog the hint does not offer starting free', () => {
    PLANS_OVERRIDE = PLANS.filter((p) => p.key !== 'free');
    create('change', current('trial', false));
    expect(el().querySelector('.hint')!.textContent).toContain('Deneme süresince her şey açık');
    PLANS_OVERRIDE = null;
  });

  it('issue #1091: a student sees "your parent chooses" instead of a buy button on paid plans', () => {
    create('change', current('trial', false), { parentPays: true });
    expect(card('teacher-pro').querySelector('[data-testid="parent-pays"]')!.textContent).toContain('velin seçer');
    expect(card('teacher-pro').querySelector('button')).toBeNull();
    expect(el().querySelector('.hint')!.textContent).toContain('velinle birlikte');
  });

  it("issue #1091: for a child the parent sees the child's plans and buys for the child", () => {
    create('change', current('free'), { childId: 7 });
    expect(service.childPlan).toHaveBeenCalledWith(7);
    expect(service.plans).not.toHaveBeenCalled();
    expect(el().querySelector('h2')!.textContent).toContain('Çocuğun planı');
    expect(card('student-basic').textContent).toContain('Mevcut');
    // Öğrenci özellikleri yanıttan gelir (veli kendi rolüyle features ucundan alamaz).
    expect(card('student-pro').textContent).toContain('Sınırsız');

    const bought: ChildSubscriptionPlan = { ...CHILD, planKey: 'student-pro', displayNameKey: 'subscription.plans.studentPro' };
    service.childCheckout.mockReturnValue(of(bought));
    button(card('student-pro')).click();
    fixture.detectChanges();
    (el().querySelector('[data-testid="complete-checkout"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service.childCheckout).toHaveBeenCalledWith(7, 'student-pro', undefined);
    expect(service.checkout).not.toHaveBeenCalled();
    expect(emitted).toEqual([expect.objectContaining({ planKey: 'student-pro' })]);
  });

  describe('issue #1108: scope, subject and duration for a child with a grade', () => {
    const OFFER: StudentPlanOffer = {
      band: 'Middle',
      gradeLevel: 7,
      subjects: [{ id: 3, name: 'Matematik' }, { id: 4, name: 'Fen Bilimleri' }],
      summerOnSale: false,
      siblingDiscountPercent: 0,
      prices: [
        { planKey: 'student-basic', scope: 'AllSubjects', duration: 'Monthly', amount: 199 },
        { planKey: 'student-basic', scope: 'AllSubjects', duration: 'SixMonths', amount: 1075 },
        { planKey: 'student-basic', scope: 'AllSubjects', duration: 'Summer', amount: 100 },
        { planKey: 'student-basic', scope: 'SingleSubject', duration: 'Monthly', amount: 119 },
        { planKey: 'student-basic', scope: 'SingleSubject', duration: 'SixMonths', amount: 645 },
        { planKey: 'student-pro', scope: 'AllSubjects', duration: 'Monthly', amount: 549 },
      ],
    };

    function openCheckout(key: string, offer: StudentPlanOffer = OFFER): void {
      const child: ChildSubscriptionPlan = { ...CHILD, planKey: 'free', displayNameKey: null, offer };
      create('change', current('free'), { childId: 7 });
      service.childPlan.mockReturnValue(of(child));
      component.load();
      fixture.detectChanges();
      button(card(key)).click();
      fixture.detectChanges();
    }

    const query = (selector: string): HTMLElement | null => el().querySelector(selector);
    const radio = (testId: string): HTMLInputElement => query(`[data-testid="${testId}"] input`) as HTMLInputElement;
    const complete = (): HTMLButtonElement => query('[data-testid="complete-checkout"]') as HTMLButtonElement;

    it('shows the band, the durations on sale and the total; summer only in season', () => {
      openCheckout('student-basic');
      expect(query('[data-testid="checkout-band"]')!.textContent).toContain('7. sınıf · Ortaokul');
      expect(query('[data-testid="duration-Monthly"]')).not.toBeNull();
      expect(query('[data-testid="duration-SixMonths"]')!.textContent).toContain('aylık');
      expect(query('[data-testid="duration-TwelveMonths"]')).toBeNull();
      expect(query('[data-testid="duration-Summer"]')).toBeNull();
      expect(query('[data-testid="checkout-total"]')!.textContent).toContain('199');
    });

    it('buys a single subject for six months once a subject is picked', () => {
      openCheckout('student-basic');
      service.childCheckout.mockReturnValue(of({ ...CHILD, offer: OFFER }));
      radio('scope-single').click();
      fixture.detectChanges();
      expect(complete().disabled).toBe(true);

      component.subjectId.set(3);
      radio('duration-SixMonths').click();
      fixture.detectChanges();
      expect(query('[data-testid="checkout-total"]')!.textContent).toContain('645');
      expect(complete().disabled).toBe(false);
      complete().click();

      expect(service.childCheckout).toHaveBeenCalledWith(7, 'student-basic', { subjectId: 3, duration: 'SixMonths' });
    });

    it('hides the single-subject choice when the plan has no single-subject price (Pro)', () => {
      openCheckout('student-pro');
      expect(query('[data-testid="scope-single"]')).toBeNull();
      expect(query('[data-testid="duration-Monthly"]')).not.toBeNull();
    });

    it('shows the sibling discount with the original price struck through', () => {
      openCheckout('student-basic', { ...OFFER, siblingDiscountPercent: 20 });
      expect(query('[data-testid="sibling-discount"]')!.textContent).toContain('%20');
      expect(query('[data-testid="duration-Monthly"] s')!.textContent).toContain('199');
      expect(query('[data-testid="checkout-total"]')!.textContent).toContain('159.2');
    });

    it('falls back to the monthly card price without options when the child has no grade', () => {
      openCheckout('student-basic', { ...OFFER, band: null, gradeLevel: null, prices: [], subjects: [] });
      expect(query('[data-testid="checkout-band"]')).toBeNull();
      expect(query('[data-testid="duration-Monthly"]')).toBeNull();
      service.childCheckout.mockReturnValue(of(CHILD));
      complete().click();
      expect(service.childCheckout).toHaveBeenCalledWith(7, 'student-basic', undefined);
    });
  });
});
