/**
 * Issue #85/#86 — GET /api/exam/admin/dashboard/summary yanıtı.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/Admin/DashboardDtos.cs (DashboardSummaryDto)
 *
 * Yalnızca anlık toplamlar içerir; zaman serisi / trend alanları ayrı DTO'da (AdminDashboardTrends).
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

/**
 * Issue #87/#88 — günlük zaman serisinde tek nokta (DailyPointDto).
 * `date` ISO `yyyy-MM-dd` (saat bileşeni yok, UTC gün).
 */
export interface AdminDashboardTrendPoint {
  date: string;
  count: number;
}

/**
 * Issue #87/#88/#89 — GET /api/exam/admin/dashboard/trends?days=N yanıtı (DashboardTrendsDto).
 * Her seri tam olarak N eleman içerir, tarihe göre artan, veri olmayan günler 0 ile doldurulmuş.
 * `questionSolved` practice + worksheet toplamı olarak tek seridir.
 * `studentLogin` (Issue #89) yalnızca Success=true ve Role=Student login olaylarını sayar.
 */
export interface AdminDashboardTrends {
  questionCreated: AdminDashboardTrendPoint[];
  questionSolved: AdminDashboardTrendPoint[];
  studentLogin: AdminDashboardTrendPoint[];
}
