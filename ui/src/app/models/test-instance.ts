import { Subject } from 'rxjs';
import { Answer } from './answer';
import { Question } from './question';

export enum TestStatus {
  NotStarted = -1,
  Started = 0,
  Completed = 1,
  Expired = 2,
}

// Backend enum'ları number olarak serialize edilir (string-enum converter yok).
export enum WorksheetTeacherSharing {
  Private = 0,
  PublicView = 1,
  PublicAssignable = 2,
}

export enum WorksheetStudentVisibility {
  Normal = 0,
  Restricted = 1,
}

// public enum WorksheetInstanceStatus
// {
//     Started = 0,   // 🟢 Test başladı
//     Completed = 1, // ✅ Test tamamlandı
//     Expired = 2    // ⏳ Süre doldu
// }

export interface TestInstanceQuestion {
  id: number;
  question: Question;
  order: number;
  selectedAnswerId: number;
  answerPayload?: string;
  timeTaken: number;
}
export interface TestInstance {
  id: number;
  testName: string;
  status: TestStatus;
  maxDurationSeconds: number;
  testInstanceQuestions: TestInstanceQuestion[];
  isPracticeTest: boolean;
}

export interface Exam {
  id: number;
  name: string;
  description: string;
  maxDurationSeconds: number;
  totalQuestions: number;
  instanceStatus: TestStatus;
  testInstanceId: number;
  bookId?: number;
  bookTestId?: number;
}

export interface Paged<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  items: T[];
}

export interface Test {
  id: number | null;
  name: string;
  description?: string;
  gradeId?: number;
  maxDurationSeconds: number;
  isPracticeTest: boolean;
  imageUrl?: string;
  subtitle?: string;
  badgeText?: string;
  bookId?: number;
  bookTestId?: number;
  questionCount?: number;
  instance?: InstanceSummary;
  instanceCount?: number;
  newBookName?: string;
  newBookTestName?: string;
  subjectId?: number;
  topicId?: number;
  subTopicId?: number;
  /** Backend: (sahibi && CreateUserId>0) || admin */
  canEdit?: boolean;
  createdByUserId?: number | null;
  /** Sadece istek sahibi admin ise dolu gelir. */
  createdByName?: string | null;
  /** Öğretmenler arası paylaşım ekseni (issue #9). Backend listelerde her zaman gönderir. */
  teacherSharing?: WorksheetTeacherSharing;
  /** Öğrenciye görünürlük ekseni (issue #9). Backend listelerde her zaman gönderir. */
  studentVisibility?: WorksheetStudentVisibility;
  /** İstek sahibi bu worksheet'in sahibi mi. */
  isOwner?: boolean;
  /** Tekil detay (GetById) akışında admin VEYA sahip için dolu; liste akışlarında yalnız admin için dolu. */
  ownerName?: string | null;
  /** İstek sahibi bu worksheet'i öğrenciye atayabilir mi. */
  canAssign?: boolean;
  /** Öğrenci discover listesinde: true ise "Atanan sınavlar", false ise "Keşfet" grubunda gösterilir (issue #14). */
  isAssigned?: boolean;
}

export interface InstanceSummary {
  id: number;
  name: string;
  imageUrl?: string;
  completedDate: Date;
  score: number;
  durationMinutes: number;
  correctAnswers: number;
  wrongAnswers: number;
  totalQuestions: number;
  status: number;
}
