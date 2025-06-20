export interface TestRow {
  name: string;
  description?: string;
  gradeId: number | string;
  gradeName?: string;
  maxDurationMinutes: number;
  isPracticeTest: boolean;
  subtitle?: string;
  imageUrl?: string;
  newBookName?: string;
  newBookTestName?: string;
  bookTestId?: number | string;
  bookTestName?: string;
  bookId?: number | string;
  bookName?: string;
  subjectId?: number | string;
  topicId?: number | string;
  subtopicId?: number | string;
  questionCount?: number;
  __status?: string;
  __original?: any;
}
