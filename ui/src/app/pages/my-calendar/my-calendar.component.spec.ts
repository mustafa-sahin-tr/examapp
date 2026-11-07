import type { MockedObject } from "vitest";
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { of } from 'rxjs';

import { MyCalendarComponent } from './my-calendar.component';
import { TestService } from '../../services/test.service';
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

function makeEvent(overrides: Partial<CalendarEvent> & {
    date: string;
    worksheetId: number;
}): CalendarEvent {
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
    let router: MockedObject<Router>;
    let dialog: MockedObject<MatDialog>;
    let bottomSheet: MockedObject<MatBottomSheet>;

    beforeEach(() => {
        const testService = {
            getMyCalendar: vi.fn().mockName("TestService.getMyCalendar")
        };
        testService.getMyCalendar.mockReturnValue(of([]));

        router = {
            navigate: vi.fn().mockName("Router.navigate")
        } as unknown as MockedObject<Router>;
        dialog = {
            open: vi.fn().mockName("MatDialog.open")
        } as unknown as MockedObject<MatDialog>;
        bottomSheet = {
            open: vi.fn().mockName("MatBottomSheet.open")
        } as unknown as MockedObject<MatBottomSheet>;

        TestBed.configureTestingModule({
            imports: [MyCalendarComponent, translocoTesting],
            providers: [
                { provide: TestService, useValue: testService },
                // Gerçek AuthService HttpClient ister; bu testler öğrenci görünümünü kullanır.
                { provide: AuthService, useValue: { hasRealmRole: () => false, user: signal(null) } as Partial<AuthService> },
                { provide: Router, useValue: router },
                { provide: MatDialog, useValue: dialog },
                { provide: MatBottomSheet, useValue: bottomSheet },
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
        const [comp, config] = vi.mocked(dialog.open).mock.lastCall as [
            unknown,
            {
                data: {
                    events: CalendarEvent[];
                };
            }
        ];
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
        expect(vi.mocked(bottomSheet.open).mock.lastCall![0]).toBe(CalendarDayDialogComponent as any);
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

    function create(roles: string[], user: UserProfile | null): {
        component: MyCalendarComponent;
        router: MockedObject<Router>;
    } {
        const testService = {
            getMyCalendar: vi.fn().mockName("TestService.getMyCalendar")
        };
        testService.getMyCalendar.mockReturnValue(of([]));
        const authStub: Partial<AuthService> = { hasRealmRole: (role: string) => roles.includes(role), user: signal(user) };
        const router = {
            navigate: vi.fn().mockName("Router.navigate")
        } as unknown as MockedObject<Router>;
        TestBed.configureTestingModule({
            imports: [MyCalendarComponent, translocoTesting],
            providers: [
                { provide: TestService, useValue: testService },
                { provide: AuthService, useValue: authStub },
                { provide: Router, useValue: router },
                { provide: MatDialog, useValue: {
                        open: vi.fn().mockName("MatDialog.open")
                    } },
                { provide: MatBottomSheet, useValue: {
                        open: vi.fn().mockName("MatBottomSheet.open")
                    } },
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
    function clickSingleBooking(roles: string[], user: UserProfile | null): MockedObject<Router> {
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
        expect(showCta(['Teacher'], teacherProfile(false))).toBe(false);
    });

    it('IndependentTeacher_ShowsCta', () => {
        expect(showCta(['Teacher'], teacherProfile(true))).toBe(true);
    });

    it('FlagUnknown_ShowsCta', () => {
        expect(showCta(['Teacher'], teacherProfile(undefined))).toBe(true);
    });

    it('Student_NoCta', () => {
        expect(showCta(['Student'], null)).toBe(false);
    });
});

/** Issue #888: haftalık ajanda — gerçek router (routerLink'ler) ve navigate casusu. */
describe('MyCalendarComponent week agenda (issue #888)', () => {
    let fixture: ComponentFixture<MyCalendarComponent>;
    let component: MyCalendarComponent;
    let getMyCalendar: ReturnType<typeof vi.fn>;
    let router: { navigate: ReturnType<typeof vi.fn> };

    beforeEach(() => {
        getMyCalendar = vi.fn(() => of([] as CalendarEvent[]));
        TestBed.configureTestingModule({
            imports: [MyCalendarComponent, translocoTesting],
            providers: [
                provideRouter([]),
                { provide: TestService, useValue: { getMyCalendar } },
                { provide: AuthService, useValue: { hasRealmRole: () => false, user: signal(null) } as Partial<AuthService> },
            ],
        });
        router = TestBed.inject(Router) as unknown as { navigate: ReturnType<typeof vi.fn> };
        vi.spyOn(router, 'navigate').mockResolvedValue(true);
        fixture = TestBed.createComponent(MyCalendarComponent);
        component = fixture.componentInstance;
        component.isMobile.set(false);
    });

    it('defaultView_IsWeek_FetchesMondayToNextMonday_issue888', () => {
        fixture.detectChanges();

        expect(component.view()).toBe('week');
        const [from, to] = getMyCalendar.mock.calls[0] as [Date, Date];
        expect(from.getDay()).toBe(1);
        expect((to.getTime() - from.getTime()) / 86_400_000).toBeCloseTo(7, 0);
        expect(from.getTime()).toBe(component.weekStart().getTime());
    });

    it('emptyWeek_Student_ShowsTwoActions_issue888', () => {
        fixture.detectChanges();

        const empty: HTMLElement = fixture.nativeElement.querySelector('[data-test="week-empty"]');
        expect(empty.textContent).toContain('Bu hafta planlı bir şey yok');
        const buttons = Array.from(empty.querySelectorAll('button')).map((b) => b.textContent?.trim());
        expect(buttons).toEqual(['Program oluştur', 'Günün sorularını çöz']);
        expect(fixture.nativeElement.querySelector('[data-test="agenda"]')).toBeNull();
    });

    it('weekWithEvents_RendersSevenDaysTodayHighlightedAndStartAction_issue888', () => {
        const today = new Date();
        today.setHours(23, 30, 0, 0);
        getMyCalendar.mockReturnValue(of([makeEvent({ date: today.toISOString(), worksheetId: 42, worksheetTitle: 'Kesirler' })]));
        fixture.detectChanges();

        const agenda: HTMLElement = fixture.nativeElement.querySelector('[data-test="agenda"]');
        expect(agenda.querySelectorAll('.agenda__day').length).toBe(7);
        const todayEl = agenda.querySelector('.agenda__day--today') as HTMLElement;
        expect(todayEl.getAttribute('aria-current')).toBe('date');
        expect(todayEl.textContent).toContain('bugün');
        const row = todayEl.querySelector('.agenda__row') as HTMLElement;
        expect(row.dataset['kind']).toBe('assignment');
        expect(row.textContent).toContain('Kesirler');
        expect(row.textContent).toContain('Ödev · Matematik · teslim');
        expect(agenda.querySelectorAll('.agenda__free').length).toBe(6);

        (row.querySelector('.agenda__action') as HTMLButtonElement).click();
        expect(router.navigate).toHaveBeenCalledWith(['/test', 42]);
    });

    it('prevNext_InWeekView_MovesBySevenDays_issue888', () => {
        fixture.detectChanges();
        const start = component.weekStart().getTime();

        component.next();
        expect(component.weekStart().getTime() - start).toBeCloseTo(7 * 86_400_000, -6);
        component.prev();
        component.prev();
        expect(start - component.weekStart().getTime()).toBeCloseTo(7 * 86_400_000, -6);
        component.goToday();
        expect(component.weekStart().getTime()).toBe(start);
    });

    it('setView_Month_ShowsGridAndRefetchesMonthRange_issue888', () => {
        fixture.detectChanges();
        getMyCalendar.mockClear();

        component.setView('month');
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelector('app-month-calendar-grid')).toBeTruthy();
        expect(fixture.nativeElement.querySelector('[data-test="agenda"]')).toBeNull();
        expect(getMyCalendar).toHaveBeenCalledTimes(1);
        const [from, to] = getMyCalendar.mock.calls[0] as [Date, Date];
        expect((to.getTime() - from.getTime()) / 86_400_000).toBeGreaterThanOrEqual(28);
        expect(component.viewMonth().getMonth()).toBe(component.weekStart().getMonth());
    });

});
