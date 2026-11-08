/** Issue #1018 (epic #1017): kitaptan aktarım işi. Sunucu enum'ları adlarıyla gelir. */
export type BookImportStatus =
  | 'Queued'
  | 'Running'
  | 'AwaitingConfirmation'
  | 'ReadyForReview'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

/** Takip ekranındaki adımlar; sıra sunucudaki `BookImportStage` ile aynı. */
export const BOOK_IMPORT_STAGES = [
  'Upload',
  'Pages',
  'Setup',
  'SetupConfirmation',
  'Questions',
  'Classification',
  'Review',
  'Approval',
] as const;
export type BookImportStage = (typeof BOOK_IMPORT_STAGES)[number];

export type BookImportTestStatus = 'Pending' | 'Running' | 'Done' | 'NeedsReview' | 'Failed';

export interface BookImportJob {
  id: string;
  bookName: string;
  gradeId: number;
  subjectId: number | null;
  bookId: number | null;
  status: BookImportStatus;
  stage: BookImportStage;
  fileName: string | null;
  fileSizeBytes: number;
  pageCount: number | null;
  stageTotal: number;
  stageProcessed: number;
  message: string | null;
  error: string | null;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  ownerId: number | null;
}

export interface BookImportJobTest {
  order: number;
  unit: number;
  testNo: number;
  title: string | null;
  status: BookImportTestStatus;
  questionsFound: number;
  keyAnswers: number;
  flags: string[];
  error: string | null;
}

export interface BookImportJobDetail extends BookImportJob {
  tests: BookImportJobTest[];
}

/** `limit` null = sınırsız (Admin, QuestionTrainer ya da sınırsız plan; kart gizlenir), 0 = planda yok (#1019). */
export interface BookImportQuota {
  limit: number | null;
  used: number;
  resetsAtUtc: string | null;
  allowed: boolean;
  planKey: string | null;
}

export interface StartBookImportRequest {
  file: File;
  bookName: string;
  gradeId: number;
  subjectId: number | null;
}

/** İş hâlâ ilerliyor mu (takip ekranı yoklamaya devam eder). */
export function isBookImportActive(status: BookImportStatus): boolean {
  return status === 'Queued' || status === 'Running';
}

/** Durum çipi rengi (maket: Sırada nötr, çalışıyor bilgi, onay bekliyor uyarı, hazır/tamam başarı, hata tehlike). */
export function bookImportStatusTone(status: BookImportStatus): 'neutral' | 'info' | 'warning' | 'success' | 'danger' {
  switch (status) {
    case 'Queued':
    case 'Cancelled':
      return 'neutral';
    case 'Running':
      return 'info';
    case 'AwaitingConfirmation':
      return 'warning';
    case 'ReadyForReview':
    case 'Completed':
      return 'success';
    case 'Failed':
      return 'danger';
  }
}

/** Adımın durumu: tamamlandı, şu an, bekliyor; kurulum onayında bekleyen iş sarı gösterilir. */
export function stageState(
  job: Pick<BookImportJob, 'stage' | 'status'>,
  stage: BookImportStage,
): 'done' | 'current' | 'waiting' | 'failed' | 'todo' {
  const index = BOOK_IMPORT_STAGES.indexOf(stage);
  const current = BOOK_IMPORT_STAGES.indexOf(job.stage);
  if (job.status === 'Completed' || index < current) return 'done';
  if (index > current) return 'todo';
  if (job.status === 'Failed') return 'failed';
  if (job.status === 'AwaitingConfirmation') return 'waiting';
  return 'current';
}
