using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>Admin onay panelinde listelenen bekleyen bağımsız öğretmen başvurusu (issue #94).</summary>
public class PendingTeacherApplicationDto
{
    public int TeacherId { get; set; }
    public int UserId { get; set; }

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Teacher kaydının oluşturulma zamanı (BaseEntity.CreateTime).</summary>
    public DateTime AppliedAt { get; set; }
}

public class TeacherRejectRequestDto
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Red nedeni boş olamaz.")]
    [MaxLength(500, ErrorMessage = "Red nedeni en fazla 500 karakter olabilir.")]
    public string Reason { get; set; } = string.Empty;
}
