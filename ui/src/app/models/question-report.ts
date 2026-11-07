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

export type QuestionReportOutcome = 'fixed' | 'noIssue';

/** #865: bir satır bir sorudur; aynı sorunun bildirimleri birleşir, `id` en yeni bildirim. */
export interface QuestionReportItem {
  id: number;
  questionId: number;
  questionImageUrl: string | null;
  reason: QuestionReportReason;
  reasons: QuestionReportReason[];
  note: string | null;
  notes: string[];
  reportCount: number;
  testNames: string[];
  testCount: number;
  answeredCount: number;
  status: 'open' | 'reviewed';
  createTime: string;
  outcome: QuestionReportOutcome | null;
  outcomeNote: string | null;
  reviewedAt: string | null;
  reviewedByMe: boolean;
}

export interface ReviewQuestionReport {
  outcome: QuestionReportOutcome;
  note: string | null;
}

export interface QuestionReportPage {
  totalCount: number;
  items: QuestionReportItem[];
}
