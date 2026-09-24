using System;
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

    /// <summary>issue #287: öğretmen hesabı onaylı mı (Teachers.AccountApprovedAt dolu). Yeni kayıtta her zaman false.</summary>
    public bool AccountApproved { get; set; }

    /// <summary>
    /// issue #277 (madde 2): reddedilen öğretmenin yeni okul talebi bekleme süresine takıldı → controller 429 +
    /// <c>Retry-After</c> döner. <see cref="RetryAfterUtc"/> yeni talebin açılabileceği an.
    /// </summary>
    public bool TooManyRequests { get; set; }

    /// <summary>issue #277 (madde 2): <see cref="TooManyRequests"/> ise yeni okul talebinin açılabileceği en erken an (UTC).</summary>
    public DateTime? RetryAfterUtc { get; set; }

    /// <summary>Okul bağlantısı talebi admin onayı bekliyor mu (RequestedSchoolId dolu + Pending).</summary>
    public bool SchoolApprovalPending =>
        RequestedSchoolId.HasValue && ApprovalStatus == TeacherApprovalStatus.Pending;
}

