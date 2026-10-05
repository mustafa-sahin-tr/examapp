/**
 * Issue #106 — öğrenci ↔ öğretmen doğrudan mesajlaşma sözleşmesi.
 * Kaynak: `api/ExamApp.Api/Models/Dtos/DirectMessages/DirectMessageDtos.cs` (dilim a; JSON camelCase).
 * Tarihler sunucuda UTC `DateTime`; ekranda `parseUtcDate` ile okunur.
 */

/** Transloco scope'u: `public/i18n/direct-messages/<lang>.json` (iki sayfa + dialoglar ortak). */
export const DIRECT_MESSAGES_SCOPE = 'direct-messages';

/** Gövde üst sınırı (temizlik sonrası) — backend `BodyTooLong`. */
export const DIRECT_MESSAGE_BODY_MAX_LENGTH = 2000;
/** Şikayet notu üst sınırı — backend `ReportNoteTooLong`. */
export const DIRECT_MESSAGE_REPORT_NOTE_MAX_LENGTH = 500;
/** Liste sayfa boyutu (backend 1..50'ye sıkıştırır, varsayılan 20). */
export const DIRECT_MESSAGE_PAGE_SIZE = 20;
/** Konuşma geçmişi tek sayfa (backend 1..50, varsayılan 30). */
export const DIRECT_MESSAGE_HISTORY_TAKE = 30;

export type DirectMessageRelation = 'school' | 'assignment' | 'both';
export type DirectMessageSenderRole = 'Student' | 'Teacher';
export type TeacherInboxFilter = 'all' | 'unread' | 'blocked';

export const DIRECT_MESSAGE_REPORT_REASONS = ['spam', 'abuse', 'personalInfo', 'other'] as const;
export type DirectMessageReportReason = (typeof DIRECT_MESSAGE_REPORT_REASONS)[number];

/** Öğrencinin mesajlaşabileceği öğretmen (A ∪ B). */
export interface MessageableTeacher {
  teacherId: number;
  fullName: string;
  avatar: string;
  relation: DirectMessageRelation;
  /** Mevcut konuşma varsa Id; yoksa ilk mesaj konuşmayı açar. */
  conversationId?: number | null;
}

export interface MessageableTeacherPage {
  items: MessageableTeacher[];
  page: number;
  pageSize: number;
  totalCount: number;
  /** Ad araması aday sınırına takıldıysa true — "aramayı daralt" bilgisi gösterilir. */
  truncated?: boolean;
}

/** Ad araması en az bu kadar karakterle sunucuya gider (daha kısası istemcide gönderilmez). */
export const TEACHER_SEARCH_MIN_LENGTH = 2;
/** Arama girişi debounce süresi (ms). */
export const TEACHER_SEARCH_DEBOUNCE_MS = 300;

/** Konuşma listesi satırı (öğrenci: konuşmalarım; öğretmen: gelen kutusu). */
export interface ConversationSummary {
  conversationId: number;
  counterpartName: string;
  counterpartAvatar: string;
  teacherId?: number | null;
  studentId?: number | null;
  lastMessageAt: string;
  lastMessagePreview: string;
  lastMessageIsMine: boolean;
  unreadCount: number;
  /** Yalnız öğretmen görünümünde dolu; öğrencide null (engel sızdırılmaz). */
  isBlocked?: boolean | null;
}

export interface ConversationPage {
  items: ConversationSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface DirectMessage {
  id: number;
  conversationId: number;
  body: string;
  senderRole: DirectMessageSenderRole;
  isMine: boolean;
  sentAt: string;
}

/** Konuşma geçmişi sayfası (eskiden yeniye) + başlık. GET okundu İŞARETLEMEZ; ayrı `POST .../read`. */
export interface ConversationMessages {
  conversationId: number;
  counterpartName: string;
  counterpartAvatar: string;
  teacherId?: number | null;
  studentId?: number | null;
  canSend: boolean;
  isBlocked?: boolean | null;
  items: DirectMessage[];
  hasMore: boolean;
  nextBeforeId?: number | null;
}

/** `ResponseBaseDto` + `errorCode`. */
export interface DirectMessageResponse {
  success: boolean;
  message: string;
  errorCode?: string | null;
}

/** POST .../messages (201). */
export interface SendDirectMessageResult extends DirectMessageResponse {
  conversationId: number;
  conversationCreated: boolean;
  directMessage?: DirectMessage | null;
}

/** POST .../block | /unblock (200, idempotent). */
export interface DirectMessageBlockResult extends DirectMessageResponse {
  conversationId: number;
  isBlocked: boolean;
  changed: boolean;
}

/** POST .../conversations/{id}/read gövdesi: bu Id'ye kadar (dahil) karşı taraf mesajları okundu. */
export interface MarkConversationReadRequest {
  upToMessageId: number;
}

export interface ReportDirectMessageRequest {
  messageId?: number;
  reason: DirectMessageReportReason;
  note?: string;
}

/** POST .../read (200): bu çağrıda okundu yapılan mesaj sayısı (zaten okunmuşsa 0, idempotent). */
export interface MarkDirectMessagesReadResult extends DirectMessageResponse {
  conversationId: number;
  markedCount: number;
}

/** POST .../report (200; tekrar şikayet idempotent). */
export interface DirectMessageReportResult extends DirectMessageResponse {
  reportId: number;
  alreadyReported: boolean;
}

/** Backend `DirectMessageErrorCodes` sabitleriyle birebir (DirectMessageDtos.cs). */
export const DIRECT_MESSAGE_ERROR_CODES = [
  'CannotMessageTeacher',
  'RelationshipEnded',
  // 404: konuşma yok YA DA istek sahibi taraf değil (varlık sızdırılmaz).
  'ConversationNotFound',
  'BlockTeacherOnly',
  'MessageNotFound',
  'CannotReportOwnMessage',
  'StudentProfileNotFound',
  'TeacherProfileNotFound',
  'BodyRequired',
  'BodyTooLong',
  'BodyInvalidCharacters',
  'InvalidReportReason',
  'ReportNoteTooLong',
  'ReportNoteInvalidCharacters',
  'InvalidFilter',
  'SearchTooShort',
  'NameLookupUnavailable',
  'InvalidUpToMessageId',
  'RateLimited',
] as const;
export type DirectMessageErrorCode = (typeof DIRECT_MESSAGE_ERROR_CODES)[number];

export function isDirectMessageErrorCode(value: unknown): value is DirectMessageErrorCode {
  return typeof value === 'string' && (DIRECT_MESSAGE_ERROR_CODES as readonly string[]).includes(value);
}
