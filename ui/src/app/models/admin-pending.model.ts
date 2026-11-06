/** Issue #861: admin "Bekleyenler" — kuyruk başına bekleyen sayısı (`GET admin/pending-summary`). */
export interface AdminPendingSummary {
  teacherApplications: number;
  schoolApplications: number;
  commentReports: number;
  questionReports: number;
  openTextAnswers: number;
  total: number;
}
