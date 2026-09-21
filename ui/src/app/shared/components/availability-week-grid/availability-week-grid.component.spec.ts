import { ComponentFixture, TestBed, fakeAsync, tick, flush } from '@angular/core/testing';
import { PLATFORM_ID } from '@angular/core';
import { AvailabilityWeekGridComponent } from './availability-week-grid.component';
import { AvailabilitySlot, ActiveBookingStatus } from '../../../models/booking.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import taTr from '../../../../../public/i18n/teacher-availability/tr.json';
import taEn from '../../../../../public/i18n/teacher-availability/en.json';

// Scope sözlüğü gerçek dosyadan yüklenir; aksi halde şablon ham anahtarı basar ve etiket testleri anlamsızlaşır.
const translocoTesting = translocoTestingModule({
  langs: { 'teacher-availability/tr': taTr, 'teacher-availability/en': taEn },
});

/**
 * Helper: İçinde bulunulan (Pazartesi başlangıçlı) haftanın `dayOffset`. gününde bir an üretir.
 * FullCalendar da aynı `new Date()` haftasını gösterdiği için slot her zaman görünen aralıkta kalır.
 */
function createWeekTestDate(dayOffset: number, hour: number, minute: number = 0): Date {
  // Get Monday of current week
  const now = new Date();
  const day = now.getDay();
  const diff = now.getDate() - day + (day === 0 ? -6 : 1); // Monday
  const monday = new Date(now.setDate(diff));
  monday.setHours(0, 0, 0, 0);

  // Add dayOffset days (0=Mon, 3=Thu, 5=Sat, 6=Sun)
  const slotDate = new Date(monday);
  slotDate.setDate(slotDate.getDate() + dayOffset);
  slotDate.setHours(hour, minute, 0, 0);
  return slotDate;
}

/**
 * Create AvailabilitySlot within the visible week.
 * @param id - slot ID
 * @param dayOffset - 0-6 (Mon-Sun)
 * @param startHour - start time hour
 * @param endHour - end time hour
 * @param isBooked - booking status
 * @param bookingStatus - if booked, status type
 */
function createSlotInWeek(
  id: number,
  dayOffset: number,
  startHour: number,
  endHour: number,
  isBooked = false,
  bookingStatus?: ActiveBookingStatus | null
): AvailabilitySlot {
  const start = createWeekTestDate(dayOffset, startHour);
  const end = createWeekTestDate(dayOffset, endHour);
  const dateStr = start.toISOString().split('T')[0];

  return {
    id,
    teacherId: 10,
    date: dateStr,
    startTime: start.toISOString().split('T')[1].substring(0, 8),
    endTime: end.toISOString().split('T')[1].substring(0, 8),
    createdAt: new Date().toISOString(),
    startUtc: start.toISOString(),
    endUtc: end.toISOString(),
    isBooked,
    bookingStatus,
  };
}

describe('AvailabilityWeekGridComponent', () => {
  let component: AvailabilityWeekGridComponent;
  let fixture: ComponentFixture<AvailabilityWeekGridComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AvailabilityWeekGridComponent, translocoTesting],
    }).compileComponents();

    fixture = TestBed.createComponent(AvailabilityWeekGridComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should render exactly one event for a single valid slot', fakeAsync(() => {
    fixture.componentRef.setInput('slots', [createSlotInWeek(1, 3, 14, 15)]);
    fixture.detectChanges();
    tick();

    expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
  }));

  it('should apply is-free class to free slots', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00, free

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBeGreaterThan(0);
    const freeEvent = Array.from(eventElements).find((el) => el.classList.contains('is-free'));
    expect(freeEvent).toBeTruthy();
  }));

  it('should apply is-approved class to approved bookings', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, true, 'Approved');

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBeGreaterThan(0);
    const approvedEvent = Array.from(eventElements).find((el) => el.classList.contains('is-approved'));
    expect(approvedEvent).toBeTruthy();
  }));

  it('should apply is-pending class to pending bookings', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, true, 'Pending');

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBeGreaterThan(0);
    const pendingEvent = Array.from(eventElements).find((el) => el.classList.contains('is-pending'));
    expect(pendingEvent).toBeTruthy();
  }));

  it('should apply is-past class to past slots', fakeAsync(() => {
    // Saat haftanın ortasına (Çarşamba 12:00) sabitlenir; böylece "geçmiş" slot her zaman görünen haftada kalır
    // (Pazartesi gece yarısı civarında koşulsa bile).
    const past = createSlotInWeek(1, 0, 10, 11); // Pzt 10:00-11:00
    const future = createSlotInWeek(2, 4, 10, 11); // Cum 10:00-11:00
    jasmine.clock().mockDate(createWeekTestDate(2, 12));

    fixture.componentRef.setInput('slots', [past, future]);
    fixture.detectChanges();
    tick();

    const events = Array.from(
      fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>
    );
    expect(events.length).toBe(2);
    expect(events.filter((el) => el.classList.contains('is-past')).length).toBe(1);
  }));

  it('should render legend with three status items', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const legendItems = fixture.nativeElement.querySelectorAll('.awg__legend-item');
    expect(legendItems.length).toBe(3);
  }));

  it('should render legend swatches with correct status classes', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const swatches = fixture.nativeElement.querySelectorAll('.awg__swatch');
    const classes = Array.from(swatches as NodeListOf<HTMLElement>).map((el) => {
      if (el.classList.contains('is-free')) return 'is-free';
      if (el.classList.contains('is-pending')) return 'is-pending';
      if (el.classList.contains('is-approved')) return 'is-approved';
      return null;
    });

    expect(classes).toContain('is-free');
    expect(classes).toContain('is-pending');
    expect(classes).toContain('is-approved');
  }));

  it('should show loading indicator when loading input is true', fakeAsync(() => {
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('loading', true);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg--loading');
    expect(section).toBeTruthy();
  }));

  it('should set aria-busy when loading', fakeAsync(() => {
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('loading', true);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg');
    expect(section.getAttribute('aria-busy')).toBe('true');
  }));

  it('should render navigation toolbar with prev/next/today buttons', fakeAsync(() => {
    fixture.detectChanges();
    tick(100);

    const navGroup = fixture.nativeElement.querySelector('[role="group"]') as HTMLElement;
    expect(navGroup).toBeTruthy();

    const buttons = navGroup?.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    expect(buttons?.length).toBeGreaterThanOrEqual(3);
  }));

  describe('hafta gezinme', () => {
    /** `rangeTitle` `datesSet` içinde `queueMicrotask` ile yazılır: mikro görevleri boşalt, sonra yeniden render et. */
    function rangeText(): string {
      tick();
      fixture.detectChanges();
      return (fixture.nativeElement.querySelector('.awg__range') as HTMLElement).textContent?.trim() ?? '';
    }

    function navButtons(): HTMLButtonElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.awg__nav button') as NodeListOf<HTMLButtonElement>);
    }

    it('ilk render sonrası hafta aralığı başlığı dolu', fakeAsync(() => {
      fixture.detectChanges();

      expect(rangeText()).not.toBe('');
      expect(fixture.nativeElement.querySelector('full-calendar')).toBeTruthy();
    }));

    it('sonraki / önceki hafta başlığı değiştirir ve geri döndürür', fakeAsync(() => {
      fixture.detectChanges();
      const initial = rangeText();
      const [prev, , next] = navButtons();

      next.click();
      const afterNext = rangeText();
      expect(afterNext).not.toBe(initial);

      prev.click();
      expect(rangeText()).toBe(initial);

      prev.click();
      const afterPrev = rangeText();
      expect(afterPrev).not.toBe(initial);
      expect(afterPrev).not.toBe(afterNext);
    }));

    it('"Bugün" içinde bulunulan haftaya döndürür', fakeAsync(() => {
      fixture.detectChanges();
      const initial = rangeText();
      const [, today, next] = navButtons();

      next.click();
      next.click();
      expect(rangeText()).not.toBe(initial);

      today.click();
      expect(rangeText()).toBe(initial);
    }));

    it('gezinme sonrası yalnızca o haftanın olayları görünür', fakeAsync(() => {
      fixture.componentRef.setInput('slots', [createSlotInWeek(1, 3, 14, 15)]);
      fixture.detectChanges();
      tick();
      expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);

      navButtons()[2].click();
      tick();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(0);
    }));
  });

  it('should have tabindex="0" on wrapper element and NOT on .fc-event', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00, free

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    // Wrapper .awg__ev should have tabindex="0"
    const eventWrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
    expect(eventWrappers.length).toBeGreaterThan(0);
    const wrapperWithTabindex = Array.from(eventWrappers).find((el) => el.getAttribute('tabindex') === '0');
    expect(wrapperWithTabindex).toBeTruthy('Wrapper should have tabindex="0"');

    // .fc-event elements should NOT have tabindex (eventInteractive off)
    const fcEvents = fixture.nativeElement.querySelectorAll('.fc-event') as NodeListOf<HTMLElement>;
    for (const event of Array.from(fcEvents)) {
      expect(event.hasAttribute('tabindex')).toBeFalsy('.fc-event should not have tabindex');
    }
  }));

  it('should expose day, time, status and student name in aria-label of a booked slot', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, true, 'Approved');
    slot.studentName = 'Ayşe';

    fixture.componentRef.setInput('slots', [slot]);
    fixture.detectChanges();
    tick();

    const wrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
    expect(wrappers.length).toBe(1);

    const label = wrappers[0].getAttribute('aria-label') ?? '';
    const parts = label.split(' · ');
    expect(parts.length).toBe(4); // gün · saat · durum · öğrenci
    expect(parts[1]).toContain('14:00');
    expect(parts[1]).toContain('15:00');
    expect(parts[2]).toBe(taTr.status.approved);
    expect(parts[3]).toBe('Öğrenci: Ayşe');
    expect(wrappers[0].querySelector('.awg__ev-note')?.textContent).toContain('Ayşe');
  }));

  it('should not expose student name when slot is not booked', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, false);
    slot.studentName = 'Ayşe';

    fixture.componentRef.setInput('slots', [slot]);
    fixture.detectChanges();
    tick();

    const wrapper = fixture.nativeElement.querySelector('.awg__ev') as HTMLElement;
    const label = wrapper.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Ayşe');
    expect(label.split(' · ')[2]).toBe(taTr.status.free);
    expect(wrapper.querySelector('.awg__ev-note')?.textContent?.trim()).toBe(taTr.status.free);
  }));

  it('should skip invalid slots', fakeAsync(() => {
    const validSlot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00

    const invalidSlot: AvailabilitySlot = {
      id: 2,
      teacherId: 10,
      date: validSlot.date,
      startTime: '16:00:00',
      endTime: '15:00:00', // Invalid: end before start
      createdAt: new Date().toISOString(),
      startUtc: new Date(new Date(validSlot.startUtc).getTime() + 2 * 60 * 60 * 1000).toISOString(),
      endUtc: new Date(new Date(validSlot.startUtc).getTime() + 1 * 60 * 60 * 1000).toISOString(),
      isBooked: false,
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [validSlot, invalidSlot]);
    });

    fixture.detectChanges();
    tick(500);

    // Component should render without error - invalid slot is skipped internally
    const calendar = fixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
    expect(calendar).toBeTruthy();

    // Verify only the valid slot is rendered (invalid one is skipped)
    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBe(1, 'Should render only 1 valid event, skip invalid');
  }));

  it('should handle empty slots', fakeAsync(() => {
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', []);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg');
    expect(section).toBeTruthy();
  }));

  it('should only render calendar on browser (not SSR)', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const canvas = fixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
    expect(canvas).toBeTruthy();
  }));

  it('should NOT render calendar in SSR environment but render toolbar', fakeAsync(() => {
    // Create a separate test bed with server platform
    const ssrTestBed = TestBed.resetTestingModule();
    ssrTestBed.configureTestingModule({
      imports: [AvailabilityWeekGridComponent, translocoTesting],
      providers: [{ provide: PLATFORM_ID, useValue: 'server' }],
    });

    const ssrFixture = ssrTestBed.createComponent(AvailabilityWeekGridComponent);
    ssrFixture.detectChanges();
    tick();

    // In SSR, FullCalendar canvas should not be rendered
    const canvas = ssrFixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
    expect(canvas).toBeFalsy('Calendar canvas should NOT be rendered in SSR');

    // But toolbar should still be rendered
    const toolbar = ssrFixture.nativeElement.querySelector('.awg__toolbar') as HTMLElement;
    expect(toolbar).toBeTruthy('Toolbar should be rendered in SSR');

    // Scroll area should not be rendered in SSR
    const scrollArea = ssrFixture.nativeElement.querySelector('.awg__scroll') as HTMLElement;
    expect(scrollArea).toBeFalsy('Scroll area should NOT be rendered in SSR');
  }));

  it('should have role="region" on scroll area for accessibility', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const scrollArea = fixture.nativeElement.querySelector('.awg__scroll');
    expect(scrollArea?.getAttribute('role')).toBe('region');
  }));

  it('should include aria-label on scroll area', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const scrollArea = fixture.nativeElement.querySelector('.awg__scroll');
    expect(scrollArea?.getAttribute('aria-label')).toBeTruthy();
  }));
});
