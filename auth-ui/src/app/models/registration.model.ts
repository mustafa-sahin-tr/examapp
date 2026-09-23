export interface Grade {
  id: number;
  name: string;
}

/** GET /api/school — anonymous, used to populate school dropdowns before login. */
export interface School {
  id: number;
  name: string;
  city?: string;
}

/** Response shape shared by /api/exam/{student,teacher,parent}/register. */
export interface RegisterProfileResponse {
  accessToken: string;
  expiresIn: number;
  profileId: number;
}

/** Backend enum ExamApp.Api.Data.TeacherApprovalStatus — JSON'a sayı olarak serialize edilir. */
export enum TeacherApprovalStatus {
  Pending = 0,
  Approved = 1,
  Rejected = 2,
}

/**
 * Issue #234 — POST /api/exam/teacher/register yanıtı (TeacherController.RegisterTeacher `Ok(new { ... })`).
 * Okul talebi admin onayına kadar bağ kurmaz; onaylı `schoolId` bu yanıtta dönmez.
 */
export interface RegisterTeacherResponse extends RegisterProfileResponse {
  approvalStatus: TeacherApprovalStatus;
  /** Onay bekleyen okul bağlantısı talebi; yoksa null. */
  requestedSchoolId: number | null;
  /** true → okul bağlantısı yönetici onayı bekliyor. */
  schoolApprovalPending: boolean;
}

/** 400 / 409 hata gövdesi (409 → teacher.registrationChangeNotAllowed, localize). */
export interface RegisterErrorBody {
  message?: string;
}

export interface RegisterStudentPayload {
  studentNumber: string;
  schoolId: number | null;
  gradeId: number;
}

/** POST /api/teacher/register — mirrors backend RegisterTeacherDto. */
export interface RegisterTeacherPayload {
  schoolId: number | null;
  /** true → bağımsız özel ders öğretmeni; hesap admin onayı bekler (Pending). */
  isIndependentTutor: boolean;
}

/** POST /api/auth/register — auth-api RegisterDto. `role`: Student | Teacher | Parent (#240 allowlist). */
export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: string;
}

/**
 * POST /api/auth/register yanıtı (auth-api RegisterResponse, #240). E-posta yeni de olsa kayıtlı da olsa aynı
 * gövde döner; kullanıcı id'si/token içermez.
 */
export interface RegisterResponse {
  message: string;
}
