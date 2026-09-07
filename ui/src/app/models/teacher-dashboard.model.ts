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
