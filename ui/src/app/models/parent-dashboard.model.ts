/**
 * Issue #420 (epic #407 V2): veli paneli. Backend: `/api/exam/parent/children/{studentId}/summary`
 * (gateway `/api/exam/{everything}` → API `/api/parent/children/{studentId}/summary`).
 * Yalnızca toplamlar — içerik, cevap anahtarı, mesaj ya da iletişim bilgisi gelmez.
 */

export interface ParentChildAssignmentCounts {
  completed: number;
  overdue: number;
  pending: number;
  /** Teslim tarihi geçmiş atamaların geriye bakış penceresi (gün). */
  windowDays: number;
}

export interface ParentChildSummary {
  studentId: number;
  /** Haftanın Pazartesi'si (Europe/Istanbul), "yyyy-MM-dd". */
  weekStart: string;
  questionsSolvedThisWeek: number;
  assignments: ParentChildAssignmentCounts;
  totalPoints: number;
  /** ISO-8601 UTC; hiç aktivite yoksa null. */
  lastActivityAt: string | null;
}

/** Seçili çocuğu hatırlayan query param (`/parent?child=<studentId>`). */
export const PARENT_CHILD_QUERY_PARAM = 'child';
