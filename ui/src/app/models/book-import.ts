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

export type BookImportTestStatus = 'Pending' | 'Running' | 'Done' | 'NeedsReview' | 'Failed' | 'Approved' | 'Removed';

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
  /** #1023: inceleme işlemleri bu id ile. */
  id: number;
  order: number;
  unit: number;
  testNo: number;
  title: string | null;
  status: BookImportTestStatus;
  questionsFound: number;
  keyAnswers: number;
  flags: string[];
  /** #1022: taslak kaydedilen ve kırpması bankada olduğu için atlanan sorular. */
  questionsSaved: number;
  duplicatesSkipped: number;
  reviewFlags: BookImportReviewFlag[];
  /** #1023: taslak test, dosya sayfaları ve cevapsız soru sayısı (onayı engeller). */
  worksheetId: number | null;
  fileFrom: number | null;
  fileTo: number | null;
  answerless: number;
  error: string | null;
}

/** #1023: incelemedeki testin sorusu; `answerIndex` A = 0, cevapsızsa null. */
export interface BookImportReviewQuestion {
  id: number;
  number: string | null;
  filePage: number;
  optionCount: number;
  imageUrl: string | null;
  answerIndex: number | null;
}

/** #1023: "Sorunsuz testleri onayla" sonucu; atlananların nedeni `answerless` ya da `failed`. */
export interface ApproveAllBookImportResult {
  approved: number[];
  skipped: { id: number; reason: 'answerless' | 'failed' }[];
}

/** #1023: onaylanabilir test — aktarımı bitmiş, taslak testi var ve cevapsız sorusu yok. */
export function isApprovable(test: BookImportJobTest): boolean {
  return (test.status === 'Done' || test.status === 'NeedsReview') && test.worksheetId != null && test.answerless === 0;
}

/** #1023: incelemede bakılması gereken: işaretli, hatalı ya da cevapsız sorusu olan (onaylanmamış / silinmemiş). */
export function needsAttention(test: BookImportJobTest): boolean {
  return test.status === 'NeedsReview' || test.status === 'Failed' || (test.status === 'Done' && test.answerless > 0);
}

/** #1022: aktarım inceleme işareti; `pages` dosya sayfaları, `numbers` soru numaraları. */
export interface BookImportReviewFlag {
  code: string;
  pages: number[];
  numbers: number[] | null;
  found: number | null;
  expected: number | null;
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

/** Issue #1021: kurulum onay ekranı. Anahtar: soru sırasıyla harf (A–E, okunamayan "?"); null = anahtar yok. */
export interface BookImportSetup {
  jobId: string;
  bookName: string;
  pageCount: number | null;
  /** Basılı = dosya + ofset; null: basılı sayfa no okunamadı. */
  offset: number | null;
  answerKeyRanges: { fileFrom: number; fileTo: number }[];
  selfAssessmentPages: number[];
  units: BookImportSetupUnit[];
  orphanKeys: string[];
  noAnswerKey: boolean;
  aiCostUsd: number;
  schemaRevision: number;
  existingBook: BookImportBookRef | null;
  tests: BookImportSetupTest[];
}

export interface BookImportBookRef {
  id: number;
  name: string;
}

export interface BookImportSetupUnit {
  unit: number;
  title: string | null;
  fileFrom: number;
  fileTo: number;
  topicId: number | null;
  topicName: string | null;
}

export interface BookImportSetupTest {
  order: number;
  unit: number;
  testNo: number;
  title: string | null;
  minutes: number | null;
  fileFrom: number | null;
  fileTo: number | null;
  expectedQuestions: number | null;
  answerKey: string | null;
  /** "." uyumlu, harf ikinci okumanın değeri, "-" ikinci okumada yok. */
  keyConflicts: string | null;
  flags: string[];
}

export interface ConfirmBookImportSetup {
  bookName: string;
  existingBookId: number | null;
  tests: {
    order: number;
    title: string | null;
    minutes: number | null;
    fileFrom: number;
    fileTo: number;
    answerKey: string | null;
  }[];
}
