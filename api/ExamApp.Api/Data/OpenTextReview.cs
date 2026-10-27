using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #583: açık uçlu (<c>openText</c>) cevabın değerlendirmesi — oturumdaki soru satırı başına (<c>WorksheetInstanceQuestionId</c>,
/// tekil) en çok bir kayıt; öğretmen düzeltince aynı satır güncellenir. Puan YOK (etkinlik puansız): hiçbir puan/XP olayı yazılmaz.
/// <para>
/// Güncellik: <see cref="AnswerSha256"/> değerlendirilen metnin özeti (<c>OpenTextGrader.AnswerHash</c>). Öğrenci metni değiştirince
/// (save-answer / kontrol, aynı satır kilidi altında) <see cref="Stale"/> = özet farklı mı; eski değerlendirme silinmez (öğrenci
/// "cevabın değişti, yeniden değerlendirilecek" notuyla görür), cevap gönderilmişse yeniden kuyruğa düşer. Metin eskisine dönerse
/// yeniden güncel olur. #609 yeniden çözmede oturumun kayıtları silinir.
/// </para>
/// İleride AI önerisi (ayrı issue) <see cref="Source"/> = <see cref="AnswerGradedBy.AI"/> + öğretmen onayı olarak eklenecek; bu sürüm
/// yalnız <see cref="AnswerGradedBy.Teacher"/> yazar.
/// </summary>
public class OpenTextReview
{
    public const int MaxFeedbackLength = 1000;

    public int Id { get; set; }

    public int WorksheetInstanceQuestionId { get; set; }

    public WorksheetInstanceQuestion WorksheetInstanceQuestion { get; set; } = null!;

    public OpenTextEvaluation Evaluation { get; set; }

    /// <summary>Öğrenciye gösterilen geri bildirim (düz metin, ≤ <see cref="MaxFeedbackLength"/>); yoksa null.</summary>
    [MaxLength(MaxFeedbackLength)]
    public string? Feedback { get; set; }

    /// <summary>Değerlendirmeyi kim verdi; bu sürümde her zaman <see cref="AnswerGradedBy.Teacher"/>.</summary>
    public AnswerGradedBy Source { get; set; } = AnswerGradedBy.Teacher;

    /// <summary>Son değerlendiren/düzelten kullanıcı (exam API <c>Users.Id</c>; öğretmen ya da admin).</summary>
    public int ReviewedByUserId { get; set; }

    public DateTime ReviewedAt { get; set; }

    /// <summary>Değerlendirilen metnin SHA-256'sı (küçük harf hex, 64).</summary>
    [MaxLength(64)]
    public string AnswerSha256 { get; set; } = string.Empty;

    /// <summary>Öğrencinin şimdiki metni değerlendirilenden farklı (değerlendirme eski).</summary>
    public bool Stale { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>issue #583: açık uçlu cevabın değerlendirmesi (puansız). API'de <c>sufficient | partial | insufficient</c>.</summary>
public enum OpenTextEvaluation
{
    Sufficient = 0,
    Partial = 1,
    Insufficient = 2
}
