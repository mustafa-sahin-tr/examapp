import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import {
  TEACHER_BOOKING_STATUS_VIEW,
  TeacherBookingRequestsComponent,
  awaitsTeacherReply,
} from './teacher-booking-requests.component';
import { Booking, BookingStatus } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import teacherBookingsTr from '../../../../public/i18n/teacher-bookings/tr.json';

/** Issue #315: öğretmen randevu talepleri — iptal / erteleme / no-show. */
describe('TeacherBookingRequestsComponent (issue #315)', () => {
  let fixture: ComponentFixture<TeacherBookingRequestsComponent>;
  let service: jasmine.SpyObj<BookingService>;
  let dialog: { open: jasmine.Spy };
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  const HOUR = 60 * 60 * 1000;

  function booking(id: number, status: BookingStatus, startMs: number, overrides: Partial<Booking> = {}): Booking {
    return {
      id,
      teacherId: 4,
      studentId: 21,
      studentName: `Öğrenci ${id}`,
      availabilitySlotId: 100 + id,
      date: new Date(startMs).toISOString().slice(0, 10),
      startTime: '11:00:00',
      endTime: '12:00:00',
      startUtc: new Date(startMs).toISOString(),
      endUtc: new Date(startMs + HOUR).toISOString(),
      status,
      createdAt: '2030-10-01T08:00:00Z',
      ...overrides,
    };
  }

  function approved(id: number, startMs: number, overrides: Partial<Booking> = {}): Booking {
    return booking(id, 'Approved', startMs, {
      cancellationDeadlineUtc: new Date(startMs - 24 * HOUR).toISOString(),
      noShowReportableUntilUtc: new Date(startMs + HOUR + 7 * 24 * HOUR).toISOString(),
      canProposeReschedule: true,
      ...overrides,
    });
  }

  function proposal(start: number, by: 'Teacher' | 'Student'): Partial<Booking> {
    return {
      canProposeReschedule: false,
      proposedSlotId: 300,
      proposedStartUtc: new Date(start + 24 * HOUR).toISOString(),
      proposedEndUtc: new Date(start + 25 * HOUR).toISOString(),
      proposedByRole: by,
      canRespondReschedule: true,
    };
  }

  async function setup(items: Booking[], dialogResult: unknown = undefined, showAll = true): Promise<HTMLElement> {
    service = jasmine.createSpyObj<BookingService>('BookingService', [
      'getTeacherRequests',
      'extractError',
      'respondReschedule',
      'reportNoShow',
    ]);
    service.getTeacherRequests.and.returnValue(of({ success: true, items }));
    service.respondReschedule.and.returnValue(of({ success: true, message: 'Sunucu mesajı' }));
    service.reportNoShow.and.returnValue(of({ success: true, message: 'Bildirildi' }));
    service.extractError.and.callFake((err: HttpErrorResponse) => (err.error as { message: string }).message);
    dialog = { open: jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(dialogResult) }) };
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        TeacherBookingRequestsComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'teacher-bookings/tr': teacherBookingsTr } }),
      ],
      providers: [
        provideRouter([]),
        { provide: BookingService, useValue: service },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(TeacherBookingRequestsComponent);
    // `whenStable` kullanılmaz: katılım penceresi `interval`'i uygulamayı hiç "stable" bırakmaz.
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    if (showAll) {
      (el.querySelectorAll('mat-button-toggle button')[1] as HTMLButtonElement).click();
      fixture.detectChanges();
    }
    return el;
  }

  const btn = (el: HTMLElement, test: string) => el.querySelector<HTMLButtonElement>(`[data-test="${test}"]`);

  afterEach(() => fixture?.destroy());

  it('StatusView_MapsCancelled', () => {
    expect(TEACHER_BOOKING_STATUS_VIEW.Cancelled.statusKey).toBe('status.cancelled');
  });

  it('Approved_Cancel_OpensCancelDialog_ReloadsWithServerMessage', async () => {
    const el = await setup([approved(1, Date.now() + 5 * 24 * HOUR)], { success: true, message: 'Randevu iptal edildi.' });

    btn(el, 'cancel-booking')!.click();

    const config = dialog.open.calls.mostRecent().args[1] as { data: { bookingId: number; mode: string; counterpart: string } };
    expect(config.data).toEqual(jasmine.objectContaining({ bookingId: 1, mode: 'cancel', counterpart: 'Öğrenci 1' }));
    expect(service.getTeacherRequests).toHaveBeenCalledTimes(2);
    expect(snackBar.open.calls.mostRecent().args[0]).toBe('Randevu iptal edildi.');
  });

  it('Approved_AfterDeadline_CancelDisabled', async () => {
    const el = await setup([approved(1, Date.now() + 2 * HOUR, { canProposeReschedule: false })]);

    expect(btn(el, 'cancel-booking')!.disabled).toBeTrue();
  });

  it('Approved_Propose_UsesMySlots', async () => {
    const el = await setup([approved(1, Date.now() + 5 * 24 * HOUR)], { success: true });

    btn(el, 'reschedule-propose')!.click();

    const config = dialog.open.calls.mostRecent().args[1] as { data: { source: string } };
    expect(config.data.source).toBe('mySlots');
    expect(service.getTeacherRequests).toHaveBeenCalledTimes(2);
  });

  it('Pending_NoLifecycleActions', async () => {
    const el = await setup([booking(1, 'Pending', Date.now() + 5 * 24 * HOUR)]);

    expect(btn(el, 'cancel-booking')).toBeNull();
    expect(btn(el, 'reschedule-propose')).toBeNull();
    expect(btn(el, 'withdraw-request')).toBeNull();
  });

  it('StudentProposal_VisibleInPendingFilter_Accept', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([approved(1, start, proposal(start, 'Student')), approved(2, start)], undefined, false);

    // "Bekleyenler" filtresi: yalnız yanıt bekleyen öneri.
    expect(el.querySelectorAll('.breq__row').length).toBe(1);
    expect(el.querySelector('[data-test="reschedule-proposal"]')?.textContent).toContain('Öğrencinin erteleme önerisi');

    btn(el, 'reschedule-accept')!.click();
    expect(service.respondReschedule).toHaveBeenCalledWith(1, true);
    expect(snackBar.open.calls.mostRecent().args[0]).toBe('Sunucu mesajı');
    expect(service.getTeacherRequests).toHaveBeenCalledTimes(2);
  });

  it('OwnProposal_WithdrawOnly', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([approved(1, start, proposal(start, 'Teacher'))]);

    expect(btn(el, 'reschedule-accept')).toBeNull();
    btn(el, 'reschedule-withdraw')!.click();
    expect(service.respondReschedule).toHaveBeenCalledWith(1, false);
  });

  it('RespondConflict_ShowsServerMessage_AndReloads', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([approved(1, start, proposal(start, 'Student'))]);
    service.respondReschedule.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, message: 'Erteleme penceresi kapandı.', errorCode: 'RescheduleWindowClosed' },
          })
      )
    );

    btn(el, 'reschedule-decline')!.click();

    expect(snackBar.open.calls.mostRecent().args[0]).toBe('Erteleme penceresi kapandı.');
    expect(service.getTeacherRequests).toHaveBeenCalledTimes(2);
  });

  it('AfterLesson_NoShow_ConfirmsThenReports', async () => {
    const el = await setup([approved(1, Date.now() - 3 * HOUR, { canProposeReschedule: false })], true);

    btn(el, 'report-no-show')!.click();

    expect(service.reportNoShow).toHaveBeenCalledWith(1);
    expect(service.getTeacherRequests).toHaveBeenCalledTimes(2);
  });

  it('CancelledAndNoShowRows_ShowWhoAndReason', async () => {
    const el = await setup([
      booking(1, 'Cancelled', Date.now() + 5 * 24 * HOUR, { cancelledByRole: 'Student', cancellationReason: 'Sınavım var' }),
      approved(2, Date.now() - 3 * HOUR, { noShowParty: 'Student', canProposeReschedule: false }),
    ]);

    expect(el.querySelector('[data-test="cancelled-by"]')?.textContent).toContain('Öğrenci bu randevuyu iptal etti');
    expect(el.querySelector('[data-test="cancellation-reason"]')?.textContent).toContain('Sınavım var');
    expect(el.querySelector('[data-test="no-show-info"]')?.textContent).toContain('Öğrencinin derse katılmadığı');
    expect(btn(el, 'report-no-show')).toBeNull();
  });

  it('awaitsTeacherReply_OnlyStudentProposalOnApproved', () => {
    const start = Date.now() + 5 * 24 * HOUR;
    expect(awaitsTeacherReply(approved(1, start, proposal(start, 'Student')))).toBeTrue();
    expect(awaitsTeacherReply(approved(1, start, proposal(start, 'Teacher')))).toBeFalse();
    expect(awaitsTeacherReply(approved(1, start))).toBeFalse();
    // Bayat öneri (kabul edilemez) "Bekleyenler"de tutulmaz.
    expect(awaitsTeacherReply(approved(1, start, { ...proposal(start, 'Student'), canRespondReschedule: false }))).toBeFalse();
  });
});
