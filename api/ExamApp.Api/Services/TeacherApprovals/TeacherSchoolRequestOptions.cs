using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Services.TeacherApprovals;

/// <summary>
/// issue #277 (madde 2): <c>TeacherApprovals</c> ayarları. Reddedilen öğretmen, son ret anından
/// (<see cref="Data.Teacher.LastRejectedAt"/>) itibaren <see cref="SchoolRequestCooldownHours"/> saat dolmadan yeni okul
/// bağlantısı talebi açamaz (<c>POST api/teacher/register</c> → 429 + <c>Retry-After</c>). 0 = bekleme yok.
/// </summary>
public sealed class TeacherSchoolRequestOptions
{
    public const string SectionName = "TeacherApprovals";

    /// <summary>Retten sonra yeni okul talebi için bekleme süresi (saat). Varsayılan 24.</summary>
    [Range(0, 24 * 365)]
    public int SchoolRequestCooldownHours { get; set; } = 24;
}
