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
}

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