using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public enum PracticeSessionStatus
{
    Active = 0,
    Ended = 1
}

/// <summary>
/// Issue #524: where a practice session (free or the daily set's) was started. Sent by the client, so it is a product
/// metric only (share of sessions started from the assistant); no permission or points decision depends on it.
/// </summary>
public enum PracticeStartedVia
{
    Ui = 0,
    Assistant = 1,
    DeepLink = 2,
    Notification = 3
}

public static class PracticeStartedVias
{
    /// <summary>Empty → <see cref="PracticeStartedVia.Ui"/>; only the defined names (case-insensitive), no numbers.</summary>
    public static bool TryParse(string? value, out PracticeStartedVia via)
    {
        via = PracticeStartedVia.Ui;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        foreach (var name in Enum.GetNames<PracticeStartedVia>())
        {
            if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                via = Enum.Parse<PracticeStartedVia>(name);
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// "Soru Çöz" pratik oturumu (issue #62). Worksheet'ten bağımsızdır; <see cref="WorksheetInstance"/>
/// zorunlu <c>WorksheetId</c> istediği için yeniden kullanılamadı. Seçilen kapsam (ders/konu) ve
/// oturum başındaki sınıf, raporlama için referans olarak saklanır.
/// </summary>
public class PracticeSession : BaseEntity
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = default!;

    /// <summary>Oturum açıldığında öğrencinin sınıfı; havuz filtresi buna göre hesaplanır.</summary>
    public int GradeId { get; set; }

    [ForeignKey(nameof(GradeId))]
    public Grade Grade { get; set; } = default!;

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public PracticeSessionStatus Status { get; set; } = PracticeSessionStatus.Active;

    /// <summary>Seçilen ders id'leri, JSON dizi (örn. "[1,3]"); null/boş = tüm dersler.</summary>
    public string? SubjectIdsJson { get; set; }

    /// <summary>Seçilen konu id'leri, JSON dizi; null/boş = konu filtresi yok.</summary>
    public string? TopicIdsJson { get; set; }

    /// <summary>Issue #500: alt konu kapsamı.</summary>
    public string? SubTopicIdsJson { get; set; }

    /// <summary>Issue #500: bu kadar soru gösterilince oturumun havuzu biter. Boşsa sınır yok.</summary>
    public int? TargetCount { get; set; }

    /// <summary>Issue #529: tekrar oturumunun sabit soru listesi (JSON, sıralı); serbest pratikte null.</summary>
    public string? FixedQuestionIdsJson { get; set; }

    /// <summary>Issue #524: start source (metric only).</summary>
    public PracticeStartedVia StartedVia { get; set; } = PracticeStartedVia.Ui;

    public ICollection<PracticeSessionQuestion> Questions { get; set; } = new List<PracticeSessionQuestion>();
}
