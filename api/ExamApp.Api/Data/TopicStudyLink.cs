using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ExamApp.Api.Data;

/// <summary>
/// Konu / alt konu için harici çalışma linki (YouTube videosu, makale vb.) — issue #61.
/// Görsel tabanlı <see cref="StudyItem"/> (eski adı StudyPage) ile KARIŞTIRILMAMALI: bu kayıt yalnızca
/// dış kaynağa işaret eden bir URL'dir, öğrenciye yanlış cevapladığı sorunun alt konusu için önerilir.
///
/// Kapsam (StudyItem deseniyle tutarlı; ikisi de nullable ama en az biri dolu olmak zorunda):
/// <list type="bullet">
///   <item><c>SubTopicId</c> dolu → alt konu linki. <c>TopicId</c> alt konunun üst konusuyla doldurulur (filtre kolaylığı).</item>
///   <item><c>SubTopicId</c> boş, <c>TopicId</c> dolu → konu linki.</item>
/// </list>
/// "En fazla 7 aktif link" limiti bu kapsam anahtarı (alt konu varsa alt konu, yoksa konu) üzerinden sayılır.
/// </summary>
public class TopicStudyLink : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int? TopicId { get; set; }

    [ForeignKey("TopicId")]
    public Topic? Topic { get; set; }

    public int? SubTopicId { get; set; }

    [ForeignKey("SubTopicId")]
    public SubTopic? SubTopic { get; set; }

    [Required]
    [MaxLength(TopicStudyLinkLimits.TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(TopicStudyLinkLimits.UrlMaxLength)]
    public string Url { get; set; } = string.Empty;

    public TopicStudyLinkSourceType SourceType { get; set; } = TopicStudyLinkSourceType.Other;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public int CreatedByUserId { get; set; }

    [MaxLength(200)]
    public string CreatedByName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string CreatedByRole { get; set; } = string.Empty;

    /// <summary>Son değiştiren kullanıcının adı (kimliği BaseEntity.UpdateUserId). Oluşturulduktan sonra hiç değişmediyse null.</summary>
    [MaxLength(200)]
    public string? UpdatedByName { get; set; }
}

/// <summary>Çalışma linki denetim kaydının eylem tipi. DB'de int — değerleri değiştirme.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TopicStudyLinkAuditAction>))]
public enum TopicStudyLinkAuditAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Activate = 3,
    Deactivate = 4,
    Reorder = 5
}

/// <summary>
/// Çalışma linki değişikliklerinin salt-ekleme denetim kaydı (issue #61 güvenlik incelemesi, MEDIUM-1). Link değişikliğiyle
/// AYNI SaveChanges/transaction içinde yazılır. Linkler doğrudan yayına girdiği (moderasyon yok) için kötü niyetli bir
/// URL değişikliğinin kim tarafından, ne zaman ve neden → neye yapıldığını izlemeye yarar. BaseEntity DEĞİL:
/// soft-delete / global filtre uygulanmaz, kayıt silinmez.
/// </summary>
public class TopicStudyLinkAudit
{
    [Key]
    public long Id { get; set; }

    public int LinkId { get; set; }

    [ForeignKey("LinkId")]
    public TopicStudyLink? Link { get; set; }

    public TopicStudyLinkAuditAction Action { get; set; }

    public int ActorUserId { get; set; }

    [MaxLength(50)]
    public string ActorRole { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    [MaxLength(TopicStudyLinkLimits.UrlMaxLength)]
    public string? OldUrl { get; set; }

    [MaxLength(TopicStudyLinkLimits.UrlMaxLength)]
    public string? NewUrl { get; set; }

    [MaxLength(TopicStudyLinkLimits.TitleMaxLength)]
    public string? OldTitle { get; set; }

    [MaxLength(TopicStudyLinkLimits.TitleMaxLength)]
    public string? NewTitle { get; set; }
}

/// <summary>
/// Çalışma linkinin kaynak tipi. DB'de int olarak saklanır; JSON'da "YouTube" / "Other" string'i olarak taşınır.
/// Değerleri değiştirme — mevcut satırlar int olarak kayıtlı.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TopicStudyLinkSourceType>))]
public enum TopicStudyLinkSourceType
{
    Other = 0,
    YouTube = 1
}

/// <summary>Çalışma linki iş kuralı sabitleri (issue #61). UI'daki limit/uzunluk kontrolleri bu değerlerle aynı olmalı.</summary>
public static class TopicStudyLinkLimits
{
    /// <summary>Bir konu veya alt konu için aynı anda en fazla bu kadar AKTİF link olabilir (pasifler sayılmaz).</summary>
    public const int MaxActiveLinksPerScope = 7;

    /// <summary>Bir konu veya alt konu için toplam (aktif + pasif, silinmemiş) link üst sınırı — pasif linklerle şişirmeyi önler.</summary>
    public const int MaxTotalLinksPerScope = 30;

    public const int TitleMaxLength = 200;

    public const int UrlMaxLength = 2048;
}
