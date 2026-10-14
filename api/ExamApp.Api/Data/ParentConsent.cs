using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #438 (epic #435): velinin, çocuk hesabını açarken verdiği açık rıza (KVKK aydınlatma/rıza metni sürümü). Hesap açma ile
/// AYNI transaction'da yazılır (rıza kaydı olmadan çocuk hesabı oluşmaz). Append-only: düzenlenmez, silinmez. FK'lar
/// ClientNoAction — veli/öğrenci soft-delete edilir, rıza izi kalır.
/// </summary>
public class ParentConsent
{
    [Key]
    public int Id { get; set; }

    /// <summary>Rızayı veren velinin <see cref="Parent.Id"/>'si.</summary>
    public int ParentId { get; set; }

    public Parent Parent { get; set; } = null!;

    /// <summary>Rızanın verildiği (velinin açtığı) öğrencinin <see cref="Student.Id"/>'si.</summary>
    public int StudentId { get; set; }

    public Student Student { get; set; } = null!;

    /// <summary>Velinin onayladığı rıza metninin sürümü (<c>ParentConsentVersions</c>).</summary>
    [Required, MaxLength(32)]
    public string Version { get; set; } = string.Empty;

    /// <summary>Onay anı (UTC).</summary>
    public DateTime AcceptedAt { get; set; }

    /// <summary>
    /// İsteğin geldiği istemci IP'si (gateway'in üzerine yazdığı <c>X-Forwarded-For</c>, yoksa bağlantı adresi). IPv6 dahil en
    /// fazla 45 karakter; çözülemezse null.
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }
}
