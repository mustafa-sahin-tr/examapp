import { WorksheetKind } from './activity';

export interface AssignedWorksheet {
  assignmentId: number;
  worksheetId: number;
  name: string;
  description: string;
  gradeId: number;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicId?: number | null;
  maxDurationSeconds: number;
  isPracticeTest: boolean;
  /** Issue #573: sayfa türü (0 Test, 1 Etkinlik). */
  kind?: WorksheetKind;
  subtitle?: string | null;
  imageUrl?: string | null;
  badgeText?: string | null;
  bookTestId?: number | null;
  bookId?: number | null;
  questionCount: number;
  startAt: string;
  endAt?: string | null;
  isGradeAssignment: boolean;
  assignedGradeId?: number | null;
  instanceId?: number | null;
  instanceStatus?: number | null;
  instanceStartTime?: string | null;
  instanceEndTime?: string | null;
  assignmentStatus: string;
  hasStarted: boolean;
  isCompleted: boolean;
  /** Issue #396: oturum bitti (tamamlandı ya da süresi doldu) — teslim uyarısı/"devam ediyor" gösterilmez. */
  isFinished?: boolean;
  /** Issue #105: atama override'ı (null = worksheet varsayılanı). */
  commentsEnabledOverride?: boolean | null;
  /** Issue #105: öğrenci için etkin değer (`override ?? worksheet.commentsEnabled`). */
  commentsEnabled?: boolean;
  /** Issue #573: etkinlikte ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null. */
  firstCheckCorrectCount?: number | null;
  /** Issue #573: etkinlikte ilk kontroldeki toplam satır sayısı; kontrol yoksa / testte null. */
  firstCheckTotalCount?: number | null;
  /**
   * Issue #609: tamamlanmış etkinlik yeniden çözülüyor — `instanceStatus` 0 (Started, oynatıcı devam eder),
   * `assignmentStatus` "Completed" ve `isCompleted` true kalır. Testte hep false.
   */
  retaking?: boolean;
}

export type AssignmentStudentStatus = 'Scheduled' | 'NotStarted' | 'InProgress' | 'Completed' | 'Expired';

export interface TeacherAssignmentStudentSummary {
  studentId: number;
  userId: number;
  studentNumber: string;
  gradeId?: number | null;
  gradeName?: string | null;
  status: AssignmentStudentStatus;
  instanceId?: number | null;
  lastActivity?: string | null;
  /** Issue #573: etkinlikte ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null. */
  firstCheckCorrectCount?: number | null;
  /** Issue #573: etkinlikte ilk kontroldeki toplam satır sayısı (yanlış = toplam − doğru); kontrol yoksa / testte null. */
  firstCheckTotalCount?: number | null;
  /**
   * Issue #609: öğrenci tamamlanmış etkinliği şu an yeniden çözüyor. `status` yine Completed ve ilk kontrol özeti ilk
   * denemeninkidir. Testte hep false.
   */
  retaking?: boolean;
  /** Issue #609: etkinlikte "Yeniden çöz" sayısı (0 = hiç); testte hep 0. */
  retakeCount?: number;
}

export interface TeacherWorksheetAssignment {
  assignmentId: number;
  targetType: 'Grade' | 'Student';
  targetName: string;
  isActive: boolean;
  startAt: string;
  endAt?: string | null;
  /** Issue #105: atama bazlı yorum-soru override'ı; null = worksheet varsayılanı. */
  commentsEnabledOverride?: boolean | null;
  studentCount: number;
  completedCount: number;
  inProgressCount: number;
  notStartedCount: number;
  scheduledCount: number;
  expiredCount: number;
  students: TeacherAssignmentStudentSummary[];
}

export interface AssignmentProgressSummary {
  totalAssignments: number;
  activeAssignments: number;
  upcomingAssignments: number;
  totalStudents: number;
  completedCount: number;
  inProgressCount: number;
  notStartedCount: number;
  scheduledCount: number;
  expiredCount: number;
}

export interface TeacherWorksheetAssignments {
  worksheetId: number;
  worksheetName: string;
  /** Issue #573: sayfa türü — etkinlikte "Çalışma takibi" tablosu gösterilir. */
  worksheetKind?: WorksheetKind;
  retrievedAt: string;
  summary: AssignmentProgressSummary;
  assignments: TeacherWorksheetAssignment[];
}

export interface WorksheetAssignmentRequest {
  worksheetId: number;
  studentId?: number | null;
  gradeId?: number | null;
  startAt: string;
  endAt?: string | null;
  /** Issue #105: bu atamaya özel yorum-soru açık/kapalı; null = worksheet varsayılanı. */
  commentsEnabledOverride?: boolean | null;
}

export interface ApiResponse {
  success: boolean;
  message: string;
  objectId?: number | null;
}
