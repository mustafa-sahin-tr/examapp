using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class CoachStudentDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? GradeId { get; set; }

    /// <summary>School: öğretmenin kendi okulundan. PrivateLesson: onaylı özel ders.</summary>
    public string Relationship { get; set; } = string.Empty;
}

public class SaveCoachGoalDto
{
    [Required, MaxLength(CoachWeeklyGoal.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Haftanın Pazartesi'si; boşsa bu hafta. Güncellemede yok sayılır.</summary>
    public DateOnly? WeekStart { get; set; }
}

public class CoachGoalDto
{
    public int Id { get; set; }
    public DateOnly WeekStart { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class SaveCoachNoteDto
{
    [Required, MaxLength(CoachNote.MaxBodyLength)]
    public string Body { get; set; } = string.Empty;
}

public class CoachNoteDto
{
    public int Id { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Haftanın planı: "Planım"daki testler ve varsa kişisel programın maddeleri.</summary>
public class CoachPlanCompletionDto
{
    public DateOnly WeekStart { get; set; }
    public int Planned { get; set; }
    public int Completed { get; set; }

    /// <summary>Kişisel program okunamadıysa false: sayılar yalnız "Planım" testlerini içerir.</summary>
    public bool IncludesProgram { get; set; }
}

/// <summary>Soru ve cevap içeriği taşımaz: yalnız sayılar ve konu adları.</summary>
public class CoachOverviewDto
{
    public int StudentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public CoachPlanCompletionDto Plan { get; set; } = new();
    public List<MockExamResultDto> RecentMockExams { get; set; } = [];
    public List<TopicPerformanceItemDto> WeakTopics { get; set; } = [];
    public int ActiveMistakeCount { get; set; }

    /// <summary>Yalnız bu öğretmenin bu haftaki hedefleri.</summary>
    public List<CoachGoalDto> Goals { get; set; } = [];
}

public class StudentCoachGoalDto
{
    public DateOnly WeekStart { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CoachName { get; set; }
}

public class ParentCoachGoalDto
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>Veli yanıtı: notlar ve öğretmen kimliği yok.</summary>
public class ParentCoachSummaryDto
{
    public DateOnly WeekStart { get; set; }
    public int PlannedCount { get; set; }
    public int CompletedCount { get; set; }
    public List<ParentCoachGoalDto> Goals { get; set; } = [];
}
