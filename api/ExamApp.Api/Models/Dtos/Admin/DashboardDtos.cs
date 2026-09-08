using System;
using System.Collections.Generic;

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

/// <summary>Günlük zaman serisinde tek bir nokta (UTC gün).</summary>
public class DailyPointDto
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Admin dashboard trend serileri (Issue #87). Her seri son N gün için günlük, boşluksuz
/// (veri olmayan günler 0) ve tarihe göre artan sıradadır.
/// </summary>
public class DashboardTrendsDto
{
    /// <summary>Gün başına oluşturulan soru sayısı (Question.CreateTime).</summary>
    public List<DailyPointDto> QuestionCreated { get; set; } = new();

    /// <summary>
    /// Gün başına çözülen soru sayısı. PracticeSessionQuestion (AnsweredAt) ve
    /// WorksheetInstanceQuestion (cevaplanmış satırın UpdateTime'ı) toplamı, tek seri.
    /// </summary>
    public List<DailyPointDto> QuestionSolved { get; set; } = new();
}
