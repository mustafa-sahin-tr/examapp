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
