using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetAssignmentRequestDto
{
    [Required]
    public int WorksheetId { get; set; }

    public int? StudentId { get; set; }

    public int? GradeId { get; set; }

    [Required]
    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    /// <summary>issue #105: bu atamaya özel yorum-soru açık/kapalı. null = worksheet varsayılanı.</summary>
    public bool? CommentsEnabledOverride { get; set; }
}
