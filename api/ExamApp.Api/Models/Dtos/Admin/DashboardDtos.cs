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

    /// <summary>
    /// Issue #441 hedef kümesi: Active velisi ve aktivasyon bekleyen daveti olmayan öğrenciler (hoşgörü sayacı henüz başlamamış olanlar
    /// dahil). Kayıp riskini izlemek için.
    /// </summary>
    public int StudentsWithoutParent { get; set; }

    /// <summary>Issue #441: hedef kümeden hoşgörü süresi içinde olanlar (sayaç başlamış, süre dolmamış).</summary>
    public int StudentsInParentGrace { get; set; }

    /// <summary>Issue #441: hedef kümeden süresi dolmuş olanlar (yalnız "veli bilgisi ekle" ekranını görür).</summary>
    public int StudentsParentGraceExpired { get; set; }

    /// <summary>Issue #440: velisi henüz aktive etmemiş / onaylamamış (açık davetli) öğrenciler — kısıtlı mod.</summary>
    public int StudentsAwaitingParentActivation { get; set; }
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

    /// <summary>
    /// Gün başına başarılı öğrenci login sayısı (Issue #89). LoginEvent üzerinden Success=true ve
    /// Role=Student (case-insensitive) filtresiyle sayılır; diğer roller bu seriye dahil edilmez.
    /// </summary>
    public List<DailyPointDto> StudentLogin { get; set; } = new();
}
