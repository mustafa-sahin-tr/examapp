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

    public ICollection<PracticeSessionQuestion> Questions { get; set; } = new List<PracticeSessionQuestion>();
}
