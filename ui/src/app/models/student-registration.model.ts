import { ParentStatus } from './parent-status.model';

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
  /**
   * Issue #440: veli bilgisi — veli kuralına tabi öğrencide (yetişkin rolü yok) zorunlu; eksik/geçersizse sunucu kaydı
   * tamamlamaz (400 `ParentInfoRequired` / `InvalidParent*`). Diğer hesaplarda gönderilmez.
   */
  parentFullName?: string;
  parentEmail?: string;
  parentPhone?: string;
}

/** Başarılı yanıt — StudentController.RegisterStudent `Ok(new { ... })` anonim nesnesi. */
export interface StudentRegistrationResult {
  accessToken: string;
  expiresIn: number;
  profileId: number;
  /** Issue #440: davet açıldıysa güncel veli durumu (kısıtlı mod; Development'ta dev aktivasyon bağlantısı dahil). */
  parentStatus?: ParentStatus | null;
}
