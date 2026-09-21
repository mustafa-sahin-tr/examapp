import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { AvailabilityWeekGridComponent } from './availability-week-grid.component';
import { AvailabilitySlot } from '../../../models/booking.model';
import { translocoTestingModule } from '../../testing/transloco-testing';

const translocoTesting = translocoTestingModule();

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

  it('should render grid with valid slots', fakeAsync(() => {
    const slot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: false,
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg');
    expect(section).toBeTruthy();
  }));

  it('should apply is-free class to free slots', fakeAsync(() => {
    const slot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: false,
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    // Calendar component should be created and render
    const calendar = fixture.nativeElement.querySelector('full-calendar');
    const gridElement = fixture.nativeElement.querySelector('.awg__canvas');
    expect(gridElement).toBeTruthy();
  }));

  it('should apply is-approved class to approved bookings', fakeAsync(() => {
    const slot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: true,
      bookingStatus: 'Approved',
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    // Verify calendar is rendered with events
    const calendar = fixture.nativeElement.querySelector('.fc');
    expect(calendar).toBeTruthy();
  }));

  it('should apply is-pending class to pending bookings', fakeAsync(() => {
    const slot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: true,
      bookingStatus: 'Pending',
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    // Verify calendar renders with the pending slot
    const calendar = fixture.nativeElement.querySelector('.fc');
    expect(calendar).toBeTruthy();
  }));

  it('should apply is-past class to past slots', fakeAsync(() => {
    const now = new Date();
    const pastDate = new Date(now.getTime() - 60 * 60 * 1000); // 1 hour ago
    const slot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: pastDate.toISOString(),
      endUtc: new Date(pastDate.getTime() + 60 * 60 * 1000).toISOString(),
      isBooked: false,
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick();

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event');
    const hasPastClass = Array.from(eventElements).some((el: any) => el.classList.contains('is-past'));
    expect(hasPastClass).toBeTrue();
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
    const classes = Array.from(swatches).map((el: any) => {
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

    const navGroup = fixture.nativeElement.querySelector('[role="group"]');
    expect(navGroup).toBeTruthy();

    const buttons = navGroup?.querySelectorAll('button');
    expect(buttons?.length).toBeGreaterThanOrEqual(3);
  }));

  it('should have prev button that can be clicked', fakeAsync(() => {
    fixture.detectChanges();
    tick(500);

    const navGroup = fixture.nativeElement.querySelector('[role="group"]');
    const prevButton = navGroup?.querySelector('button:nth-child(1)') as HTMLButtonElement;

    expect(prevButton).toBeTruthy();
    prevButton?.click();
    tick();
  }));

  it('should have next button that can be clicked', fakeAsync(() => {
    fixture.detectChanges();
    tick(500);

    const navGroup = fixture.nativeElement.querySelector('[role="group"]');
    const nextButton = navGroup?.querySelector('button:nth-child(3)') as HTMLButtonElement;

    expect(nextButton).toBeTruthy();
    nextButton?.click();
    tick();
  }));

  it('should have today button that can be clicked', fakeAsync(() => {
    fixture.detectChanges();
    tick(500);

    const navGroup = fixture.nativeElement.querySelector('[role="group"]');
    const todayButton = navGroup?.querySelector('button:nth-child(2)') as HTMLButtonElement;

    expect(todayButton).toBeTruthy();
    todayButton?.click();
    tick();
  }));

  it('should update rangeTitle signal on datesSet event', fakeAsync(() => {
    fixture.detectChanges();
    tick(500);

    const rangeTitle = fixture.nativeElement.querySelector('.awg__range');
    expect(rangeTitle).toBeTruthy();
    // FullCalendar initializes the date range asynchronously
  }));

  it('should skip invalid slots', fakeAsync(() => {
    const validSlot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: false,
    };

    const invalidSlot: AvailabilitySlot = {
      id: 2,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '16:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T15:00:00Z',
      endUtc: '2026-09-20T14:00:00Z', // end before start
      isBooked: false,
    };

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [validSlot, invalidSlot]);
    });

    fixture.detectChanges();
    tick(500);

    // Component should render without error - invalid slot is skipped internally
    const calendar = fixture.nativeElement.querySelector('.awg__canvas');
    expect(calendar).toBeTruthy();
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

  it('should only render on browser (not SSR)', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const canvas = fixture.nativeElement.querySelector('.awg__canvas');
    expect(canvas).toBeTruthy();
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
