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
