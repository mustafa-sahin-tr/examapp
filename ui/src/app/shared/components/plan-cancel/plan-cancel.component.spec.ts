import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { PlanCancelComponent } from './plan-cancel.component';
import { PurchaseStatus } from '../../../models/subscription';
import { SubscriptionService } from '../../../services/subscription.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import subscriptionTr from '../../../../../public/i18n/subscription/tr.json';

const MONTHLY: PurchaseStatus = { duration: 'Monthly', pricePaid: 200, currency: 'TRY', cancelledAtUtc: null, fullRefundUntilUtc: null };
const SIX_MONTHS: PurchaseStatus = { ...MONTHLY, duration: 'SixMonths', pricePaid: 1500, fullRefundUntilUtc: '2026-11-25T00:00:00Z' };

/** Issue #1116: plan iptali — onay adımında sonuç (dönem sonu / tam iade), iptal edilmiş durum, hata kodu. */
describe('PlanCancelComponent (issue #1116)', () => {
  let fixture: ComponentFixture<PlanCancelComponent>;
  let service: MockedObject<SubscriptionService>;

  function create(purchase: PurchaseStatus | null, now = '2026-11-20T00:00:00Z', studentId: number | null = null): HTMLElement {
    service = { cancel: vi.fn(), cancelChildPlan: vi.fn() } as unknown as MockedObject<SubscriptionService>;
    TestBed.configureTestingModule({
      imports: [PlanCancelComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [{ provide: SubscriptionService, useValue: service }],
    });
    fixture = TestBed.createComponent(PlanCancelComponent);
    fixture.componentInstance.now = () => new Date(now);
    fixture.componentRef.setInput('purchase', purchase);
    fixture.componentRef.setInput('validToUtc', '2026-12-10T09:00:00Z');
    fixture.componentRef.setInput('studentId', studentId);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const click = (el: HTMLElement, id: string): void => {
    (el.querySelector(`[data-testid="${id}"]`) as HTMLButtonElement).click();
    fixture.detectChanges();
  };
  const text = (el: HTMLElement, id: string): string => el.querySelector(`[data-testid="${id}"]`)!.textContent!.replace(/\s+/g, ' ').trim();

  it('shows nothing without a purchase', () => {
    const el = create(null);
    expect(el.querySelector('[data-testid="plan-cancel"]')).toBeNull();
  });

  it('monthly: the confirmation says access runs to the end of the period, then cancels my own plan', () => {
    const el = create(MONTHLY);
    expect(el.querySelector('[data-testid="plan-refund-until"]')).toBeNull();
    click(el, 'plan-cancel');
    expect(text(el, 'plan-cancel-effect')).toContain('Plan yenilenmez');
    expect(text(el, 'plan-cancel-effect')).toContain('2026');

    service.cancel.mockReturnValue(of({ accessUntilUtc: '2026-12-10T09:00:00Z', refundAmount: null, currency: 'TRY' }));
    const cancelled = vi.fn();
    fixture.componentInstance.cancelled.subscribe(cancelled);
    click(el, 'plan-cancel-yes');

    expect(service.cancel).toHaveBeenCalled();
    expect(cancelled).toHaveBeenCalled();
    expect(el.querySelector('[data-testid="plan-refunded"]')).toBeNull();
  });

  it('long plan within 14 days: shows the refund deadline and a full-refund confirmation', () => {
    const el = create(SIX_MONTHS, '2026-11-20T00:00:00Z', 7);
    expect(el.querySelector('[data-testid="plan-refund-until"]')).not.toBeNull();
    click(el, 'plan-cancel');
    expect(text(el, 'plan-cancel-effect')).toContain('tamamı iade edilir');

    service.cancelChildPlan.mockReturnValue(of({ accessUntilUtc: '2026-11-20T00:00:00Z', refundAmount: 1500, currency: 'TRY' }));
    click(el, 'plan-cancel-yes');
    expect(service.cancelChildPlan).toHaveBeenCalledWith(7);
    expect(text(el, 'plan-refunded')).toContain('iade edilecek');
  });

  it('long plan after 14 days: no refund deadline, partial refund on request', () => {
    const el = create(SIX_MONTHS, '2026-12-01T00:00:00Z');
    expect(el.querySelector('[data-testid="plan-refund-until"]')).toBeNull();
    click(el, 'plan-cancel');
    expect(text(el, 'plan-cancel-effect')).toContain('kısmi iade');
  });

  it('cancelled purchase shows only the status line', () => {
    const el = create({ ...MONTHLY, cancelledAtUtc: '2026-11-19T00:00:00Z' });
    expect(text(el, 'plan-cancelled')).toContain('yenilenmeyecek');
    expect(el.querySelector('[data-testid="plan-cancel"]')).toBeNull();
  });

  it('maps the already-cancelled error code', () => {
    const el = create(MONTHLY);
    service.cancel.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'subscription.alreadyCancelled' } }))
    );
    click(el, 'plan-cancel');
    click(el, 'plan-cancel-yes');
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('zaten iptal edildi');
  });
});
