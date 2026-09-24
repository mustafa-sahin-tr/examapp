import { Teacher } from "./teacher";
import { TeacherApplicationStatus } from "./teacher-application.model";

/**
 * GET /api/exam/teacher/check-teacher yanıtı (TeacherController.CheckTeacher anonim nesnesi).
 * Issue #287: onay alanları yalnız `hasTeacherRecord=true` iken gelir (`TeacherApprovalState`).
 */
export interface CheckkTeacherResponse {
    hasTeacherRecord: boolean;
    teacher: Teacher | null;
    teacherAccountApproved?: boolean;
    teacherApplicationStatus?: TeacherApplicationStatus;
    rejectionReason?: string | null;
  }
