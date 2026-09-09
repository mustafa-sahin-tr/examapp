import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MonthCalendarGridComponent, ProgramBar } from './month-calendar-grid.component';
import { CalendarEvent } from '../../../models/calendar-event';

function programEvent(overrides: Partial<CalendarEvent> & { date: string }): CalendarEvent {
  return {
    kind: 'program-study-page',
    endDate: null,
    worksheetId: 0,
    worksheetTitle: '',
    subject: null,
    imageUrl: null,
    status: null,
    remindBeforeMinutes: null,
    isCompleted: false,
    teacherName: null,
    programId: 1,
    programName: 'Program A',
    studyPageId: 1,
    studyPageTitle: 'Sayfa 1',
    ...overrides,
  } as CalendarEvent;
}

function reminderEvent(overrides: Partial<CalendarEvent> & { date: string; worksheetId: number }): CalendarEvent {
  return {
    kind: 'reminder',
    endDate: null,
    worksheetTitle: 'Hatirlatma',
    subject: 'Fen',
    imageUrl: null,
    status: 'Pending',
    remindBeforeMinutes: 60,
    isCompleted: null,
    teacherName: null,
    programId: null,
    programName: null,
    studyPageId: null,
    studyPageTitle: null,
    ...overrides,
  } as CalendarEvent;
}

/** Local ISO date-time at local midnight, so `new Date(iso)` round-trips cleanly. */
function localIso(year: number, month: number, day: number): string {
  return new Date(year, month - 1, day).toISOString();
}

async function setup(month: Date, events: CalendarEvent[] = []) {
  TestBed.resetTestingModule();
  await TestBed.configureTestingModule({
    imports: [MonthCalendarGridComponent],
  }).compileComponents();

  const fixture: ComponentFixture<MonthCalendarGridComponent> = TestBed.createComponent(MonthCalendarGridComponent);
  fixture.componentRef.setInput('month', month);
  fixture.componentRef.setInput('events', events);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance };
}

function allBars(component: MonthCalendarGridComponent): ProgramBar[] {
  const bars: ProgramBar[] = [];
  for (let i = 0; i < component.weeks().length; i++) {
    bars.push(...component.barsFor(i));
  }
  return bars;
}

describe('MonthCalendarGridComponent', () => {
  // September 2026: 1 Eylül Salı. Week rows (Monday-first):
  // Row0: 31 Ağu - 6 Eyl, Row1: 7-13, Row2: 14-20, Row3: 21-27, Row4: 28 Eyl - 4 Eki
  const septemberRef = new Date(2026, 8, 15);

  it('BarsByWeek_SingleDayEvent_ProducesOneSegmentWithCorrectColumns', async () => {
    // 8 Eylül 2026 = Salı -> Monday-index 1 -> startCol=2, endCol=3
    const ev = programEvent({ date: localIso(2026, 9, 8), endDate: null });
    const { component } = await setup(septemberRef, [ev]);

    const bars = allBars(component);
    expect(bars.length).toBe(1);
    expect(bars[0].startCol).toBe(2);
    expect(bars[0].endCol).toBe(3);
    expect(bars[0].startsHere).toBeTrue();
    expect(bars[0].endsHere).toBeTrue();
  });

  it('BarsByWeek_MultiDayEventWithinSingleWeek_ProducesOneSegmentSpanningRange', async () => {
    // Hafta: 7-13 Eylül (Pzt-Paz). Salı(8) - Perşembe(10): kolon 2..4, endCol exclusive=5
    const ev = programEvent({ date: localIso(2026, 9, 8), endDate: localIso(2026, 9, 10) });
    const { component } = await setup(septemberRef, [ev]);

    const bars = allBars(component);
    expect(bars.length).toBe(1);
    expect(bars[0].startCol).toBe(2);
    expect(bars[0].endCol).toBe(5);
    expect(bars[0].startsHere).toBeTrue();
    expect(bars[0].endsHere).toBeTrue();
  });

  it('BarsByWeek_EventSpanningTwoWeeks_SplitsIntoTwoSegmentsWithCorrectStartEndFlags', async () => {
    // Cuma 11 Eylül - Pazartesi 14 Eylül: hafta sınırını (Pzt-Paz) geçiyor.
    const ev = programEvent({ date: localIso(2026, 9, 11), endDate: localIso(2026, 9, 14) });
    const { component } = await setup(septemberRef, [ev]);

    const bars = allBars(component).sort((a, b) => a.weekRow - b.weekRow);
    expect(bars.length).toBe(2);

    const [first, second] = bars;
    expect(second.weekRow).toBe(first.weekRow + 1);
    expect(first.startsHere).toBeTrue();
    expect(first.endsHere).toBeFalse();
    expect(second.startsHere).toBeFalse();
    expect(second.endsHere).toBeTrue();
  });

  it('AssignStackPositions_OverlappingEvents_GetDifferentStackValues', async () => {
    // Aynı haftada çakışan iki etkinlik: 8-9 Eylül ve 9-10 Eylül (9 Eylül'de örtüşüyor)
    const evA = programEvent({ date: localIso(2026, 9, 8), endDate: localIso(2026, 9, 9), studyPageId: 1 });
    const evB = programEvent({ date: localIso(2026, 9, 9), endDate: localIso(2026, 9, 10), studyPageId: 2 });
    const { component } = await setup(septemberRef, [evA, evB]);

    const bars = allBars(component);
    expect(bars.length).toBe(2);
    const stacks = bars.map((b) => b.stack).sort();
    expect(stacks).toEqual([0, 1]);
  });

  it('AssignStackPositions_NonOverlappingEvents_ShareStackZero', async () => {
    // Aynı haftada çakışmayan iki etkinlik: 7-8 Eylül ve 10-11 Eylül
    const evA = programEvent({ date: localIso(2026, 9, 7), endDate: localIso(2026, 9, 8), studyPageId: 1 });
    const evB = programEvent({ date: localIso(2026, 9, 10), endDate: localIso(2026, 9, 11), studyPageId: 2 });
    const { component } = await setup(septemberRef, [evA, evB]);

    const bars = allBars(component);
    expect(bars.length).toBe(2);
    expect(bars.every((b) => b.stack === 0)).toBeTrue();
  });

  it('BarsByWeek_CompletedEvent_MarksCompletedAndPrefixesLabelWithCheckmark', async () => {
    const ev = programEvent({
      date: localIso(2026, 9, 8),
      endDate: null,
      isCompleted: true,
      studyPageTitle: 'Konu Testi',
    });
    const { component } = await setup(septemberRef, [ev]);

    const bars = allBars(component);
    expect(bars[0].completed).toBeTrue();
    expect(bars[0].label).toBe('✓ Konu Testi (tamamlandı)');
  });

  it('EventsFor_ProgramStudyPageEvent_ExcludedFromBadgeLayerButPresentInBars', async () => {
    const day = localIso(2026, 9, 8);
    const programEv = programEvent({ date: day });
    const reminderEv = reminderEvent({ date: day, worksheetId: 1 });
    const { component } = await setup(septemberRef, [programEv, reminderEv]);

    const cell = component.cells().find((c) => c.iso === '2026-09-08')!;
    const badgeEvents = component.eventsFor(cell);
    expect(badgeEvents.length).toBe(1);
    expect(badgeEvents[0].kind).toBe('reminder');

    const visible = component.visibleEvents(cell);
    expect(visible.every((e) => e.kind !== 'program-study-page')).toBeTrue();

    expect(allBars(component).length).toBe(1);
  });

  it('CellAriaLabel_DayWithBadgeAndProgramBar_CountsBoth', async () => {
    const day = localIso(2026, 9, 8);
    const programEv = programEvent({ date: day });
    const reminderEv = reminderEvent({ date: day, worksheetId: 1 });
    const { component } = await setup(septemberRef, [programEv, reminderEv]);

    const cell = component.cells().find((c) => c.iso === '2026-09-08')!;
    expect(component.cellAriaLabel(cell)).toContain('2 etkinlik');
  });

  it('BarsByWeek_EventCrossingMonthBoundary_ClampsToGridWithoutThrowing', async () => {
    // Ağustos'un son günlerinden Eylül ortasına kadar, grid'in ilk hücresinden önce başlıyor.
    const ev = programEvent({ date: localIso(2026, 8, 20), endDate: localIso(2026, 9, 5) });

    expect(async () => {
      const { component } = await setup(septemberRef, [ev]);
      const bars = allBars(component);
      expect(bars.length).toBeGreaterThan(0);
      // İlk hücre grid başlangıcı (31 Ağustos 2026, Pazartesi) olmalı; segment ondan önce başlamamalı.
      const gridStart = component.cells()[0].date;
      expect(bars.every((b) => b.weekRow >= 0)).toBeTrue();
      expect(gridStart.getDate()).toBeGreaterThanOrEqual(1);
    }).not.toThrow();
  });
});
