import { TeacherApprovalStatus } from './tutor.model';

/**
 * Issue #277 (madde 6) — POST /api/exam/teacher/register gövdesi.
 * Kaynak: api/ExamApp.Api/Models/Dtos/RegisterTeacherDto.cs. Serbest metin okul adı backend'de okunmaz.
 */
export interface RegisterTeacherRequest {
  /** GET /api/exam/school listesinden seçilen okul; bağımsız öğretmen veya okulsuz kayıt için null. */
  schoolId: number | null;
  /** true → bağımsız özel ders öğretmeni (issue #92); bu durumda `schoolId` gönderilmez (null). */
  isIndependentTutor: boolean;
}

/**
 * Issue #234 — POST /api/exam/teacher/register başarılı yanıtı.
 * Kaynak: api/ExamApp.Api/Controllers/TeacherController.cs → RegisterTeacher `Ok(new { ... })` anonim nesnesi
 * (TeacherRegistrationResultDto'dan türetilir).
 */
export interface TeacherRegistrationResult {
  accessToken: string;
  expiresIn: number;
  profileId: number;
  /** Onaylı okul; okul talebi onay bekliyorsa veya okulsuzsa null. */
  schoolId?: number | null;
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

/**
 * Issue #277 (madde 2) — 429 gövdesi: reddedilen okul talebinden sonra bekleme süresi (24 saat) dolmadan yeni okul
 * talebi. `Retry-After` başlığı da saniye cinsinden aynı değeri taşır.
 */
export interface TeacherSchoolRequestCooldownError {
  message?: string;
  retryAfterSeconds?: number;
  /** ISO-8601 UTC. */
  retryAfterUtc?: string | null;
}
