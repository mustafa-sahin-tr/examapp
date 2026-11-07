using System;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #700: koçun öğrenci için yazdığı haftalık hedef; öğrenci ve velisi görür. <see cref="BaseEntity"/>'den türemez:
/// silinen hedef ve not çocuk verisidir, soft-delete ile saklanmaz.
/// </summary>
public class CoachWeeklyGoal
{
    public const int MaxTitleLength = 300;

    /// <summary>Temizleyici <c>&amp;</c> ve <c>&lt;</c>'yi kodlar: sınır kodlanmış metne de uygulanır.</summary>
    public const int MaxStoredTitleLength = MaxTitleLength * 2;

    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public int TeacherUserId { get; set; }

    /// <summary>Haftanın Pazartesi'si (yerel takvim).</summary>
    public DateOnly WeekStart { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Issue #893: öğrencinin hedefi "yaptım" diye işaretlediği an (UTC); işaretlemediyse null.</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Issue #700: koçun sonraki ders için özel notu. Yalnız yazan öğretmen okur; öğrenci ve veli yanıtlarına girmez.</summary>
public class CoachNote
{
    public const int MaxBodyLength = 2000;
    public const int MaxStoredBodyLength = MaxBodyLength * 2;

    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public int TeacherUserId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
