using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Services.Practice;

/// <summary>"Günün soruları" ayarları (issue #99), <c>DailyQuestions</c> bölümü. Açılışta doğrulanır (ValidateOnStart).</summary>
public sealed class DailyQuestionsOptions
{
    public const string SectionName = "DailyQuestions";

    /// <summary>Günlük set hedef soru sayısı (N). Havuz azsa set eldeki kadar soruyla üretilir.</summary>
    [Range(1, 50)]
    public int QuestionCount { get; set; } = 5;

    /// <summary>
    /// Öğrencinin son kaç yerel günde (bugün dahil) cevapladığı sorular sete tercih dışı bırakılır. Havuz yetmezse bu
    /// sorulardan tamamlanır. 0 = dışlama kapalı.
    /// </summary>
    [Range(0, 365)]
    public int RecentAnswerExclusionDays { get; set; } = 14;
}
