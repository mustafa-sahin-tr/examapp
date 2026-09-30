import { Teacher } from "./teacher";
import { TeacherApplicationStatus } from "./teacher-application.model";

/**
 * GET /api/exam/teacher/check-teacher yanıtı (`CheckTeacherResponseDto`/`CheckTeacherRecordResponseDto`, issue #298).
 * Issue #287: onay alanları yalnız `hasTeacherRecord=true` iken gelir (`TeacherApprovalState`).
 */
export interface CheckkTeacherResponse {
    hasTeacherRecord: boolean;
    teacher: Teacher | null;
    teacherAccountApproved?: boolean;
    /** Issue #289: hesap onayı askıda (true iken `teacherAccountApproved` false). Neden dönmez. */
    teacherAccountSuspended?: boolean;
    teacherApplicationStatus?: TeacherApplicationStatus;
    rejectionReason?: string | null;
  }
