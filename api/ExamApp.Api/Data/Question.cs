using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class Question : BaseEntity
{
    public int Id { get; set; }
    public string? Text { get; set; } = string.Empty;  // Soru metni
    public string? SubText { get; set; } = string.Empty;  // Soru metni
    public string? ImageUrl { get; set; }  // Eğer soru resimli ise
    public string? BookName { get; set; }

    public int? SubjectId { get; set; }  // Soru hangi derse ait
    public Subject Subject { get; set; }

    public int Point { get; set; }  // Sorunun puan değeri (1, 5, 10 vb.)

    public int? TopicId { get; set; } // Ana Konu (Opsiyonel)

    public Topic Topic { get; set; }

    [Required]
    public int DifficultyLevel { get; set; } = 1; // 1-10 arasında zorluk seviyesi

    // Bir soru birçok teste ait olabilir
    public ICollection<WorksheetQuestion> WorksheetQuestions { get; set; } = new List<WorksheetQuestion>();

    public ICollection<QuestionSubTopic> QuestionSubTopics { get; set; } = new List<QuestionSubTopic>(); // New collection for many-to-many relationship

    public ICollection<QuestionLearningOutcome> QuestionLearningOutcomes { get; set; } = new List<QuestionLearningOutcome>(); // Issue #692

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();  // Şıklar

    public int? CorrectAnswerId { get; set; }

    [ForeignKey("CorrectAnswerId")]
    public Answer CorrectAnswer { get; set; }

    public int? PassageId { get; set; } // 🟢 Kapsam ID (Opsiyonel)

    [ForeignKey("PassageId")]
    public Passage Passage { get; set; } // 🟢 Navigation Property

    // If true, UI can show passage first, then reveal the question.
    public bool ShowPassageFirst { get; set; } = false;

    public string? PracticeCorrectAnswer { get; set; } // Eğer çalışma testi ise bu alan kullanılacak
    public bool IsExample { get; set; } = false;// Eğer true ise bu soru örnek sorudur ve cevabı otomatik gösterilir.
    public int AnswerColCount { get; set; } = 3;
    // Şıkların kaç sütun olacağı. 
    // 3 demek yan yana 3 şık olacak demektir.
    // 1 demek cevaplar alt alta sıralanacak demektir.
    // Canvas interaction type (e.g. mcq, dragDropLabeling)
    public string? InteractionType { get; set; }

    // Optional interaction metadata (JSON) — issue #570: çözümsüz saklanır.
    public string? InteractionPlan { get; set; }

    // issue #570: etkileşim çözümü (JSON, ör. {"placements":[...]}) plandan ayrı; öğrenciye yalnız örnek soruda /
    // bitmiş testte, öğretmene yalnız sahibi/admin ise döner. Entity yanlışlıkla serileştirilirse sızmasın diye JsonIgnore.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? InteractionSolution { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    // Height of question-v2 image (question area without answer block).
    public int? SanitizedHeight { get; set; }

    // Width of the source page this question was cropped from (issue #778). Null for older questions.
    public int? SourcePageWidth { get; set; }

    // Teacher's layout choice while labeling (issue #778): "top", "side", or "whole". Null means automatic.
    [MaxLength(MaxLayoutChoiceLength)]
    public string? LayoutChoice { get; set; }

    public const int MaxLayoutChoiceLength = 16;
    public const string LayoutChoicePattern = "^(top|side|whole)$";

    public bool IsCanvasQuestion { get; set; } = false;

    public ClassificationSource? ClassificationSource { get; set; }

    // Issue #690: içeriğin kaynağı ve lisansı. Mevcut sorular Unknown kalır; lisanslı sayılmaz.
    public QuestionSourceType SourceType { get; set; } = QuestionSourceType.Unknown;

    [MaxLength(200)]
    public string? RightsHolder { get; set; }

    [MaxLength(200)]
    public string? LicenseReference { get; set; }

    public DateTime? LicenseEndsAt { get; set; }

    /// <summary>Hak sahibi talebiyle yeni test ve pratiklerden çekildi; başlamış oturumlar etkilenmez.</summary>
    public bool IsUnpublished { get; set; }

    /// <summary>Issue #826: testten bağımsız havuz paylaşımı (<see cref="QuestionBankSharing"/>); düzenleme yetkisi vermez.</summary>
    public QuestionBankSharing BankSharing { get; set; } = QuestionBankSharing.Private;

    /// <summary>Issue #703: öğretmenin beyanıyla bağlam temelli soru (bkz. docs/context-based-questions.md); doğrulanmaz.</summary>
    public bool IsContextBased { get; set; }

    public ICollection<QuestionSkill> QuestionSkills { get; set; } = new List<QuestionSkill>(); // Issue #703

    /// <summary>Ortamlar arası eşleşme kimliği (bkz. QuestionContentHash). İçerik değişince sıfırlanır, Hangfire işi yeniden hesaplar.</summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    /// <summary>Metin, şıklar, doğru cevap, plan ve çözümün hash'i. Aynı <see cref="ContentHash"/> farklı içerikle çakışma demektir.</summary>
    [MaxLength(64)]
    public string? ContentFingerprint { get; set; }
}

/// <summary>Issue #690: sorunun kaynağı.</summary>
public enum QuestionSourceType
{
    Unknown = 0,
    Commissioned = 1,
    Publisher = 2,
    Teacher = 3,

    /// <summary>Kazanımdan Claude ile üretildi, kaydeden öğretmen ya da admin inceledi.</summary>
    Generated = 4
}
