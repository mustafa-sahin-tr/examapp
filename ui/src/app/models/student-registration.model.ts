/**
 * Issue #277 (madde 6) — POST /api/exam/student/register gövdesi.
 * Kaynak: api/ExamApp.Api/Models/Dtos/RegisterStudentDto.cs. Backend yalnız bu üç alanı okur; serbest metin okul adı
 * (`schoolName`) okunmaz. `schoolId` opsiyoneldir; ilk atanan okul kilitlenir (#259) — sonradan yalnız admin değiştirir.
 */
export interface RegisterStudentRequest {
  /** Zorunlu, en fazla 50 karakter. */
  studentNumber: string;
  /** GET /api/exam/school listesinden seçilen okul; okulsuz kayıt için null. */
  schoolId: number | null;
  /** Zorunlu (`[Required] int`). */
  gradeId: number;
}

/** Başarılı yanıt — StudentController.RegisterStudent `Ok(new { ... })` anonim nesnesi. */
export interface StudentRegistrationResult {
  accessToken: string;
  expiresIn: number;
  profileId: number;
}
