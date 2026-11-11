namespace ExamApp.Api.Models.Dtos.ParentDashboard;

/// <summary>
/// Issue #1112: velinin haftalık raporu (Pazartesi–Pazar, Europe/Istanbul). Sayılar veli panelinin kaynaklarından gelir: cevaplar
/// özetle (<c>ParentDashboardService</c>), ders ve konu doğruluğu konu performansıyla, ödevler V3 kapsamıyla, denemeler öğrencinin
/// sonuç ekranıyla aynı kuraldadır. Soru içeriği, cevap anahtarı ve başka öğrencinin verisi yoktur (sıra yalnız kendi yeri).
/// </summary>
public sealed class ParentWeeklyReportDto
{
    public int StudentId { get; set; }

    /// <summary>Haftanın Pazartesi'si (yerel).</summary>
    public DateOnly WeekStart { get; set; }

    public int QuestionsSolved { get; set; }
    public int Correct { get; set; }
    public int ActiveDays { get; set; }

    /// <summary>Cevaplarda geçen süre (dakika; soru başına en çok 10 dk sayılır).</summary>
    public int StudyMinutes { get; set; }

    /// <summary>Haftada kazanılan puan (günlük puan defteri).</summary>
    public int Points { get; set; }

    /// <summary>Ders başına doğruluk, en çok cevaplanan önce; konusu belli olmayan cevaplar girmez.</summary>
    public List<ParentWeeklySubjectDto> Subjects { get; set; } = [];

    /// <summary>Doğruluğu önceki haftaya göre en az 15 puan düşen konular (iki haftada da en az 3 cevap), en çok düşen önce, en çok 3.</summary>
    public List<ParentWeeklyTopicDropDto> DroppedTopics { get; set; } = [];

    /// <summary>Hafta içinde tamamlanan ödevler.</summary>
    public int AssignmentsCompleted { get; set; }

    /// <summary>Hafta sonunda gecikmiş ödevler (son 30 gün, panel kovasıyla aynı).</summary>
    public int AssignmentsOverdue { get; set; }

    /// <summary>Hafta içinde bitirilen denemeler.</summary>
    public List<ParentWeeklyMockExamDto> MockExams { get; set; } = [];

    /// <summary>Hafta içinde kazanılan rozetler.</summary>
    public List<ParentWeeklyBadgeDto> Badges { get; set; } = [];

    /// <summary>Tek satırlık öneri: <c>noActivity</c>, <c>overdue</c>, <c>reviewTopic</c>, <c>moreDays</c> ya da <c>keepGoing</c>.</summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary><see cref="Suggestion"/> <c>reviewTopic</c> ise konu adı.</summary>
    public string? SuggestionTopic { get; set; }
}

public sealed class ParentWeeklySubjectDto
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int Answered { get; set; }
    public int Correct { get; set; }
    public int AccuracyPercent { get; set; }
}

public sealed class ParentWeeklyTopicDropDto
{
    public int TopicId { get; set; }
    public string TopicName { get; set; } = string.Empty;
    public string? SubjectName { get; set; }
    public int AccuracyPercent { get; set; }
    public int PreviousAccuracyPercent { get; set; }
}

public sealed class ParentWeeklyMockExamDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Net { get; set; }
    public int? Rank { get; set; }
    public int? Takers { get; set; }
    public DateTime FinishedAtUtc { get; set; }
}

public sealed class ParentWeeklyBadgeDto
{
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
}
