import { Subject } from 'rxjs';
import { Answer } from './answer';
import { Question } from './question';

export enum TestStatus {
  NotStarted = -1,
  Started = 0,
  Completed = 1,
  Expired = 2,
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
