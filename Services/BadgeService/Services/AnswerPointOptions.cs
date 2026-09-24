using System.ComponentModel.DataAnnotations;

namespace BadgeService.Services;

/// <summary>
/// <c>AnswerPoints</c> config bölümü (issue #279, item 6). Consumer tarafında sahte/bozuk bir
/// <c>AnswerSubmittedEvent.QuestionPoint</c> değeriyle puan şişirmeyi engelleyen üst sınır.
/// </summary>
public sealed class AnswerPointOptions
{
    public const string SectionName = "AnswerPoints";

    /// <summary>
    /// Kabul edilen en yüksek soru puanı. Varsayılan 100: exam API'de <c>Question.Point</c> tek/çift haneli
    /// değerler kullanır (1, 5, 10 …) — 100, gerçekçi kullanımın çok üstünde ama makul bir güvenlik payı
    /// bırakır. Bunun üstündeki (ya da negatif) bir değer bozuk/kötü niyetli mesaj sayılır: reddedilmez,
    /// bu tavana KIRPILIR (event zaten geçerli bir cevabı temsil ediyor olabilir; puanı sıfırlamak yerine
    /// sınırlamak öğrencinin cevabını kaybetmez) — bkz. <see cref="AnswerSubmissionAggregationService"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxQuestionPoint { get; set; } = 100;
}
