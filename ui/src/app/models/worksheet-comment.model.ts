/**
 * Issue #105 — worksheet / soru yorum-soru thread'leri.
 * `api/ExamApp.Api/Models/Dtos/WorksheetComments/WorksheetCommentDtos.cs` ile birebir eşleşir; gateway üzerinden
 * `/api/exam/worksheet/{worksheetId}/comments`.
 *
 * Kişisel veri: yazar kimliği yok, yalnızca görünen ad + rol + `isMine`.
 */

export type WorksheetCommentAuthorRole = 'Student' | 'Teacher';

/** `WorksheetCommentLockReasons` — yalnız öğrencide dolu; öğretmen/admin için her zaman null. */
export type WorksheetCommentLockReason = 'comments-disabled' | 'question-not-answered' | 'worksheet-not-started';

/** `WorksheetCommentDto` — tek yorum (kök veya reply). */
export interface WorksheetComment {
  id: number;
  worksheetId: number;
  questionId: number | null;
  /**
   * Issue #309: soru thread'inde sorunun worksheet içindeki 1 tabanlı sırası; worksheet thread'inde null.
   * Eski sunucuda alan yok (opsiyonel). Güvenilmeyen değer — gösterimden önce {@link toQuestionOrder} ile doğrulanır.
   */
  questionOrder?: number | null;
  parentCommentId: number | null;
  /** Öğrenci: "Ad S."; öğretmen: görünen ad. Düz metin, bidi-izole gösterilir. */
  authorDisplayName: string;
  authorRole: WorksheetCommentAuthorRole;
  isMine: boolean;
  /** Düz metin (en fazla 2000 karakter). Asla HTML olarak yorumlanmaz. */
  body: string;
  /** ISO 8601, UTC. */
  createdAt: string;
}

/** `WorksheetCommentThreadDto` — kök yorum + en son en fazla 5 reply (eskiden yeniye). */
export interface WorksheetCommentRoot extends WorksheetComment {
  replies: WorksheetComment[];
  replyCount: number;
  canReply: boolean;
}

/** `WorksheetCommentPageDto` — kökler yeniden eskiye. */
export interface WorksheetCommentPage {
  items: WorksheetCommentRoot[];
  nextCursor: string | null;
  canWrite: boolean;
  lockReason: WorksheetCommentLockReason | null;
  /**
   * Issue #309: öğretmen/admin için öğrencilerin efektif yorum durumu özeti; öğrencide null. Eski sunucuda alan yok.
   * Gösterimden önce {@link parseStudentCommentsSummary} ile doğrulanır.
   */
  studentCommentsSummary?: WorksheetCommentStudentSummary | null;
  /**
   * Issue #309: soru thread'inin 1 tabanlı numarası (sayfa boşken de dolu); worksheet thread'inde null.
   * Gösterimden önce {@link toQuestionOrder} ile doğrulanır.
   */
  questionOrder?: number | null;
}

/** `WorksheetCommentOverrideCountsDto` — çağıranın görebildiği aktif atamalardaki override sayıları. */
export interface WorksheetCommentOverrideCounts {
  enabled: number;
  disabled: number;
}

/** `WorksheetCommentStudentSummaryDto` (issue #309). */
export interface WorksheetCommentStudentSummary {
  /** Worksheet varsayılanı (`Worksheet.CommentsEnabled`). */
  worksheetDefault: boolean;
  assignmentOverrides: WorksheetCommentOverrideCounts;
}

/** `WorksheetCommentRepliesPageDto` — reply'lar eskiden yeniye. */
export interface WorksheetCommentRepliesPage {
  items: WorksheetComment[];
  nextCursor: string | null;
  canReply: boolean;
  replyCount: number;
  /** Issue #309: kök soru thread'indeyse 1 tabanlı soru numarası; worksheet seviyesinde null. */
  questionOrder?: number | null;
}

/** `CreateWorksheetCommentDto`. */
export interface CreateWorksheetCommentRequest {
  questionId: number | null;
  parentCommentId: number | null;
  body: string;
}

export interface WorksheetCommentQuery {
  questionId?: number | null;
  cursor?: string | null;
  take?: number;
}

export interface WorksheetCommentRepliesQuery {
  cursor?: string | null;
  take?: number;
}

/** `WorksheetCommentErrorCodes` — hata gövdesindeki `errorCode`. */
export const WORKSHEET_COMMENT_ERROR_CODES = [
  'WorksheetNotFound',
  'AccessDenied',
  'QuestionNotInWorksheet',
  'InvalidParent',
  'BodyRequired',
  'BodyTooLong',
  'BodyInvalidCharacters',
  'RootCommentNotFound',
  'InvalidCursor',
  'CommentsDisabled',
  'WorksheetNotStarted',
  'QuestionNotAnswered',
  'NotResponsibleTeacher',
  'RateLimited',
] as const;

export type WorksheetCommentErrorCode = (typeof WORKSHEET_COMMENT_ERROR_CODES)[number];

/** `WorksheetCommentLimits`. */
export const WORKSHEET_COMMENT_MAX_LENGTH = 2000;
export const WORKSHEET_COMMENT_DEFAULT_TAKE = 20;
export const WORKSHEET_COMMENT_MAX_TAKE = 50;

export function isWorksheetCommentErrorCode(value: unknown): value is WorksheetCommentErrorCode {
  return typeof value === 'string' && (WORKSHEET_COMMENT_ERROR_CODES as readonly string[]).includes(value);
}

// ---------------------------------------------------------------------------------------------------------------
// Bildirim derin linki (issue #105 dilim 3). Kalıcı bildirim `data` JSON'u ve SignalR payload'ı aynı kimlikleri taşır:
// `{ worksheetId, questionId|null, commentId, rootCommentId, questionOrder|null }` (questionOrder: issue #309). İkisi de güvenilmeyen veri sayılır.
// ---------------------------------------------------------------------------------------------------------------

/** Kalıcı bildirim `type` değerleri (BadgeService consumer'ları) — SignalR event adlarıyla aynı. */
export const WORKSHEET_COMMENT_CREATED_TYPE = 'WorksheetCommentCreated';
export const WORKSHEET_COMMENT_REPLIED_TYPE = 'WorksheetCommentReplied';

export function isWorksheetCommentNotificationType(type: string | null | undefined): boolean {
  return type === WORKSHEET_COMMENT_CREATED_TYPE || type === WORKSHEET_COMMENT_REPLIED_TYPE;
}

export interface WorksheetCommentRef {
  worksheetId: number;
  questionId: number | null;
  commentId: number;
  rootCommentId: number | null;
  /**
   * Issue #309: sorunun 1 tabanlı sırası (yalnız bildirim `data`'sı / SignalR payload'ından; URL'den okunmaz ve derin
   * linke yazılmaz — sayfa başlığı sırayı thread yanıtından alır).
   */
  questionOrder?: number | null;
}

/** Route'a giden kimlik alanları: pozitif, güvenli tam sayı; aksi hâlde null. */
export function toPositiveId(value: unknown): number | null {
  const n = typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : value;
  return typeof n === 'number' && Number.isSafeInteger(n) && n > 0 ? n : null;
}

/** Soru sırası (issue #309): yalnız pozitif, güvenli tam sayı (JSON number); string/ondalık/0/negatif → null. */
export function toQuestionOrder(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
}

function toCount(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
}

/**
 * Thread yanıtındaki `studentCommentsSummary`'yi doğrular (issue #309). Biçim bozuksa (boolean olmayan varsayılan,
 * negatif/ondalık sayı) null — UI eski `commentsEnabled` şeridine düşer.
 */
export function parseStudentCommentsSummary(value: unknown): WorksheetCommentStudentSummary | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const record = value as Record<string, unknown>;
  const overrides = record['assignmentOverrides'];
  if (typeof record['worksheetDefault'] !== 'boolean' || !overrides || typeof overrides !== 'object') {
    return null;
  }
  const counts = overrides as Record<string, unknown>;
  const enabled = toCount(counts['enabled']);
  const disabled = toCount(counts['disabled']);
  if (enabled === null || disabled === null) {
    return null;
  }
  return { worksheetDefault: record['worksheetDefault'], assignmentOverrides: { enabled, disabled } };
}

/**
 * Güvenilmeyen nesneden (`data` JSON'u ya da SignalR payload'ı) yorum referansını çıkarır. `worksheetId` ve
 * `commentId` zorunlu; diğerleri geçersizse null'a düşer.
 */
export function parseWorksheetCommentRef(value: unknown): WorksheetCommentRef | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const record = value as Record<string, unknown>;
  const worksheetId = toPositiveId(record['worksheetId']);
  const commentId = toPositiveId(record['commentId']);
  if (worksheetId === null || commentId === null) {
    return null;
  }
  return {
    worksheetId,
    questionId: toPositiveId(record['questionId']),
    commentId,
    rootCommentId: toPositiveId(record['rootCommentId']),
    questionOrder: toQuestionOrder(record['questionOrder']),
  };
}

/** Kalıcı bildirimin `data` JSON string'ini ayrıştırır; bozuksa null. */
export function parseWorksheetCommentNotificationData(data: string | null): WorksheetCommentRef | null {
  if (!data) {
    return null;
  }
  try {
    return parseWorksheetCommentRef(JSON.parse(data));
  } catch {
    return null;
  }
}

/** Router hedefi: `commands` + (varsa) `queryParams`. */
export interface AppRouteLink {
  commands: (string | number)[];
  queryParams?: Record<string, string | number>;
}

/**
 * Derin link: `/test/{worksheetId}?commentId={id}[&questionId={qid}][&rootCommentId={rid}]`.
 * `rootCommentId` yalnız reply'larda (commentId'den farklıysa) eklenir — son 5 reply dışında kalan bir cevabı
 * sayfa, kökün replies ucundan yükleyerek bulabilsin.
 */
export function worksheetCommentLink(ref: WorksheetCommentRef): AppRouteLink {
  const queryParams: Record<string, number> = { commentId: ref.commentId };
  if (ref.questionId !== null) {
    queryParams['questionId'] = ref.questionId;
  }
  if (ref.rootCommentId !== null && ref.rootCommentId !== ref.commentId) {
    queryParams['rootCommentId'] = ref.rootCommentId;
  }
  return { commands: ['/test', ref.worksheetId], queryParams };
}
