import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { SubscriptionPlansComponent } from './subscription-plans.component';
import { AuthService } from '../../services/auth.service';
import { SubscriptionService } from '../../services/subscription.service';
import { CurrentSubscriptionPlan } from '../../models/subscription';
import { ParentStatus } from '../../models/parent-status.model';
import { StudentParentService } from '../../services/student-parent.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import subscriptionTr from '../../../../public/i18n/subscription/tr.json';
import placementTr from '../../../../public/i18n/placement/tr.json';
import { PlacementStatus } from '../../models/placement.model';
import { PlacementService } from '../../services/placement.service';

/** Issue #1090: deneme bitip planı olmayan kullanıcı plan sayfasında nedenini görür; öğrencide planı veli seçer. */
describe('SubscriptionPlansComponent (issue #1090)', () => {
  function create(role: 'Teacher' | 'Student', locked: boolean, parentMode: ParentStatus['mode'] = 'Ok',
    placement: PlacementStatus = { available: false, taken: false, gradeName: null, questionCount: 0 }): HTMLElement {
    const me: CurrentSubscriptionPlan = {
      planKey: 'free', planName: 'Free', displayNameKey: 'subscription.plans.free', validToUtc: null, trialAvailable: false, locked,
      features: [],
    };
    TestBed.configureTestingModule({
      imports: [SubscriptionPlansComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr, 'placement/tr': placementTr } })],
      providers: [
        provideRouter([]),
        {
          provide: SubscriptionService,
          useValue: { current: vi.fn().mockReturnValue(of(me)), plans: vi.fn().mockReturnValue(of([])), features: vi.fn().mockReturnValue(of([])) },
        },
        { provide: PlacementService, useValue: { status: () => of(placement) } },
        { provide: AuthService, useValue: { hasRealmRole: (r: string) => r === role } },
        {
          provide: StudentParentService,
          useValue: { ensureLoaded: () => of({ mode: parentMode, requiresParent: true, graceDays: 7, isFinalWarning: false }) },
        },
      ],
    });
    const fixture = TestBed.createComponent(SubscriptionPlansComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const notice = (el: HTMLElement) => el.querySelector('[data-testid="plan-locked"]');

  it('tells a locked student that the parent must choose a plan', () => {
    expect(notice(create('Student', true))!.textContent).toContain('velinin bir plan seçmesi gerekiyor');
  });

  it('tells a locked teacher to choose a plan', () => {
    expect(notice(create('Teacher', true))!.textContent).toContain('Devam etmek için bir plan seç');
  });

  it('shows nothing when the account is not locked', () => {
    expect(notice(create('Teacher', false))).toBeNull();
  });

  const parentNotice = (el: HTMLElement) => el.querySelector('[data-testid="parent-pays-notice"]');

  it('issue #1091: tells a student that the parent chooses the plan', () => {
    const el = create('Student', false);
    expect(parentNotice(el)!.textContent).toContain('Planını velin seçer');
    expect(el.querySelector('[data-testid="add-parent"]')).toBeNull();
  });

  it('issue #1091: a student without a linked parent is sent to add one first', () => {
    const el = create('Student', true, 'Expired');
    expect(parentNotice(el)!.textContent).toContain('Henüz bağlı bir velin yok');
    expect(el.querySelector('[data-testid="add-parent"]')!.getAttribute('href')).toBe('/parent-required');
  });

  it('issue #1091: a teacher buys for themselves', () => {
    expect(parentNotice(create('Teacher', false))).toBeNull();
  });

  const placementNotice = (el: HTMLElement) => el.querySelector('[data-testid="placement-notice"]');

  it('issue #1114: a student is pointed to the free placement test when one is published', () => {
    const el = create('Student', true, 'Ok', { available: true, taken: false, gradeName: '7. Sınıf', questionCount: 20 });
    expect(placementNotice(el)!.textContent).toContain('Ücretsiz seviye tespitini çöz');
    expect(el.querySelector('[data-testid="placement-link"]')!.getAttribute('href')).toBe('/placement');
  });

  it('issue #1114: no placement notice without a published test', () => {
    expect(placementNotice(create('Student', false))).toBeNull();
  });
});
