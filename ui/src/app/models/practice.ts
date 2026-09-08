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
