using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Issue #1113: haftalık plan görevinin türü; arayüz bağlantıyı türden kurar.</summary>
public enum StudyPlanTaskKind
{
    /// <summary>Süren ödev (<see cref="WeeklyStudyPlanTask.WorksheetId"/>).</summary>
    Assignment = 1,
    /// <summary>Çözülmemiş deneme (<see cref="WeeklyStudyPlanTask.MockExamId"/>).</summary>
    MockExam = 2,
    /// <summary>Zayıf konuda pratik (<see cref="WeeklyStudyPlanTask.TopicId"/>).</summary>
    PracticeTopic = 3,
    /// <summary>Yanlışlarım'ı yeniden çöz.</summary>
    Mistakes = 4,
    /// <summary>Bilgi kartlarını tekrar et.</summary>
    Flashcards = 5,
    /// <summary>Konu seçmeden pratik (veri azken).</summary>
    Practice = 6,
}

/// <summary>
/// Issue #1113 (epic #1118): öğrencinin haftalık çalışma planı (Pro, <c>weekly-plan</c>). Hafta (Pazartesi, Europe/Istanbul) başına tek
/// plan; Pazartesi işi ya da öğrencinin o haftaki ilk açılışı kurar, sonra değişmez (öğrenci yalnız görev işaretler).
/// </summary>
public class WeeklyStudyPlan
{
    [Key]
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateOnly WeekStart { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<WeeklyStudyPlanTask> Tasks { get; set; } = new();
}

public class WeeklyStudyPlanTask
{
    public const int MaxTitleLength = 200;

    [Key]
    public int Id { get; set; }

    public int PlanId { get; set; }
    public WeeklyStudyPlan Plan { get; set; } = null!;

    public int Order { get; set; }

    public StudyPlanTaskKind Kind { get; set; }

    /// <summary>Kurulduğu andaki ad (ödev / deneme adı, konu adı); sonradan değişse de plan aynı kalır.</summary>
    [MaxLength(MaxTitleLength)]
    public string? Title { get; set; }

    [MaxLength(MaxTitleLength)]
    public string? SubjectName { get; set; }

    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? WorksheetId { get; set; }
    public int? MockExamId { get; set; }

    public DateTime? DueAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
