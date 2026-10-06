/**
 * Issue #361: onay bekleyen öğrenci okul üyeliği — `GET /api/exam/student-school-requests` satırı
 * (`StudentSchoolRequestDto`). Öğrenci numarası listede kısmi (son 4 karakter, #262); e-posta dönmez.
 */
export interface StudentSchoolRequest {
  studentId: number;
  fullName: string;
  studentNumber: string;
  gradeId: number | null;
  gradeName: string | null;
  schoolId: number;
  schoolName: string;
  /** Öğrenci kaydının oluşturulma anı, ISO-8601 (UTC). */
  registeredAt: string;
}

/** Sunucunun sayfalı yanıtı (`Paged<T>`). */
export interface StudentSchoolRequestPage {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  items: StudentSchoolRequest[];
}

/** Onay/ret yanıtı ve hata gövdesi: `{ message }`. */
export interface StudentSchoolDecisionResponse {
  message?: string;
}
