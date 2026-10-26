import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CalendarEventBadgeComponent } from './calendar-event-badge.component';
import { CalendarEvent } from '../../../models/calendar-event';
import { translocoTestingModule } from '../../testing/transloco-testing';

function event(overrides: Partial<CalendarEvent>): CalendarEvent {
  return {
    kind: 'assignment-deadline',
    date: new Date(2026, 9, 26, 14, 0).toISOString(),
    endDate: null,
    worksheetId: 3,
    worksheetTitle: 'Hayvanlar',
    subject: null,
    imageUrl: null,
    status: null,
    remindBeforeMinutes: null,
    isCompleted: false,
    teacherName: null,
    programId: null,
    programName: null,
    studyItemId: null,
    studyItemTitle: null,
    ...overrides,
  } as CalendarEvent;
}

describe('CalendarEventBadgeComponent (#573)', () => {
  let fixture: ComponentFixture<CalendarEventBadgeComponent>;

  function setup(ev: CalendarEvent, compact = false): HTMLElement {
    TestBed.configureTestingModule({ imports: [CalendarEventBadgeComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(CalendarEventBadgeComponent);
    fixture.componentRef.setInput('event', ev);
    fixture.componentRef.setInput('compact', compact);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('activity_UsesDistinctIconAndAriaLabel', () => {
    const el = setup(event({ worksheetKind: 1 }));

    expect(el.querySelector('mat-icon')?.textContent).toBe('extension');
    expect(el.querySelector('.badge')?.getAttribute('aria-label')).toContain('(Etkinlik sayfası)');
  });

  it('test_KeepsVariantIcon', () => {
    const el = setup(event({ worksheetKind: 0 }), true);

    expect(el.querySelector('mat-icon')?.textContent).toBe('flag');
    expect(el.querySelector('.badge__dot')?.getAttribute('aria-label')).not.toContain('Etkinlik');
  });
});
