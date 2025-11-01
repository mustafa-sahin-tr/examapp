using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

public class TeacherWorksheetAssignmentsDto
{
    public int WorksheetId { get; set; }
    public string WorksheetName { get; set; } = string.Empty;
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
}

public static class AssignmentStudentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string NotStarted = "NotStarted";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Expired = "Expired";
}
