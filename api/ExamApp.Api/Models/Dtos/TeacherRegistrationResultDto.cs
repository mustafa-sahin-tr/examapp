using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// <c>ITeacherService.Save</c> sonucu (issue #234). Controller profil önbelleğine ve Keycloak "school_id"
/// attribute'una istekteki değeri DEĞİL, burada dönen kayıtlı/onaylı <see cref="SchoolId"/>'yi yazar —
/// onaysız okul hiçbir yere taşınmaz.
/// </summary>
public class TeacherRegistrationResultDto : ResponseBaseDto
{
    /// <summary>Öğretmenin onaylı okulu (Teachers.SchoolId). Okul talebi onay bekliyorsa null.</summary>
    public int? SchoolId { get; set; }

    /// <summary>Onay bekleyen okul bağlantısı talebi (Teachers.RequestedSchoolId); yoksa null.</summary>
    public int? RequestedSchoolId { get; set; }

    public TeacherApprovalStatus ApprovalStatus { get; set; }

    /// <summary>Okul bağlantısı talebi admin onayı bekliyor mu (RequestedSchoolId dolu + Pending).</summary>
    public bool SchoolApprovalPending =>
        RequestedSchoolId.HasValue && ApprovalStatus == TeacherApprovalStatus.Pending;
}

