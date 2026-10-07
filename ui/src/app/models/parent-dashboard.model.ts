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

/**
 * Issue #421 (epic #407 V3): veli ödev/test takibi. Backend: `/api/exam/parent/children/{studentId}/assignments` ve
 * `/api/exam/parent/children/{studentId}/test-results/{testInstanceId}`. Salt okunur; soru metni/görseli, cevap anahtarı ya da
 * çocuğun seçtiği şık gelmez.
 */
export type ParentAssignmentStatus = 'completed' | 'overdue' | 'pending';

/** Filtre çipleri (sıra = ekrandaki sıra). */
export const PARENT_ASSIGNMENT_STATUSES: readonly ParentAssignmentStatus[] = ['pending', 'overdue', 'completed'];

export interface ParentTestScore {
  scorePercent: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  totalCount: number;
  durationSeconds: number;
}

export interface ParentChildAssignmentItem {
  worksheetId: number;
  title: string;
  subject: string | null;
  teacherName: string | null;
  /** ISO-8601 UTC. */
  startAt: string;
  /** ISO-8601 UTC; açık uçlu ödevde null. */
  deadline: string | null;
  status: ParentAssignmentStatus;
  /** Sonucu olan (bitmiş) testin oturumu; yoksa null. */
  testInstanceId: number | null;
  result: ParentTestScore | null;
}

export interface ParentChildAssignmentList {
  studentId: number;
  status: ParentAssignmentStatus | null;
  page: number;
  pageSize: number;
  totalCount: number;
  /** Filtreden bağımsız kova sayıları (V2 özet kartıyla aynı). */
  counts: ParentChildAssignmentCounts;
  items: ParentChildAssignmentItem[];
}

export interface ParentTestTopicResult {
  topicId: number | null;
  name: string;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  totalCount: number;
}

export interface ParentChildTestResult {
  studentId: number;
  testInstanceId: number;
  worksheetId: number;
  title: string;
  subject: string | null;
  outcome: 'completed' | 'timedOut';
  startedAt: string;
  finishedAt: string | null;
  score: ParentTestScore;
  topics: ParentTestTopicResult[];
}
