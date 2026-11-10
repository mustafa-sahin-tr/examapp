import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { SubscriptionPlansComponent } from './subscription-plans.component';
import { AuthService } from '../../services/auth.service';
import { SubscriptionService } from '../../services/subscription.service';
import { CurrentSubscriptionPlan } from '../../models/subscription';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import subscriptionTr from '../../../../public/i18n/subscription/tr.json';

/** Issue #1090: deneme bitip planı olmayan kullanıcı plan sayfasında nedenini görür; öğrencide planı veli seçer. */
describe('SubscriptionPlansComponent (issue #1090)', () => {
  function create(role: 'Teacher' | 'Student', locked: boolean): HTMLElement {
    const me: CurrentSubscriptionPlan = {
      planKey: 'free', planName: 'Free', displayNameKey: 'subscription.plans.free', validToUtc: null, trialAvailable: false, locked,
      features: [],
    };
    TestBed.configureTestingModule({
      imports: [SubscriptionPlansComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [
        provideRouter([]),
        {
          provide: SubscriptionService,
          useValue: { current: vi.fn().mockReturnValue(of(me)), plans: vi.fn().mockReturnValue(of([])), features: vi.fn().mockReturnValue(of([])) },
        },
        { provide: AuthService, useValue: { hasRealmRole: (r: string) => r === role } },
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
});
