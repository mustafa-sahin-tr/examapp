/**
 * Issue #53 — GET /api/exam/teacher/dashboard-summary yanıtı.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/TeacherDashboardSummaryDto.cs
 *
 * Sadece giriş yapan öğretmenin sahip olduğu (CreateUserId == teacherId) worksheet'ler sayılır;
 * paylaşılanlar hariçtir. Aktif/pasif ayrımı yoktur.
 */
export interface TeacherDashboardSummary {
  totalWorksheets: number;
  totalUniqueStudents: number;
}

/**
 * Issue #54 — GET /api/exam/teacher/worksheets-overview yanıtının tek satırı.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/TeacherWorksheetOverviewDto.cs
 *
 * Öğretmenin sahip olduğu worksheet'ler, ada göre sıralı gelir; hiç yoksa boş dizi.
 */
export interface TeacherWorksheetOverview {
  worksheetId: number;
  name: string;
  /** Hedeflenen benzersiz öğrenci sayısı (direkt + sınıf/okul bazlı atamalar, distinct). */
  assignedStudentCount: number;
  /** 0-100 arası, 2 ondalık. Atanan öğrenci yoksa 0. */
  completionPercentage: number;
}

/**
 * Issue #55 — GET /api/exam/teacher/lagging-students yanıtının tek satırı.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/TeacherLaggingStudentDto.cs
 *
 * Yalnızca "geride kalan" (en az bir bayrağı true olan) öğrenci/worksheet çiftleri döner;
 * studentName, sonra worksheetName'e göre sıralı. Geride kalan yoksa boş dizi.
 */
export interface TeacherLaggingStudent {
  studentId: number;
  /** Auth-api'den çözümlenen ad-soyad; erişilemezse "Öğrenci #{StudentNumber}" fallback'i. */
  studentName: string;
  worksheetId: number;
  worksheetName: string;
  /** Bu bağlamda ikili sinyal: 0 (tamamlanmadı) veya 100 (tamamlandı). */
  completionPercentage: number;
  /** completionPercentage < 50. */
  isLowCompletion: boolean;
  /** Atamanın bitiş tarihi geçmiş ve tamamlanmamış. */
  isExpired: boolean;
}
