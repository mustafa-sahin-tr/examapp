using ExamApp.Api.Data;
using ExamApp.Api.Services.Storage;
using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class BulkQuestionCreateDto
{
    public string ImageData { get; set; } // MinIO'ya yüklenecek, EF'e ImageUrl olarak gidecek
    public List<BulkPassageDto> Passages { get; set; }
    public List<BulkQuestionDto> Questions { get; set; }
    public HeaderInfo Header { get; set; }

    /// <summary>
    /// Issue #565: sorunun sınıfı, kaynak açıkça biliyorsa (işaretleme ekranındaki seçim). <c>Header.TestId</c> bir teste
    /// çözülürse testin sınıfı esastır ve buradaki farklı değer reddedilir; test yoksa var olan bir sınıf olmalı.
    /// Yalnız <c>QuestionCreatedEvent.GradeId</c>'ye gider (Question'da kolon yok).
    /// </summary>
    public int? GradeId { get; set; }
}

public class StudyPageAttachImageDto
{
    public string ImageData { get; set; }
}

public class StudyPageAttachImageResponseDto : ResponseBaseDto
{
    [StorageUrl(StorageArea.StudyPage)]
    public string ImageUrl { get; set; } = string.Empty;
}

public class BulkPassageDto
{
    public string Id { get; set; } // JSON'dan geliyor, ancak EF’de kullanılmayacak
    public double X { get; set; }
    public double Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>issue #573: kapsam türü (0 = Reading, 1 = Activity); null = Reading.</summary>
    [EnumDataType(typeof(PassageKind))]
    public PassageKind? Kind { get; set; }

    /// <summary>issue #573: etkinlik yönergesi (isteğe bağlı).</summary>
    [MaxLength(ExamApp.Api.Data.Passage.MaxInstructionLength)]
    [ExamApp.Api.Helpers.PlainText] // düz metin; kontrol karakteri → 400 (innerHTML ile render edilmez)
    public string? Instruction { get; set; }
}

public class BulkQuestionDto
{
    public string Name { get; set; }
    public string? BookName { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? InteractionType { get; set; }

    /// <summary>issue #570: yinelenen anahtarsız bir JSON nesnesi olmalı (InteractionPlanSolution.IsValidPlan).</summary>
    [MaxLength(ExamApp.Api.Helpers.InteractionPlanSolution.MaxPlanLength)]
    public string? InteractionPlan { get; set; }

    /// <summary>
    /// issue #570: etkileşim çözümü (JSON nesnesi, ör. <c>{"placements":[...]}</c>), plandan ayrı. Boşsa plan içindeki
    /// <c>solution</c> (eski istemci) ayrılıp kullanılır; plan her durumda çözümsüz saklanır.
    /// </summary>
    [MaxLength(ExamApp.Api.Helpers.InteractionPlanSolution.MaxSolutionLength)]
    public string? InteractionSolution { get; set; }

    /// <summary>Issue #580: çözüm kaynağı <c>Key</c> | <c>AI</c> | <c>Human</c> (boş = Human); Key/AI onay bekler.</summary>
    [MaxLength(16)]
    public string? SolutionSource { get; set; }

    /// <summary>Issue #580: true + çağıran Admin/QuestionTrainer → Key/AI çözümü onaylı kaydedilir.</summary>
    public bool? SolutionApproved { get; set; }

    /// <summary>Issue #580: Key/AI çözümünü üreten talimat sürümü.</summary>
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int? SolutionPromptVersion { get; set; }

    /// <summary>
    /// Issue #580: işaretleme ekranındaki taslak çözümün id'si (<c>POST api/labeling/activity-solution</c> → <c>draftSolutionId</c>).
    /// Verilirse kaynak/onay/sürüm sunucuda bu taslaktan türetilir (<see cref="SolutionSource"/> yok sayılır); taslağın tipi ve çözümü
    /// (taslak id'leriyle) gönderilenle aynı olmalı, yoksa 400 <c>questions.solution.draftMismatch</c>. Onaylı yalnız taslak onaylıysa.
    /// </summary>
    public int? DraftSolutionId { get; set; }

    public bool ShowPassageFirst { get; set; }
    public bool IsExample { get; set; } // EF'de `IsExample`
    public string? ExampleAnswer { get; set; } // EF'de `PracticeCorrectAnswer`
    public List<BulkAnswerDto> Answers { get; set; } = new List<BulkAnswerDto>();
    public string PassageId { get; set; }

    /// <summary>
    /// Issue #454: soru bazında opsiyonel sınıflandırma. <c>null</c> → <see cref="BulkQuestionCreateDto.Header"/>
    /// davranışı; alan bazında boş (null/0/boş liste) değerler header'a düşer. Bilerek iç içe nesne:
    /// UI region nesnelerini <c>questions[]</c>'a yayıyor ve region'larda üst seviyede
    /// <c>topicId</c>/<c>subtopicId</c>/sayısal <c>classificationSource</c> zaten bulunabiliyor.
    /// </summary>
    public BulkQuestionClassificationDto? Classification { get; set; }

    /// <summary>
    /// Issue #577: kitap kaynağı — yalnız sunucu içinden (işaretleme ekranı bankaya kayıt) doldurulur; istemci JSON'undan
    /// bağlanmaz. Doluysa toplu kayıt aynı transaction'da <c>QuestionSource</c> yazar; kırpma SHA-256'sı yüklenen soru
    /// görselinin baytlarından hesaplanır.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public BulkQuestionSourceInfo? Source { get; set; }
}

/// <summary>Issue #577: <see cref="BulkQuestionDto.Source"/> — sunucu içi kitap kaynağı bilgisi.</summary>
public sealed record BulkQuestionSourceInfo(int BookId, int FilePage, int? PrintedPage, string? ActivityLabel, string? ItemLabel);

public class BulkQuestionClassificationDto
{
    /// <summary>Soru başına alt konu üst sınırı (header ile aynı); [ApiController] model doğrulaması aşımda 400 döner.</summary>
    public const int MaxSubTopicIds = 50;

    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }

    [MaxLength(MaxSubTopicIds)]
    public List<int>? SubTopicIds { get; set; }

    /// <summary>"AI" | "Human" (büyük/küçük harf duyarsız); null/geçersiz → header kaynağı → Human.</summary>
    public string? Source { get; set; }
}

public class BulkAnswerDto
{
    public string Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsCorrect { get; set; }
    public string? Tag { get; set; }
    public int? Order { get; set; }
}


public class HeaderInfo
{
    public int? TestId { get; set; }
    public int? TopicId { get; set; }

    public int? SubjectId { get; set; }

    [MaxLength(BulkQuestionClassificationDto.MaxSubTopicIds)]
    public List<int>? Subtopics { get; set; }
    public string? ClassificationSource { get; set; }

}