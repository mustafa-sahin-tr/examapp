namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// issue #861: admin "Bekleyenler" kutusu — kuyruk başına karar bekleyen iş sayısı (tek çağrı; menü rozeti ve sol liste).
/// Liste uçları yerinde kalır; burada yalnız sayılar var.
/// </summary>
public class AdminPendingSummaryDto
{
    public int TeacherApplications { get; set; }
    public int SchoolApplications { get; set; }
    public int CommentReports { get; set; }
    public int QuestionReports { get; set; }
    public int OpenTextAnswers { get; set; }

    public int Total => TeacherApplications + SchoolApplications + CommentReports + QuestionReports + OpenTextAnswers;
}
