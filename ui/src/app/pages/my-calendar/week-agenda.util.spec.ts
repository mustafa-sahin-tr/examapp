import { CalendarEvent } from '../../models/calendar-event';
import { buildWeekAgenda, startOfWeek } from './week-agenda.util';

function ev(overrides: Partial<CalendarEvent> & { date: string }): CalendarEvent {
    return {
        kind: 'assignment-deadline',
        endDate: null,
        worksheetId: 1,
        worksheetTitle: 'Kesirler',
        subject: 'Matematik',
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

describe('week-agenda.util (issue #888)', () => {
    // Cuma 6 Kasım 2026, 12:00 yerel; hafta 2–8 Kasım.
    const now = new Date(2026, 10, 6, 12, 0);
    const monday = startOfWeek(now);

    it('startOfWeek_AnyDay_ReturnsMondayOfThatWeek', () => {
        expect(monday).toEqual(new Date(2026, 10, 2));
        expect(startOfWeek(new Date(2026, 10, 8, 23, 0))).toEqual(new Date(2026, 10, 2));
        expect(startOfWeek(new Date(2026, 10, 9))).toEqual(new Date(2026, 10, 9));
    });

    it('buildWeekAgenda_SevenDaysWithTodayAndPastFlags', () => {
        const days = buildWeekAgenda([], monday, now, true);

        expect(days.length).toBe(7);
        expect(days.map((d) => d.date.getDate())).toEqual([2, 3, 4, 5, 6, 7, 8]);
        expect(days.findIndex((d) => d.isToday)).toBe(4);
        expect(days.filter((d) => d.isPast).length).toBe(4);
        expect(days.every((d) => d.rows.length === 0)).toBe(true);
    });

    it('buildWeekAgenda_AssignmentPastDue_IsOverdueWithStartAction', () => {
        const days = buildWeekAgenda([ev({ date: new Date(2026, 10, 4, 17, 0).toISOString() })], monday, now, true);

        const [row] = days[2].rows;
        expect(row.kind).toBe('assignment');
        expect(row.overdue).toBe(true);
        expect(row.action).toBe('start');
    });

    it('buildWeekAgenda_CompletedAssignment_DoneWithoutActionAndSortedLast', () => {
        const day = new Date(2026, 10, 6);
        const days = buildWeekAgenda([
            ev({ date: new Date(2026, 10, 6, 9, 0).toISOString(), isCompleted: true, worksheetTitle: 'Bitti' }),
            ev({ date: new Date(2026, 10, 6, 18, 0).toISOString(), worksheetTitle: 'Açık' }),
        ], monday, now, true);

        const rows = days.find((d) => d.date.getTime() === day.getTime())!.rows;
        expect(rows.map((r) => r.title)).toEqual(['Açık', 'Bitti']);
        expect(rows[1].done).toBe(true);
        expect(rows[1].action).toBeNull();
        expect(rows[0].overdue).toBe(false);
    });

    it('buildWeekAgenda_ProgramPlanSpanningDays_AppearsOnEveryCoveredDayOfTheWeek', () => {
        const plan = ev({
            kind: 'program-study-page',
            date: new Date(2026, 9, 30, 0, 0).toISOString(),
            endDate: new Date(2026, 10, 3, 23, 0).toISOString(),
            worksheetTitle: '',
            programId: 4,
            programName: 'LGS',
            studyItemTitle: 'Türkçe',
        });

        const days = buildWeekAgenda([plan], monday, now, true);

        expect(days.map((d) => d.rows.length)).toEqual([1, 1, 0, 0, 0, 0, 0]);
        expect(days[0].rows[0]).toEqual(expect.objectContaining({ kind: 'program', title: 'Türkçe', action: 'program', time: null }));
    });

    it('buildWeekAgenda_BookingForSchoolTeacher_HasNoAction', () => {
        const booking = ev({
            kind: 'booking',
            date: new Date(2026, 10, 7, 17, 0).toISOString(),
            endDate: new Date(2026, 10, 7, 18, 0).toISOString(),
            worksheetTitle: 'Deniz ile ders',
        });

        expect(buildWeekAgenda([booking], monday, now, true)[5].rows[0].action).toBe('bookings');
        const row = buildWeekAgenda([booking], monday, now, false)[5].rows[0];
        expect(row.action).toBeNull();
        expect(row.endTime).toEqual(new Date(2026, 10, 7, 18, 0));
        expect(row.done).toBe(false);
    });
});
