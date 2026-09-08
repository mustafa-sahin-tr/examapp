namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin dashboard'ın üst özet kartları (Issue #85, Phase 1). Yalnızca anlık toplamlar içerir;
/// zaman serisi / trend alanları Phase 2'de ayrı bir DTO ile eklenecek.
/// </summary>
public class DashboardSummaryDto
{
    public int TeacherCount { get; set; }
    public int StudentCount { get; set; }
    public int WorksheetCount { get; set; }
    public int QuestionCount { get; set; }
    public int AiClassifiedQuestionCount { get; set; }

    /// <summary>AiClassifiedQuestionCount / QuestionCount, 0..1 aralığında. Soru yoksa 0.</summary>
    public double AiClassifiedRatio { get; set; }
}
