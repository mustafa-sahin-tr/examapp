using System;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1115 (epic #1118): öğrencilerinden en az birinin ücretli planı olan öğretmen Teacher Essential'ı ücretsiz kullanır. Satır
/// sponsorluğun kaydıdır (denetim izi): günlük <c>TeacherSponsorshipJob</c> açar, uygunluk düşünce <see cref="GraceEndsAtUtc"/>'yi
/// yazar, yeniden uygun olursa temizler, grace bitince <see cref="EndedAtUtc"/> ile kapatır; yeniden uygun olan yeni satır alır.
/// Kimseye plan ataması yazılmaz: plan çözümü (<c>SchoolLicensePlans.ForUsersAsync</c>) okul lisansı gibi bu satıra bakar; ücretli
/// kişisel plan üst kademedeyse (Pro) o kazanır.
/// </summary>
public class TeacherSponsorship : BaseEntity
{
    public int Id { get; set; }

    public int TeacherUserId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Son günlük değerlendirmede uygun bulunduğu an.</summary>
    public DateTime LastQualifiedAtUtc { get; set; }

    /// <summary>Uygun öğrenci kalmayınca: bu ana kadar plan sürer (yeniden uygun olursa null).</summary>
    public DateTime? GraceEndsAtUtc { get; set; }

    /// <summary>Grace bitti, sponsorluk kapandı.</summary>
    public DateTime? EndedAtUtc { get; set; }
}
