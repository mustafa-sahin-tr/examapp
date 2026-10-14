import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { of } from 'rxjs';

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
    for (const status of ['Pending', 'Approved', 'Rejected', 'AwaitingParent', 'ParentRejected', 'Expired'] as const) {
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
