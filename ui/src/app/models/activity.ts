import { HttpErrorResponse } from '@angular/common/http';
import { TestSessionResult } from './test-instance';

/**
 * Issue #573: sayfa türü — backend `WorksheetKind` (int olarak serialize edilir).
 * `Activity` puansızdır: skor/yüzde/XP hiçbir etkinlik ekranında gösterilmez.
 */
export enum WorksheetKind {
  Test = 0,
  Activity = 1,
}

/** Issue #573: kapsam (passage) türü — backend `PassageKind`. Etkinlik kapsamı bir yönerge taşır. */
export enum PassageKind {
  Reading = 0,
  Activity = 1,
}

/** Backend `Passage.MaxInstructionLength`. */
export const PASSAGE_INSTRUCTION_MAX_LENGTH = 2000;

/** Backend `TestSessionErrorCodes` (#571/#573) ve `WorksheetAuthoringErrorCodes` (#573). */
export const ACTIVITY_ERROR_CODES = {
  activityCheckRequired: 'ActivityCheckRequired',
  notAnActivity: 'NotAnActivity',
  testNotInProgress: 'TestNotInProgress',
  invalidCheckRequest: 'InvalidCheckRequest',
  invalidAnswerPayload: 'InvalidAnswerPayload',
  rateLimited: 'RateLimited',
  worksheetKindLocked: 'WorksheetKindLocked',
} as const;

/** Backend `ActivityCheckRequestDto.MaxAnswers`. */
export const ACTIVITY_CHECK_MAX_ANSWERS = 100;

/** `POST api/test-instances/{id}/check` gövdesindeki tek madde (backend `ActivityCheckAnswerDto`). */
export interface ActivityCheckAnswer {
  instanceQuestionId: number;
  selectedAnswerId?: number | null;
  answerPayload?: string | null;
}

export interface ActivityCheckRequest {
  answers: ActivityCheckAnswer[];
}

/** Backend `ActivityCheckQuestionResultDto`. `feedback` JSON string: `{"rows":[{"id","correct"}]}` ya da null (MCQ). */
export interface ActivityCheckQuestionResult {
  instanceQuestionId: number;
  isCorrect: boolean;
  correctCount: number;
  totalCount: number;
  feedback: string | null;
}

/** Backend `ActivityCheckResultDto` (çözüm içermez). */
export interface ActivityCheckResult extends TestSessionResult {
  correctCount: number;
  totalCount: number;
  questions: ActivityCheckQuestionResult[];
}

/** Backend `ActivitySolutionDto`. */
export interface ActivitySolution {
  instanceQuestionId: number;
  questionId: number;
  /** MCQ: doğru şık; etkileşimli soruda null. */
  correctAnswerId: number | null;
  /** Etkileşimli soru: çözüm JSON'u (ör. `{"placements":[...]}`); MCQ'da null. */
  interactionSolution: string | null;
}

/** Backend `ActivitySolutionsResultDto`. */
export interface ActivitySolutionsResult extends TestSessionResult {
  questions: ActivitySolution[];
}

/** Satır bazlı geri bildirim — `feedback` JSON'undan ayrıştırılır. */
export interface ActivityFeedbackRow {
  id: string;
  correct: boolean;
}

/** `feedback` JSON'unu güvenli ayrıştırır; bozuk/boş girdide boş liste. */
export function parseActivityFeedback(json: string | null | undefined): ActivityFeedbackRow[] {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json) as { rows?: unknown } | null;
    if (!parsed || !Array.isArray(parsed.rows)) return [];
    return parsed.rows.filter(
      (row): row is ActivityFeedbackRow =>
        !!row && typeof (row as ActivityFeedbackRow).id === 'string' && typeof (row as ActivityFeedbackRow).correct === 'boolean'
    );
  } catch {
    return [];
  }
}

/** Hata yanıtındaki `errorCode` (HttpErrorResponse gövdesi ya da düz gövde). */
export function activityErrorCode(source: unknown): string | null {
  const body = source instanceof HttpErrorResponse ? source.error : source;
  if (!body || typeof body !== 'object') return null;
  const code = (body as { errorCode?: unknown }).errorCode;
  return typeof code === 'string' ? code : null;
}

/** Hata yanıtındaki yerelleştirilmiş sunucu mesajı (yoksa null). */
export function activityErrorMessage(source: unknown): string | null {
  const body = source instanceof HttpErrorResponse ? source.error : source;
  if (!body || typeof body !== 'object') return null;
  const message = (body as { message?: unknown }).message;
  return typeof message === 'string' && message.trim() ? message : null;
}

/**
 * Issue #573: soru/toplu kayıt 400 yanıtındaki okunur mesaj — servis gövdesi `{ message }` (ör.
 * `questions.passage.invalidInstruction`) ya da model doğrulaması `{ errors: { "<alan>": ["mesaj"] } }`. Yoksa null.
 */
export function saveValidationMessage(source: unknown): string | null {
  if (!(source instanceof HttpErrorResponse) || source.status !== 400) return null;
  const direct = activityErrorMessage(source);
  if (direct) return direct;
  const errors = (source.error as { errors?: Record<string, unknown> } | null)?.errors;
  if (!errors || typeof errors !== 'object') return null;
  for (const [key, value] of Object.entries(errors)) {
    if (key.startsWith('$.')) continue; // JSON yolu hataları ayrı bildirilir
    const first = Array.isArray(value) ? value.find((m): m is string => typeof m === 'string' && !!m.trim()) : null;
    if (first) return first;
  }
  return null;
}

/** Etkinlik oynatıcısının rotası. */
export function activityRoute(instanceId: number): (string | number)[] {
  return ['/activity', instanceId];
}
