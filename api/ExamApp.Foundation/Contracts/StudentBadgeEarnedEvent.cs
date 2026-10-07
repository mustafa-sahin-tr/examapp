using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #422 (veli paneli V4): BadgeService bir öğrenciye rozet verdiğinde (<c>BadgeEarned</c> satırı) KENDİ outbox'ına
/// (badge DB) aynı SaveChanges içinde yazdığı event. <c>badge-outbox-publisher</c> RabbitMQ'ya taşır; exam API
/// <c>StudentBadgeEarnedConsumer</c> ile tüketip <c>StudentBadgeProjections</c>'a (öğrenci + rozet UNIQUE) idempotent yazar —
/// veli paneli rozetleri yalnızca bu projeksiyondan okur (servisler arası HTTP/DB paylaşımı yok; #225 deseni).
/// Hassas veri yok: sayısal kullanıcı id'si, rozet tanımı ve allowlist'ten geçmiş ikon adı.
/// Geriye dönük doldurma yok: bu event'ten önce kazanılmış rozetler projeksiyona yazılmaz (ürün kararı, prod yok).
/// </summary>
public class StudentBadgeEarnedEvent
{
    /// <summary>auth-api User.Id (BadgeService anahtarı). exam API bunu <c>Students.UserId</c> ile eşler.</summary>
    public int UserId { get; set; }

    /// <summary>BadgeService <c>BadgeDefinition.Id</c>.</summary>
    public Guid BadgeDefinitionId { get; set; }

    /// <summary>Rozetin kazanıldığı andaki adı.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Allowlist'ten geçmiş Material Symbols adı; yoksa null.</summary>
    public string? Icon { get; set; }

    /// <summary>Kazanma anı (UTC).</summary>
    public DateTime EarnedAtUtc { get; set; }
}
