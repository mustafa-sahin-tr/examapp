using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>Admin onay panelinde listelenen bekleyen öğretmen başvurusu: bağımsız öğretmen (issue #94) ya da okul bağlantısı talebi (issue #234).</summary>
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

    /// <summary>
    /// issue #234: başvuru türü. true → bağımsız öğretmen başvurusu (#94); false → okul bağlantısı talebi
    /// (<see cref="RequestedSchoolId"/> dolu). Onay/red aynı uçlardan yapılır.
    /// </summary>
    public bool IsIndependentTutor { get; set; }

    /// <summary>issue #234: onay bekleyen okul bağlantısı talebinin okulu; bağımsız başvuruda null.</summary>
    public int? RequestedSchoolId { get; set; }

    /// <summary>issue #234: talep edilen okulun adı; bağımsız başvuruda null.</summary>
    public string? RequestedSchoolName { get; set; }
}

public class TeacherRejectRequestDto
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "admin.teacherApplication.rejectReasonRequired")]
    [MaxLength(500, ErrorMessage = "admin.teacherApplication.rejectReasonMaxLength")]
    public string Reason { get; set; } = string.Empty;
}
