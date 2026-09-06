import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { of } from 'rxjs';

import { MyCalendarComponent } from './my-calendar.component';
import { TestService } from '../../services/test.service';
import { CalendarDayDialogComponent } from '../../shared/components/calendar-day-dialog/calendar-day-dialog.component';
import { CalendarEvent } from '../../models/calendar-event';

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
      imports: [MyCalendarComponent],
      providers: [
        { provide: TestService, useValue: testService },
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
