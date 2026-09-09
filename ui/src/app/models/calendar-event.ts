/**
 * Öğrenci takvimi etkinlikleri — backend `CalendarEventDto` ile birebir
 * (api/ExamApp.Api/Models/Dtos/CalendarEventDto.cs).
 * Kaynak: GET /api/exam/worksheet/calendar/me (bkz. TestService.getMyCalendar).
 * Etkinlikler `[from, to)` aralığında döner — `to` hariç.
 */

export type CalendarEventKind = 'reminder' | 'assignment-deadline' | 'program-study-page';

export type CalendarEventStatus = 'Pending' | 'Sent';

export interface CalendarEvent {
  /** "reminder" | "assignment-deadline" | "program-study-page" */
  kind: CalendarEventKind;
  /**
   * Etkinliğin anı/başlangıcı — UTC ISO. reminder: ScheduledFor, deadline: EndAt,
   * program-study-page: plan StartDate.
   */
  date: string;
  /** Çok günlü etkinliklerde bitiş anı (UTC ISO). Yalnızca kind === 'program-study-page'. */
  endDate: string | null;
  /** Worksheet tabanlı etkinliklerde dolu; program-study-page için 0. */
  worksheetId: number;
  /** program-study-page için boş string. */
  worksheetTitle: string;
  subject: string | null;
  imageUrl: string | null;
  /** Yalnızca kind === 'reminder'. */
  status: CalendarEventStatus | null;
  remindBeforeMinutes: number | null;
  /** assignment-deadline ve program-study-page için dolu. */
  isCompleted: boolean | null;
  /** Yalnızca kind === 'assignment-deadline'. */
  teacherName: string | null;
  /** Yalnızca kind === 'program-study-page'. */
  programId: number | null;
  programName: string | null;
  studyPageId: number | null;
  studyPageTitle: string | null;
}

export interface StudentCalendarResponse {
  events: CalendarEvent[];
}
