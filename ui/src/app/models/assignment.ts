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
}

export interface TeacherWorksheetAssignment {
  assignmentId: number;
  targetType: 'Grade' | 'Student';
  targetName: string;
  isActive: boolean;
  startAt: string;
  endAt?: string | null;
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
}

export interface ApiResponse {
  success: boolean;
  message: string;
  objectId?: number | null;
}
