using System;
using System.Collections.Generic;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class TeacherWorksheetAssignmentsDto
{
    public int WorksheetId { get; set; }
    public string WorksheetName { get; set; } = string.Empty;

    /// <summary>issue #573: sayfa türü (0 = Test, 1 = Activity) — UI etkinlikte "Çalışma takibi" tablosunu gösterir.</summary>
    public WorksheetKind WorksheetKind { get; set; } = WorksheetKind.Test;
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
    public AssignmentProgressSummaryDto Summary { get; set; } = new();
    public List<TeacherWorksheetAssignmentDto> Assignments { get; set; } = new();
}

public class AssignmentProgressSummaryDto
{
    public int TotalAssignments { get; set; }
    public int ActiveAssignments { get; set; }
    public int UpcomingAssignments { get; set; }
    public int TotalStudents { get; set; }
    public int CompletedCount { get; set; }
    public int InProgressCount { get; set; }
    public int NotStartedCount { get; set; }
    public int ScheduledCount { get; set; }
    public int ExpiredCount { get; set; }
}

public class TeacherWorksheetAssignmentDto
{
    public int AssignmentId { get; set; }
    public string TargetType { get; set; } = string.Empty; // "Grade" | "Student"
    public string TargetName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    /// <summary>issue #105: atama bazlı yorum-soru override'ı; null = worksheet varsayılanı.</summary>
    public bool? CommentsEnabledOverride { get; set; }
    public int StudentCount { get; set; }
    public int CompletedCount { get; set; }
    public int InProgressCount { get; set; }
    public int NotStartedCount { get; set; }
    public int ScheduledCount { get; set; }
    public int ExpiredCount { get; set; }
    public List<TeacherAssignmentStudentDto> Students { get; set; } = new();
}

public class TeacherAssignmentStudentDto
{
    public int StudentId { get; set; }
    public int UserId { get; set; }
    public string StudentNumber { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public string? GradeName { get; set; }
    public string Status { get; set; } = AssignmentStudentStatuses.NotStarted;
    public int? InstanceId { get; set; }
    public DateTime? LastActivity { get; set; }

    /// <summary>issue #573: etkinlikte ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null.</summary>
    public int? FirstCheckCorrectCount { get; set; }

    /// <summary>issue #573: etkinlikte ilk kontroldeki toplam satır sayısı; kontrol yoksa / testte null.</summary>
    public int? FirstCheckTotalCount { get; set; }

    /// <summary>
    /// issue #609: öğrenci tamamlanmış etkinliği şu an yeniden çözüyor. <see cref="Status"/> yine Completed ve ilk kontrol özeti
    /// ilk denemeninkidir; UI "yeniden çözüyor" rozeti gösterebilir. Testte hep false.
    /// </summary>
    public bool Retaking { get; set; }

    /// <summary>issue #609: etkinlikte "Yeniden çöz" sayısı (0 = hiç); testte hep 0.</summary>
    public int RetakeCount { get; set; }
}

public static class AssignmentStudentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string NotStarted = "NotStarted";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Expired = "Expired";
}
