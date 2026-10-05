/**
 * Issue #313 — PUT /api/exam/admin/teachers/{id}/school (id = Teacher.Id).
 * Kaynak: api/ExamApp.Api/Models/Dtos/Admin/AdminTeacherSchoolDtos.cs. Sözleşme öğrenci ucuyla (#277,
 * `admin-student-school.model.ts`) paraleldir.
 */
export interface AdminTeacherSchoolRequest {
  schoolId: number;
}

/**
 * 200 yanıtı. Öğretmen zaten o okuldaysa da 200 döner (`changed = false`, yan etkisiz).
 * Hatalar `{ message }`: 400 (okul eksik/bilinmiyor), 403 (hedef admin/servis hesabı ya da kendisi), 404, 409 (eşzamanlı
 * değişiklik), 502 (auth-api/Keycloak hatası; okul değişmez). 429 gövdesi düz metindir, `Retry-After` saniye taşır.
 */
export interface AdminTeacherSchoolResponse {
  /** Teacher.Id */
  teacherId: number;
  /** Öğretmenin güncel okulu. */
  schoolId: number;
  /** Değişiklikten önceki okul; okulsuzdu ise null. */
  previousSchoolId: number | null;
  changed: boolean;
  /**
   * true → okul yazıldı ama oturum/profil önbelleği temizlenemedi; değişiklik (claim) 1 saate kadar gecikebilir.
   * Eski backend sürümleri alanı göndermeyebilir → yoksa false kabul edilir.
   */
  profileCacheStale?: boolean;
}
