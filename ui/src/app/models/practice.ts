import { Question } from './question';

/**
 * "Soru Çöz" pratik oturumu modelleri (issue #62/#63).
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/PracticeSessionDto.cs
 */

/** POST /api/exam/practice/sessions gövdesi. İkisi de boşsa: öğrencinin sınıfı + tüm dersler. */
export interface PracticeSessionStartRequest {
  subjectIds?: number[];
  topicIds?: number[];
}

export type PracticeSessionStatus = 'Active' | 'Ended';

export interface PracticeSession {
  id: number;
  gradeId: number;
  startTime: string;
  endTime: string | null;
  status: PracticeSessionStatus;
  subjectIds: number[];
  topicIds: number[];
  answeredCount: number;
  correctCount: number;
  skippedCount: number;
}

/**
 * GET /api/exam/practice/sessions/{id}/next cevabı. Havuz bittiğinde 404 değil,
 * `question: null` + `poolExhausted: true` ile 200 döner. `question.correctAnswerId`
 * cevap öncesi asla dolu gelmez.
 */
export interface PracticeNextQuestion {
  sessionId: number;
  question: Question | null;
  poolExhausted: boolean;
  answeredCount: number;
  correctCount: number;
}

/** POST /api/exam/practice/sessions/{id}/answer gövdesi. Pas için `skipped: true` + `selectedAnswerId: null`. */
export interface PracticeAnswerSubmitRequest {
  questionId: number;
  selectedAnswerId: number | null;
  skipped: boolean;
  /** Saniye. */
  timeTaken: number;
}

/** Pending: gösterildi, cevaplanmadı. */
export type PracticeReviewQuestionStatus = 'Pending' | 'Answered' | 'Skipped';

/**
 * GET /api/exam/practice/sessions/{id}/review içindeki soru satırı.
 * `question`, canlı akıştaki (`PracticeNextQuestion.question`) ile aynı tam `QuestionDto`
 * şeklidir; canvas geometrisi, pasaj ve şıklar dahildir. `question.correctAnswerId` ve
 * `question.answers[].isCorrect` yalnızca cevaplanmış/pas geçilmiş satırda açıklanır —
 * `Pending` satırda sunucu bilinçli olarak gizler (aktif oturumda doğru şık sızmasın).
 */
export interface PracticeSessionReviewQuestion {
  question: Question;
  status: PracticeReviewQuestionStatus;
  isSkipped: boolean;
  isCorrect: boolean;
  selectedAnswerId: number | null;
  /** Saniye. */
  timeTaken: number;
  shownAt: string;
  answeredAt: string | null;
}

/** GET /api/exam/practice/sessions/{id}/review cevabı; `questions` `shownAt` sırasıyla. */
export interface PracticeSessionReview {
  session: PracticeSession;
  questions: PracticeSessionReviewQuestion[];
}

export interface PracticeAnswerResult {
  sessionId: number;
  questionId: number;
  isCorrect: boolean;
  skipped: boolean;
  /** Anında geri bildirim için doğru şık. */
  correctAnswerId: number | null;
  answeredCount: number;
  correctCount: number;
}

/**
 * Günün soruları (issue #99). GET /api/exam/practice/daily cevabı; set yoksa sunucu lazy üretir.
 * Backend sözleşmesi paralel yazılıyor (DailySetDto önerisi, issue #99 devir notu).
 */
export type DailySetStatus = 'NotStarted' | 'InProgress' | 'Completed' | 'Empty';

export interface DailySet {
  /** Europe/Istanbul günü, `yyyy-MM-dd`. */
  date: string;
  status: DailySetStatus;
  /** Gerçek set boyutu; havuz azsa `targetCount`'tan küçüktür. */
  total: number;
  /** Yapılandırılmış hedef N. */
  targetCount: number;
  /** `correct + wrong + skipped`. */
  answered: number;
  correct: number;
  wrong: number;
  skipped: number;
  /** Günlük setin practice oturumu; başlamadıysa null. Tamamlanmış sette de dolu (review için). */
  sessionId: number | null;
  /** #66 kapsam seçimi için ayrılmış; #99'da kullanılmaz. */
  scope: unknown | null;
}

/**
 * POST /api/exam/practice/daily/start cevabı (idempotent). Tamamlanmış sette de 200 + `Completed` +
 * mevcut `sessionId` döner; 409 yalnız set boşken (`{ message }`).
 */
export interface DailySetStartResult {
  sessionId: number;
  status: Exclude<DailySetStatus, 'Empty'>;
}
