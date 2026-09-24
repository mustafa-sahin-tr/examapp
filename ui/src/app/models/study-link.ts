/**
 * Konu / alt konu harici çalışma linkleri (issue #61).
 * Backend karşılığı: `api/ExamApp.Api/Models/Dtos/StudyLinks/TopicStudyLinkDtos.cs`.
 * `sourceType` JSON'da string enum olarak taşınır ("YouTube" | "Other").
 */
export type StudyLinkSourceType = 'YouTube' | 'Other';

/** Backend `TopicStudyLinkLimits.MaxActiveLinksPerScope` — liste yanıtı gelmeden önceki varsayılan. */
export const MAX_ACTIVE_STUDY_LINKS = 7;

/** Backend `TopicStudyLinkLimits.TitleMaxLength` / `UrlMaxLength`. */
export const STUDY_LINK_TITLE_MAX_LENGTH = 200;
export const STUDY_LINK_URL_MAX_LENGTH = 2048;

/** Backend `TopicStudyLinkErrorCodes.ActiveLimitReached`. */
export const STUDY_LINK_ACTIVE_LIMIT_REACHED = 'ActiveLimitReached';

/** Link kapsamı: TAM OLARAK biri dolu (`subTopicId` → alt konu linkleri, `topicId` → yalnız konu seviyesi linkler). */
export type StudyLinkScope = { topicId: number; subTopicId?: never } | { subTopicId: number; topicId?: never };

/** `TopicStudyLinkDto` — yönetici görünümü. */
export interface StudyLink {
  id: number;
  topicId: number | null;
  subTopicId: number | null;
  title: string;
  url: string;
  sourceType: StudyLinkSourceType;
  sortOrder: number;
  isActive: boolean;
  createdByUserId: number;
  createdByName: string;
  createdByRole: string;
  createTime: string;
  updateTime: string | null;
}

/** `TopicStudyLinkQueryDto` (GET /api/exam/study-links). */
export type StudyLinkQuery = StudyLinkScope & {
  includeInactive?: boolean;
  skip?: number;
  take?: number;
};

/** `TopicStudyLinkListResultDto` — GET listesi ve PUT reorder yanıtı. */
export interface StudyLinkListResponse {
  success: boolean;
  message?: string | null;
  items: StudyLink[];
  totalCount: number;
  activeCount: number;
  maxActiveLinks: number;
}

/** `CreateTopicStudyLinkDto` (POST /api/exam/study-links). */
export interface CreateStudyLinkRequest {
  topicId?: number | null;
  subTopicId?: number | null;
  title: string;
  url: string;
  sourceType?: StudyLinkSourceType | null;
  sortOrder?: number | null;
  isActive: boolean;
}

/** `UpdateTopicStudyLinkDto` (PUT /api/exam/study-links/{id}); konu/alt konu değiştirilemez. */
export interface UpdateStudyLinkRequest {
  title: string;
  url: string;
  sourceType?: StudyLinkSourceType | null;
  sortOrder?: number | null;
  isActive?: boolean | null;
}

/** `TopicStudyLinkOrderItemDto`. */
export interface StudyLinkOrderItem {
  id: number;
  sortOrder: number;
}

/** `ReorderTopicStudyLinksDto` (PUT /api/exam/study-links/reorder). */
export type ReorderStudyLinksRequest = StudyLinkScope & { items: StudyLinkOrderItem[] };

/** Hata gövdesi: `ResponseBaseDto` (+ `TopicStudyLinkResultDto.errorCode`). */
export interface StudyLinkErrorResponse {
  success: false;
  message?: string | null;
  notFound?: boolean;
  forbidden?: boolean;
  conflict?: boolean;
  objectId?: number;
  errorCode?: string | null;
}

// ---- Öğrenci sonuç ekranı (GET /api/exam/study-links/for-result/{testInstanceId}) ----

/** `StudyLinkGroupKind` — alt konu grubu ya da konu seviyesi yedek grup. */
export type StudyLinkGroupKind = 'SubTopic' | 'Topic';

/** `StudyLinkSummaryDto` — yalnız aktif linkler. */
export interface StudyLinkSummary {
  id: number;
  title: string;
  url: string;
  sourceType: StudyLinkSourceType;
}

/** `StudyLinkGroupDto`. */
export interface StudyLinkGroup {
  kind: StudyLinkGroupKind;
  subTopicId: number | null;
  topicId: number | null;
  name: string;
  links: StudyLinkSummary[];
}

/** `QuestionStudyLinkSuggestionDto` — yanlış cevaplanan bir soru. */
export interface QuestionStudyLinkSuggestion {
  questionId: number;
  testInstanceQuestionId: number;
  groups: StudyLinkGroup[];
}
