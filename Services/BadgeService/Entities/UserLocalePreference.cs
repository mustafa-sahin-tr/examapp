using System;
using System.ComponentModel.DataAnnotations;
using ExamApp.Foundation.Localization;

namespace BadgeService.Entities;

/// <summary>
/// Kullanıcının dil tercihinin BadgeService yerel kopyası (issue #185). auth-api'nin outbox'a
/// yazdığı <see cref="ExamApp.Foundation.Contracts.UserPreferredLocaleChangedEvent"/>'i tüketen
/// <c>UserPreferredLocaleChangedConsumer</c> tarafından upsert edilir. Bildirim metinleri
/// (<c>NotificationTextFactory</c>) hedef kullanıcının dilini <c>IUserLocaleResolver</c>
/// üzerinden buradan okur — senkron auth-api çağrısı yoktur.
/// </summary>
public class UserLocalePreference
{
    /// <summary>auth-api User.Id — PK, upsert anahtarı.</summary>
    public int UserId { get; set; }

    /// <summary>SignalR/loglama için saklanır; dil çözümlemesinde UserId birincil anahtardır.</summary>
    public string? KeycloakId { get; set; }

    /// <summary>Normalize edilmiş dil kodu (<see cref="SupportedLocales"/>): "tr", "en".</summary>
    [MaxLength(8)]
    public string Locale { get; set; } = SupportedLocales.Default;

    /// <summary>
    /// Kaynak event'in <c>ChangedAtUtc</c> değeri. Geç gelen/eski bir event'in daha yeni bir
    /// kaydın üzerine yazmasını engellemek için kullanılır (out-of-order teslim idempotency'si).
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
