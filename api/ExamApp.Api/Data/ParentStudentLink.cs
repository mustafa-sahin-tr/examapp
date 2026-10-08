using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Veli–öğrenci bağlantı durumu (issue #419). String saklanır.</summary>
public enum ParentStudentLinkStatus
{
    /// <summary>
    /// Onay bekliyor; hiçbir erişim vermez. Issue #436: ikinci veli kodu girince (<see cref="ParentStudentLinkOrigin.InviteCode"/>)
    /// BİRİNCİL VELİNİN onayını bekler (<c>ParentLinkRules.PendingValidity</c>); yalnız geçiş dönemindeki
    /// <see cref="ParentStudentLinkOrigin.LegacyV1"/> istekleri öğrencinin onayını bekler (<c>ParentLinkRules.LegacyPendingValidity</c>).
    /// </summary>
    Pending = 0,
    Active = 1,
    Revoked = 2
}

/// <summary>
/// Issue #436 (epic #435): bağlantının nasıl kurulduğu. String saklanır (yeniden adlandırma geçmiş satırları bozar).
/// </summary>
public enum ParentStudentLinkOrigin
{
    /// <summary>
    /// Atanmamış (CLR varsayılanı) — GEÇERSİZ: <c>CK_ParentStudentLinks_Origin_Known</c> CHECK kısıtı reddeder; her yazım yolu
    /// kuruluş yolunu açıkça vermek zorunda (security MINOR-2: unutulan alan sessizce "eski bağlantı" sayılmasın).
    /// </summary>
    Unknown = 0,
    /// <summary>#436 öncesi (#419 öğrenci kodu + öğrenci onayı) — migration mevcut tüm satırlara yazar.</summary>
    LegacyV1 = 1,
    /// <summary>Birincil velinin ürettiği "ikinci veli davet kodu" ile; birincil veli onaylayınca Active.</summary>
    InviteCode = 2,
    /// <summary>Veli çocuk hesabını kendi panelinden açtı (#438) — doğrudan Active.</summary>
    ParentCreated = 3,
    /// <summary>Öğrenci kayıtta veli bilgisi verdi, veli aktivasyonla Active oldu (#440).</summary>
    StudentRegistered = 4
}

/// <summary>
/// Issue #419 (epic #407 V1) → issue #436 (epic #435, veli-öncelikli): velinin bir öğrenciye bağlantısı. Bağlantıyı veli tarafı
/// kurar (<see cref="Origin"/>): birincil velinin ikinci veli kodunu (<see cref="ParentInviteCode"/>) giren velinin satırı
/// <see cref="ParentStudentLinkStatus.Pending"/> açılır, birincil veli onaylayınca <see cref="ParentStudentLinkStatus.Active"/>
/// olur (reddedilen / süresi dolan istek Revoked); öğrenci onaylamaz (geçiş dönemindeki LegacyV1 istekleri hariç). Yalnızca
/// Active bağlantı erişim verir. Bağlantı koparılınca (birincil veli / kendi ayrılan veli / admin) satır silinmez: <see cref="Status"/> = Revoked + <see cref="RevokedAt"/>/
/// <see cref="RevokedByUserId"/> (erişim anında biter; geçmiş kalır). Çift başına en fazla bir AÇIK (Active ya da Pending)
/// satır (filtreli tekil index); aynı çift sonra yeni kodla yeniden bağlanabilir.
/// <see cref="BaseEntity"/>'den türemez: soft-delete bayrağı yerine durum makinesi kullanılır.
/// </summary>
public class ParentStudentLink
{
    [Key]
    public int Id { get; set; }

    public int ParentId { get; set; }

    public Parent Parent { get; set; } = null!;

    public int StudentId { get; set; }

    public Student Student { get; set; } = null!;

    public ParentStudentLinkStatus Status { get; set; }

    /// <summary>Satırın oluşturulduğu an (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Active olduğu an (UTC): birincil veli onayı, veli-açılışında oluşturma ya da geçiş dönemi öğrenci onayı; Pending iken null.</summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>Koparıldığı / reddedildiği / süresi dolup kapatıldığı an (UTC); açıkken null.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Koparan / reddeden kullanıcının exam/auth user id'si (öğrenci, veli ya da admin); süresi dolup kapatılanda null.</summary>
    public int? RevokedByUserId { get; set; }

    /// <summary>Issue #436: bağlantının nasıl kurulduğu (geçiş kuralları ve onay yolu buna bakar).</summary>
    public ParentStudentLinkOrigin Origin { get; set; }

    /// <summary>
    /// Issue #436: öğrencinin BİRİNCİL velisi — ikinci veli kodu üretir, bekleyen isteği onaylar, bağlantıları koparır. Öğrenci
    /// başına en fazla bir Active + IsPrimary satır (filtreli tekil index). Birincil veli koparılır ya da hesabı silinirse kalan
    /// en eski Active veli birincil olur (<c>ParentLinkPrimary.EnsurePrimaryAsync</c>; silinme için süpürücü job + okuma anında
    /// aynı kural).
    /// </summary>
    public bool IsPrimary { get; set; }
}
