using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public enum AskTeacherStatus
{
    /// <summary>Öğretmenin kuyruğunda; <see cref="AskTeacherQuestion.DueAt"/>'e kadar cevaplanabilir.</summary>
    Open = 1,
    /// <summary>Cevaplandı; öğrencinin aylık hakkından bir kullanım düştü.</summary>
    Answered = 2,
    /// <summary>Süre doldu, cevap yok; hak düşmedi (iade).</summary>
    Expired = 3,
    /// <summary>Öğrenci cevaplanmadan geri çekti; hak düşmedi.</summary>
    Cancelled = 4,
}

/// <summary>
/// Issue #1111 (epic #1118): öğrencinin çözemediği sorunun fotoğrafı, seçtiği öğretmene gider. Öğretmen süre dolmadan yazılı
/// (isteğe bağlı görselle) cevaplar. Hak (<c>ask-teacher</c>) yalnız cevapta düşer; gönderimde açık sorular da sınırdan sayılır.
/// Görseller MinIO'da özel nesnelerdir, yalnız soru sahibi öğrenci ve öğretmen API ucundan okur.
/// </summary>
public class AskTeacherQuestion : BaseEntity
{
    public const int MaxNoteLength = 1000;
    public const int MaxAnswerLength = 4000;

    [Key]
    public int Id { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    /// <summary>Öğrencinin user id'si (hak ve bildirim).</summary>
    public int StudentUserId { get; set; }

    /// <summary>Teachers.Id.</summary>
    public int TeacherId { get; set; }
    public Teacher Teacher { get; set; } = default!;

    public int TeacherUserId { get; set; }

    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    [MaxLength(MaxNoteLength)]
    public string? Note { get; set; }

    /// <summary>Soru görseli (<c>/img/{bucket}/ask-teacher/...</c>; yönü düzeltilmiş JPEG).</summary>
    [MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    public AskTeacherStatus Status { get; set; } = AskTeacherStatus.Open;

    public DateTime CreatedAt { get; set; }

    /// <summary>Bu ana kadar cevaplanmazsa süre dolar (<see cref="AskTeacherStatus.Expired"/>), hak düşmez.</summary>
    public DateTime DueAt { get; set; }

    public DateTime? AnsweredAt { get; set; }

    /// <summary>Cevaplanmadan kapandığı an (süre doldu ya da geri çekildi).</summary>
    public DateTime? ClosedAt { get; set; }

    [MaxLength(MaxAnswerLength)]
    public string? AnswerText { get; set; }

    [MaxLength(500)]
    public string? AnswerImageUrl { get; set; }

    /// <summary>Öğrencinin cevaba verdiği puan (1–5).</summary>
    public int? Rating { get; set; }
}
