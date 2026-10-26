using ExamApp.Api.Services.Storage;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

public class QuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; }
    public string? SubText { get; set; }
    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// issue #365 (S2): canvas görünümünün kullandığı cevap bloğu kırpılmış <c>question-v2</c> görseli (yalnız
    /// <c>.../question.ext</c> adlı görsellerde; yoksa null). Salt okunur — istemciden okunmaz; çıktıda imzalanır.
    /// </summary>
    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrlV2 => ExamApp.Api.Helpers.QuestionImageVariant.ToV2(ImageUrl);

    public string? BookName { get; set; }
    public string CategoryName { get; set; }

    public int? SubjectId { get; set; }

    public int? TopicId { get; set; } // Konu ID'si

    // issue #279 review (security L2): UI zaten 1-20 aralığına kısıtlıyor (question.component.html,
    // client-side) ama sunucu tarafında bir üst sınır yoktu — BadgeService'teki
    // AnswerPointOptions.MaxQuestionPoint (varsayılan 100) ile aynı tavan kullanıldı. Mevcut veride/UI'da
    // 100'ü aşan bir Point kullanımı bulunamadı (grep ile kontrol edildi).
    [Range(0, 100)]
    public int Point { get; set; }
    public int DifficultyLevel { get; set; }

    public string? Image { get; set; } // Base64 formatında geliyor
    public bool IsExample { get; set; }
    public string? PracticeCorrectAnswer { get; set; }
    public int AnswerColCount { get; set; }

    public int? TestId { get; set; } // Test ID'si

    public bool IsCanvasQuestion { get; set; } // Canvas sorusu mu? (Evet/Hayır)
    public PassageDto? Passage { get; set; }

    // If true, the UI may show only the passage first and reveal the question on demand.
    public bool ShowPassageFirst { get; set; }

    public List<AnswerDto> Answers { get; set; } = new();

    // Many-to-many: QuestionSubTopics -> SubTopic
    // Serialized as "subTopics" to match UI model.
    public List<SubTopicDto> SubTopics { get; set; } = new();

    public int? CorrectAnswerId { get; set; } // Doğru cevap ID'si
    public string? CorrectAnswer { get; set; } // Doğru cevap ID'si

    // Canvas interaction (e.g. mcq, dragDropLabeling)
    public string? InteractionType { get; set; }

    // Optional interaction metadata (JSON) — issue #570: yanıtlarda her zaman çözümsüz (solution alanı yok).
    // Kayıtta yinelenen anahtarsız bir JSON nesnesi olmalı (InteractionPlanSolution.IsValidPlan).
    [MaxLength(ExamApp.Api.Helpers.InteractionPlanSolution.MaxPlanLength)]
    public string? InteractionPlan { get; set; }

    /// <summary>
    /// issue #570: etkileşim çözümü (JSON nesnesi, ör. <c>{"placements":[...]}</c>). Kayıtta: boşsa plan içindeki
    /// <c>solution</c> ayrılır, o da yoksa güncellemede mevcut çözüm korunur. Yanıtta yalnız soru sahibi/admin düzenleme
    /// yanıtında, öğrenciye yalnız örnek (<c>IsExample</c>) soruda ya da doğru cevapların açıldığı bitmiş oturumda dolu.
    /// </summary>
    [MaxLength(ExamApp.Api.Helpers.InteractionPlanSolution.MaxSolutionLength)]
    public string? InteractionSolution { get; set; }

    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? SanitizedHeight { get; set; }

    public int Order { get; set; }
    public ClassificationSource ClassificationSource { get; set; }
}

public class SubTopicDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int TopicId { get; set; }
}

public class PassageDto
{
    public int? Id { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    /// <summary>
    /// issue #573: kapsam türü (0 = Reading, 1 = Activity). Yanıtlarda her zaman dolu. İstekte null = oluştururken Reading,
    /// var olan kapsamı bağlarken değişmez (alanı göndermeyen eski istemciler türü sıfırlamasın).
    /// </summary>
    [EnumDataType(typeof(PassageKind))]
    public PassageKind? Kind { get; set; }

    /// <summary>issue #573: etkinlik yönergesi. İstekte null = var olan kapsamda değişmez; boş string = temizle.</summary>
    [MaxLength(ExamApp.Api.Data.Passage.MaxInstructionLength)]
    [ExamApp.Api.Helpers.PlainText] // düz metin; kontrol karakteri → 400 (innerHTML ile render edilmez)
    public string? Instruction { get; set; }
}
public class AnswerDto
{
    public int? Id { get; set; }
    public string? Text { get; set; }
    public string? Image { get; set; } // Base64 formatında geliyor
    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }

    public bool IsCorrect { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    // Sıralama ve etiket
    public string? Tag { get; set; } // A, B, C, D, E
    public int? Order { get; set; } // 0, 1, 2, ...
}

public class QuestionSavedDto : ResponseBaseDto
{
    public int? QuestionId { get; set; }

    /// <summary>
    /// True when the request was rejected before anything was written because an input value is invalid
    /// (e.g. a passage image URL outside the allowlist, issue #365). Lets the controller map to HTTP 400.
    /// </summary>
    public bool InvalidInput { get; set; }
}