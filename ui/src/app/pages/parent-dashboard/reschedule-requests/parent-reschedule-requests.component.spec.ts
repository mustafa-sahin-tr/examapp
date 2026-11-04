import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { BookingResult, ParentRescheduleRequest } from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { LocaleService } from '../../../services/locale.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import { ParentRescheduleRequestsComponent } from './parent-reschedule-requests.component';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

function request(overrides: Partial<ParentRescheduleRequest> = {}): ParentRescheduleRequest {
  return {
    id: 7,
    studentId: 11,
    studentName: 'Ayşe',
    teacherId: 3,
    teacherName: 'Ali Hoca',
    startUtc: '2026-11-10T10:00:00Z',
    endUtc: '2026-11-10T11:00:00Z',
    proposedSlotId: 55,
    proposedStartUtc: '2026-11-12T10:00:00Z',
    proposedEndUtc: '2026-11-12T11:00:00Z',
    proposedByRole: 'Teacher',
    canApprove: true,
    ...overrides,
  };
}

describe('ParentRescheduleRequestsComponent (#486)', () => {
  let service: MockedObject<BookingService>;
  let snackBar: { open: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<ParentRescheduleRequestsComponent>;

  async function render(items: ParentRescheduleRequest[]): Promise<HTMLElement> {
    service = {
      getParentRescheduleRequests: vi.fn().mockReturnValue(of({ items })),
      parentApproveReschedule: vi.fn().mockReturnValue(of({ success: true } as BookingResult)),
      parentRejectReschedule: vi.fn().mockReturnValue(of({ success: true } as BookingResult)),
      extractError: vi.fn((_err: unknown, fallback: string) => fallback),
    } as unknown as MockedObject<BookingService>;
    snackBar = { open: vi.fn() };

    TestBed.configureTestingModule({
      imports: [
        ParentRescheduleRequestsComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn } }),
      ],
      providers: [
        { provide: BookingService, useValue: service },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(ParentRescheduleRequestsComponent);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const q = (el: HTMLElement, test: string) => el.querySelector<HTMLElement>(`[data-test="${test}"]`);

  it('takes no room when nothing is waiting', async () => {
    const el = await render([]);

    expect(q(el, 'reschedule-requests-section')).toBeNull();
  });

  it('shows who, the old and the new time', async () => {
    const el = await render([request()]);

    const row = q(el, 'reschedule-request-row')!;
    expect(row.textContent).toContain('Ayşe');
    expect(row.textContent).toContain('Ali Hoca');
    expect(q(el, 'reschedule-request-when')!.textContent).toContain('10 Kas 2026');
    expect(q(el, 'reschedule-request-when')!.textContent).toContain('12 Kas 2026');
  });

  it('approve: calls the server, drops the row, tells the parent', async () => {
    const el = await render([request()]);

    q(el, 'reschedule-request-approve')!.click();
    await settle();

    expect(service.parentApproveReschedule).toHaveBeenCalledWith(7, 55);
    expect(q(el, 'reschedule-request-row')).toBeNull();
    expect(snackBar.open).toHaveBeenCalledTimes(1);
    expect(snackBar.open.mock.calls[0][0]).toBe(parentDashboardTr.rescheduleRequests.approved);
  });

  it('reject: calls the server and drops the row', async () => {
    const el = await render([request()]);

    q(el, 'reschedule-request-reject')!.click();
    await settle();

    expect(service.parentRejectReschedule).toHaveBeenCalledWith(7, 55);
    expect(q(el, 'reschedule-request-row')).toBeNull();
  });

  it('stale proposal: offers only reject', async () => {
    const el = await render([request({ canApprove: false })]);

    expect(q(el, 'reschedule-request-approve')).toBeNull();
    expect(q(el, 'reschedule-request-stale')).not.toBeNull();
    expect(q(el, 'reschedule-request-reject')).not.toBeNull();
  });

  it('conflict: shows the message and reloads the list', async () => {
    const el = await render([request()]);
    service.parentApproveReschedule.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    service.getParentRescheduleRequests.mockReturnValue(of({ items: [] }));

    q(el, 'reschedule-request-approve')!.click();
    await settle();

    expect(service.getParentRescheduleRequests).toHaveBeenCalledTimes(2);
    expect(q(el, 'reschedule-request-row')).toBeNull();
    expect(q(el, 'reschedule-requests-notice')!.textContent).toContain(parentDashboardTr.rescheduleRequests.actionError);
  });

  it('load error: shows retry', async () => {
    const el = await render([]);
    service.getParentRescheduleRequests.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    (fixture.componentInstance as unknown as { load(): void }).load();
    await settle();

    expect(q(el, 'reschedule-requests-error')).not.toBeNull();
  });
});
