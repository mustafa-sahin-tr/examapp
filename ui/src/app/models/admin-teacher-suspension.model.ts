/**
 * Issue #289 — öğretmen hesap onayını askıya alma / geri açma.
 * Kaynak: api/ExamApp.Api/Models/Dtos/Admin/AdminTeacherSuspensionDtos.cs
 * (`AdminTeacherSuspendRequestDto`, `AdminTeacherSuspensionResponseDto`, `AdminTeacherSuspensionErrorCodes`).
 * Uçlar: `POST /api/exam/admin/teachers/{id}/suspend` (gövde `{ reason }`) ve `.../unsuspend` (gövdesiz);
 * `{id}` = Teacher.Id (admin öğretmen listesindeki `id`). Keycloak hesabına dokunmaz (#155 ayrı).
 */

/** Backend `AdminTeacherSuspensionService.ReasonMaxLength` ile aynı. */
export const TEACHER_SUSPENSION_REASON_MAX_LENGTH = 500;

/** `POST .../suspend` gövdesi. Neden zorunlu; sunucu trim'ler, 1-500 karakter. */
export interface AdminTeacherSuspendRequest {
  reason: string;
}

/** suspend/unsuspend başarılı (200) yanıtı. Neden yanıtta tekrar dönmez. */
export interface AdminTeacherSuspensionResponse {
  /** Teacher.Id */
  teacherId: number;
  /** Öğretmen özellikleri açık mı (askıya almada false, askıyı kaldırmada true). */
  accountApproved: boolean;
  accountSuspended: boolean;
  /** Askıyı kaldırmada yeni hesap onayı anı (ISO-8601 UTC); askıya almada null. */
  accountApprovedAt: string | null;
  /** Askıya almada askı anı (ISO-8601 UTC); askıyı kaldırmada null. */
  accountSuspendedAt: string | null;
}

/*
 * Hatalar `{ message (yerelleştirilmiş), errorCode }`: 400 SuspensionReasonRequired / SuspensionReasonTooLong,
 * 404 TeacherNotFound, 409 TeacherAlreadySuspended / TeacherAccountNotApproved / TeacherNotSuspended / ConcurrentChange,
 * 429 rate limit (#155 hesap durumu uçlarıyla aynı kova). UI `message`'ı gösterir; errorCode'a özel dal yok.
 */
