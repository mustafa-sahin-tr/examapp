using System;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1111: öğrencinin soru gönderebileceği öğretmen (ilişkili ya da randevulu).</summary>
public class AskTeacherTeacherDto
{
    public int TeacherId { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary>Öğretmenin bu dersi verdiği biliniyor (ders seçildiyse önce gelir).</summary>
    public bool TeachesSubject { get; set; }
}

/// <summary>Soru: öğrenciye ve öğretmene aynı biçim; görseller <c>HasImage</c> / <c>HasAnswerImage</c> ile API ucundan okunur.</summary>
public class AskTeacherQuestionDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    /// <summary>Öğretmen tarafında öğrencinin kısa adı ("Ad S."); öğrenci tarafında boş.</summary>
    public string StudentName { get; set; } = string.Empty;
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public string? AnswerText { get; set; }
    public bool HasAnswerImage { get; set; }
    public int? Rating { get; set; }
}

/// <summary>Bu ayki hak: <c>Limit</c> null sınırsız, 0 planda yok; <c>Pending</c> cevap bekleyen sorular (kalandan düşer).</summary>
public class AskTeacherQuotaDto
{
    public int? Limit { get; set; }
    public int Used { get; set; }
    public int Pending { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
    public string? PlanKey { get; set; }
}
