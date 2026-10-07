using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Veli–öğrenci bağlantı durumu (issue #419). String saklanır.</summary>
public enum ParentStudentLinkStatus
{
    /// <summary>Veli kodu girdi, öğrencinin onayı bekleniyor (en fazla <c>ParentLinkRules.PendingValidity</c>); hiçbir erişim vermez.</summary>
    Pending = 0,
    Active = 1,
    Revoked = 2
}

/// <summary>
/// Issue #419 (epic #407 V1): velinin bir öğrenciye bağlantısı. Öğrenci davet kodu üretir (<see cref="ParentInviteCode"/>),
/// veli kodu girince satır <see cref="ParentStudentLinkStatus.Pending"/> açılır, öğrenci onaylayınca
/// <see cref="ParentStudentLinkStatus.Active"/> olur (reddedilen / süresi dolan istek Revoked). Yalnızca Active bağlantı erişim
/// verir. Taraflardan biri koparınca satır silinmez: <see cref="Status"/> = Revoked + <see cref="RevokedAt"/>/
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

    /// <summary>Öğrencinin onayladığı (Active olduğu) an (UTC); Pending iken null.</summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>Koparıldığı / reddedildiği / süresi dolup kapatıldığı an (UTC); açıkken null.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Koparan / reddeden kullanıcının exam/auth user id'si (öğrenci ya da veli); süresi dolup kapatılanda null.</summary>
    public int? RevokedByUserId { get; set; }
}
