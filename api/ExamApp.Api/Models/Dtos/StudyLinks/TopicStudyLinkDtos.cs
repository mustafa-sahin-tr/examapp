using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.StudyLinks;

// Konu / alt konu çalışma linkleri (issue #61). Gateway üzerinden /api/exam/study-links/... ile erişilir.
// sourceType JSON'da "YouTube" | "Other" string'i olarak taşınır (TopicStudyLinkSourceType üzerindeki converter).

/// <summary>
/// GET /api/study-links — yönetici (Admin/Teacher) listesi. <see cref="TopicId"/> veya <see cref="SubTopicId"/>'den
/// TAM OLARAK biri verilmelidir. TopicId → yalnızca konu seviyesi linkler (SubTopicId boş olanlar);
/// SubTopicId → o alt konunun linkleri.
/// </summary>
public class TopicStudyLinkQueryDto
{
    public int? TopicId { get; set; }

    public int? SubTopicId { get; set; }

    /// <summary>Pasif linkler de dönsün mü? Yönetim ekranı için varsayılan true.</summary>
    public bool IncludeInactive { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int Skip { get; set; } = 0;

    /// <summary>Sayfa boyutu; 1..100, varsayılan 50.</summary>
    [Range(1, 100)]
    public int Take { get; set; } = 50;
}

/// <summary>POST /api/study-links — yeni çalışma linki.</summary>
public class CreateTopicStudyLinkDto
{
    /// <summary>Konu seviyesi link için. SubTopicId ile birlikte verilirse alt konunun üst konusuyla eşleşmelidir.</summary>
    public int? TopicId { get; set; }

    /// <summary>Alt konu seviyesi link için. TopicId veya SubTopicId'den en az biri zorunlu.</summary>
    public int? SubTopicId { get; set; }

    [Required(ErrorMessage = "studyLinks.titleRequired")]
    [MaxLength(TopicStudyLinkLimits.TitleMaxLength, ErrorMessage = "studyLinks.titleTooLong")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "studyLinks.urlRequired")]
    [MaxLength(TopicStudyLinkLimits.UrlMaxLength, ErrorMessage = "studyLinks.urlTooLong")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Boş bırakılırsa URL'den çıkarılır (youtube.com / youtu.be → YouTube, diğerleri → Other).</summary>
    public TopicStudyLinkSourceType? SourceType { get; set; }

    /// <summary>Boş bırakılırsa kapsamdaki en büyük SortOrder + 1.</summary>
    [Range(0, 10_000)]
    public int? SortOrder { get; set; }

    /// <summary>Varsayılan true (moderasyon yok — doğrudan yayında). Aktif eklemede 7-aktif-link limiti uygulanır.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// PUT /api/study-links/{id} — linki günceller. Title/Url zorunlu (tam değiştirme). SourceType boşsa URL'den
/// yeniden çıkarılır; SortOrder / IsActive boşsa mevcut değer korunur. Konu/alt konu değiştirilemez.
/// </summary>
public class UpdateTopicStudyLinkDto
{
    [Required(ErrorMessage = "studyLinks.titleRequired")]
    [MaxLength(TopicStudyLinkLimits.TitleMaxLength, ErrorMessage = "studyLinks.titleTooLong")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "studyLinks.urlRequired")]
    [MaxLength(TopicStudyLinkLimits.UrlMaxLength, ErrorMessage = "studyLinks.urlTooLong")]
    public string Url { get; set; } = string.Empty;

    public TopicStudyLinkSourceType? SourceType { get; set; }

    [Range(0, 10_000)]
    public int? SortOrder { get; set; }

    /// <summary>Pasif → aktif geçişinde 7-aktif-link limiti uygulanır.</summary>
    public bool? IsActive { get; set; }
}

/// <summary>
/// PUT /api/study-links/reorder — bir kapsamdaki linklerin sırasını toplu günceller.
/// TopicId veya SubTopicId'den TAM OLARAK biri verilir; tüm <see cref="Items"/> o kapsama ait olmalıdır.
/// Listede olmayan linklerin sırası değişmez.
/// </summary>
public class ReorderTopicStudyLinksDto
{
    public int? TopicId { get; set; }

    public int? SubTopicId { get; set; }

    [Required(ErrorMessage = "studyLinks.reorder.itemsRequired")]
    [MinLength(1, ErrorMessage = "studyLinks.reorder.itemsRequired")]
    [MaxLength(100, ErrorMessage = "studyLinks.reorder.tooManyItems")]
    public List<TopicStudyLinkOrderItemDto> Items { get; set; } = new();
}

public class TopicStudyLinkOrderItemDto
{
    public int Id { get; set; }

    [Range(0, 10_000)]
    public int SortOrder { get; set; }
}

/// <summary>Yönetici görünümü (Admin/Teacher).</summary>
public class TopicStudyLinkDto
{
    public int Id { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public TopicStudyLinkSourceType SourceType { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedByRole { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; }

    /// <summary>Son değiştiren kullanıcı (BaseEntity.UpdateUserId); hiç değişmediyse null.</summary>
    public int? UpdatedByUserId { get; set; }

    public string? UpdatedByName { get; set; }

    public DateTime? UpdateTime { get; set; }
}

/// <summary>Yönetim uçlarının ortak yanıt tabanı: ResponseBaseDto + makine tarafından okunabilir hata kodu.</summary>
public abstract class StudyLinkResponseDto : ResponseBaseDto
{
    /// <summary>Hata durumunda UI özel davranışı için kod — bkz. <see cref="TopicStudyLinkErrorCodes"/>. Başarıda null.</summary>
    public string? ErrorCode { get; set; }
}

/// <summary>Servisten controller'a tekil link sonucu; ResponseBaseDto bayrakları HTTP koduna eşlenir.</summary>
public class TopicStudyLinkResultDto : StudyLinkResponseDto
{
    public TopicStudyLinkDto? Link { get; set; }
}

/// <summary>Yönetici liste sonucu. <see cref="ActiveCount"/> / <see cref="MaxActiveLinks"/> UI'da "ekle" butonunu kapatmak için.</summary>
public class TopicStudyLinkListResultDto : StudyLinkResponseDto
{
    public List<TopicStudyLinkDto> Items { get; set; } = new();

    /// <summary>Filtreden bağımsız, kapsamdaki toplam kayıt sayısı (IncludeInactive'e göre).</summary>
    public int TotalCount { get; set; }

    /// <summary>Kapsamdaki aktif link sayısı (sayfalamadan bağımsız).</summary>
    public int ActiveCount { get; set; }

    public int MaxActiveLinks { get; set; } = TopicStudyLinkLimits.MaxActiveLinksPerScope;
}

public static class TopicStudyLinkErrorCodes
{
    /// <summary>409 — kapsamda zaten 7 aktif link var.</summary>
    public const string ActiveLimitReached = "ActiveLimitReached";

    /// <summary>409 — kapsamda zaten 30 (aktif + pasif) link var.</summary>
    public const string TotalLimitReached = "TotalLimitReached";

    /// <summary>403 — öğretmen kaydı yok veya onaylı değil.</summary>
    public const string TeacherNotApproved = "TeacherNotApproved";

    /// <summary>403 — öğretmen başkasının linkini güncellemeye/silmeye çalıştı.</summary>
    public const string NotOwner = "NotOwner";
}

// ---- Öğrenci sonuç ekranı (GET /api/study-links/for-result/{testInstanceId}) ----

/// <summary>Öğrencinin YANLIŞ cevapladığı bir soru için alt konu bazlı link grupları.</summary>
public class QuestionStudyLinkSuggestionDto
{
    public int QuestionId { get; set; }

    /// <summary>WorksheetInstanceQuestion kimliği (sonuç ekranındaki test-instance-question ile eşleştirme için).</summary>
    public int TestInstanceQuestionId { get; set; }

    /// <summary>Önce alt konu grupları (Kind=SubTopic), ardından konu seviyesi yedek gruplar (Kind=Topic).</summary>
    public List<StudyLinkGroupDto> Groups { get; set; } = new();
}

/// <summary>Grup türü — JSON'da "SubTopic" | "Topic".</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<StudyLinkGroupKind>))]
public enum StudyLinkGroupKind
{
    SubTopic = 0,
    Topic = 1
}

/// <summary>
/// Link grubu. Kind=SubTopic → SubTopicId dolu (TopicId alt konunun üst konusu); Kind=Topic → yalnızca TopicId dolu,
/// grup konunun konu seviyesi (SubTopicId boş) linklerini taşır. <see cref="Name"/> alt konu veya konu adıdır.
/// </summary>
public class StudyLinkGroupDto
{
    public StudyLinkGroupKind Kind { get; set; }
    public int? SubTopicId { get; set; }
    public int? TopicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<StudyLinkSummaryDto> Links { get; set; } = new();
}

/// <summary>Öğrenciye dönen yalın link görünümü (yalnızca aktif linkler).</summary>
public class StudyLinkSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public TopicStudyLinkSourceType SourceType { get; set; }
}

public class StudyLinkSuggestionsResultDto : ResponseBaseDto
{
    public List<QuestionStudyLinkSuggestionDto> Items { get; set; } = new();
}
