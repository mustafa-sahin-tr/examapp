/**
 * Issue #277 (madde 8) — PUT /api/exam/admin/students/{id}/school.
 * Kaynak: api/ExamApp.Api/Models/Dtos/Admin/AdminStudentSchoolDtos.cs.
 */
export interface AdminStudentSchoolRequest {
  schoolId: number;
}

/**
 * 200 yanıtı. Öğrenci zaten o okuldaysa da 200 döner (`changed = false`, yan etkisiz).
 * Hatalar `{ message }`: 400 (okul eksik/bilinmiyor), 403 (kendisi / korumalı rol), 404, 409 (eşzamanlı değişiklik),
 * 502 (auth-api/Keycloak hatası; okul değişmez). 429 gövdesi düz metindir, `Retry-After` saniye taşır.
 */
export interface AdminStudentSchoolResponse {
  /** Student.Id */
  studentId: number;
  /** Öğrencinin güncel okulu. */
  schoolId: number;
  /** Değişiklikten önceki okul; okulsuzdu ise null. */
  previousSchoolId: number | null;
  changed: boolean;
}
