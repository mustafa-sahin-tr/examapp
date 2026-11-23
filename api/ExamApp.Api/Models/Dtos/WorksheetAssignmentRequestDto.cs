using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetAssignmentRequestDto
{
    [Required]
    public int WorksheetId { get; set; }

    public int? StudentId { get; set; }

    public int? GradeId { get; set; }

    /// <summary>issue #1296: şube hedefi (öğrenci ve sınıfla birlikte verilmez); atamanın sınıfı ve okulu şubeden gelir.</summary>
    public int? SectionId { get; set; }

    [Required]
    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    /// <summary>issue #105: bu atamaya özel yorum-soru açık/kapalı. null = worksheet varsayılanı.</summary>
    public bool? CommentsEnabledOverride { get; set; }

    /// <summary>issue #1283: ödev olarak ver (teslim tarihi zorunlu; platform geneli atamada olmaz).</summary>
    public bool IsHomework { get; set; }
}
