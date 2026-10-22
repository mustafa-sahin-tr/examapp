using ExamApp.Api.Data;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// issue #471: soru bankası araması için <c>GET api/questions/bank</c> filtreleri. Tüm filtreler opsiyonel ve birlikte
/// (VE) uygulanır; <see cref="SubTopicIds"/> kendi içinde VEYA (soru seçilen alt konulardan en az birine bağlı).
/// </summary>
public class QuestionBankQuery
{
    public const int DefaultTake = 24;
    public const int MaxTake = 60;

    /// <summary>Kapsam: <c>mine</c> (varsayılan) yalnız çağıranın soruları; <c>all</c> çağıranın görebildiği tüm sorular.</summary>
    public string? Scope { get; set; }

    /// <summary>Sınıf: sorunun konusunun sınıfı (<c>Topic.GradeId</c>).</summary>
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int[]? SubTopicIds { get; set; }

    /// <summary>Zorluk aralığı (1-10, iki uç dahil).</summary>
    public int? MinDifficulty { get; set; }
    public int? MaxDifficulty { get; set; }

    /// <summary>Sınıflandırma kaynağı: <c>AI</c> | <c>Human</c>. Kaynağı boş (hiç sınıflandırılmamış) sorular hiçbirine uymaz.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// true → ders ve konusu belirlenmiş sorular; false → sınıflandırması bitmemiş (ders ya da konu boş) sorular.
    /// </summary>
    public bool? Classified { get; set; }

    /// <summary>true → doğru cevabı belirlenmiş sorular; false → doğru cevabı boş sorular.</summary>
    public bool? HasCorrectAnswer { get; set; }

    /// <summary>
    /// true → en az bir teste bağlı; false → çağıranın görebildiği hiçbir teste bağlı değil; null → farketmez. Sayım
    /// <see cref="QuestionBankItemDto.TestCount"/> ile aynıdır (öğretmen için görebildiği silinmemiş testler).
    /// </summary>
    public bool? InTest { get; set; }

    public int Skip { get; set; }
    public int Take { get; set; } = DefaultTake;

    public const int MaxSubTopicIds = 50;

    /// <summary>Kapsam <c>all</c> değilse (boş dahil) yalnız çağıranın soruları.</summary>
    public bool IsAllScope => string.Equals(Scope, "all", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sayfa boyutu: 0/negatif → varsayılan, üst sınır <see cref="MaxTake"/>.</summary>
    public int EffectiveTake => Take <= 0 ? DefaultTake : Math.Min(Take, MaxTake);

    /// <summary>Yalnız ad eşleşmesi (büyük/küçük harf duyarsız) — Enum.TryParse'ın kabul ettiği "0" / "AI,Human" geçersiz.</summary>
    public ClassificationSource? ParsedSource =>
        Enum.GetValues<ClassificationSource>()
            .Where(v => string.Equals(v.ToString(), Source, StringComparison.OrdinalIgnoreCase))
            .Select(v => (ClassificationSource?)v)
            .FirstOrDefault();

    /// <summary>Bilinmeyen kapsam/kaynak, aralık dışı zorluk, negatif skip ya da aşırı uzun alt konu listesi → false (400).</summary>
    public bool IsValid()
    {
        if (!string.IsNullOrEmpty(Scope) && !IsAllScope && !string.Equals(Scope, "mine", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(Source) && ParsedSource is null)
            return false;
        if (MinDifficulty is < 1 or > 10 || MaxDifficulty is < 1 or > 10)
            return false;
        if (MinDifficulty is { } min && MaxDifficulty is { } max && min > max)
            return false;
        if (Skip < 0 || SubTopicIds is { Length: > MaxSubTopicIds })
            return false;
        return true;
    }
}

/// <summary>issue #471: soru bankası arama sonucunun bir satırı (önizleme kartı).</summary>
public class QuestionBankItemDto
{
    public int Id { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }

    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
    public int? GradeId { get; set; }
    public List<SubTopicDto> SubTopics { get; set; } = new();

    public int DifficultyLevel { get; set; }

    /// <summary>Null → hiç sınıflandırılmamış (eski soru ya da sınıflandırıcı henüz çalışmadı).</summary>
    public ClassificationSource? ClassificationSource { get; set; }

    /// <summary>Ders ve konusu belirlenmiş mi.</summary>
    public bool IsClassified { get; set; }
    public bool HasCorrectAnswer { get; set; }

    /// <summary>Sorunun bağlı olduğu, çağıranın görebildiği (silinmemiş) test sayısı; admin için tümü.</summary>
    public int TestCount { get; set; }

    /// <summary>Soru çağıranın mı (UI ipucu — yetki kararı sunucuda).</summary>
    public bool IsOwn { get; set; }

    /// <summary>
    /// issue #472: "Bankadan ekle" ile teste bağlanabilir mi (UI ipucu — karar sunucuda): kendi sorusu ya da sahibi damgalı
    /// soru. Başkasının sahipsiz (legacy) sorusu bağlanamaz (bkz. <c>QuestionTestLinkService</c>).
    /// </summary>
    public bool CanLink { get; set; }

    public DateTime CreateTime { get; set; }
}

/// <summary>issue #471: sayfalı soru bankası arama sonucu.</summary>
public class QuestionBankPageDto
{
    public List<QuestionBankItemDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

/// <summary>issue #472: <c>POST api/questions/test/{testId}/bank-questions</c> — bankadan seçilen soruları teste bağlar.</summary>
public class LinkBankQuestionsRequest
{
    public const int MaxQuestions = 60;

    /// <summary>Bağlanacak soru id'leri; istek sırasıyla testin sonuna eklenir (tekrarlar yok sayılır).</summary>
    public int[]? QuestionIds { get; set; }

    public bool IsValid() =>
        QuestionIds is { Length: > 0 and <= MaxQuestions } && QuestionIds.All(id => id > 0);
}

public class LinkBankQuestionsResultDto
{
    /// <summary>Teste yeni bağlanan soru id'leri (eklenme sırasıyla).</summary>
    public List<int> AddedQuestionIds { get; set; } = new();

    /// <summary>Zaten testte olduğu için atlanan soru id'leri.</summary>
    public List<int> AlreadyInTestQuestionIds { get; set; } = new();
}
