import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { of } from 'rxjs';

import { MyCalendarComponent } from './my-calendar.component';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { CalendarDayDialogComponent } from '../../shared/components/calendar-day-dialog/calendar-day-dialog.component';
import { CalendarEvent } from '../../models/calendar-event';
import { AuthService, UserProfile } from '../../services/auth.service';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import myCalendarTr from '../../../../public/i18n/my-calendar/tr.json';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({ langs: { 'my-calendar/tr': myCalendarTr } });

function makeEvent(overrides: Partial<CalendarEvent> & { date: string; worksheetId: number }): CalendarEvent {
  return {
    kind: 'assignment-deadline',
    worksheetTitle: 'Test',
    subject: 'Matematik',
    imageUrl: null,
    status: null,
    remindBeforeMinutes: null,
    isCompleted: false,
    teacherName: 'Ali Öğretmen',
    ...overrides,
  } as CalendarEvent;
}

describe('MyCalendarComponent', () => {
  let component: MyCalendarComponent;
  let router: jasmine.SpyObj<Router>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let bottomSheet: jasmine.SpyObj<MatBottomSheet>;

  beforeEach(() => {
    const testService = jasmine.createSpyObj<TestService>('TestService', ['getMyCalendar']);
    testService.getMyCalendar.and.returnValue(of([]));

    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    bottomSheet = jasmine.createSpyObj<MatBottomSheet>('MatBottomSheet', ['open']);

    TestBed.configureTestingModule({
      imports: [MyCalendarComponent, translocoTesting],
      providers: [
        { provide: TestService, useValue: testService },
        // Gerçek AuthService HttpClient ister; bu testler öğrenci görünümünü kullanır.
        { provide: AuthService, useValue: { hasRealmRole: () => false, user: signal(null) } as Partial<AuthService> },
        { provide: Router, useValue: router },
        { provide: MatDialog, useValue: dialog },
        { provide: MatBottomSheet, useValue: bottomSheet },
        // Gerçek AuthService HttpClient ister; bu testler yalnız rolü okur.
        { provide: AuthService, useValue: jasmine.createSpyObj<AuthService>('AuthService', { hasRealmRole: false }) },
      ],
    });

    component = TestBed.createComponent(MyCalendarComponent).componentInstance;
    component.isMobile.set(false);
  });

  it('onDayClick_DesktopSingleEventDay_NavigatesToTestWithoutDialog', () => {
    const day = new Date(2026, 8, 15, 10, 0);
    component.events.set([makeEvent({ date: day.toISOString(), worksheetId: 42 })]);

    component.onDayClick(day);

    expect(router.navigate).toHaveBeenCalledWith(['/test', 42]);
    expect(dialog.open).not.toHaveBeenCalled();
    expect(bottomSheet.open).not.toHaveBeenCalled();
  });

  it('onDayClick_DesktopMultiEventDay_OpensDialogWithThatDaysEvents', () => {
    const morning = new Date(2026, 8, 15, 9, 0);
    const noon = new Date(2026, 8, 15, 12, 0);
    const otherDay = new Date(2026, 8, 16, 9, 0);
    component.events.set([
      makeEvent({ date: morning.toISOString(), worksheetId: 1 }),
      makeEvent({ date: noon.toISOString(), worksheetId: 2 }),
      makeEvent({ date: otherDay.toISOString(), worksheetId: 3 }),
    ]);

    component.onDayClick(new Date(2026, 8, 15));

    expect(dialog.open).toHaveBeenCalled();
    const [comp, config] = dialog.open.calls.mostRecent().args as [unknown, { data: { events: CalendarEvent[] } }];
    expect(comp).toBe(CalendarDayDialogComponent);
    expect(config.data.events.length).toBe(2);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('onDayClick_MobileSingleEventDay_OpensBottomSheet', () => {
    component.isMobile.set(true);
    const day = new Date(2026, 8, 15, 10, 0);
    component.events.set([makeEvent({ date: day.toISOString(), worksheetId: 7 })]);

    component.onDayClick(day);

    expect(bottomSheet.open).toHaveBeenCalled();
    expect(bottomSheet.open.calls.mostRecent().args[0]).toBe(CalendarDayDialogComponent as any);
    expect(router.navigate).not.toHaveBeenCalled();
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('onDayClick_DayWithNoEvents_DoesNothing', () => {
    component.events.set([makeEvent({ date: new Date(2026, 8, 20, 10, 0).toISOString(), worksheetId: 1 })]);

    component.onDayClick(new Date(2026, 8, 15));

    expect(router.navigate).not.toHaveBeenCalled();
    expect(dialog.open).not.toHaveBeenCalled();
    expect(bottomSheet.open).not.toHaveBeenCalled();
  });
});

/** Issue #418: müsaitlik bağımsız öğretmen özelliği — okula bağlı öğretmene boş takvimde `/availability` önerilmez. */
describe('MyCalendarComponent availability CTA (issue #418)', () => {
  function teacherProfile(isIndependentTutor: boolean | undefined): UserProfile {
    return {
      email: '',
      avatar: '',
      fullName: '',
      id: 1,
      keycloakId: 'k',
      profileId: 1,
      role: 'Teacher',
      teacher: { id: 1, userId: 1, schoolName: '', schoolId: 7, isIndependentTutor },
    } as UserProfile;
  }

  function create(roles: string[], user: UserProfile | null): { component: MyCalendarComponent; router: jasmine.SpyObj<Router> } {
    const testService = jasmine.createSpyObj<TestService>('TestService', ['getMyCalendar']);
    testService.getMyCalendar.and.returnValue(of([]));
    const authStub: Partial<AuthService> = { hasRealmRole: (role: string) => roles.includes(role), user: signal(user) };
    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    TestBed.configureTestingModule({
      imports: [MyCalendarComponent, translocoTesting],
      providers: [
        { provide: TestService, useValue: testService },
        { provide: AuthService, useValue: authStub },
        { provide: Router, useValue: router },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: MatBottomSheet, useValue: jasmine.createSpyObj<MatBottomSheet>('MatBottomSheet', ['open']) },
      ],
    });
    const component = TestBed.createComponent(MyCalendarComponent).componentInstance;
    component.isMobile.set(false);
    return { component, router };
  }

  function showCta(roles: string[], user: UserProfile | null): boolean {
    return create(roles, user).component['showAvailabilityCta']();
  }

  /** Masaüstünde tek etkinlikli gün doğrudan hedefe gider (dialog yok). */
  function clickSingleBooking(roles: string[], user: UserProfile | null): jasmine.SpyObj<Router> {
    const { component, router } = create(roles, user);
    const day = new Date(2026, 8, 15, 10, 0);
    component.events.set([makeEvent({ kind: 'booking', date: day.toISOString(), worksheetId: 0 })]);
    component.onDayClick(day);
    return router;
  }

  it('BookingClick_NotIndependentTeacher_DoesNotNavigateToBookingRequests', () => {
    expect(clickSingleBooking(['Teacher'], teacherProfile(false)).navigate).not.toHaveBeenCalled();
  });

  it('BookingClick_IndependentOrUnknownTeacher_NavigatesToBookingRequests', () => {
    expect(clickSingleBooking(['Teacher'], teacherProfile(true)).navigate).toHaveBeenCalledWith(['/booking-requests']);
    TestBed.resetTestingModule();
    expect(clickSingleBooking(['Teacher'], teacherProfile(undefined)).navigate).toHaveBeenCalledWith(['/booking-requests']);
  });

  it('BookingClick_Student_NavigatesToMyBookings', () => {
    expect(clickSingleBooking(['Student'], null).navigate).toHaveBeenCalledWith(['/my-bookings']);
  });

  it('NotIndependentTeacher_HidesCta', () => {
    expect(showCta(['Teacher'], teacherProfile(false))).toBeFalse();
  });

  it('IndependentTeacher_ShowsCta', () => {
    expect(showCta(['Teacher'], teacherProfile(true))).toBeTrue();
  });

  it('FlagUnknown_ShowsCta', () => {
    expect(showCta(['Teacher'], teacherProfile(undefined))).toBeTrue();
  });

  it('Student_NoCta', () => {
    expect(showCta(['Student'], null)).toBeFalse();
  });
});
