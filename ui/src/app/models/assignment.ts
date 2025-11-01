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
}
