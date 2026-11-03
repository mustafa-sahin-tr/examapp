using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Issue #690: hak sahibine göre toplu yayından kaldırma ve geri alma kaydı. CreateUserId işlemi yapan admindir.</summary>
public class QuestionSourceActionLog : BaseEntity
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string RightsHolder { get; set; } = string.Empty;

    /// <summary>true: yayından kaldırıldı; false: geri alındı.</summary>
    public bool Unpublished { get; set; }

    public int AffectedCount { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}
