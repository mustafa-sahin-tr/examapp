import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { STUDENT_BOOKING_STATUS_VIEW, StudentBookingsComponent } from './student-bookings.component';
import { Booking, BookingStatus } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import studentBookingsTr from '../../../../public/i18n/student-bookings/tr.json';
import studentBookingsEn from '../../../../public/i18n/student-bookings/en.json';

/** Issue #439: öğrenci randevu listesinde veli onayı durumları. */
describe('StudentBookingsComponent (issue #439)', () => {
  let fixture: ComponentFixture<StudentBookingsComponent>;

  function booking(id: number, status: BookingStatus, overrides: Partial<Booking> = {}): Booking {
    const day = 20 + id;
    return {
      id,
      teacherId: 4,
      teacherName: `Öğretmen ${id}`,
      studentId: 21,
      availabilitySlotId: 100 + id,
      date: `2030-10-${day}`,
      startTime: '11:00:00',
      endTime: '12:00:00',
      startUtc: `2030-10-${day}T11:00:00Z`,
      endUtc: `2030-10-${day}T12:00:00Z`,
      status,
      createdAt: '2030-10-01T08:00:00Z',
      ...overrides,
    };
  }

  async function setup(items: Booking[], lang = 'tr'): Promise<HTMLElement> {
    const service = jasmine.createSpyObj<BookingService>('BookingService', ['getStudentRequests', 'extractError']);
    service.getStudentRequests.and.returnValue(of({ success: true, items }));

    TestBed.configureTestingModule({
      imports: [
        StudentBookingsComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { 'student-bookings/tr': studentBookingsTr, 'student-bookings/en': studentBookingsEn },
        }),
      ],
      providers: [
        provideRouter([]),
        { provide: BookingService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(StudentBookingsComponent);
    // `whenStable` kullanılmaz: sayfanın katılım penceresi `interval`'i uygulamayı hiç "stable" bırakmaz.
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const chips = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('[data-test="status-chip"]')).map((c) => c.textContent?.trim());

  afterEach(() => fixture?.destroy());

  it('StatusView_MapsEveryStatus', () => {
    for (const status of ['Pending', 'Approved', 'Rejected', 'AwaitingParent', 'ParentRejected', 'Expired', 'Cancelled'] as const) {
      expect(STUDENT_BOOKING_STATUS_VIEW[status]).withContext(status).toBeDefined();
    }
  });

  it('AwaitingParent_ShowsBadgeAndExpiryHint', async () => {
    const el = await setup([booking(1, 'AwaitingParent', { expiresAt: '2030-10-08T08:00:00Z' })]);

    expect(chips(el)).toEqual(['Veli onayı bekleniyor']);
    expect(el.querySelector('[data-test="status-chip"]')?.classList).toContain('is-awaiting-parent');
    expect(el.querySelector('[data-test="awaiting-parent-hint"]')?.textContent).toContain('tarihine kadar onaylamazsa');
  });

  it('ActiveFilter_HidesClosedStatuses_AllShowsParentNote', async () => {
    const el = await setup([
      booking(1, 'Pending'),
      booking(2, 'ParentRejected', { rejectionReason: 'Bu hafta sınav var' }),
      booking(3, 'Expired'),
      booking(4, 'Rejected'),
    ]);

    expect(chips(el)).toEqual(['Onay bekliyor']);

    (el.querySelectorAll('mat-button-toggle button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(chips(el)).toEqual(['Onay bekliyor', 'Velin reddetti', 'Talep kapandı', 'Reddedildi']);
    // Expired: veli süresi ya da öğretmen askısı/slot silme — genel açıklama (issue #439 review).
    expect(el.querySelectorAll('[data-test="expired-hint"]').length).toBe(1);
    expect(el.querySelector('[data-test="expired-hint"]')?.textContent).toContain('öğretmen bu saati artık sunmuyor');
    const reasons = Array.from(el.querySelectorAll('[data-test="rejection-reason"]')).map((r) => r.textContent?.trim());
    expect(reasons).toEqual(['Velinin notu: Bu hafta sınav var']);
    expect(el.querySelector('[data-test="awaiting-parent-hint"]')).toBeNull();
  });

  it('English_LabelsNewStatuses', async () => {
    const el = await setup([booking(1, 'AwaitingParent', { expiresAt: '2030-10-08T08:00:00Z' })], 'en');

    expect(chips(el)).toEqual(['Awaiting parent approval']);
  });
});

/** Issue #315: iptal / erteleme / no-show aksiyonları. */
describe('StudentBookingsComponent (issue #315)', () => {
  let fixture: ComponentFixture<StudentBookingsComponent>;
  let service: jasmine.SpyObj<BookingService>;
  let dialog: { open: jasmine.Spy };
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  const HOUR = 60 * 60 * 1000;

  function booking(id: number, status: BookingStatus, startMs: number, overrides: Partial<Booking> = {}): Booking {
    return {
      id,
      teacherId: 4,
      teacherName: `Öğretmen ${id}`,
      studentId: 21,
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

  async function setup(items: Booking[], dialogResult: unknown = undefined): Promise<HTMLElement> {
    service = jasmine.createSpyObj<BookingService>('BookingService', [
      'getStudentRequests',
      'extractError',
      'respondReschedule',
      'reportNoShow',
    ]);
    service.getStudentRequests.and.returnValue(of({ success: true, items }));
    service.respondReschedule.and.returnValue(of({ success: true, message: 'Sunucu mesajı' }));
    service.reportNoShow.and.returnValue(of({ success: true, message: 'Bildirildi' }));
    service.extractError.and.callFake((err: HttpErrorResponse) => (err.error as { message: string }).message);
    dialog = { open: jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(dialogResult) }) };
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        StudentBookingsComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'student-bookings/tr': studentBookingsTr } }),
      ],
      providers: [
        provideRouter([]),
        { provide: BookingService, useValue: service },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(StudentBookingsComponent);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const btn = (el: HTMLElement, test: string) => el.querySelector<HTMLButtonElement>(`[data-test="${test}"]`);

  afterEach(() => fixture?.destroy());

  it('Approved_Cancel_OpensCancelDialog_ReloadsWithServerMessage', async () => {
    const el = await setup([approved(1, Date.now() + 5 * 24 * HOUR)], { success: true, message: 'Randevu iptal edildi.' });

    btn(el, 'cancel-booking')!.click();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    const config = dialog.open.calls.mostRecent().args[1] as { data: { bookingId: number; mode: string } };
    expect(config.data.bookingId).toBe(1);
    expect(config.data.mode).toBe('cancel');
    expect(service.getStudentRequests).toHaveBeenCalledTimes(2);
    expect(snackBar.open.calls.mostRecent().args[0]).toBe('Randevu iptal edildi.');
  });

  it('Approved_CancelDismissed_ReloadsList', async () => {
    const el = await setup([approved(1, Date.now() + 5 * 24 * HOUR)], undefined);

    btn(el, 'cancel-booking')!.click();

    expect(service.getStudentRequests).toHaveBeenCalledTimes(2); // dialog içi hata sonrası kapanmış olabilir → tazele
  });

  it('Approved_AfterDeadline_CancelDisabled', async () => {
    const el = await setup([approved(1, Date.now() + 2 * HOUR, { canProposeReschedule: false })]);

    expect(btn(el, 'cancel-booking')!.disabled).toBeTrue();
    expect(btn(el, 'reschedule-propose')).toBeNull();
  });

  it('Approved_Propose_OpensRescheduleDialogWithTeacherOpenSlots', async () => {
    const el = await setup([approved(1, Date.now() + 5 * 24 * HOUR)], { success: true });

    btn(el, 'reschedule-propose')!.click();

    const config = dialog.open.calls.mostRecent().args[1] as { data: { source: string; booking: Booking } };
    expect(config.data.source).toBe('teacherOpenSlots');
    expect(config.data.booking.id).toBe(1);
    expect(service.getStudentRequests).toHaveBeenCalledTimes(2);
  });

  it('Pending_ShowsWithdrawRequest', async () => {
    const el = await setup([booking(1, 'Pending', Date.now() + 5 * 24 * HOUR)], { success: true });

    expect(btn(el, 'cancel-booking')).toBeNull();
    btn(el, 'withdraw-request')!.click();

    const config = dialog.open.calls.mostRecent().args[1] as { data: { mode: string } };
    expect(config.data.mode).toBe('withdraw');
  });

  it('TeacherProposal_AcceptAndDecline', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([
      approved(1, start, {
        canProposeReschedule: false,
        proposedSlotId: 300,
        proposedStartUtc: new Date(start + 24 * HOUR).toISOString(),
        proposedEndUtc: new Date(start + 25 * HOUR).toISOString(),
        proposedByRole: 'Teacher',
        canRespondReschedule: true,
      }),
    ]);

    expect(el.querySelector('[data-test="reschedule-proposal"]')?.textContent).toContain('Öğretmenin erteleme önerisi');
    expect(btn(el, 'reschedule-propose')).toBeNull();
    expect(btn(el, 'reschedule-withdraw')).toBeNull();

    btn(el, 'reschedule-accept')!.click();
    expect(service.respondReschedule).toHaveBeenCalledWith(1, true);
    expect(snackBar.open.calls.mostRecent().args[0]).toBe('Sunucu mesajı');
    expect(service.getStudentRequests).toHaveBeenCalledTimes(2);

    fixture.detectChanges();
    btn(el, 'reschedule-decline')!.click();
    expect(service.respondReschedule).toHaveBeenCalledWith(1, false);
  });

  it('OwnProposal_WithdrawOnly', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([
      approved(1, start, {
        canProposeReschedule: false,
        proposedSlotId: 300,
        proposedStartUtc: new Date(start + 24 * HOUR).toISOString(),
        proposedEndUtc: new Date(start + 25 * HOUR).toISOString(),
        proposedByRole: 'Student',
      }),
    ]);

    expect(btn(el, 'reschedule-accept')).toBeNull();
    btn(el, 'reschedule-withdraw')!.click();
    expect(service.respondReschedule).toHaveBeenCalledWith(1, false);
  });

  it('RespondError_ShowsServerMessage_AndReloads', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([
      approved(1, start, {
        proposedSlotId: 300,
        proposedStartUtc: new Date(start + 24 * HOUR).toISOString(),
        proposedEndUtc: new Date(start + 25 * HOUR).toISOString(),
        proposedByRole: 'Teacher',
        canRespondReschedule: true,
      }),
    ]);
    service.respondReschedule.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, message: 'Önerilen saat artık uygun değil.', errorCode: 'ProposedSlotUnavailable' },
          })
      )
    );

    btn(el, 'reschedule-accept')!.click();

    expect(snackBar.open.calls.mostRecent().args[0]).toBe('Önerilen saat artık uygun değil.');
    expect(service.getStudentRequests).toHaveBeenCalledTimes(2);
  });

  it('AfterLesson_NoShow_ConfirmsThenReports', async () => {
    const el = await setup([approved(1, Date.now() - 3 * HOUR, { canProposeReschedule: false })], true);

    expect(btn(el, 'cancel-booking')).toBeNull();
    btn(el, 'report-no-show')!.click();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(service.reportNoShow).toHaveBeenCalledWith(1);
    expect(service.getStudentRequests).toHaveBeenCalledTimes(2);
  });

  it('CancelledAndNoShowRows_ShowWhoAndReason', async () => {
    const el = await setup([
      booking(1, 'Cancelled', Date.now() + 5 * 24 * HOUR, { cancelledByRole: 'Teacher', cancellationReason: 'Hastayım' }),
      approved(2, Date.now() - 3 * HOUR, { noShowParty: 'Teacher', canProposeReschedule: false }),
    ]);
    (el.querySelectorAll('mat-button-toggle button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    const chipTexts = Array.from(el.querySelectorAll('[data-test="status-chip"]')).map((c) => c.textContent?.trim());
    expect(chipTexts).toContain('İptal edildi');
    expect(el.querySelector('[data-test="cancelled-by"]')?.textContent).toContain('Öğretmen bu randevuyu iptal etti');
    expect(el.querySelector('[data-test="cancellation-reason"]')?.textContent).toContain('Hastayım');
    expect(el.querySelector('[data-test="no-show-info"]')?.textContent).toContain('Öğretmenin derse katılmadığı');
    expect(btn(el, 'report-no-show')).toBeNull();
  });

  it('ActiveFilter_HidesCancelled', async () => {
    const el = await setup([booking(1, 'Cancelled', Date.now() + 5 * 24 * HOUR)]);

    expect(el.querySelectorAll('[data-test="status-chip"]').length).toBe(0);
  });

  it('StaleTeacherProposal_HidesAccept_KeepsDecline', async () => {
    const start = Date.now() + 5 * 24 * HOUR;
    const el = await setup([
      approved(1, start, {
        canProposeReschedule: false,
        proposedSlotId: 300,
        proposedStartUtc: new Date(start + 24 * HOUR).toISOString(),
        proposedEndUtc: new Date(start + 25 * HOUR).toISOString(),
        proposedByRole: 'Teacher',
        canRespondReschedule: false,
      }),
    ]);

    expect(btn(el, 'reschedule-accept')).toBeNull();
    expect(btn(el, 'reschedule-decline')).not.toBeNull();
  });
});
