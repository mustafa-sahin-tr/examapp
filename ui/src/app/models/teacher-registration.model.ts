import { TeacherApprovalStatus } from './tutor.model';

/**
 * Issue #234 — POST /api/exam/teacher/register başarılı yanıtı.
 * Kaynak: api/ExamApp.Api/Controllers/TeacherController.cs → RegisterTeacher `Ok(new { ... })` anonim nesnesi
 * (TeacherRegistrationResultDto'dan türetilir). `schoolId` (onaylı okul) bu yanıtta DÖNMEZ.
 */
export interface TeacherRegistrationResult {
  accessToken: string;
  expiresIn: number;
  profileId: number;
  /** Sayı olarak serialize edilir (JsonStringEnumConverter yok): Pending=0, Approved=1, Rejected=2. */
  approvalStatus: TeacherApprovalStatus;
  /** Onay bekleyen okul bağlantısı talebi; yoksa null. */
  requestedSchoolId: number | null;
  /** true → okul bağlantısı admin onayı bekliyor; o zamana kadar öğretmen okulsuz sayılır. */
  schoolApprovalPending: boolean;
  /**
   * Issue #287: öğretmen hesabı onaylı mı. Yeni kayıtta her zaman false — UI teacher dashboard yerine
   * "başvuru durumu" sayfasına geçer.
   */
  teacherAccountApproved: boolean;
  /**
   * Issue #287 (review): istek diline göre yerelleştirilmiş sunucu metni (ör. `teacher.savedApprovalPending`). Eski
   * sunucularda yok → istemci sözlüğüne düşülür.
   */
  message?: string;
}

/** 400 / 409 hata gövdesi: `{ message }` (409 → teacher.registrationChangeNotAllowed, localize). */
export interface TeacherRegistrationError {
  message?: string;
}
