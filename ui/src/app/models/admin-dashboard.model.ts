/**
 * Issue #85/#86 — GET /api/exam/admin/dashboard/summary yanıtı.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/Admin/DashboardDtos.cs (DashboardSummaryDto)
 *
 * Yalnızca anlık toplamlar içerir; zaman serisi / trend alanları Phase 2'de ayrı bir DTO ile gelecek.
 */
export interface AdminDashboardSummary {
  teacherCount: number;
  studentCount: number;
  worksheetCount: number;
  questionCount: number;
  aiClassifiedQuestionCount: number;
  /** aiClassifiedQuestionCount / questionCount, 0..1 aralığında. Soru yoksa 0. */
  aiClassifiedRatio: number;
}
