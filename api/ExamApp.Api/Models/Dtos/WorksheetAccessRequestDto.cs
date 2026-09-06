using System;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Sahibin "gelen atama izni talepleri" ekranı için tek satır (issue #13).
/// </summary>
public class WorksheetAccessRequestDto
{
    public int Id { get; set; }
    public int WorksheetId { get; set; }
    public string WorksheetName { get; set; } = string.Empty;
    public int RequesterUserId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>Pending / Approved / Rejected.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreateTime { get; set; }
    public DateTime? DecisionAt { get; set; }
}
