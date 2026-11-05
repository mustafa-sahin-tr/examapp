export type QuestionReportReason = 'wrongAnswer' | 'unreadable' | 'wrongContent' | 'other';

export const QUESTION_REPORT_REASONS: readonly QuestionReportReason[] = [
  'wrongAnswer',
  'unreadable',
  'wrongContent',
  'other',
];

export const QUESTION_REPORT_NOTE_MAX_LENGTH = 500;

export type QuestionReportListStatus = 'Open' | 'Reviewed';

export interface CreateQuestionReport {
  testInstanceId: number;
  reason: QuestionReportReason;
  note: string | null;
}

export interface QuestionReportCreated {
  alreadyReported: boolean;
}

export interface QuestionReportItem {
  id: number;
  questionId: number;
  questionImageUrl: string | null;
  reason: QuestionReportReason;
  note: string | null;
  status: 'open' | 'reviewed';
  createTime: string;
}

export interface QuestionReportPage {
  totalCount: number;
  items: QuestionReportItem[];
}
