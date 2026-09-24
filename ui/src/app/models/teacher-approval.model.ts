import { HttpErrorResponse } from '@angular/common/http';
import type { UserProfile } from '../services/auth.service';

/** Issue #287: onaysız öğretmenin yönlendirildiği "başvuru durumu" sayfası (app.routes.ts ile aynı). */
export const TEACHER_APPROVAL_PENDING_PATH = 'teacher-approval-pending';
export const TEACHER_APPROVAL_PENDING_URL = `/${TEACHER_APPROVAL_PENDING_PATH}`;

/** Backend `TeacherAccessErrorCodes.TeacherNotApproved`. */
export const TEACHER_NOT_APPROVED_ERROR_CODE = 'TeacherNotApproved';

/**
 * Issue #287 — onaysız öğretmen öğretmen özelliği gerektiren bir uca geldiğinde dönen 403 gövdesi.
 * Kaynak: api/ExamApp.Api/Models/Dtos/Teachers/TeacherAccessDtos.cs → `TeacherNotApprovedResponseDto`.
 */
export interface TeacherNotApprovedErrorBody {
  success: false;
  errorCode: typeof TEACHER_NOT_APPROVED_ERROR_CODE;
  /** İstek diline göre yerelleştirilmiş `teacher.notApproved` metni. */
  message: string;
}

/** 403 + `errorCode: "TeacherNotApproved"` mi? (Diğer 403'ler — rol yok vb. — gövdesizdir.) */
export function isTeacherNotApprovedError(err: unknown): err is HttpErrorResponse {
  if (!(err instanceof HttpErrorResponse) || err.status !== 403) {
    return false;
  }
  const body: unknown = err.error;
  return (
    !!body &&
    typeof body === 'object' &&
    (body as Partial<TeacherNotApprovedErrorBody>).errorCode === TEACHER_NOT_APPROVED_ERROR_CODE
  );
}

/**
 * Profildeki öğretmen hesabı onay bilgisi: true/false, alan yoksa null (= bilinmiyor; login/exchange yanıtı
 * bu alanı taşımaz, yalnız exam-api refresh doldurur).
 */
export function teacherAccountApprovalOf(profile: UserProfile | null | undefined): boolean | null {
  const approved = profile?.teacher?.teacherAccountApproved;
  return typeof approved === 'boolean' ? approved : null;
}
