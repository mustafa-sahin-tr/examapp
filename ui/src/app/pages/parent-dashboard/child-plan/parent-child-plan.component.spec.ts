import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { ParentChildPlanComponent } from './parent-child-plan.component';
import { ChildSubscriptionPlan } from '../../../models/subscription';
import { SubscriptionService } from '../../../services/subscription.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import subscriptionTr from '../../../../../public/i18n/subscription/tr.json';

const PLAN: ChildSubscriptionPlan = {
  planKey: 'student-essential', displayNameKey: 'subscription.plans.studentEssential', validToUtc: '2026-12-10T09:00:00Z',
  isTrial: false, plans: [], features: [],
};

/** Issue #1091: veli panelinde çocuğun planı ve "Plan seç". */
describe('ParentChildPlanComponent (issue #1091)', () => {
  let fixture: ComponentFixture<ParentChildPlanComponent>;
  let service: MockedObject<SubscriptionService>;

  function create(plan: ChildSubscriptionPlan | HttpErrorResponse, now = new Date('2026-11-20T00:00:00Z')): HTMLElement {
    service = {
      childPlan: vi.fn().mockReturnValue(plan instanceof HttpErrorResponse ? throwError(() => plan) : of(plan)),
      childCheckout: vi.fn(),
    } as unknown as MockedObject<SubscriptionService>;
    TestBed.configureTestingModule({
      imports: [
        ParentChildPlanComponent,
        translocoTestingModule({ langs: { 'parent-dashboard/tr': parentDashboardTr, 'subscription/tr': subscriptionTr } }),
      ],
      providers: [{ provide: SubscriptionService, useValue: service }],
    });
    fixture = TestBed.createComponent(ParentChildPlanComponent);
    fixture.componentInstance.now = () => now;
    fixture.componentRef.setInput('studentId', 7);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const q = (el: HTMLElement, id: string) => el.querySelector(`[data-test="${id}"]`);

  it("shows the child's plan and its end date", () => {
    const el = create(PLAN);
    expect(service.childPlan).toHaveBeenCalledWith(7);
    expect(q(el, 'child-plan-name')!.textContent).toContain('Essential');
    expect(q(el, 'child-plan-until')!.textContent).toContain('2026');
    expect(q(el, 'child-plan-ending')).toBeNull();
    expect(q(el, 'child-plan-choose')!.textContent).toContain('Planı değiştir');
  });

  it("notes a plan that comes from the child's school license (issue #1092)", () => {
    expect(q(create(PLAN), 'child-plan-school-license')).toBeNull();
    TestBed.resetTestingModule();
    const el = create({ ...PLAN, planKey: 'student-pro', displayNameKey: 'subscription.plans.studentPro', schoolLicense: true });
    expect(q(el, 'child-plan-school-license')!.textContent).toContain('Okulunun lisansıyla');
  });

  it('marks a trial that ends within three days', () => {
    const el = create({ ...PLAN, planKey: 'trial', displayNameKey: 'subscription.plans.trial', isTrial: true },
      new Date('2026-12-08T09:00:00Z'));
    expect(q(el, 'child-plan-name')!.textContent).toContain('deneme');
    expect(q(el, 'child-plan-until')!.textContent).toContain('bitiyor');
    expect(q(el, 'child-plan-ending')).not.toBeNull();
    expect(q(el, 'child-plan-choose')!.textContent).toContain('Plan seç');
  });

  it('says the child has no plan', () => {
    const el = create({ ...PLAN, planKey: 'free', displayNameKey: 'subscription.plans.free', validToUtc: null });
    expect(q(el, 'child-plan-none')).not.toBeNull();
  });

  it('opens the picker for the child and reloads after a purchase', () => {
    const el = create(PLAN);
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    (q(el, 'child-plan-choose') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('app-plan-picker')).not.toBeNull();

    fixture.componentInstance.onCompleted({
      planKey: 'student-pro', planName: null, displayNameKey: 'subscription.plans.studentPro', validToUtc: null,
      trialAvailable: false, features: [],
    });
    fixture.detectChanges();
    expect(q(el, 'child-plan-bought')!.textContent).toContain('Pro');
    // İlk yükleme, seçicinin kendi yüklemesi ve alımdan sonra yenileme.
    expect(service.childPlan).toHaveBeenCalledTimes(3);
    expect(changed).toHaveBeenCalled();
  });

  it('reports 404 so the panel refreshes the child list', () => {
    const el = create(new HttpErrorResponse({ status: 404 }));
    const notFound = vi.fn();
    fixture.componentInstance.notFound.subscribe(notFound);
    fixture.componentInstance.reload();
    expect(notFound).toHaveBeenCalled();
    expect(q(el, 'child-plan-error')).toBeNull();
  });
});
