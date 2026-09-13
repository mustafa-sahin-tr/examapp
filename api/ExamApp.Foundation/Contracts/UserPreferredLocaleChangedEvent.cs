using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Bir kullanıcının dil tercihi değiştiğinde ya da ilk kez oluştuğunda (issue #185) auth-api'nin
/// aynı transaction içinde outbox'a yazdığı event. BadgeService bunu tüketip
/// <c>UserLocalePreference</c> tablosuna upsert eder; bildirim metinleri buradan okunan dille
/// üretilir.
///
/// İki üretim noktası:
///  - <c>PUT /api/auth/me/locale</c>: değer gerçekten değiştiğinde.
///  - <c>POST /api/auth/register</c>: kullanıcı ilk oluştuğunda, varsayılan dille (henüz hiç
///    tercih yapılmamışsa BadgeService'in kullanıcıyı default'tan farklı davranmaması için ayrı
///    bir event yerine bu event tekrar kullanılır — <see cref="IndependentTeacherRegisteredEvent"/>
///    gibi başka bir amaca hizmet eden event'e alan eklenmez).
///
/// Payload minimum tutulur: id'ler + dil kodu + zaman damgası. Hassas veri taşınmaz.
/// </summary>
public class UserPreferredLocaleChangedEvent
{
    /// <summary>auth-api User.Id — BadgeService tarafında idempotency/upsert anahtarı.</summary>
    public int UserId { get; set; }

    /// <summary>Kullanıcının Keycloak subject'i — SignalR hedeflemesiyle aynı kimlik.</summary>
    public string KeycloakId { get; set; } = string.Empty;

    /// <summary>Normalize edilmiş dil kodu (<see cref="Localization.SupportedLocales"/>): "tr", "en".</summary>
    public string PreferredLocale { get; set; } = Localization.SupportedLocales.Default;

    /// <summary>Değişikliğin/oluşumun gerçekleştiği an (UTC). Eski event'in geç gelmesi bu alanla tespit edilir.</summary>
    public DateTime ChangedAtUtc { get; set; }
}
