using System;
using System.Collections.Generic;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Models.Dtos.OpenText;

/// <summary>issue #583: değerlendirme durumları ve API'deki değerlendirme adları (tek kaynak).</summary>
public static class OpenTextReviewStatuses
{
    /// <summary>Cevap gönderildi (kontrol edildi / oturum tamamlandı), değerlendirme yok.</summary>
    public const string Pending = "pending";

    /// <summary>Şimdiki metin değerlendirildi.</summary>
    public const string Reviewed = "reviewed";

    /// <summary>Değerlendirme önceki bir metne ait; öğrenci metni değiştirdi (gönderilmişse yeniden kuyrukta).</summary>
    public const string Outdated = "outdated";

    public const string Sufficient = "sufficient";
    public const string Partial = "partial";
    public const string Insufficient = "insufficient";

    public static string ToApi(OpenTextEvaluation evaluation) => evaluation switch
    {
        OpenTextEvaluation.Sufficient => Sufficient,
        OpenTextEvaluation.Partial => Partial,
        _ => Insufficient,
    };

    public static bool TryParse(string? value, out OpenTextEvaluation evaluation)
    {
        switch (value?.Trim())
        {
            case Sufficient: evaluation = OpenTextEvaluation.Sufficient; return true;
            case Partial: evaluation = OpenTextEvaluation.Partial; return true;
            case Insufficient: evaluation = OpenTextEvaluation.Insufficient; return true;
            default: evaluation = default; return false;
        }
    }

    public static string SourceToApi(AnswerGradedBy source) => source switch
    {
        AnswerGradedBy.AI => "ai",
        AnswerGradedBy.Teacher => "teacher",
        _ => "auto",
    };
}

/// <summary>
/// issue #583: öğrencinin kendi açık uçlu cevabının değerlendirme durumu — oturum yanıtında (<c>test-canvas-instance(-result)</c>)
/// ve kontrol yanıtında openText satırında. Cevap yoksa / gönderilmemiş ve değerlendirme yoksa alan null.
/// </summary>
public class OpenTextStudentReviewDto
{
    /// <summary><c>pending</c> | <c>reviewed</c> | <c>outdated</c> (bkz. <see cref="OpenTextReviewStatuses"/>).</summary>
    public string Status { get; set; } = OpenTextReviewStatuses.Pending;

    /// <summary><c>sufficient</c> | <c>partial</c> | <c>insufficient</c>; pending'de null. outdated'da eski değerlendirme.</summary>
    public string? Evaluation { get; set; }

    /// <summary>Öğretmenin geri bildirimi (düz metin); yoksa null.</summary>
    public string? Feedback { get; set; }

    public DateTime? ReviewedAt { get; set; }
}

/// <summary>issue #583: kuyruk satırındaki mevcut değerlendirme (öğretmen görünümü).</summary>
public class OpenTextTeacherReviewDto
{
    public string Evaluation { get; set; } = OpenTextReviewStatuses.Sufficient;
    public string? Feedback { get; set; }

    /// <summary><c>teacher</c> (ileride <c>ai</c>).</summary>
    public string Source { get; set; } = "teacher";

    public int ReviewedByUserId { get; set; }
    public DateTime ReviewedAt { get; set; }

    /// <summary>true = değerlendirme önceki metne ait.</summary>
    public bool Stale { get; set; }
}

/// <summary>
/// issue #583: <c>GET api/open-text-reviews</c> sorgusu. <c>status</c>: <c>pending</c> (varsayılan: değerlendirmesi olmayan ya da
/// eskimiş gönderilmiş cevaplar) | <c>reviewed</c> (güncel değerlendirmesi olanlar) | <c>all</c>. <c>page</c> 1..10000,
/// <c>pageSize</c> 1..50 (varsayılan 20).
/// </summary>
public class OpenTextReviewQueueQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxPage = 10_000;

    public string? Status { get; set; }
    public int? WorksheetId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
}

/// <summary>issue #583: kuyruk satırı (öğretmen). Öğrencinin metni JSON string'dir; istemci düz metin olarak gösterir (HTML değil).</summary>
public class OpenTextReviewItemDto
{
    /// <summary>Değerlendirme ucunun anahtarı (<c>WorksheetInstanceQuestion.Id</c>).</summary>
    public int InstanceQuestionId { get; set; }
    public int InstanceId { get; set; }
    public int WorksheetId { get; set; }
    public string WorksheetName { get; set; } = string.Empty;

    public int QuestionId { get; set; }

    /// <summary>Maddenin oynatıcıdaki sırası (1 tabanlı; oturumun maddeleri <c>(Order, Id)</c> sırasıyla). UI harfe çevirir (a, b, …).</summary>
    public int ItemNumber { get; set; }
    public string? QuestionText { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? QuestionImageUrl { get; set; }

    public int StudentId { get; set; }
    public int StudentUserId { get; set; }
    public string? StudentNumber { get; set; }
    public string? GradeName { get; set; }

    /// <summary>Öğrencinin şimdiki metni.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Metnin özeti — değerlendirme ucuna aynen geri gönderilir (metin bu arada değiştiyse 409).</summary>
    public string AnswerHash { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public int? MinWords { get; set; }
    public int? MaxWords { get; set; }

    /// <summary>Örnek cevap (değerlendiren herkese).</summary>
    public string? SampleAnswer { get; set; }

    /// <summary>Rubrik — yalnız sorunun sahibine ve Admin'e; diğer değerlendirenlere null. Öğrenciye hiç gitmez.</summary>
    public string? Rubric { get; set; }

    /// <summary>Satırın son yazımı (yaklaşık gönderim zamanı).</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary><c>pending</c> | <c>reviewed</c> | <c>outdated</c>.</summary>
    public string Status { get; set; } = OpenTextReviewStatuses.Pending;

    /// <summary>İstek sahibi bu satırı değerlendirebilir / düzeltebilir mi (değerlendirme yok ya da kendisinin, veya Admin).</summary>
    public bool CanEdit { get; set; }

    public OpenTextTeacherReviewDto? Review { get; set; }
}

/// <summary>
/// issue #583: <c>PUT api/open-text-reviews/{instanceQuestionId}</c> gövdesi. Sınırlar serviste denetlenir (tek biçimli 400).
/// </summary>
public class OpenTextReviewRequestDto
{
    /// <summary><c>sufficient</c> | <c>partial</c> | <c>insufficient</c>.</summary>
    public string? Evaluation { get; set; }

    /// <summary>Düz metin, ≤ 1000 karakter; boş = geri bildirimsiz.</summary>
    public string? Feedback { get; set; }

    /// <summary>Kuyruktan gelen <see cref="OpenTextReviewItemDto.AnswerHash"/>; farklıysa 409 <c>OpenTextAnswerChanged</c>.</summary>
    public string? AnswerHash { get; set; }
}

/// <summary>
/// issue #583: değerlendirme yazımının sonucu (<see cref="TestSessionResultDto"/> biçimi: <c>success, message, errorCode,
/// invalidInput, notFound, conflict</c>). Başarıda <see cref="Item"/> güncel kuyruk satırıdır.
/// </summary>
public class OpenTextReviewResultDto : TestSessionResultDto
{
    public OpenTextReviewItemDto? Item { get; set; }
}

public static class OpenTextReviewErrorCodes
{
    /// <summary>400 — değerlendirme değeri / geri bildirim / özet geçersiz.</summary>
    public const string InvalidReview = "OpenTextInvalidReview";

    /// <summary>404 — satır yok, açık uçlu değil, etkinlik değil ya da istek sahibinin değerlendiremeyeceği öğrenci.</summary>
    public const string NotFound = "OpenTextReviewNotFound";

    /// <summary>403 — değerlendirmeyi başka bir kullanıcı verdi; yalnız o kullanıcı ve Admin düzeltebilir.</summary>
    public const string Forbidden = "OpenTextReviewForbidden";

    /// <summary>400 — geçersiz kuyruk sorgusu (status / page / pageSize / worksheetId).</summary>
    public const string InvalidQuery = "OpenTextInvalidQuery";

    /// <summary>409 — cevap boş ya da henüz gönderilmedi (kontrol edilmedi, oturum tamamlanmadı).</summary>
    public const string NotSubmitted = "OpenTextNotSubmitted";

    /// <summary>409 — öğrencinin metni kuyruk yüklendikten sonra değişti; kuyruğu yenileyip yeniden değerlendirin.</summary>
    public const string AnswerChanged = "OpenTextAnswerChanged";
}
