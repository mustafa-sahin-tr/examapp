using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #419: tek kullanımlık veli davet kodu. Issue #436'dan beri yalnızca öğrencinin BİRİNCİL velisi üretir (ikinci veli
/// daveti, <see cref="CreatedByParentId"/>); <see cref="StudentId"/> kodun bağlayacağı çocuk. Kodun DÜZ METNİ hiçbir yerde saklanmaz
/// (yalnızca üretim yanıtında bir kez döner); <see cref="CodeHash"/> = HMAC-SHA256(sunucu pepper'ı, normalize kod), hex.
/// Öğrenci başına aynı anda tek geçerli (kullanılmamış + süresi dolmamış) kod vardır: yeni kod üretmek öncekilerin
/// <see cref="ExpiresAt"/>'ini "şimdi"ye çeker. Kullanılan kodda <see cref="UsedAt"/>/<see cref="UsedByParentId"/> dolar.
/// </summary>
public class ParentInviteCode
{
    /// <summary>HMAC-SHA256 hex uzunluğu.</summary>
    public const int CodeHashLength = 64;

    [Key]
    public int Id { get; set; }

    public int StudentId { get; set; }

    public Student Student { get; set; } = null!;

    [Required, MaxLength(CodeHashLength)]
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Geçerlilik sonu (UTC). Yeni kod üretilince ya da kullanılınca geçersiz sayılır.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Kullanıldığı an (UTC); kullanılmamışsa null.</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>Kodu kullanan veli (Parents.Id).</summary>
    public int? UsedByParentId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Issue #436: kodu üreten BİRİNCİL veli (Parents.Id) — "ikinci veli davet kodu". Null = #419 öğrenci kodu (artık kullanılamaz).
    /// </summary>
    public int? CreatedByParentId { get; set; }
}
