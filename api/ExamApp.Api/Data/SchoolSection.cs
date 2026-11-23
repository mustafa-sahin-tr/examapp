using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #1279 (#1071 S4): okulun bir sınıf seviyesindeki şubesi (ör. 8. sınıf "A"). Okul yöneticisi oluşturur / adlandırır /
/// siler (soft delete; silinince aktif üyelikler <see cref="SchoolSectionEndReason.SectionDeleted"/> ile kapanır). Ad okul +
/// sınıf içinde büyük/küçük harf duyarsız tekildir (<see cref="NormalizedName"/>, tr-TR büyük harf; silinmişler hariç).
/// </summary>
public class SchoolSection : BaseEntity
{
    public const int NameMaxLength = 20;

    [Key]
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School School { get; set; } = null!;

    public int GradeId { get; set; }
    public Grade Grade { get; set; } = null!;

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Tekillik anahtarı: kırpılmış ad, tr-TR büyük harf (servis yazar).</summary>
    [MaxLength(NameMaxLength)]
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>issue #1296: üyelikler (geçmiş dahil); şube hedefli atama görünürlüğü aktif üyeliğe bakar.</summary>
    public ICollection<SchoolSectionMembership> Memberships { get; set; } = new List<SchoolSectionMembership>();
}

/// <summary>Şube üyeliğinin bitiş nedeni (string saklanır).</summary>
public enum SchoolSectionEndReason
{
    /// <summary>Okul yöneticisi öğrenciyi başka şubeye taşıdı.</summary>
    Moved,
    /// <summary>Okul yöneticisi öğrenciyi şubeden çıkardı.</summary>
    Removed,
    /// <summary>Şube silindi.</summary>
    SectionDeleted,
    /// <summary>Öğrencinin okulu değişti / okulsuz yapıldı (admin, <c>AdminStudentSchoolService</c>).</summary>
    SchoolChanged
}

/// <summary>
/// issue #1279: öğrencinin şube üyeliği, geçmişiyle. Aktif üyelik <see cref="EndedAt"/> boş olandır; öğrenci başına en çok bir
/// aktif üyelik (kısmi tekil indeks). Kapanan satır silinmez (geçmiş).
/// </summary>
public class SchoolSectionMembership
{
    [Key]
    public int Id { get; set; }

    public int SectionId { get; set; }
    public SchoolSection Section { get; set; } = null!;

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public int? StartedByUserId { get; set; }

    public DateTime? EndedAt { get; set; }
    public int? EndedByUserId { get; set; }
    public SchoolSectionEndReason? EndReason { get; set; }
}
