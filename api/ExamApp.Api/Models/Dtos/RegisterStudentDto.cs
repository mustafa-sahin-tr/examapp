using System.ComponentModel.DataAnnotations;

public class RegisterStudentDto
{
    [Required]
    [MaxLength(50)]
    public string StudentNumber { get; set; }

    public int? SchoolId { get; set; }

    [Required]
    public int GradeId { get; set; }

    /// <summary>
    /// Issue #440: veli adı. Veli kuralına tabi (<c>ParentRequirement</c>) ve Active velisi / açık daveti olmayan öğrencide ZORUNLU
    /// (e-posta ve telefonla birlikte); eksik/geçersizse kayıt tamamlanmaz (400). Diğer öğrencilerde yok sayılır.
    /// </summary>
    [MaxLength(200)]
    public string? ParentFullName { get; set; }

    /// <summary>Issue #440: veli e-postası (biçim doğrulanır, küçük harfe çevrilir).</summary>
    [MaxLength(320)]
    public string? ParentEmail { get; set; }

    /// <summary>Issue #440: veli cep telefonu (TR: 05xxxxxxxxx, 5xxxxxxxxx, +905xxxxxxxxx; boşluk serbest) — yalnız saklanır.</summary>
    [MaxLength(32)]
    public string? ParentPhone { get; set; }
}
