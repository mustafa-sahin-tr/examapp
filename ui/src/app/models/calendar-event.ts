/**
 * Öğrenci takvimi etkinlikleri — backend `CalendarEventDto` ile birebir.
 * Kaynak: GET /api/exam/calendar/me (bkz. ExamController.GetMyCalendar).
 */

export type CalendarEventKind = 'reminder' | 'assignment-deadline';

export type CalendarEventStatus = 'Pending' | 'Sent';

export interface CalendarEvent {
  /** "reminder" | "assignment-deadline" */
  kind: CalendarEventKind;
  /** Etkinliğin anı — UTC ISO. reminder: ScheduledFor, deadline: EndAt. */
  date: string;
  worksheetId: number;
  worksheetTitle: string;
  subject: string | null;
  imageUrl: string | null;
  /** Yalnızca kind === 'reminder'. */
  status: CalendarEventStatus | null;
  remindBeforeMinutes: number | null;
  /** Yalnızca kind === 'assignment-deadline'. */
  isCompleted: boolean | null;
  teacherName: string | null;
}

export interface StudentCalendarResponse {
  events: CalendarEvent[];
}
