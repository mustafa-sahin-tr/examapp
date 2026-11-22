namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>PUT api/admin/teachers/{id}/school</c> gövdesi (issue #313): <c>{ "schoolId": 12 }</c>. Alan zorunludur — eksik/null
/// ise controller yerelleştirilmiş <c>{ message }</c> ile 400 döner (öğretmeni okulsuz bırakmak bu uçla yapılmaz).
/// <c>[Required]</c> bilinçli olarak yok (<see cref="AdminStudentSchoolRequestDto"/> ile aynı gerekçe).
/// </summary>
public sealed class AdminTeacherSchoolRequestDto
{
    public int? SchoolId { get; init; }
}

/// <summary>Başarılı yanıt: öğretmenin yeni okulu ve değişiklik olup olmadığı (aynı okul → <c>changed = false</c>).</summary>
public sealed class AdminTeacherSchoolResponseDto
{
    /// <summary>Teacher.Id.</summary>
    public int TeacherId { get; init; }

    /// <summary>Öğretmenin güncel okulu (Teachers.SchoolId).</summary>
    public int SchoolId { get; init; }

    /// <summary>Değişiklikten önceki okul; okulsuzdu ise null.</summary>
    public int? PreviousSchoolId { get; init; }

    /// <summary>false → öğretmen zaten bu okuldaydı (idempotent, yan etki ve audit yok).</summary>
    public bool Changed { get; init; }

    /// <summary>
    /// issue #313 review (O1): true → okul değişti ama öğretmenin profil önbelleği denemelere rağmen düşürülemedi; öğretmen
    /// en geç 1 saat eski okulun kapsamında görünebilir (audit outcome <c>SucceededCacheStale</c>). Normalde false.
    /// </summary>
    public bool ProfileCacheStale { get; init; }

    /// <summary>issue #539: okul değişince <c>SchoolOnly</c>'dan <c>Private</c>'a alınan sınav sayısı (değişmediyse 0).</summary>
    public int PrivatizedWorksheetCount { get; init; }
}

/// <summary><c>GET api/admin/teachers/{id}/school-change-impact</c> yanıtı (issue #539).</summary>
public sealed class AdminTeacherSchoolChangeImpactDto
{
    /// <summary>Okul değişirse <c>Private</c> yapılacak (silinmemiş) <c>SchoolOnly</c> sınav sayısı.</summary>
    public int SchoolOnlyWorksheetCount { get; init; }
}

/// <summary>issue #1276: <c>PUT api/admin/teachers/{id}/school-admin</c> gövdesi. Alan eksikse 400.</summary>
public sealed class AdminTeacherSchoolAdminRequestDto
{
    public bool? IsSchoolAdmin { get; init; }
}

/// <summary>issue #1276: okul yöneticisi yetkisi yanıtı (aynı değer → <c>changed = false</c>).</summary>
public sealed class AdminTeacherSchoolAdminResponseDto
{
    public int TeacherId { get; init; }
    public bool IsSchoolAdmin { get; init; }
    public int? SchoolId { get; init; }
    public bool Changed { get; init; }
}

/// <summary>
/// issue #1277 / #1278: okul yöneticisi paneli özeti (<c>GET api/school-admin/summary</c>; menü rozeti + okul paneli kartları).
/// Sayılar yalnız yöneticinin okulu: öğretmen = okula bağlı, hesabı onaylı, bağımsız olmayan (askıdakiler dahil);
/// öğrenci = üyeliği doğrulanmış; bekleyen öğrenci = okulu seçip onay bekleyen.
/// </summary>
public sealed class SchoolAdminSummaryDto
{
    public int SchoolId { get; init; }
    public string SchoolName { get; init; } = string.Empty;
    public int TeacherCount { get; init; }
    public int StudentCount { get; init; }
    public int PendingTeacherApplications { get; init; }
    public int PendingStudentRequests { get; init; }
}

/// <summary>issue #1278: <c>GET api/school-admin/teachers</c> satırı (okulun öğretmenleri; salt okunur).</summary>
public sealed class SchoolAdminTeacherDto
{
    public int TeacherId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public bool IsSchoolAdmin { get; init; }
    public bool IsSuspended { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public IReadOnlyList<string> Subjects { get; init; } = [];
}

/// <summary>issue #1278: <c>GET api/school-admin/students</c> satırı (doğrulanmış üyeler; numara maskeli).</summary>
public sealed class SchoolAdminStudentDto
{
    public int StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string StudentNumber { get; init; } = string.Empty;
    public int? GradeId { get; init; }
    public string? GradeName { get; init; }
    public DateTime? VerifiedAt { get; init; }
    /// <summary>issue #1279: aktif şube (yoksa null).</summary>
    public int? SectionId { get; init; }
    public string? SectionName { get; init; }
}
