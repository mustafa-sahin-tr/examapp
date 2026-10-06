using ExamApp.Api.Services.Storage;
using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class AssignedWorksheetDto
{
    public int AssignmentId { get; set; }
    public int WorksheetId { get; set; }

    // Worksheet metadata (aligned with WorksheetDto)
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }
    public string? Subtitle { get; set; }
    [StorageUrl(StorageArea.WorksheetCover)]
    public string? ImageUrl { get; set; }
    public string? BadgeText { get; set; }
    public int? BookTestId { get; set; }
    public int? BookId { get; set; }
    public int QuestionCount { get; set; }

    // Assignment metadata
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsGradeAssignment { get; set; }
    public int? AssignedGradeId { get; set; }

    // issue #105: atama override'ı (null = worksheet varsayılanı) ve öğrenci için etkin değer
    // (CommentsEnabledOverride ?? Worksheet.CommentsEnabled).
    public bool? CommentsEnabledOverride { get; set; }
    public bool CommentsEnabled { get; set; } = true;

    // Student instance metadata
    public int? InstanceId { get; set; }
    public WorksheetInstanceStatus? InstanceStatus { get; set; }
    public DateTime? InstanceStartTime { get; set; }
    public DateTime? InstanceEndTime { get; set; }
    public string AssignmentStatus { get; set; } = "NotStarted";

    public bool HasStarted => InstanceStatus.HasValue;
    public bool IsCompleted => InstanceStatus == WorksheetInstanceStatus.Completed;

    /// <summary>
    /// issue #396: oturum bitti mi (Completed ya da süresi dolmuş Expired) — devam/tekrar yok; UI teslim tarihi uyarısını
    /// ve "devam ediyor" görünümünü buna göre kapatır. <see cref="IsCompleted"/> yalnız puanlı tamamlanmayı söyler.
    /// </summary>
    public bool IsFinished => ExamApp.Api.Helpers.WorksheetInstanceStatusRules.IsFinished(InstanceStatus);
}
